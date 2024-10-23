using CalDavSynchronizer.DataAccess;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CalDavSynchronizer.Ui;
using log4net;
using Y360OutlookConnector.Configuration;
using Outlook = Microsoft.Office.Interop.Outlook;
using Y360OutlookConnector.Clients;
using System.Linq;

namespace Y360OutlookConnector.Synchronization
{
    public class SyncManager : IDisposable
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private const string CalDavUrl = "https://caldav.yandex.ru/";
        private const string CardDavUrl = "https://carddav.yandex.ru/";

        private readonly LoginController _loginController;
        private readonly string _dataFolderPath;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Scheduler _scheduler;
        private readonly SyncConfigController _syncConfig;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly InvitesInfoStorage _invitesInfo;

        private Task<List<SyncTargetInfo>> _syncTargetsTask;
        private DateTime _syncStartTime;
        private List<SyncTargetInfo> _cachedSyncTargets;
        private Dictionary<Guid,string> _ctags;

        public string UserEmail { get; private set; }
        public SyncStatus Status { get; set; }

        public SyncManager(Outlook.Application application, IHttpClientFactory httpClientFactory,
            LoginController loginController, ProxyOptionsProvider proxyOptionsProvider, string dataFolderPath,
            InvitesInfoStorage invitesInfo)
        {
            _httpClientFactory = httpClientFactory;
            _dataFolderPath = dataFolderPath;

            _ctags = new Dictionary<Guid,string>();
            Status = new SyncStatus();

            _scheduler = new Scheduler(application.Session, httpClientFactory, dataFolderPath, Status, invitesInfo);

            _syncConfig = new SyncConfigController(dataFolderPath);

            _invitesInfo = invitesInfo;
            _loginController = loginController;
            _loginController.LoginStateChanged += LoginController_LoginStateChanged;

            _timer = new System.Windows.Forms.Timer();
            _timer.Tick += Timer_Tick;

            proxyOptionsProvider.ProxyOptionsChanged += OnProxyOptionsChanged;

            _ = UpdateSyncTargetsAsync(false);
        }

        public void Launch()
        {
            if (_loginController.IsUserLoggedIn)
            {
                if (AppConfig.IsAutoSyncEnabled)
                {
                    _timer.Interval = (int) TimeSpan.FromSeconds(5).TotalMilliseconds;
                    _timer.Start();
                }
                else
                {
                    s_logger.Warn("Auto-sync is disabled");
                }
            }
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            await OnAutoSync();
        }

        private async Task OnAutoSync()
        {
            _timer.Stop();
            if (Status.State != SyncState.Running)
            {
                await RunSynchronization();
            }
            ThisAddIn.UiContext.Post(x => 
            {
                _timer.Interval = (int) TimeSpan.FromMinutes(1).TotalMilliseconds;
                _timer.Start();
            },
            null);
        }

