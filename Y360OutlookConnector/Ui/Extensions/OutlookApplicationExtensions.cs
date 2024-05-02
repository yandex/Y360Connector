using Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Ui.Extensions
{
    internal static class OutlookApplicationExtensions
    {
        public static Application GetApplication() => ThisAddIn.Components?.OutlookApplication;

        public static void UpdateStatusLine(this Inspector inspector, string text)
        {
            if (inspector == null)
            {
                return;
            }

            var formRegions = Globals.FormRegions[inspector];

            var formRegion = formRegions.TelemostStatusLineRegion;

            if (formRegion == null)
            {
                return;
            }

            formRegion.SetMessage(text);
        }

        public static bool IsAppointmentValid(this Application outlook, AppointmentItem appointment)
        {
            var inspectors = outlook?.Inspectors;

            if (inspectors == null)
            {
                return false;
            }

            if (appointment == null)
            {
                return false;
            }

            foreach(var item in inspectors)
            {
                var inspector = item as Inspector;
                if (inspector == null)
                {
                    continue;
                }

                if (!(inspector.CurrentItem is AppointmentItem a))
                {
                    continue;
                }
                if (a.GlobalAppointmentID == appointment.GlobalAppointmentID)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
