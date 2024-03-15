using System;
using System.Runtime.InteropServices;

namespace Y360OutlookConnector.Ui
{
    public class OutlookWin32Window
    {
        [DllImport("user32")]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        public static IntPtr GetHandle(object windowObject)
        {
            if (windowObject == null)
                return IntPtr.Zero;

            var caption = windowObject.GetType().InvokeMember("Caption",
                System.Reflection.BindingFlags.GetProperty, null, windowObject, null).ToString();
            return FindWindow("rctrl_renwnd32\0", caption);
        }
    }
}
