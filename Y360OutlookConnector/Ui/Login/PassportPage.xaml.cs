using System;
using System.Collections.Specialized;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using log4net;

namespace Y360OutlookConnector.Ui.Login
{
    /// <summary>
    /// Interaction logic for PassportPage.xaml
    /// </summary>
    public partial class PassportPage
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public class ConfirmationCodeReceivedArgs : EventArgs
        {
            public string Code { get; set; }
        }

        public class ExternalAuthNeededArgs : EventArgs
        {
            public Uri Url { get; set; }
        }

        public class WebViewFailureArgs : EventArgs
        {
            public Uri Url { get; set; }
        }

        private readonly LogonSession _logonSession;

        private bool _hasNavigateError = false;

        public event EventHandler<ConfirmationCodeReceivedArgs> ConfirmationCodeReceived;
        public event EventHandler<ExternalAuthNeededArgs> ExternalAuthNeeded;
        public event EventHandler<WebViewFailureArgs> WebViewFailure;

        public PassportPage(LogonSession logonSession)
        {
            _logonSession = logonSession;

            InitializeComponent();

            SetSuppressCookie(true);
            Unloaded += PassportPage_Unloaded;
            Loaded += PassportPage_Loaded;
        }

        private void PassportPage_Loaded(object sender, RoutedEventArgs args)
        {
            webBrowser.IsWebBrowserContextMenuEnabled = false;
            webBrowser.ScriptErrorsSuppressed = true;
            webBrowser.AllowWebBrowserDrop = false;

            webBrowser.Navigating += WebBrowser_Navigating;
            webBrowser.Navigated += WebBrowser_Navigated;
            webBrowser.DocumentCompleted += WebBrowser_DocumentCompleted;
            webBrowser.NavigateError += WebBrowser_NavigateError;
            webBrowser.NewWindow += (o, e) => { e.Cancel = true; };

            var passportUrl = _logonSession.GetPassportUrl();
            webBrowser.Navigate(passportUrl);
            webBrowser.Focus();
        }

        private static void PassportPage_Unloaded(object sender, RoutedEventArgs e)
        {
            SetSuppressCookie(false);
        }

        private void WebBrowser_DocumentCompleted(object sender, System.Windows.Forms.WebBrowserDocumentCompletedEventArgs e)
        {
            if (IsUrlLike(e.Url, "passport.yandex.*/redirect*")
                || IsUrlLike(e.Url, "oauth.yandex.*/authorize*")
                || _hasNavigateError)
            {
                throbber.Visibility = Visibility.Visible;
                webBrowserHost.Visibility = Visibility.Hidden;
            }
            else
            {
                throbber.Visibility = Visibility.Collapsed;
                webBrowserHost.Visibility = Visibility.Visible;
            }

            webBrowser.Focus();
            _hasNavigateError = false;
        }

        private void WebBrowser_Navigating(object sender, System.Windows.Forms.WebBrowserNavigatingEventArgs e)
        {
            if (IsUrlLike(e.Url, "oauth.yandex.*/verification_code*"))
            {
                var queryParams = ParseQuery(e.Url.Query);
                var code = queryParams.Get("code") ?? "";

                ConfirmationCodeReceived?.Invoke(null, new ConfirmationCodeReceivedArgs { Code = code });

                e.Cancel = true;
            }
            else if (IsUrlLike(e.Url, "passport.yandex.*/auth/sso*"))
            {
                ExternalAuthNeeded?.Invoke(null, new ExternalAuthNeededArgs { Url = e.Url });

                e.Cancel = true;
            }
        }

        private void WebBrowser_Navigated(object sender, System.Windows.Forms.WebBrowserNavigatedEventArgs e)
        {

        }

        private void WebBrowser_NavigateError(object sender, WebBrowser.NavigateErrorEventArgs e)
        {
            var errorCodeDesc = e.StatusCode < 0 ? $"0x{e.StatusCode:x}" : $"status {e.StatusCode}";
            s_logger.Error($"Navigate error ({errorCodeDesc}): {e.Url}");

            _hasNavigateError = true;

            WebViewFailure?.Invoke(this, new WebViewFailureArgs());
        }

        // Matches a string with a pattern similar to a globe, for example. 'file23.txt 'will match 'file??.*'
        private static bool IsStringLike(string str, string pattern)
        {
            var re = new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return re.IsMatch(str);
        }

        private static bool IsUrlLike(Uri uri, string pattern)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(uri.Authority);
            stringBuilder.Append('/');
            stringBuilder.Append(uri.PathAndQuery);

            return IsStringLike(stringBuilder.ToString(), pattern);
        }

        private NameValueCollection ParseQuery(string str)
        {
            var result = new NameValueCollection();
            string[] parts = str.Split('?', '&');
            foreach (string part in parts)
            {
                string[] pair = part.Split('=');
                string key = (pair.Length > 0) ? pair[0] : part;
                string value = (pair.Length > 1) ? pair[1] : "";
                result.Add(key, value);
            }
            return result;
        }

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption,
            IntPtr lpBuffer, int lpdwBufferLength);

        private static void SetSuppressCookie(bool value)
        {
            const int INTERNET_OPTION_SUPPRESS_BEHAVIOR = 81;
            const int INTERNET_SUPPRESS_COOKIE_PERSIST = 3;
            const int INTERNET_SUPPRESS_COOKIE_PERSIST_RESET = 4;

            int dwOption = INTERNET_OPTION_SUPPRESS_BEHAVIOR;
            int option = value ? INTERNET_SUPPRESS_COOKIE_PERSIST : INTERNET_SUPPRESS_COOKIE_PERSIST_RESET;

            IntPtr optionPtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(optionPtr, option);

            InternetSetOption(IntPtr.Zero, dwOption, optionPtr, sizeof(int));
            Marshal.FreeHGlobal(optionPtr);
        }
    }
}
