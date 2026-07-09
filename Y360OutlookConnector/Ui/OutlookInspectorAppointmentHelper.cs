using Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Ui
{
    /// <summary>
    /// Доступ к <see cref="AppointmentItem"/> из окна встречи без привязки к VSTO-ленте.
    /// </summary>
    internal static class OutlookInspectorAppointmentHelper
    {
        internal static AppointmentItem TryGetAppointmentItemFromInspector(Inspector inspector)
        {
            if (inspector == null)
            {
                return null;
            }

            return inspector.CurrentItem as AppointmentItem;
        }
    }
}
