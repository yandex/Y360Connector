using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Y360OutlookConnector.Ui.Login
{
    // https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.webbrowser.createsink?view=windowsdesktop-7.0#examples
    public class WebBrowser : System.Windows.Forms.WebBrowser
    {
        private AxHost.ConnectionPointCookie _cookie;
        private WebBrowser2EventHelper _helper;

        public class NavigateErrorEventArgs : EventArgs
        {
            public string Url { get; set; }
            public string Frame { get; set; }
            public int StatusCode{ get; set; }
            public bool Cancel { get; set; }
        }

        public event EventHandler<NavigateErrorEventArgs> NavigateError;

        protected override void CreateSink()
        {
            base.CreateSink();

            _helper = new WebBrowser2EventHelper(this);
            _cookie = new AxHost.ConnectionPointCookie(ActiveXInstance, _helper, typeof(DWebBrowserEvents2));
        }

        protected override void DetachSink()
        {
            _cookie?.Disconnect();
            _cookie = null;

            base.DetachSink();
        }

        protected virtual void OnNavigateError(NavigateErrorEventArgs e)
        {
            NavigateError?.Invoke(this, e);
        }

        private class WebBrowser2EventHelper : StandardOleMarshalObject, DWebBrowserEvents2
        {
            private readonly WebBrowser _parent;

            public WebBrowser2EventHelper(WebBrowser parent)
            {
                _parent = parent;
            }

            public void NavigateError(object dispatch, ref object url,
                ref object frame, ref object statusCode, ref bool cancel)
            {
                _parent.OnNavigateError(new NavigateErrorEventArgs
                {
                    Url = (string) url,
                    Cancel = cancel,
                    Frame = (string) frame,
                    StatusCode = (int) statusCode
                });
            }
        }
    }

    [ComImport, Guid("34A715A0-6587-11D0-924A-0020AFC7AC4D"),
     InterfaceType(ComInterfaceType.InterfaceIsIDispatch),
     TypeLibType(TypeLibTypeFlags.FHidden)]
    public interface DWebBrowserEvents2
    {
        [DispId(271)]
        void NavigateError(
            [In, MarshalAs(UnmanagedType.IDispatch)] object dispatch,
            [In] ref object url, [In] ref object frame,
            [In] ref object statusCode, [In, Out] ref bool cancel);
    }
}
