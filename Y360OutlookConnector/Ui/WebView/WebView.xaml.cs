using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using log4net;

namespace Y360OutlookConnector.Ui.WebView
{
    using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
    using ConnectionPointCookie = System.Windows.Forms.AxHost.ConnectionPointCookie;

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

    /// <summary>
    /// Interaction logic for WebView.xaml
    /// </summary>
    public partial class WebView
    {
        public event EventHandler<WebViewNavigatingEventArgs> Navigating;
        public event EventHandler<WebViewDocumentCompletedEventArgs> DocumentCompleted;
        public event EventHandler<WebViewNavigateErrorEventArgs> NavigateError;

        public bool ScriptErrorsSuppressed { get; set; }
        public bool AllowWebBrowserDrop { get; set; }

        private readonly WebBrowserHostUiHandler _hostUiHandler;
        private ConnectionPointCookie _connectionCookie;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public WebView()
        {
            SetSuppressCookie(true);
            InitializeComponent();

            _hostUiHandler = new WebBrowserHostUiHandler(WebBrowser);
            WebBrowser.Unloaded += WebBrowser_Unloaded;
            WebBrowser.LoadCompleted += WebBrowser_LoadCompleted;
            WebBrowser.Navigating += WebBrowser_Navigating;
        }

        private void WebBrowser_Unloaded(object sender, RoutedEventArgs e)
        {
            _connectionCookie?.Disconnect();
            _connectionCookie = null;

            SetSuppressCookie(false);
        }

        public void Navigate(Uri source)
        {
            WebBrowser.Navigate(source);
        }

        private void WebBrowser_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            ConfigureActiveXInstance();

            var eventArgs = new WebViewNavigatingEventArgs{ Url = e.Uri, Cancel = false };
            Navigating?.Invoke(this, eventArgs);
            e.Cancel = eventArgs.Cancel;
        }

        private void WebBrowser_LoadCompleted(object sender, NavigationEventArgs e)
        {
            if (WebBrowser?.Document is NativeMethods.ICustomDoc customDoc)
                customDoc.SetUIHandler(_hostUiHandler);
            else
                s_logger.Warn("Couldn't retrieve ICustomDoc from WebBrowser document");

            DocumentCompleted?.Invoke(this, new WebViewDocumentCompletedEventArgs{ Url = e.Uri });
        }

        private void ConfigureActiveXInstance()
        {
            dynamic activeX = WebBrowser.GetType().InvokeMember("ActiveXInstance",
                BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                null, WebBrowser, new object[] { });

            if (activeX != null)
            {
                activeX.Silent = ScriptErrorsSuppressed;
                activeX.RegisterAsDropTarget = AllowWebBrowserDrop;

                if (_connectionCookie == null)
                {
                    var helper = new WebBrowser2Event(this);
                    _connectionCookie = new ConnectionPointCookie(activeX, helper, 
                        typeof(NativeMethods.DWebBrowserEvents2));
                }
            }
            else
            {
                s_logger.Warn("ActiveXInstance is null");
            }
        }

        private void OnNavigateError(string url, int statusCode)
        {
            var errorCodeDesc = statusCode < 0 ? $"0x{statusCode:x}" : $"status {statusCode}";
            s_logger.Error($"Navigate error ({errorCodeDesc}): {SanitizeUrl(url)}");

            NavigateError?.Invoke(this, new WebViewNavigateErrorEventArgs { StatusCode = statusCode, Url = url });
        }