        public void Dispose()
        {
            _invitesInfo.Save();
            _timer?.Dispose();
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

        public void ApplySyncConfig(List<SyncTargetInfo> syncTargets)
        {
            UserEmail = _loginController.UserInfo.Email;
            var userCommonName = _loginController.UserInfo.RealName;

            _syncConfig.SelectUser(UserEmail);
            _syncConfig.SetConfig(syncTargets.ConvertAll(x => x.Config));

            if (_cachedSyncTargets != null)
            {
                CleanupEntityCaches(_cachedSyncTargets, syncTargets);
            }

            _cachedSyncTargets = new List<SyncTargetInfo>(syncTargets);
            _syncTargetsTask = Task.FromResult(new List<SyncTargetInfo>(_cachedSyncTargets));

            _scheduler.ApplySettings(syncTargets, UserEmail, userCommonName);
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

        public Task<List<SyncTargetInfo>> GetSyncTargets()
        {
            return _syncTargetsTask;
        }

        public async Task SynchronizeNowAsync()
        {
            await RunSynchronization(true);
        }

        public async Task RunSynchronization(bool manuallyTriggered = false)
        {
            bool isBlankShot = false;
            try
            {
                OnSyncStarted();

                ThisAddIn.RestoreUiContext();
                await UpdateSyncTargetsAsync(manuallyTriggered);
                isBlankShot = await _scheduler.RunSynchronization(manuallyTriggered, _ctags) == false;
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
            finally
            {
                OnSyncFinished(isBlankShot);
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

        private void OnSyncFinished(bool isBlankShot)
        {
            try
            {
                Status.OnSynchronizationFinished();

                if (!isBlankShot)
                {
                    var duration = DateTime.UtcNow - _syncStartTime;
                    Telemetry.Signal(Telemetry.SyncReportsEvents, "sync_complete");

                    s_logger.Info($"Sync complete. Duration: {duration}");

                    Status.SendReportsTelemetry(_syncTargetsTask.Result);

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
            if (e.IsUserLoggedIn)
            {
                if (AppConfig.IsAutoSyncEnabled)
                {
                    s_logger.Info("Sync triggered by user log-in");
                    _ = OnAutoSync();
                }
                else
                {
                    s_logger.Warn("Auto-sync is disabled");
                }
            }
            else
            {
                _timer.Stop();
                _scheduler.ClearSettings();
                Status.Reset();
            }
        }

        private async Task UpdateSyncTargetsAsync(bool manuallyTriggered)
        {
            if (!_loginController.IsUserLoggedIn)
            {
                return;
            }

            UserEmail = _loginController.UserInfo.Email;
            _syncConfig.SelectUser(UserEmail);

            var task = Task.Run(async () =>
            {
                var result = new List<SyncTargetInfo>();

                var webDavClient = _httpClientFactory.CreateWebDavClient(new CancellationTokenSource());
                var calDavTargets = await GetCalDavResources(webDavClient);
                var cardDavTargets = await GetCardDavResources(webDavClient);

                result.AddRange(calDavTargets);
                result.AddRange(cardDavTargets);

                foreach (var item in result)
                {
                    s_logger.Debug($"Sync target: {item.Id} - {item.Name} - {item.Config.Url}");
                }

                // At this point, we are pretty sure that there is no critical error
                // (such as a proxy error, or no internet)
                ThisAddIn.UiContext.Post(_ => Status.SetCriticalError(CriticalError.None), null);

                return result;
            });

            ThisAddIn.RestoreUiContext();
            _syncTargetsTask = task.ContinueWith(t =>
            {
                try
                {
                    _cachedSyncTargets = t.Result;
                    AutoPopulateConfig(_cachedSyncTargets, UserEmail);
                }
                catch (Exception exc)
                {
                    SyncErrorHandler.HandleException(exc, !manuallyTriggered);
                }
                return _cachedSyncTargets;
            }, 
            TaskScheduler.FromCurrentSynchronizationContext());

            await _syncTargetsTask;
        }

        private void AutoPopulateConfig(List<SyncTargetInfo> syncTargets, string userEmail)
        {
            var session = ThisAddIn.Components.OutlookApplication.Session;

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

            ApplySyncConfig(syncTargets);
        }

        private async Task<List<SyncTargetInfo>> GetCalDavResources(IWebDavClient webDavClient)
        {
            var calDavDataProvider = new CalDavResourcesDataAccess(new Uri(CalDavUrl), webDavClient);
            var resources = await calDavDataProvider.GetResources();

            var ctags = new Dictionary<Guid,string>();

            var items = new List<SyncTargetInfo>();
            int calendarsCounter = 0;
            foreach (var calendar in resources.CalendarResources)
            {
                var targetConfig = GetSyncTargetConfig(calendar.Uri);
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
                var targetConfig = GetSyncTargetConfig(new Uri(taskList.Id));
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

            _ctags = ctags;
            return items;
        }

        private async Task<List<SyncTargetInfo>> GetCardDavResources(IWebDavClient webDavClient)
        {
            var calDavDataAccess = new CardDavDataAccess(new Uri(CardDavUrl), webDavClient, string.Empty, contentType => true);
            var resources = await calDavDataAccess.GetUserAddressBooksNoThrow(false);

            var items = new List<SyncTargetInfo>();
            int counter = 0;
            foreach (var addressBook in resources)
            {
                var targetConfig = GetSyncTargetConfig(addressBook.Uri);
                items.Add(new SyncTargetInfo(targetConfig)
                {
                    TargetType = SyncTargetType.Contacts,
                    Name = addressBook.Name,
                    Privileges = addressBook.Privileges,
                    IsPrimary = counter == 0
                });
                counter++;
            }

            ThisAddIn.UiContext.Send(x =>
            {
                foreach (var item in items)
                    item.Name = GetContactsResourceDisplayName(item.Name);
            },
            null);

            return items;
        }

        private SyncTargetConfig GetSyncTargetConfig(Uri url)
        {
            var config = _syncConfig.GetSyncTargetByUrl(url);
            if (config == null)
            {
                config = new SyncTargetConfig
                {
                    Id = Guid.NewGuid(),
                    Url = url.ToString(),
                    Active = true
                };
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
    }
}
