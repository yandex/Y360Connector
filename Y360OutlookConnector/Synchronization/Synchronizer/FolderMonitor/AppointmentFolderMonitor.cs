using System;
using System.Reflection;
using CalDavSynchronizer.ChangeWatching;
using log4net;
using Y360OutlookConnector.Utilities;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization.Synchronizer.FolderMonitor
{
    public class AppointmentFolderMonitor : FolderMonitorBase
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private readonly InvitesInfoStorage _invitesInfo;

        public AppointmentFolderMonitor(Outlook.Folder folder, InvitesInfoStorage invitesInfo)
            : base(folder)
        {
            _invitesInfo = invitesInfo ?? throw new ArgumentNullException(nameof(invitesInfo));
        }

        protected override void HandleItem(object item, ItemAction action)
        {
            try
            {
                bool wasDeleted = action == ItemAction.Remove;

                if (item is Outlook.AppointmentItem appointment)
                {
                    IOutlookId entryId = null;
                    if (appointment.MeetingStatus != Outlook.OlMeetingStatus.olMeetingReceived)
                    {
                        s_logger.Debug($"'{action}': Appointment '{appointment.Subject}' '{appointment.EntryID}' ");
                        entryId = new AppointmentId(new CalDavSynchronizer.Implementation.Events.AppointmentId(
                            appointment.EntryID, appointment.GlobalAppointmentID ?? String.Empty),
                            AppointmentItemUtils.GetLastChangeTime(appointment),
                            wasDeleted);
                    }
                    else if (wasDeleted)
                    {
                        var uid = AppointmentItemUtils.ExtractUidFromGlobalId(appointment.GlobalAppointmentID);
                        if (!String.IsNullOrEmpty(uid))
                            _invitesInfo.OnInviteDeleted(uid);
                    }
                    OnItemChanged(entryId);
                }
            }
            catch
            {
                // no-op
            }
        }
    }
}