        private void OnNewWindow(string url, ref bool cancel)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);
            cancel = true;
        }

        private static void SetSuppressCookie(bool value)
        {
            const int INTERNET_OPTION_SUPPRESS_BEHAVIOR = 81;
            const int INTERNET_SUPPRESS_COOKIE_PERSIST = 3;
            const int INTERNET_SUPPRESS_COOKIE_PERSIST_RESET = 4;

            int dwOption = INTERNET_OPTION_SUPPRESS_BEHAVIOR;
            int option = value ? INTERNET_SUPPRESS_COOKIE_PERSIST : INTERNET_SUPPRESS_COOKIE_PERSIST_RESET;

            var optionPtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(optionPtr, option);

            NativeMethods.InternetSetOption(IntPtr.Zero, dwOption, optionPtr, sizeof(int));
            Marshal.FreeHGlobal(optionPtr);
        }

        private static string SanitizeUrl(string str)
        {
            if (Uri.TryCreate(str, UriKind.Absolute, out var url))
                return $"{url.Scheme}{Uri.SchemeDelimiter}{url.Authority}{url.AbsolutePath}";
            return str;
        }

        #region Native methods helpers

        internal class WebBrowser2Event : StandardOleMarshalObject, NativeMethods.DWebBrowserEvents2
        {
            private readonly WebView _parent;

            public WebBrowser2Event(WebView parent)
            {
                _parent = parent;
            }

            public void NavigateError([In, MarshalAs(UnmanagedType.IDispatch)] object dispatch, [In] ref object url,
                [In] ref object frame, [In] ref object statusCode, [In, Out] ref bool cancel)
            {
                _parent?.OnNavigateError((string) url, (int) statusCode);
            }

            public void NewWindow3([In, MarshalAs(UnmanagedType.IDispatch), Out] ref object dispatch,
                [In, Out] ref bool cancel, [In] uint flags, [In, MarshalAs(UnmanagedType.BStr)] string urlContext,
                [In, MarshalAs(UnmanagedType.BStr)] string url)
            {
                _parent?.OnNewWindow(url, ref cancel);
            }
        }

        class WebBrowserHostUiHandler : NativeMethods.IDocHostUIHandler
        {
            private const uint E_NOTIMPL = 0x80004001;
            private const uint S_OK = 0;
            private const uint S_FALSE = 1;

            private WebBrowser _webBrowser;

            public WebBrowserHostUiHandler(WebBrowser webBrowser)
            {
                _webBrowser = webBrowser;
            }

            public uint ShowContextMenu(int menuId, NativeMethods.POINT pt, object commandTarget, object dispatch)
            {
                const int CONTEXT_MENU_CONTROL = 0x2;
                const int CONTEXT_MENU_TEXTSELECT = 0x4;
                const int CONTEXT_MENU_ANCHOR = 0x5;

                bool allowContextMenu = false;
                switch (menuId)
                {
                    case CONTEXT_MENU_CONTROL:
                    case CONTEXT_MENU_TEXTSELECT:
                    case CONTEXT_MENU_ANCHOR:
                        allowContextMenu = true;
                        break;
                }

                return allowContextMenu ? S_FALSE : S_OK;
            }

            public uint GetHostInfo(ref NativeMethods.DOCHOSTUIINFO info)
            {
                info.dwFlags = (int)NativeMethods.HostUIFlags.ENABLE_REDIRECT_NOTIFICATION 
                    | (int)NativeMethods.HostUIFlags.DISABLE_HELP_MENU
                    | (int)NativeMethods.HostUIFlags.THEME
                    | (int)NativeMethods.HostUIFlags.NO3DBORDER;

                info.dwDoubleClick = 0;
                return S_OK;
            }

            public uint ShowUI(int id, object activeObject, object commandTarget, object frame, object doc) => E_NOTIMPL;
            public uint HideUI() => E_NOTIMPL;
            public uint UpdateUI() => E_NOTIMPL;
            public uint EnableModeless(bool enable) => E_NOTIMPL;
            public uint OnDocWindowActivate(bool activate) => E_NOTIMPL;
            public uint OnFrameWindowActivate(bool activate) => E_NOTIMPL;
            public uint ResizeBorder(NativeMethods.COMRECT rect, object doc, bool frameWindow) => E_NOTIMPL;
            public uint TranslateAccelerator(ref System.Windows.Forms.Message msg, ref Guid group, int cmdId) => E_NOTIMPL;
            public uint GetOptionKeyPath(string[] key, int reserved) => E_NOTIMPL;

            public uint GetDropTarget(object dropTargetIn, out object dropTargetOut)
            {
                dropTargetOut = null;
                return E_NOTIMPL;
            }

            public uint GetExternal(out object dispatch)
            {
                dispatch = _webBrowser.ObjectForScripting;
                return S_OK;
            }

            public uint TranslateUrl(int dwTranslate, string urlIn, out string urlOut)
            {
                urlOut = null;
                return E_NOTIMPL;
            }

            public uint FilterDataObject(IDataObject dataObjectIn, out IDataObject dataObjectOut)
            {
                dataObjectOut = null;
                return E_NOTIMPL;
            }
        }

        #endregion
    }
}
