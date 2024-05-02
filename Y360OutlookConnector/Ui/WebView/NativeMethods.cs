using System;
using System.Runtime.InteropServices;

namespace Y360OutlookConnector.Ui.WebView
{
    using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

    internal class NativeMethods
    {
        [Flags]
        public enum HostUIFlags
        {
            DIALOG = 0x00000001,
            DISABLE_HELP_MENU = 0x00000002,
            NO3DBORDER = 0x00000004,
            SCROLL_NO = 0x00000008,
            DISABLE_SCRIPT_INACTIVE = 0x00000010,
            OPENNEWWIN = 0x00000020,
            DISABLE_OFFSCREEN = 0x00000040,
            FLAT_SCROLLBAR = 0x00000080,
            DIV_BLOCKDEFAULT = 0x00000100,
            ACTIVATE_CLIENTHIT_ONLY = 0x00000200,
            OVERRIDEBEHAVIORFACTORY = 0x00000400,
            CODEPAGELINKEDFONTS = 0x00000800,
            URL_ENCODING_DISABLE_UTF8 = 0x00001000,
            URL_ENCODING_ENABLE_UTF8 = 0x00002000,
            ENABLE_FORMS_AUTOCOMPLETE = 0x00004000,
            ENABLE_INPLACE_NAVIGATION = 0x00010000,
            IME_ENABLE_RECONVERSION = 0x00020000,
            THEME = 0x00040000,
            NOTHEME = 0x00080000,
            NOPICS = 0x00100000,
            NO3DOUTERBORDER = 0x00200000,
            DISABLE_EDIT_NS_FIXUP = 0x00400000,
            LOCAL_MACHINE_ACCESS_CHECK = 0x00800000,
            DISABLE_UNTRUSTEDPROTOCOL = 0x01000000,
            HOST_NAVIGATES = 0x02000000,
            ENABLE_REDIRECT_NOTIFICATION = 0x04000000,
            USE_WINDOWLESS_SELECTCONTROL = 0x08000000,
            USE_WINDOWED_SELECTCONTROL = 0x10000000,
            ENABLE_ACTIVEX_INACTIVATE_MODE = 0x20000000,
            DPI_AWARE = 0x40000000
        }

        [ComImport, Guid("BD3F23C0-D43E-11CF-893B-00AA00BDCE1A"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDocHostUIHandler
        {
            [PreserveSig]
            uint ShowContextMenu(int id, POINT pt,
                [MarshalAs(UnmanagedType.Interface)] object commandTarget,
                [MarshalAs(UnmanagedType.Interface)] object dispatch);

            [PreserveSig]
            uint GetHostInfo(ref DOCHOSTUIINFO info);

            [PreserveSig]
            uint ShowUI(int id,
                [MarshalAs(UnmanagedType.Interface)] object activeObject,
                [MarshalAs(UnmanagedType.Interface)] object commandTarget,
                [MarshalAs(UnmanagedType.Interface)] object frame,
                [MarshalAs(UnmanagedType.Interface)] object doc);

            [PreserveSig]
            uint HideUI();

            [PreserveSig]
            uint UpdateUI();

            [PreserveSig]
            uint EnableModeless(bool enable);

            [PreserveSig]
            uint OnDocWindowActivate(bool activate);

            [PreserveSig]
            uint OnFrameWindowActivate(bool activate);

            [PreserveSig]
            uint ResizeBorder(COMRECT rect, [MarshalAs(UnmanagedType.Interface)] object doc, bool frameWindow);

            [PreserveSig]
            uint TranslateAccelerator(ref System.Windows.Forms.Message msg, ref Guid group, int cmdId);

            [PreserveSig]
            uint GetOptionKeyPath([Out, MarshalAs(UnmanagedType.LPArray)] string[] key, int reserved);

            [PreserveSig]
            uint GetDropTarget(
                [In, MarshalAs(UnmanagedType.Interface)]
                object dropTargetIn,
                [MarshalAs(UnmanagedType.Interface)] out object dropTargetOut);

            [PreserveSig]
            uint GetExternal([MarshalAs(UnmanagedType.IDispatch)] out object dispatch);

            [PreserveSig]
            uint TranslateUrl(int dwTranslate,
                [MarshalAs(UnmanagedType.LPWStr)] string urlIn,
                [MarshalAs(UnmanagedType.LPWStr)] out string urlOut);

            [PreserveSig]
            uint FilterDataObject(IDataObject dataObjectIn, out IDataObject dataObjectOut);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DOCHOSTUIINFO
        {
            public int cbSize;
            public int dwFlags;
            public int dwDoubleClick;
            public IntPtr dwReserved1;
            public IntPtr dwReserved2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COMRECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public class POINT
        {
            public int x;
            public int y;
        }

        [ComImport, Guid("3050F3F0-98B5-11CF-BB82-00AA00BDCE0B"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICustomDoc
        {
            [PreserveSig]
            int SetUIHandler(IDocHostUIHandler uiHandler);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IOleServiceProvider
        {
            [PreserveSig]
            uint QueryService([In] ref Guid guidService, [In] ref Guid riid,
                [MarshalAs(UnmanagedType.IDispatch)] out object ppvObject);
        }

        [ComImport, Guid("34A715A0-6587-11D0-924A-0020AFC7AC4D"),
         InterfaceType(ComInterfaceType.InterfaceIsIDispatch),
         TypeLibType(TypeLibTypeFlags.FHidden)]
        public interface DWebBrowserEvents2
        {
            [PreserveSig, DispId(271)]
            void NavigateError(
                [In, MarshalAs(UnmanagedType.IDispatch)]
                object dispatch,
                [In] ref object url, [In] ref object frame,
                [In] ref object statusCode, [In, Out] ref bool cancel);

            [PreserveSig, DispId(0x111)]
            void NewWindow3(
                [In, Out, MarshalAs(UnmanagedType.IDispatch)]
                ref object dispatch,
                [In, Out] ref bool cancel,
                [In] uint flags,
                [In, MarshalAs(UnmanagedType.BStr)] string urlContext,
                [In, MarshalAs(UnmanagedType.BStr)] string url);
        }

        [DllImport("wininet.dll", SetLastError = true)]
        public static extern bool InternetSetOption(IntPtr hInternet, int dwOption,
            IntPtr lpBuffer, int lpdwBufferLength);
    }
}
