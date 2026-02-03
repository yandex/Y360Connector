using System;
using System.Collections.Specialized;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using log4net;
using Y360OutlookConnector.Ui.WebView;

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

            Loaded += PassportPage_Loaded;
            Unloaded += PassportPage_Unloaded;
        }

        private void PassportPage_Loaded(object sender, RoutedEventArgs args)
        {
            webView.ScriptErrorsSuppressed = true;
            webView.AllowWebBrowserDrop = true;

            webView.Navigating += WebView_Navigating;
            webView.DocumentCompleted += WebView_DocumentCompleted;
            webView.NavigateError += WebView_NavigateError;

            if (webView.IsInitialized)
            {
                NavigateToPassport();
            }
            else
            {
                webView.Initialized += WebView_Initialized;
            }

            webView.Focus();
        }

        private void WebView_Initialized(object sender, EventArgs e)
        {
            webView.Initialized -= WebView_Initialized;
            NavigateToPassport();
        }

        private void NavigateToPassport()
        {
            var passportUrl = _logonSession.GetPassportUrl();
            webView.Navigate(passportUrl);
        }

        private void PassportPage_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                webView.Reset();
            }
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to reset WebView: {ex.Message}");
            }
        }

        private void WebView_DocumentCompleted(object sender, WebViewDocumentCompletedEventArgs e)
        {
            s_logger.Debug($"Document completed: {e.Url.Host}");

            if (IsUrlLike(e.Url, "oauth.yandex.*/verification_code*"))
            {
                var queryParams = ParseQuery(e.Url.Query);
                var code = queryParams.Get("code") ?? "";
                s_logger.Debug($"OAuth verification code received from final URL");

                ConfirmationCodeReceived?.Invoke(null, new ConfirmationCodeReceivedArgs { Code = code });
            }
            else if (IsUrlLike(e.Url, "passport.yandex.*/redirect*")
                || _hasNavigateError)
            {
                throbber.Visibility = Visibility.Visible;
                webView.Visibility = Visibility.Hidden;
            }
            else
            {
                throbber.Visibility = Visibility.Collapsed;
                webView.Visibility = Visibility.Visible;
            }

            webView.Focus();
            _hasNavigateError = false;
        }

        private void WebView_Navigating(object sender, WebViewNavigatingEventArgs e)
        {
            s_logger.Debug($"Navigating: {e.Url.Host}");

            if (IsUrlLike(e.Url, "passport.yandex.*/auth/sso*"))
            {
                ExternalAuthNeeded?.Invoke(null, new ExternalAuthNeededArgs { Url = e.Url });

                e.Cancel = true;
            }
        }

        private void WebView_NavigateError(object sender, WebViewNavigateErrorEventArgs e)
        {
            if (e.StatusCode != -1)
            {
                var errorCodeDesc = e.StatusCode < 0 ? $"0x{e.StatusCode:x}" : $"status {e.StatusCode}";
                s_logger.Error($"Navigate error ({errorCodeDesc}): {e.Url}");
                _hasNavigateError = true;
                WebViewFailure?.Invoke(this, new WebViewFailureArgs());
            }
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
    }
}
