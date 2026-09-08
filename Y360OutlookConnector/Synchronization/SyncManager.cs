using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using CalDavSynchronizer.ChangeWatching;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using CalDavSynchronizer.Ui;
using CalDavSynchronizer.Ui.ConnectionTests;
using CalDavSynchronizer.Utilities;
using log4net;
using Y360OutlookConnector.Clients;
using Y360OutlookConnector.Configuration;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization
{
    public class SyncManager : IDisposable
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private sealed class SyncTargetsUpdateResult
        {
            public List<SyncTargetInfo> Targets { get; }
            public Dictionary<Guid, string> CTags { get; }

            public SyncTargetsUpdateResult(
                List<SyncTargetInfo> targets,
                Dictionary<Guid, string> ctags)
            {
                Targets = targets;
                CTags = ctags;
            }
        }

        private readonly LoginController _loginController;
        private readonly string _dataFolderPath;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Scheduler _scheduler;
        private readonly SyncConfigController _syncConfig;
        private readonly UserSyncPrefsController _syncPrefs;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly InvitesInfoStorage _invitesInfo;
        private readonly IUserEmailService _userEmailService;
        private readonly SemaphoreSlim _schedulerRunLock = new SemaphoreSlim(1, 1);

        private volatile Task<List<SyncTargetInfo>> _syncTargetsTask;
        private TaskCompletionSource<bool> _syncTargetsReplaced;
        private DateTime _syncStartTime;
        private List<SyncTargetInfo> _cachedSyncTargets;
        private string _cachedSyncTargetsUserEmail;
        private int _syncTargetsUpdateGeneration;
        private int _syncSessionEpoch;
        private Dictionary<Guid, string> _ctags;


        public string UserEmail { get; private set; }
        public SyncStatus Status { get; set; }
        public IUserEmailService UserEmailService => _userEmailService;

        /// <summary>
        /// Запрет или разрешение на выполнение синхронизации по таймеру
        /// </summary>
        public bool AutoSyncDisabled { get; set; }

        public SyncManager(Outlook.Application application, IHttpClientFactory httpClientFactory,
            LoginController loginController, ProxyOptionsProvider proxyOptionsProvider, string dataFolderPath,
            InvitesInfoStorage invitesInfo)
        {
            _httpClientFactory = httpClientFactory;
            _dataFolderPath = dataFolderPath;

            _ctags = new Dictionary<Guid, string>();
            _syncTargetsReplaced = CreateSyncTargetsReplacedSource();
            _syncTargetsTask = Task.FromResult(new List<SyncTargetInfo>());
            Status = new SyncStatus();


            _scheduler = new Scheduler(application.Session, httpClientFactory, dataFolderPath, Status, invitesInfo);

            _syncConfig = new SyncConfigController(dataFolderPath);
            _syncPrefs = new UserSyncPrefsController(dataFolderPath, _syncConfig);

            _invitesInfo = invitesInfo;
            _loginController = loginController;
            _loginController.LoginStateChanged += LoginController_LoginStateChanged;

            _userEmailService = new UserEmailService(httpClientFactory.CreateHttpClient());

            _timer = new System.Windows.Forms.Timer();
            _timer.Tick += Timer_Tick;

            proxyOptionsProvider.ProxyOptionsChanged += OnProxyOptionsChanged;

            _ = UpdateSyncTargetsAsync(false);
        }

        public void Launch()
        {
            if (_loginController.IsUserLoggedIn)
            {

                s_logger.Info("User is logged in, fetching user emails");
                _ = FetchUserEmailsAsync();

                if (AppConfig.IsAutoSyncEnabled)
                {
                    _timer.Interval = (int)TimeSpan.FromSeconds(5).TotalMilliseconds;
                    _timer.Start();
                }
                else
                {
                    s_logger.Warn("Auto-sync is disabled");
                }
            }
            else
            {
                s_logger.Info("User is not logged in");
            }
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            await OnAutoSync();
        }

        private async Task OnAutoSync()
        {
            _timer.Stop();
            if (Status.State != SyncState.Running && !AutoSyncDisabled)
            {
                await RunSynchronization();
            }
            ThisAddIn.UiContext.Post(x =>
            {
                _timer.Interval = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
                _timer.Start();
            },
            null);
        }

        public void Dispose()
        {
            _invitesInfo.Save();
            _timer?.Dispose();
            _schedulerRunLock.Dispose();
        }

        private void OnProxyOptionsChanged(object sender, EventArgs e)
        {
            if (Status.CriticalError == CriticalError.ProxyAuthFailure
                || Status.CriticalError == CriticalError.ProxyConnectFailure)
            {
                Ui.ErrorWindow.HideError(Ui.ErrorWindow.ErrorType.ProxyError);
                _ = UpdateSyncTargetsAsync(false);
            }
        }

        public void ApplySyncConfig(List<SyncTargetInfo> syncTargets, bool savePrefs = false)
        {
            if (!_loginController.IsUserLoggedIn || _loginController.UserInfo == null)
            {
                return;
            }

            var userEmail = _loginController.UserInfo.Email;
            var userCommonName = _loginController.UserInfo.RealName;
            Interlocked.Increment(ref _syncTargetsUpdateGeneration);

            ApplySyncConfigCore(syncTargets, userEmail, userCommonName, savePrefs);
        }

        private void ApplySyncConfigCore(
            List<SyncTargetInfo> syncTargets,
            string userEmail,
            string userCommonName,
            bool savePrefs = false)
        {
            UserEmail = userEmail;

            _syncConfig.SelectUser(userEmail);
            _syncConfig.SetConfig(syncTargets.ConvertAll(x => x.Config));

            if (savePrefs)
            {
                _syncPrefs.SaveAll(userEmail, syncTargets.ConvertAll(x => x.Config));
            }

            if (_cachedSyncTargets != null && String.Equals(_cachedSyncTargetsUserEmail, userEmail, StringComparison.OrdinalIgnoreCase))
            {
                CleanupEntityCaches(_cachedSyncTargets, syncTargets);
            }

            _cachedSyncTargets = syncTargets.ConvertAll(x => x.Clone());
            _cachedSyncTargetsUserEmail = userEmail;
            PublishSyncTargetsTask(Task.FromResult(_cachedSyncTargets.ConvertAll(x => x.Clone())));

            _scheduler.ApplySettings(syncTargets, userEmail, userCommonName);
        }

        private void CleanupEntityCaches(List<SyncTargetInfo> oldTargets, IReadOnlyCollection<SyncTargetInfo> newTargets)
        {
            var idsToDelete = new List<Guid>();
            foreach (var newItem in newTargets)
            {
                var oldTarget = oldTargets.Find(x => x.Id == newItem.Id);
                if (oldTarget == null) continue;

                if (oldTarget.Config.OutlookFolderEntryId != newItem.Config.OutlookFolderEntryId
                    || oldTarget.Config.OutlookFolderStoreId != newItem.Config.OutlookFolderStoreId)
                {
                    idsToDelete.Add(oldTarget.Id);
                }
            }

            foreach (var targetId in idsToDelete)
            {
                var folderPath = Path.Combine(_dataFolderPath, targetId.ToString());
                var filePath = Path.Combine(folderPath, "relations.xml");

                try
                {
                    if (File.Exists(filePath))
                    {
                        s_logger.Info($"Removing file {filePath}");
                        File.Delete(filePath);
                    }
                    if (Directory.Exists(folderPath))
                    {
                        s_logger.Info($"Removing folder {folderPath}");
                        Directory.Delete(folderPath);
                    }
                }
                catch (Exception exc)
                {
                    s_logger.Warn($"Failed to remove entities cache for profile {targetId}", exc);
                }
            }
        }

        public SyncTargetConfig GetSyncTargetConfig(string outlookFolderId, SyncTargetType targetType = SyncTargetType.Calendar)
        {
            return _cachedSyncTargets?.FirstOrDefault(s => s.TargetType == targetType &&
                                                      s.Config.Active && s.Config.OutlookFolderEntryId == outlookFolderId)?.Config;
        }

        public IWebDavClient CreateWebDavClient()
        {
            return _httpClientFactory.CreateWebDavClient(new CancellationTokenSource());
        }

        public async Task<List<SyncTargetInfo>> GetSyncTargets()
        {
            while (true)
            {
                var replaced = Volatile.Read(ref _syncTargetsReplaced);
                var task = _syncTargetsTask;
                await Task.WhenAny(task, replaced.Task);
                if (!ReferenceEquals(task, _syncTargetsTask))
                {
                    continue;
                }
                if (!task.IsCompleted)
                {
                    continue;
                }

                var result = await task;
                return result ?? new List<SyncTargetInfo>();
            }
        }

        public async Task RunSynchronization(bool manuallyTriggered = false, bool noDateConstraint = false)
        {
            bool isBlankShot = false;
            bool started = false;
            bool lockTaken = false;
            List<SyncTargetInfo> targetsSnapshot = null;
            var sessionEpoch = _syncSessionEpoch;
            try
            {
                ThisAddIn.RestoreUiContext();
                int appliedGeneration = await UpdateSyncTargetsAsync(manuallyTriggered);
                if (appliedGeneration < 0 || !IsCurrentDiscovery(sessionEpoch, appliedGeneration))
                {
                    return;
                }

                targetsSnapshot = CloneSyncTargets(_cachedSyncTargets);
                var ctagsSnapshot = _ctags != null ? new Dictionary<Guid, string>(_ctags) : new Dictionary<Guid, string>();

                await FetchUserEmailsAsync();
                if (!IsCurrentDiscovery(sessionEpoch, appliedGeneration))
                {
                    return;
                }

                await _schedulerRunLock.WaitAsync();
                lockTaken = true;
                if (!IsCurrentDiscovery(sessionEpoch, appliedGeneration))
                {
                    return;
                }

                OnSyncStarted();
                started = true;

                isBlankShot = await _scheduler.RunSynchronization(manuallyTriggered, noDateConstraint, ctagsSnapshot, () => IsCurrentSyncSession(sessionEpoch)) == false;
                if (!IsCurrentDiscovery(sessionEpoch, appliedGeneration))
                {
                    return;
                }

                await RetryFailedEntities(targetsSnapshot);
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
            finally
            {
                if (started && IsCurrentSyncSession(sessionEpoch))
                {
                    OnSyncFinished(isBlankShot, targetsSnapshot);
                }

                if (lockTaken)
                {
                    _schedulerRunLock.Release();
                }
            }
        }

        private void OnSyncStarted()
        {
            _syncStartTime = DateTime.UtcNow;

            var targetIds = new List<Guid>();
            if (_cachedSyncTargets != null)
            {
                foreach (var target in _cachedSyncTargets)
                {
                    if (target.Config.Active)
                        targetIds.Add(target.Id);
                }
            }
            Status.OnSynchronizationStarted(targetIds);
        }

        private void OnSyncFinished(bool isBlankShot, List<SyncTargetInfo> syncTargets)
        {
            try
            {
                Status.OnSynchronizationFinished();

                if (!isBlankShot)
                {
                    var duration = DateTime.UtcNow - _syncStartTime;
                    Telemetry.Signal(Telemetry.SyncReportsEvents, "sync_complete");

                    s_logger.Info($"Sync complete. Duration: {duration}");

                    Status.SendReportsTelemetry(syncTargets ?? new List<SyncTargetInfo>());

                    _invitesInfo.CleanUp();
                    _invitesInfo.Save();
                }
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        private void LoginController_LoginStateChanged(object sender, LoginStateEventArgs e)
        {
            Interlocked.Increment(ref _syncSessionEpoch);
            s_logger.Info($"LoginController_LoginStateChanged called: IsUserLoggedIn = {e.IsUserLoggedIn}");

            if (e.IsUserLoggedIn)
            {
                s_logger.Info("User logged in, fetching user emails");
                _ = FetchUserEmailsAsync();

                if (AppConfig.IsAutoSyncEnabled && !AutoSyncDisabled)
                {
                    s_logger.Info("Sync triggered by user log-in");
                    _ = OnAutoSync();
                }
                else
                {
                    s_logger.Warn("Auto-sync is disabled");
                    _ = UpdateSyncTargetsAsync(false);
                }
            }
            else
            {
                s_logger.Info("User logged out, clearing email cache");
                _timer.Stop();
                _scheduler.ClearSettings();
                Status.Reset();

                _userEmailService.ClearCache();
                ClearCachedSyncTargets();
                Ui.SyncConfigWindow.CloseCurrent();
            }
        }

        private async Task FetchUserEmailsAsync()
        {
            try
            {
                s_logger.Info("FetchUserEmailsAsync called");

                if (!_loginController.IsUserLoggedIn)
                {
                    s_logger.Warn("User is not logged in, skipping email fetch");
                    return;
                }

                s_logger.Info("User is logged in, proceeding with email fetch");

                if (_loginController.UserInfo?.AccessToken == null)
                {
                    s_logger.Error("Access token is null, cannot fetch user emails");
                    return;
                }

                var accessToken = SecureStringUtility.ToUnsecureString(_loginController.UserInfo.AccessToken);
                s_logger.Info($"Access token retrieved, length: {accessToken?.Length ?? 0}");

                await _userEmailService.GetUserEmailsAsync(accessToken);
                s_logger.Info("Successfully fetched user emails");
            }
            catch (Exception ex)
            {
                s_logger.Error($"Failed to fetch user emails: {ex.Message}", ex);
            }
        }

        private async Task<int> UpdateSyncTargetsAsync(bool manuallyTriggered)
        {
            if (!_loginController.IsUserLoggedIn)
            {
                return -1;
            }

            var userEmail = _loginController.UserInfo.Email;
            var userCommonName = _loginController.UserInfo.RealName;
            UserEmail = userEmail;
            _syncConfig.SelectUser(userEmail);

            var updateGeneration = Interlocked.Increment(ref _syncTargetsUpdateGeneration);

            // Snapshot on the UI thread before Task.Run. Use it only for the same account
            // so logout -> login of another user cannot reuse the previous cache.
            List<SyncTargetInfo> cachedSyncTargetsSnapshot = null;
            if (_cachedSyncTargets != null && String.Equals(_cachedSyncTargetsUserEmail, userEmail, StringComparison.OrdinalIgnoreCase))
            {
                cachedSyncTargetsSnapshot = _cachedSyncTargets.ConvertAll(x => x.Clone());
            }

            List<SyncTargetConfig> persistedConfigsSnapshot;
            var allConfigsSnapshot = _syncConfig.GetAllConfigs();
            if (!allConfigsSnapshot.TryGetValue(userEmail, out persistedConfigsSnapshot) || persistedConfigsSnapshot == null)
            {
                persistedConfigsSnapshot = new List<SyncTargetConfig>();
            }

            var configsByUrlSnapshot = new Dictionary<string, SyncTargetConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var config in persistedConfigsSnapshot)
            {
                if (!String.IsNullOrEmpty(config.Url))
                {
                    configsByUrlSnapshot[config.Url] = config;
                }
            }

            var activePrefsSnapshot = _syncPrefs.GetActiveSnapshot(userEmail);

            var task = Task.Run(async () =>
            {

                var webDavClient = _httpClientFactory.CreateWebDavClient(new CancellationTokenSource());
                var calDavResult = await GetCalDavResources(webDavClient, configsByUrlSnapshot, activePrefsSnapshot);
                var cardDavTargets = await GetCardDavResources(webDavClient, configsByUrlSnapshot, activePrefsSnapshot);

                // Empty CardDAV discovery is often a transient failure (empty/invalid PROPFIND),
                // not "user has no address books". Overwriting sync_config without contacts
                // regenerates GUIDs and creates duplicate Outlook folders on the next success.
                if (cardDavTargets.Count == 0)
                {
                    var preservedContacts = PreserveExistingContactTargetsIfAny(cachedSyncTargetsSnapshot, persistedConfigsSnapshot);
                    if (preservedContacts.Count > 0)
                    {
                        cardDavTargets = preservedContacts;
                    }
                }

                calDavResult.Targets.AddRange(cardDavTargets);

                foreach (var item in calDavResult.Targets)
                {
                    s_logger.Debug($"Sync target: {item.Id} - {item.Name} - {item.Config.Url}");
                }

                return calDavResult;
            });

            ThisAddIn.RestoreUiContext();
            var publishedTask = task.ContinueWith(t =>
            {
                if (!ShouldApplySyncTargetsUpdate(updateGeneration, userEmail))
                {
                    return _cachedSyncTargets;
                }

                try
                {
                    var result = t.GetAwaiter().GetResult();
                    if (!ShouldApplySyncTargetsUpdate(updateGeneration, userEmail))
                    {
                        return _cachedSyncTargets;
                    }

                    ResolveMissingContactNames(result.Targets);
                    AutoPopulateConfig(result.Targets, userEmail, userCommonName);
                    _ctags = result.CTags;
                    Status.SetCriticalError(CriticalError.None);
                }
                catch (Exception exc)
                {
                    if (ShouldApplySyncTargetsUpdate(updateGeneration, userEmail))
                    {
                        SyncErrorHandler.HandleException(
                            exc,
                            !manuallyTriggered,
                            () => ShouldApplySyncTargetsUpdate(updateGeneration, userEmail));
                    }
                }
                return _cachedSyncTargets;
            },
            TaskScheduler.FromCurrentSynchronizationContext());

            PublishSyncTargetsTask(publishedTask);
            await publishedTask;
            if (!ShouldApplySyncTargetsUpdate(updateGeneration, userEmail))
            {
                return -1;
            }

            return updateGeneration;
        }

        private void AutoPopulateConfig(List<SyncTargetInfo> syncTargets, string userEmail, string userCommonName)
        {
            var session = ThisAddIn.Components.OutlookApplication.Session;
            _syncConfig.SelectUser(userEmail);

            var accountFolders = new AccountFolders(userEmail, session);
            foreach (var item in syncTargets)
            {
                bool isNew = _syncConfig.GetSyncTargetById(item.Id) == null;
                if (!isNew) continue;

                bool folderAssigned = false;
                if (item.IsPrimary)
                {
                    var defaultFolder = accountFolders.GetDefaultFolderDescriptor(item.TargetType);
                    if (defaultFolder != null && !IsFolderInUse(syncTargets, defaultFolder))
                    {
                        item.Config.OutlookFolderEntryId = defaultFolder.EntryId;
                        item.Config.OutlookFolderStoreId = defaultFolder.StoreId;
                        folderAssigned = true;
                    }
                }

                if (!folderAssigned)
                {
                    var folder = accountFolders.CreateNewFolder(item.TargetType, item.Name);
                    if (folder != null)
                    {
                        item.Config.OutlookFolderEntryId = folder.EntryID;
                        item.Config.OutlookFolderStoreId = folder.StoreID;
                        folderAssigned = true;
                    }
                }

                item.Config.Active = folderAssigned;
            }

            ApplySyncConfigCore(syncTargets, userEmail, userCommonName);
        }

        private async Task<SyncTargetsUpdateResult> GetCalDavResources(IWebDavClient webDavClient, IReadOnlyDictionary<string, SyncTargetConfig> configsByUrl,
            IReadOnlyDictionary<string, bool> activePrefs)
        {
            var calDavDataProvider = new CalDavResourcesDataAccess(new Uri(EndpointConfig.CalDavBaseUrl), webDavClient);
            var resources = await calDavDataProvider.GetResources();

            var ctags = new Dictionary<Guid, string>();

            var items = new List<SyncTargetInfo>();
            int calendarsCounter = 0;
            foreach (var calendar in resources.CalendarResources)
            {
                var targetConfig = GetSyncTargetConfig(calendar.Uri, configsByUrl, activePrefs);
                items.Add(new SyncTargetInfo(targetConfig)
                {
                    TargetType = SyncTargetType.Calendar,
                    Name = calendar.Name,
                    Privileges = calendar.Privileges,
                    IsPrimary = calendarsCounter == 0,
                });
                ctags[targetConfig.Id] = calendar.CTag;
                calendarsCounter++;
            }
            int taskListCounter = 0;
            foreach (var taskList in resources.TaskListResources)
            {
                var targetConfig = GetSyncTargetConfig(new Uri(taskList.Id), configsByUrl, activePrefs);
                items.Add(new SyncTargetInfo(targetConfig)
                {
                    TargetType = SyncTargetType.Tasks,
                    Name = taskList.Name,
                    Privileges = taskList.Privileges,
                    IsPrimary = taskListCounter == 0
                });
                ctags[targetConfig.Id] = taskList.CTag;
                taskListCounter++;
            }

            return new SyncTargetsUpdateResult(items, ctags);
        }

        private async Task<List<SyncTargetInfo>> GetCardDavResources(IWebDavClient webDavClient, IReadOnlyDictionary<string, SyncTargetConfig> configsByUrl,
            IReadOnlyDictionary<string, bool> activePrefs)
        {
            var calDavDataAccess = new CardDavDataAccess(new Uri(EndpointConfig.CardDavBaseUrl), webDavClient, string.Empty, contentType => true);
            var resources = await calDavDataAccess.GetUserAddressBooksNoThrow(false);

            var items = new List<SyncTargetInfo>();
            int counter = 0;
            foreach (var addressBook in resources)
            {
                var targetConfig = GetSyncTargetConfig(addressBook.Uri, configsByUrl, activePrefs);
                items.Add(new SyncTargetInfo(targetConfig)
                {
                    TargetType = SyncTargetType.Contacts,
                    Name = addressBook.Name,
                    Privileges = addressBook.Privileges,
                    IsPrimary = counter == 0
                });
                counter++;
            }

            foreach (var item in items)
            {
                item.Name = GetContactsResourceDisplayName(item.Name);
            }

            return items;
        }

        private bool ShouldApplySyncTargetsUpdate(int updateGeneration, string userEmail)
        {
            if (updateGeneration != _syncTargetsUpdateGeneration)
            {
                return false;
            }

            if (!_loginController.IsUserLoggedIn)
            {
                return false;
            }

            return String.Equals(_loginController.UserInfo?.Email, userEmail, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCurrentSyncSession(int sessionEpoch)
        {
            return sessionEpoch == _syncSessionEpoch && _loginController.IsUserLoggedIn;
        }

        private bool IsCurrentDiscovery(int sessionEpoch, int updateGeneration)
        {
            return IsCurrentSyncSession(sessionEpoch) && updateGeneration == _syncTargetsUpdateGeneration;
        }

        private static List<SyncTargetInfo> CloneSyncTargets(List<SyncTargetInfo> targets)
        {
            return targets == null ? new List<SyncTargetInfo>() : targets.ConvertAll(x => x.Clone());
        }


        private void ClearCachedSyncTargets()
        {
            Interlocked.Increment(ref _syncTargetsUpdateGeneration);
            _cachedSyncTargets = null;
            _cachedSyncTargetsUserEmail = null;
            UserEmail = null;
            PublishSyncTargetsTask(Task.FromResult(new List<SyncTargetInfo>()));
            _ctags = new Dictionary<Guid, string>();
        }

        private void PublishSyncTargetsTask(Task<List<SyncTargetInfo>> task)
        {
            _syncTargetsTask = task;
            var previous = Interlocked.Exchange(ref _syncTargetsReplaced, CreateSyncTargetsReplacedSource());
            previous.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> CreateSyncTargetsReplacedSource()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }


        private List<SyncTargetInfo> PreserveExistingContactTargetsIfAny(IReadOnlyList<SyncTargetInfo> cachedSyncTargetsSnapshot, IReadOnlyList<SyncTargetConfig> persistedConfigsSnapshot)
        {
            var preserved = new List<SyncTargetInfo>();

            if (cachedSyncTargetsSnapshot != null)
            {
                foreach (var item in cachedSyncTargetsSnapshot)
                {
                    if (item.TargetType == SyncTargetType.Contacts)
                    {
                        preserved.Add(item.Clone());
                    }
                }
            }

            if (preserved.Count == 0)
            {
                var counter = 0;
                foreach (var config in persistedConfigsSnapshot)
                {
                    if (!IsLikelyContactTargetUrl(config.Url))
                    {
                        continue;
                    }

                    preserved.Add(new SyncTargetInfo(config)
                    {
                        TargetType = SyncTargetType.Contacts,
                        Name = String.Empty,
                        Privileges = AccessPrivileges.None,
                        IsPrimary = counter == 0
                    });
                    counter++;
                }
            }

            if (preserved.Count == 0)
            {
                return preserved;
            }

            s_logger.Warn($"CardDAV discovery returned no address books; keeping {preserved.Count} existing contact sync target(s) to avoid config wipe.");

            return preserved;
        }

        private void ResolveMissingContactNames(IEnumerable<SyncTargetInfo> syncTargets)
        {
            var session = ThisAddIn.Components?.OutlookApplication?.Session;
            foreach (var item in syncTargets)
            {
                if (item.TargetType != SyncTargetType.Contacts)
                {
                    continue;
                }

                if (!String.IsNullOrEmpty(item.Name))
                {
                    item.Name = GetContactsResourceDisplayName(item.Name);
                    continue;
                }

                var folderName = TryGetOutlookFolderName(session, item.Config.OutlookFolderEntryId, item.Config.OutlookFolderStoreId);
                item.Name = !String.IsNullOrEmpty(folderName) ? folderName : Localization.Strings.SyncConfigWindow_ContactsDefaultName;
            }
        }

        private static bool IsLikelyContactTargetUrl(string url)
        {
            return !String.IsNullOrEmpty(url)
                   && url.IndexOf("/addressbook/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string TryGetOutlookFolderName(Outlook.NameSpace session, string entryId, string storeId)
        {
            if (session == null || String.IsNullOrEmpty(entryId))
                return null;

            try
            {
                var folder = String.IsNullOrEmpty(storeId)
                    ? session.GetFolderFromID(entryId)
                    : session.GetFolderFromID(entryId, storeId);
                
                if (folder == null)
                {
                    return null;
                }

                using (var wrapper = GenericComObjectWrapper.Create(folder))
                {
                    return wrapper.Inner.Name;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }


        private SyncTargetConfig GetSyncTargetConfig(Uri url, IReadOnlyDictionary<string, SyncTargetConfig> configsByUrl,
            IReadOnlyDictionary<string, bool> activePrefs)
        {
            SyncTargetConfig config;
            configsByUrl.TryGetValue(url.ToString(), out config);
            config = config?.Clone();

            if (config == null)
            {
                bool prefActive;
                config = new SyncTargetConfig
                {
                    Id = Guid.NewGuid(),
                    Url = url.ToString(),
                    Active = !activePrefs.TryGetValue(url.ToString(), out prefActive) || prefActive
                };
            }
            else
            {
                bool prefActive;
                if (activePrefs.TryGetValue(config.Url, out prefActive))
                {
                    config.Active = prefActive;
                }
            }

            return config;
        }

        private bool IsFolderInUse(List<SyncTargetInfo> syncTargets, OutlookFolderDescriptor folder)
        {
            if (folder == null)
                return false;

            foreach (var item in syncTargets)
            {
                if (item.Config.OutlookFolderEntryId == folder.EntryId
                    && item.Config.OutlookFolderStoreId == folder.StoreId)
                    return true;
            }

            return _syncConfig.IsFolderInUseByOtherUsers(folder.EntryId, folder.StoreId);
        }

        private static string GetContactsResourceDisplayName(string name)
        {
            switch (name)
            {
                case "Personal":
                    return Localization.Strings.SyncConfigWindow_PersonalContactsName;
                case "Shared":
                    return Localization.Strings.SyncConfigWindow_SharedContactsName;
                case "External":
                    return Localization.Strings.SyncConfigWindow_ExternalContactsName;
                default:
                    return name;
            }
        }

        private async Task RetryFailedEntities(List<SyncTargetInfo> syncTargets)
        {
            if (syncTargets == null)
            {
                return;
            }

            try
            {
                var activeSyncTargets = syncTargets.Where(s => s.Config.Active).ToList();

                foreach(var syncTarget in activeSyncTargets)
                {
                    var targetRunner = _scheduler.GetSyncTargetRunner(syncTarget.Id);
                    if (targetRunner != null)
                    {
                        await targetRunner.RetryFailedEntities();
                    }
                }
            }
            catch (Exception exc)
            {
                s_logger.Error("Error during failed entity retry", exc);
            }
        }

        private void ClearEntityCache(Guid targetId)
        {
            var folderPath = Path.Combine(_dataFolderPath, targetId.ToString());
            var filePath = Path.Combine(folderPath, "relations.xml");
            try
            {
                if (File.Exists(filePath))
                {
                    s_logger.Info($"Removing file {filePath}");
                    File.Delete(filePath);
                }
                if (Directory.Exists(folderPath))
                {
                    s_logger.Info($"Removing folder {folderPath}");
                    Directory.Delete(folderPath);
                }
            }
            catch (Exception exc)
            {
                s_logger.Warn($"Failed to remove entities cache for profile {targetId}", exc);
            }
        }

        public async Task RestoreContactsFromServerAsync()
        {
            try
            {
                var syncTargets = await GetSyncTargets();
                if (syncTargets == null)
                {
                    return;
                }

                s_logger.Info($"RestoreContactsFromServerAsync: total sync targets='{syncTargets.Count}'");

                var sharedName = Localization.Strings.SyncConfigWindow_SharedContactsName;
                var externalName = Localization.Strings.SyncConfigWindow_ExternalContactsName;

                var contactTargets = syncTargets.Where(s => s.TargetType == SyncTargetType.Contacts &&
                                                          s.Config.Active &&
                                                          (s.Name == sharedName || s.Name == externalName)).ToList();

                foreach (var syncTarget in contactTargets)
                {
                    s_logger.Info($"RestoreContactsFromServerAsync: Clearing cache for target Id='{syncTarget.Id}', Name='{syncTarget.Name}'");
                    ClearEntityCache(syncTarget.Id);
                }

                await RunSynchronization(manuallyTriggered: true, noDateConstraint: true);
            }
            catch (Exception exc)
            {
                s_logger.Error("Error during contacts restoration", exc);
            }
        }

        private IEnumerable<SyncTargetInfo> GetSharedOrExternalContactsTargets()
        {
            if (_cachedSyncTargets == null)
            {
                return Enumerable.Empty<SyncTargetInfo>();
            }

            var sharedName = Localization.Strings.SyncConfigWindow_SharedContactsName;
            var externalName = Localization.Strings.SyncConfigWindow_ExternalContactsName;

            return _cachedSyncTargets.Where(target =>
                target.TargetType == SyncTargetType.Contacts &&
                (String.Equals(target.Name, sharedName, StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(target.Name, externalName, StringComparison.OrdinalIgnoreCase)));
        }

        public bool IsSharedOrExternalContactsFolder(string outlookFolderEntryId, string outlookFolderStoreId)
        {
            if (String.IsNullOrEmpty(outlookFolderEntryId) || String.IsNullOrEmpty(outlookFolderStoreId))
            {
                return false;
            }

            s_logger.Debug($"IsSharedOrExternalContactsFolder: folder EntryId='{outlookFolderEntryId}', StoreId='{outlookFolderStoreId}'");

            foreach (var target in GetSharedOrExternalContactsTargets())
            {
                s_logger.Debug($"Checking target: Id='{target.Id}', Name='{target.Name}', EntryId='{target.Config.OutlookFolderEntryId}', StoreId='{target.Config.OutlookFolderStoreId}'");

                if (target.Config.OutlookFolderEntryId == outlookFolderEntryId &&
                    target.Config.OutlookFolderStoreId == outlookFolderStoreId)
                {
                    s_logger.Debug("IsSharedOrExternalContactsFolder: match found");
                    return true;
                }
            }

            s_logger.Debug("IsSharedOrExternalContactsFolder: no match found");
            return false;
        }
    }
}
