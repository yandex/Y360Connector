using System;
using System.Drawing;
using System.Runtime.InteropServices;
using stdole;

namespace Y360OutlookConnector.Ui.RibbonXml
{
    /// <summary>Кэш <see cref="IPictureDisp"/> для колбэков Ribbon <c>getImage</c>.</summary>
    internal static class RibbonXmlImageCache
    {
        private static readonly object Gate = new object();

        internal static IPictureDisp PicLogin;
        internal static IPictureDisp PicSyncNow;
        internal static IPictureDisp PicToolsOk;
        internal static IPictureDisp PicToolsErr;
        internal static IPictureDisp PicSettings;
        internal static IPictureDisp PicAbout;
        internal static IPictureDisp PicHelp;
        internal static IPictureDisp PicYandexCalendar;
        internal static IPictureDisp PicTelemost2022;
        internal static IPictureDisp PicTelemostInternal;
        internal static IPictureDisp PicTelemostExternal;
        internal static IPictureDisp PicTelemostSettings;
        internal static IPictureDisp PicEdit;
        internal static IPictureDisp PicCalendar;

        internal static IPictureDisp GetOrCreateCachedImage(ref IPictureDisp cache, Bitmap source)
        {
            if (cache != null)
            {
                return cache;
            }

            if (source == null)
            {
                return null;
            }

            lock (Gate)
            {
                if (cache != null)
                {
                    return cache;
                }

                using (Bitmap copy = new Bitmap(source))
                {
                    cache = RibbonOlePicture.FromBitmap(copy);
                }

                return cache;
            }
        }

        /// <summary>
        /// Bitmap → <see cref="IPictureDisp"/> для Ribbon <c>getImage</c>.
        /// </summary>
        private static class RibbonOlePicture
        {
            private const int OlePictureTypeBitmap = 1;

            [StructLayout(LayoutKind.Sequential)]
            private struct OlePictureDescBitmap
            {
                internal readonly int StructSize;
                internal readonly int PicType;
                internal readonly IntPtr HBitmap;
                internal readonly IntPtr HPalette;

                internal OlePictureDescBitmap(IntPtr hBitmap)
                {
                    PicType = OlePictureTypeBitmap;
                    HBitmap = hBitmap;
                    HPalette = IntPtr.Zero;
                    StructSize = Marshal.SizeOf(typeof(OlePictureDescBitmap));
                }
            }

            [DllImport("OleAut32.dll", PreserveSig = false)]
            [return: MarshalAs(UnmanagedType.Interface)]
            private static extern object OleCreatePictureIndirect(
                ref OlePictureDescBitmap pictDesc,
                [In] ref Guid refiid,
                [MarshalAs(UnmanagedType.Bool)] bool fOwn);

            [DllImport("gdi32.dll")]
            private static extern bool DeleteObject(IntPtr hObject);

            internal static IPictureDisp FromBitmap(Bitmap bitmap)
            {
                if (bitmap == null)
                {
                    throw new ArgumentNullException(nameof(bitmap));
                }

                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    var desc = new OlePictureDescBitmap(hBitmap);
                    Guid iid = typeof(IPictureDisp).GUID;
                    return (IPictureDisp)OleCreatePictureIndirect(ref desc, ref iid, true);
                }
                catch
                {
                    DeleteObject(hBitmap);
                    throw;
                }
            }
        }
    }
}
