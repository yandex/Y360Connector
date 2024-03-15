using System;
using System.Reflection;
using log4net;
using Newtonsoft.Json;
using System.Net;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Web;
using Y360OutlookConnector.Clients;
using Y360OutlookConnector.Configuration;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector
{
    using UpdateChannel = RegistrySettings.UpdateChannel;

    public class AutoUpdateManager : IDisposable
    {
        public enum UpdateState
        {
            None,
            Checking,
            Downloading,
            Installing,
            WaitingForRestart,
            Restarting,
            NoUpdate
        }

        private class LastVersionInfo
        {
            [JsonProperty("file")]
            public string File { get; set; }

            [JsonProperty("sha256")]
            public string Sha256 { get; set; }

            [JsonProperty("version")]
            public Version Version { get; set; }
        }

        public event EventHandler UpdateStateChanged;

        public UpdateState State {
            get 
            { 
                lock (_stateLock) 
                    return _updateState;
            }
        }

        public Version AvailableVersion 
        {
            get 
            { 
                lock (_stateLock) 
                    return _availableVersion;
            }
        }

        private const string LastVersionInfoUrl = "https://cloud-api.yandex.net/v1/calendar/outlook-extensions/win86/installer";

        private readonly ProxyOptionsProvider _proxyProvider;
        private readonly Timer _timer;
        private readonly CancellationTokenSource _cancelTokenSource;
        private readonly Outlook.Application _application;

        private readonly object _stateLock = new object();
        private UpdateState _updateState = UpdateState.None;
        private Version _availableVersion;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public AutoUpdateManager(ProxyOptionsProvider proxyProvider, Outlook.Application application)
        {
            _application = application;
            _proxyProvider = proxyProvider;

            _timer = new Timer(OnTimer);
            _cancelTokenSource = new CancellationTokenSource();
        }

        public void Launch()
        {
            if (RegistrySettings.IsAutomaticUpdatesDisabled())
            {
                Telemetry.Signal(Telemetry.AutoUpdateEvents, "auto_update_disabled");
                s_logger.Warn("Automatic updates are disabled");
            }
            else
            {
                _timer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromDays(1));
            }
        }

        public void RestartOutlook()
        {
            s_logger.Info("Restarting Outlook...");
            ChangeState(UpdateState.Restarting);
            var applicationEvents = _application as Outlook.ApplicationEvents_11_Event;
            applicationEvents.Quit += AutoUpdateManager_AppQuitForRestart;
            _application.Quit();
        }

        private static void AutoUpdateManager_AppQuitForRestart()
        {
            var startInfo = new ProcessStartInfo()
            {
                FileName = "Outlook.exe",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }

        public void Dispose()
        {
            _cancelTokenSource.Dispose();

            using (WaitHandle timerWaitHandle = new AutoResetEvent(false))
            {
                _timer.Dispose(timerWaitHandle);
                WaitHandle.WaitAll(new[] { timerWaitHandle });
            }
        }

        private void OnTimer(object stateInfo)
        {
            try
            {
                CheckUpdate();
            }
            catch (Exception ex)
            {
                s_logger.Error("Check for update failed", ex);
            }
        }

        private void CheckUpdate()
        {
            ChangeState(UpdateState.Checking);

            var thisVersion = Assembly.GetExecutingAssembly().GetName().Version;
            var installedVersion = GetInstalledVersion();
            if (installedVersion == null) return;

            var lastVersionInfo = GetLastVersionInfo();
            if (lastVersionInfo.Version != null && lastVersionInfo.Version > installedVersion)
            {
                s_logger.Info($"New version found: {lastVersionInfo.Version}");

                string installerFileName = DownloadInstaller(lastVersionInfo);
                if (InstallUpdate(installerFileName, lastVersionInfo.Version))
                {
                    ChangeState(UpdateState.WaitingForRestart, lastVersionInfo.Version);
                }
                else
                {
                    ChangeState(UpdateState.None);
                } 
            }
            else if (installedVersion != thisVersion)
            {
                ChangeState(UpdateState.WaitingForRestart, installedVersion);
            }
            else
            {
                ChangeState(UpdateState.NoUpdate);
            }
        }

        private bool InstallUpdate(string installerFileName, Version version)
        {
            try
            {
                if (State == UpdateState.Installing)
                    return true;

                ChangeState(UpdateState.Installing);

                s_logger.Info("Installing update...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{installerFileName}\" /quiet /qn /norestart"
                };
                var process = Process.Start(startInfo);
                if (process == null)
                    throw new Exception("Unable to start msiexec");

                var task = WaitForProcessExitAsync(process, _cancelTokenSource.Token);
                task.Wait(_cancelTokenSource.Token);

                s_logger.Info($"Installer finished with exit code: {process.ExitCode}");
                if (process.ExitCode != 0)
                    throw new Exception("Update install completes with a non-zero exit code");

                Telemetry.Signal(Telemetry.AutoUpdateEvents, "update_installed", version);
                return true;
            }
            catch (Exception exc)
            {
                Telemetry.Signal(Telemetry.AutoUpdateEvents, "error_install");
                s_logger.Error("Update install failure:", exc);
                return false;
            }
        }

        private LastVersionInfo GetLastVersionInfo()
        {
            try
            {
                var updateChannel = RegistrySettings.GetUpdateChannel();
                var updateChannelUrl = GetUriForUpdateChannel(updateChannel);

                if (updateChannel != UpdateChannel.Stable)
                {
                    s_logger.Info($"Checking for update (update channel: {updateChannel.ToString().ToLower()})...");
                }

                var httpClientFactory = new HttpClientFactory(_proxyProvider);
                using (var httpClient = httpClientFactory.CreateHttpClient())
                {
                    var cancelToken = _cancelTokenSource.Token;

                    var task = httpClient.GetStringAsync(updateChannelUrl);
                    task.Wait(cancelToken);

                    return JsonConvert.DeserializeObject<LastVersionInfo>(task.Result);
                }
            }
            catch (Exception ex)
            {
                Telemetry.Signal(Telemetry.AutoUpdateEvents, "error_get_version");
                s_logger.Error("Failed to retrieve last version info", ex);
                return new LastVersionInfo();
            }
        }

        private string DownloadInstaller(LastVersionInfo versionInfo)
        {
            try
            {
                if (String.IsNullOrEmpty(versionInfo?.File)) throw new ArgumentException("invalid file url");

                ChangeState(UpdateState.Downloading);
                s_logger.Info($"Downloading the installer version {versionInfo.Version}: {versionInfo.File}");

                using (var client = new WebClient()) // TODO: replace with http client
                {
                    var cancelToken = _cancelTokenSource.Token;

                    var proxyOptions = _proxyProvider.GetProxyOptions();
                    client.Proxy = HttpClientFactory.CreateProxy(proxyOptions);

                    cancelToken.Register(client.CancelAsync);

                    string fileName = GetInstallerTempFilePath();
                    client.DownloadFile(versionInfo.File, fileName);

                    s_logger.Info($"Download complete: {fileName}");

                    return fileName;
                }
            }
            catch (Exception)
            {
                Telemetry.Signal(Telemetry.AutoUpdateEvents, "error_installer_download");
                throw;
            }
        }

        private void ChangeState(UpdateState state, Version version = null)
        {
            bool notify;
            lock (_stateLock)
            {
                notify = (_updateState != state);
                _updateState = state;
                _availableVersion = version;
            }
            if (notify)
                UpdateStateChanged?.Invoke(null, EventArgs.Empty);
        }

        private static string GetInstallerTempFilePath()
        {
            string folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(folderPath);
            return Path.Combine(folderPath, "Y360ConnectorSetup_x86.msi");
        }

        private static Uri GetUriForUpdateChannel(UpdateChannel updateChannel)
        {
            var uriBuilder = new UriBuilder(LastVersionInfoUrl);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query["build"] = updateChannel.ToString().ToLower();
            uriBuilder.Query = query.ToString();

            return uriBuilder.Uri;
        }

        private static Version GetInstalledVersion()
        {
            Version result = null;
            try
            {
                string installationPath = NormalizePath(AppDomain.CurrentDomain.BaseDirectory);
                string registrationPath = NormalizePath(GetRegistrationPath());

                if (String.Equals(installationPath, registrationPath, StringComparison.InvariantCultureIgnoreCase))
                {
                    string fileName = Assembly.GetExecutingAssembly().ManifestModule.Name;
                    var versionInfo = FileVersionInfo.GetVersionInfo(Path.Combine(installationPath, fileName));
                    result = new Version(versionInfo.ProductVersion);
                }
                else
                {
                    s_logger.Warn("Installation and registration paths do not match. Automatic update canceled");
                }
            }
            catch (Exception ex)
            {
                s_logger.Error("Failed to retrieve installed version", ex);
            }
            return result;
        }

        private static string GetRegistrationPath()
        {
            const string outlookAddInKey = "SOFTWARE\\Microsoft\\Office\\Outlook\\Addins\\Y360OutlookConnector";

            try
            {
                using (var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(outlookAddInKey))
                {
                    var value = regKey?.GetValue("Manifest");
                    if (value != null)
                    {
                        const string suffix = "|vstolocal";

                        var manifestRegValue = Convert.ToString(value);
                        if (manifestRegValue.ToLower().EndsWith(suffix))
                        {
                            manifestRegValue = manifestRegValue.Substring(0, manifestRegValue.Length - suffix.Length);
                            var manifestUrl = new Uri(manifestRegValue);
                            var manifestPath = manifestUrl.LocalPath;
                            if (File.Exists(manifestPath))
                            {
                                return Directory.GetParent(manifestPath)?.FullName;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                s_logger.Error("Filed to retrieve installation folder path", ex);
            }
            return null;
        }

        private static string NormalizePath(string path)
        {
            if (String.IsNullOrEmpty(path))
                return String.Empty;

            return Path.GetFullPath(new Uri(path).LocalPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }

        private static Task WaitForProcessExitAsync(Process process,
            CancellationToken cancellationToken)
        {
            if (process == null || process.HasExited) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(null);
            cancellationToken.Register(() => tcs.SetCanceled());

            return process.HasExited ? Task.CompletedTask : tcs.Task;
        }
    }
}
