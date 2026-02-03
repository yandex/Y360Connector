using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using log4net;
using Microsoft.Web.WebView2.Core;

namespace Y360OutlookConnector.Ui.WebView
{
    public class WebViewDocumentCompletedEventArgs : EventArgs
    {
        public Uri Url;
    }

    public class WebViewNavigateErrorEventArgs : EventArgs
    {
        public string Url;
        public int StatusCode;
    }

    public class WebViewNavigatingEventArgs : EventArgs
    {
        public Uri Url;
        public bool Cancel;
    }

    public partial class WebView2Control : IDisposable
    {
        public event EventHandler<WebViewNavigatingEventArgs> Navigating;
        public event EventHandler<WebViewDocumentCompletedEventArgs> DocumentCompleted;
        public event EventHandler<WebViewNavigateErrorEventArgs> NavigateError;
        public event EventHandler Initialized;

        public bool ScriptErrorsSuppressed { get; set; }
        public bool AllowWebBrowserDrop { get; set; }
        public bool IsInitialized => _isInitialized;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        private bool _isInitialized = false;
        private bool _disposed = false;
        private bool _isInitializing = false;

        public WebView2Control()
        {
            InitializeComponent();
            Loaded += WebView2Control_Loaded;
        }

        private async void WebView2Control_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                await InitializeWebView2();
            }
        }

        private async Task InitializeWebView2()
        {
            if (_isInitialized)
            {
                s_logger.Debug("WebView2 is already initialized, skipping");
                return;
            }

            if (_isInitializing)
            {
                s_logger.Debug("WebView2 initialization is already in progress, skipping");
                return;
            }

            _isInitializing = true;

            try
            {
                var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Yandex", "Y360.OutlookConnector", "WebView2");
                if (!Directory.Exists(userDataFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(userDataFolder);
                        s_logger.Debug($"Created WebView2 data folder");
                    }
                    catch (Exception ex)
                    {
                        s_logger.Error($"Failed to create WebView2 data folder", ex);
                        throw;
                    }
                }

                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await WebView2.EnsureCoreWebView2Async(environment);

                await ConfigureWebView2Settings();

                WebView2.CoreWebView2.NavigationStarting += WebView2_NavigationStarting;
                WebView2.CoreWebView2.NavigationCompleted += WebView2_NavigationCompleted;
                WebView2.CoreWebView2.NewWindowRequested += WebView2_NewWindowRequested;

                _isInitialized = true;
                s_logger.Debug("WebView2 initialized successfully");
                Initialized?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                s_logger.Error("Failed to initialize WebView2", ex);
                _isInitialized = false;

                NavigateError?.Invoke(this, new WebViewNavigateErrorEventArgs
                {
                    Url = "",
                    StatusCode = -1
                });
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void WebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            s_logger.Debug($"WebView2 Navigation starting: {new Uri(e.Uri).Host}");
            var args = new WebViewNavigatingEventArgs { Url = new Uri(e.Uri) };
            Navigating?.Invoke(this, args);

            if (args.Cancel)
            {
                e.Cancel = true;
            }
        }

        private void WebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var currentUrl = WebView2.CoreWebView2.Source;
            s_logger.Debug($"WebView2 Navigation completed: {new Uri(currentUrl).Host}, Success: {e.IsSuccess}");

            if (e.IsSuccess)
            {
                DocumentCompleted?.Invoke(this, new WebViewDocumentCompletedEventArgs { Url = new Uri(currentUrl) });
            }
            else
            {
                var statusCode = MapWebErrorStatusToStatusCode(e.WebErrorStatus);

                if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    s_logger.Error($"WebView2 Navigation error: {new Uri(currentUrl).Host}, status code: {statusCode}");
                }

                OnNavigateError(currentUrl, statusCode);
            }
        }

        private void WebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            OnNewWindow(e.Uri);
            e.Handled = true;
        }

        public void Navigate(Uri source)
        {
            if (!_isInitialized)
            {
                s_logger.Warn("Navigate called before WebView2 initialization completed");
                return;
            }

            WebView2.CoreWebView2.Navigate(source.AbsoluteUri);
        }

        public new void Focus()
        {
            WebView2.Focus();
        }

        private void OnNavigateError(string url, int statusCode)
        {
            NavigateError?.Invoke(this, new WebViewNavigateErrorEventArgs { Url = url, StatusCode = statusCode });
        }

        private void OnNewWindow(string url)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }

        private async Task ConfigureWebView2Settings()
        {
            try
            {
                await ClearAllCookies();
                s_logger.Debug("WebView2 settings configured successfully");
            }
            catch (Exception ex)
            {
                s_logger.Error("Failed to configure WebView2 settings", ex);
            }
        }

        private async Task ClearAllCookies()
        {
            try
            {
                if (_isInitialized && WebView2?.CoreWebView2 != null)
                {
                    var cookies = await WebView2.CoreWebView2.CookieManager.GetCookiesAsync("");
                    foreach (var cookie in cookies)
                    {
                        WebView2.CoreWebView2.CookieManager.DeleteCookie(cookie);
                    }
                    s_logger.Debug("WebView2 cookies cleared successfully");
                }
            }
            catch (Exception ex)
            {
                s_logger.Error("Failed to clear WebView2 cookies", ex);
            }
        }

        private static int MapWebErrorStatusToStatusCode(CoreWebView2WebErrorStatus webErrorStatus)
        {
            if (webErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
            {
                return -1;
            }

            return (int)webErrorStatus;
        }

        public void Reset()
        {
            if (_isInitialized && WebView2?.CoreWebView2 != null)
            {
                try
                {
                    _ = ClearAllCookies();

                    WebView2.CoreWebView2.NavigationStarting -= WebView2_NavigationStarting;
                    WebView2.CoreWebView2.NavigationCompleted -= WebView2_NavigationCompleted;
                    WebView2.CoreWebView2.NewWindowRequested -= WebView2_NewWindowRequested;
                }
                catch (Exception ex)
                {
                    s_logger.Error($"Error during WebView2 reset: {ex.Message}");
                }
            }

            _isInitialized = false;
            _isInitializing = false;
            s_logger.Debug("WebView2 has been reset");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    if (_isInitialized && WebView2?.CoreWebView2 != null)
                    {
                        _ = ClearAllCookies();

                        WebView2.CoreWebView2.NavigationStarting -= WebView2_NavigationStarting;
                        WebView2.CoreWebView2.NavigationCompleted -= WebView2_NavigationCompleted;
                        WebView2.CoreWebView2.NewWindowRequested -= WebView2_NewWindowRequested;
                    }
                }
                catch (Exception ex)
                {
                    s_logger.Error($"Error during WebView2 disposal: {ex.Message}");
                }
                finally
                {
                    _isInitialized = false;
                    _isInitializing = false;
                    _disposed = true;
                }
            }
        }
    }
}
