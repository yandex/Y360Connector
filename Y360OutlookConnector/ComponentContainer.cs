using System;
using Y360OutlookConnector.Synchronization;
using Y360OutlookConnector.Configuration;
using log4net;
using Y360OutlookConnector.Clients;
using Y360OutlookConnector.Ui;
using Y360OutlookConnector.Ui.Login;
using Outlook = Microsoft.Office.Interop.Outlook;
using Y360OutlookConnector.Clients.Telemost;

namespace Y360OutlookConnector
{
    public class ComponentContainer : IDisposable
    {
        private static readonly ILog s_logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType);

        public AutoUpdateManager AutoUpdateManager { get; private set; }
        public LoginController LoginController { get; }
        public TaskPaneController PaneController { get; }
        public ProxyOptionsProvider ProxyOptionsProvider { get; }
        public Outlook.Application OutlookApplication { get; }
        public SyncStatus SyncStatus { get => _syncManager.Status; }

        public TelemostClient TelemostClient { get; }

        private readonly SyncManager _syncManager;
        private readonly HttpClientFactory _httpClientFactory;
        private readonly IncomingInvitesMonitor _invitesMonitor;

        public ComponentContainer(Outlook.Application application)
        {
            OutlookApplication = application;

            var profileDataFolderPath = DataFolder.GetPathForProfile(application.Session.CurrentProfileName);

            LoginController = new LoginController(profileDataFolderPath);
            LoginController.LoginStateChanged += LoginController_LoginStateChanged;

            PaneController = new TaskPaneController();

            ProxyOptionsProvider = new ProxyOptionsProvider(profileDataFolderPath);
            _httpClientFactory = new HttpClientFactory(ProxyOptionsProvider);
            UpdateHttpClientFactory();

            AutoUpdateManager = new AutoUpdateManager(ProxyOptionsProvider, application);
            AutoUpdateManager.UpdateStateChanged += AutoUpdateManager_UpdateStateChanged;
            AutoUpdateManager.Launch();

            TelemostClient = new TelemostClient(_httpClientFactory);

            var invitesInfo = new InvitesInfoStorage(profileDataFolderPath);
            _invitesMonitor = new IncomingInvitesMonitor(application, invitesInfo);

            _syncManager = new SyncManager(OutlookApplication, _httpClientFactory,
                LoginController, ProxyOptionsProvider, profileDataFolderPath, invitesInfo);

            _invitesMonitor.Start();
            _syncManager.Launch();
        }

        private async void LoginController_LoginStateChanged(object sender, LoginStateEventArgs e)
        {
            UpdateHttpClientFactory();

            await PaneController.OnLoginStateChangedAsync(e.IsUserLoggedIn);
        }

        private void AutoUpdateManager_UpdateStateChanged(object sender, EventArgs e)
        {
            ThisAddIn.UiContext.Send(_ => ShowAutoUpdateWindow(), null);
        }

        private void ShowAutoUpdateWindow()
        {
            if (AutoUpdateManager.State == AutoUpdateManager.UpdateState.WaitingForRestart)
            {
                AutoUpdateWindow.ShowOrActivate(() => AutoUpdateManager.RestartOutlook());
            }
        }

        public void StartLogin()
        {
            try
            {
                s_logger.Info("Login started");

                var loginWindow = new LoginWindow(_httpClientFactory);
                loginWindow.ShowDialog(OutlookApplication.ActiveWindow());

                if (loginWindow.UserInfo == null) return;

                var userInfo = new UserInfo
                {
                    AccessToken = loginWindow.AccessToken,
                    UserName = loginWindow.UserInfo.UserName,
                    Email = loginWindow.UserInfo.DefaultEmail,
                    RealName = loginWindow.UserInfo.RealName,
                    DefaultAvatarId = loginWindow.UserInfo.IsAvatarEmpty ? "" : loginWindow.UserInfo.DefaultAvatarId
                };

                ThisAddIn.RestoreUiContext();
                LoginController.OnUserLogin(userInfo);
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        public void ShowSyncConfigWindow()
        {
            s_logger.Info("Show sync config window");

            SyncConfigWindow.ShowOrActivate(OutlookApplication, _syncManager);
        }

        public void ShowAboutWindow()
        {
            s_logger.Info("Show about window");

            var aboutWindow = new AboutWindow();
            aboutWindow.ShowDialog(OutlookApplication.ActiveWindow());
        }

        private void UpdateHttpClientFactory()
        {
            var accessToken = LoginController.IsUserLoggedIn ? LoginController.UserInfo.AccessToken : null;
            _httpClientFactory.SetAccessToken(accessToken);
        }

        public async void SynchronizeNowAsync()
        {
            await _syncManager.SynchronizeNowAsync();
        }

        public void Dispose()
        {
            _invitesMonitor.Dispose();
            _syncManager.Dispose();
            AutoUpdateManager.Dispose();
            PaneController.Dispose();
            Telemetry.Shutdown();

            s_logger.Info("ComponentContainer disposed");
        }
    }
}
