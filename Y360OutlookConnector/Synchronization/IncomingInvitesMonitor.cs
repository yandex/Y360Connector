using System;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization
{
    public class IncomingInvitesMonitor : IDisposable
    {
        private readonly InvitesInfoStorage _invitesInfo;
        private readonly Outlook.Application _outlookApp;
        private bool _started;

        public IncomingInvitesMonitor(Outlook.Application outlookApp, InvitesInfoStorage storage)
        {
            _outlookApp = outlookApp ?? throw new ArgumentNullException(nameof(outlookApp));
            _invitesInfo = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public void Start()
        {
            Stop();

            _outlookApp.NewMailEx += App_NewMailEx;

            _started = true;
        }

        private void App_NewMailEx(string entryId)
        {
            var item = _outlookApp.Session?.GetItemFromID(entryId);
            if (item is Outlook.MeetingItem meetingItem)
            {
                if (!IsMeetingRequest(meetingItem))
                {
                    return;
                }

                var appointmentItem = meetingItem.GetAssociatedAppointment(false);
                if (appointmentItem?.GlobalAppointmentID != null)
                {
                    _invitesInfo.AddIncomingInvite(appointmentItem.GlobalAppointmentID, 
                        appointmentItem.LastModificationTime.ToUniversalTime());
                }
            }
        }

        private static bool IsMeetingRequest(Outlook.MeetingItem meetingItem)
        {
            try
            {
                return meetingItem.MessageClass != null && meetingItem.MessageClass.StartsWith("IPM.Schedule.Meeting.Request", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // In some cases (e.g. when the item is already deleted) accessing MessageClass may throw an exception, treat such items as non-meeting requests
                return false;
            }
        }

        public void Stop()
        {
            if (_started)
            {
                _outlookApp.NewMailEx -= App_NewMailEx;
            }
            _started = false;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
