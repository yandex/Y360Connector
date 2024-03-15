using CalDavSynchronizer.Utilities;
using log4net;
using System;
using System.Reflection;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Y360OutlookConnector.Clients;

namespace Y360OutlookConnector.Ui.Login
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow
    {
        private readonly HttpClientFactory _httpClientFactory;

        private LogonSession _logonSession;
        private ConfirmationCodePage _confirmationCodePage;
        private ErrorPage _errorPage;
        private bool _isAuthComplete;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public SecureString AccessToken { get; private set; }
        public LoginInfo UserInfo { get; private set; }

        public LoginWindow(HttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;

            InitializeComponent();

            Loaded += LoginWindow_Loaded;
            IsVisibleChanged += LoginWindow_IsVisibleChanged;
            Closed += LoginWindow_Closed;
        }

        public bool? ShowDialog(object parentWindow)
        {
            WindowInteropHelper windowInteropHelper;
            var owner = OutlookWin32Window.GetHandle(parentWindow);
            if (owner != IntPtr.Zero)
            {
                windowInteropHelper = new WindowInteropHelper(this);
                windowInteropHelper.Owner = owner;
            }

            return ShowDialog();
        }

        private void LoginWindow_Loaded(object sender, RoutedEventArgs args)
        {
            StartLogin();
        }

        private void LoginWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
                Telemetry.Signal(Telemetry.LoginWindowEvents, "shown");
        }

        private void LoginWindow_Closed(object sender, EventArgs e)
        {
            if (_isAuthComplete)
            {
                Telemetry.Signal(Telemetry.LoginWindowEvents, "auth_complete");
                s_logger.Info("Login complete");
            }
            else
            {
                Telemetry.Signal(Telemetry.LoginWindowEvents, "canceled");
                s_logger.Info("Login canceled");
            }
        }

        private async void PassportPage_ExternalAuthNeededAsync(object sender, PassportPage.ExternalAuthNeededArgs e)
        {
            Telemetry.Signal(Telemetry.LoginWindowEvents, "external_auth", "sso");
            await OpenInExternalBrowser(e.Url);
        }

        private async void PassportPage_ConfirmationCodeReceived(object sender, PassportPage.ConfirmationCodeReceivedArgs e)
        {
            ShowThrobber(true);
            await HandleConfirmationCodeAsync(e.Code, false);

        }

        private async void ConfirmationCodePage_CodeEnteredAsync(object sender, ConfirmationCodePage.CodeEnteredArgs e)
        {
            await HandleConfirmationCodeAsync(e.Code, true);
        }

        private void PassportPage_WebViewFailure(object sender, PassportPage.WebViewFailureArgs e)
        {
            Telemetry.Signal(Telemetry.LoginWindowEvents, "webview_failure");
            ShowErrorPage();
        }

        private void StartLogin()
        {
            _logonSession = new LogonSession(_httpClientFactory.CreateHttpClient());
            var passportPage = new PassportPage(_logonSession);

            passportPage.ExternalAuthNeeded += PassportPage_ExternalAuthNeededAsync;
            passportPage.ConfirmationCodeReceived += PassportPage_ConfirmationCodeReceived;
            passportPage.WebViewFailure += PassportPage_WebViewFailure;

            CurrentPage.Content = passportPage;
        }

        private async void StartAlterLogin()
        {
            ShowThrobber(true);
            Telemetry.Signal(Telemetry.LoginWindowEvents, "external_auth", "fallback");
            _logonSession = new LogonSession(_httpClientFactory.CreateHttpClient());
            await OpenInExternalBrowser(_logonSession.GetOAuthUrl());
        }

        private async Task OpenInExternalBrowser(Uri url)
        {
            ShowThrobber(true);
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url.ToString(),
                UseShellExecute = true
            };
            var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null && process.WaitForInputIdle(4000))
            {
                // The process has started. Let's give a little more time 
                // so that its main window has time to appear
                await Task.Delay(1000);
            }

            _confirmationCodePage = new ConfirmationCodePage();
            _confirmationCodePage.CodeEntered += ConfirmationCodePage_CodeEnteredAsync;
            CurrentPage.Content = _confirmationCodePage;

            ShowThrobber(false);
        }

        private void ShowThrobber(bool value)
        {
            CurrentPage.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            Throbber.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowErrorPage()
        {
            _errorPage = new ErrorPage();
            _errorPage.RetryClicked += (o, e) => { StartLogin(); };
            _errorPage.AnotherWayClicked += (o, e) => { StartAlterLogin(); };

            CurrentPage.Content = _errorPage;
            ShowThrobber(false);
        }

        private async Task HandleConfirmationCodeAsync(string confirmationCode, bool isExternalAuth)
        {
            try
            {
                var accessToken = await _logonSession.RequestTokenAsync(confirmationCode);
                if (String.IsNullOrEmpty(accessToken) && _confirmationCodePage != null)
                {
                    _confirmationCodePage.IsAlarmed = true;
                    Telemetry.Signal(Telemetry.LoginWindowEvents, "seven_digits_code_rejected");
                    return;
                }

                if (isExternalAuth)
                {
                    Telemetry.Signal(Telemetry.LoginWindowEvents, "seven_digits_code_accepted");
                }

                ThisAddIn.RestoreUiContext();
                UserInfo = await _logonSession.QueryLoginInfoAsync(accessToken);
                AccessToken = SecureStringUtility.ToSecureString(accessToken);

                _isAuthComplete = true;
                Close();
            }
            catch (Exception exc)
            {
                s_logger.Error("Logon failure:", exc);
                ShowErrorPage();
            }
        }
    }
}
