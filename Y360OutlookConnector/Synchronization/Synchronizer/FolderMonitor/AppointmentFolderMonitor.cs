using System;
using System.Reflection;
using CalDavSynchronizer.ChangeWatching;
using CalDavSynchronizer.Implementation.ComWrappers;
using log4net;
using Microsoft.Office.Interop.Outlook;
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
                    var isIncomingInvite = !string.IsNullOrEmpty(appointment.GlobalAppointmentID) &&
                                            _invitesInfo.IsIncomingInvite(appointment.GlobalAppointmentID);
                    if (appointment.MeetingStatus == OlMeetingStatus.olMeetingReceived || isIncomingInvite)
                    {
                        if (appointment.MeetingStatus == OlMeetingStatus.olMeetingReceived &&
                            appointment.ResponseStatus == OlResponseStatus.olResponseAccepted &&
                            appointment.BusyStatus == OlBusyStatus.olTentative)
                        {
                            try
                            {
                                s_logger.Info(
                                    $"Adjusting BusyStatus for accepted invite '{appointment.EntryID}': " +
                                    $"{appointment.BusyStatus} -> {OlBusyStatus.olBusy}.");
                                appointment.BusyStatus = OlBusyStatus.olBusy;
                                appointment.Save();
                            }
                            catch (System.Exception ex)
                            {
                                s_logger.Warn("Failed to adjust BusyStatus for accepted invite.", ex);
                            }
                        }
                    }
                    if (appointment.MeetingStatus != Outlook.OlMeetingStatus.olMeetingReceived)
                    {
                        s_logger.Debug($"'{action}': Appointment '{appointment.Subject}' '{appointment.EntryID}' ");
                        entryId = new AppointmentId(new CalDavSynchronizer.Implementation.Events.AppointmentId(
                            appointment.EntryID, appointment.GlobalAppointmentID ?? String.Empty),
                            AppointmentItemUtils.GetLastChangeTime(appointment),
                            wasDeleted);

                        if (!wasDeleted &&
                            !string.IsNullOrEmpty(appointment.GlobalAppointmentID) &&
                            _invitesInfo.IsIncomingInvite(appointment.GlobalAppointmentID))
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(appointment.Organizer) && appointment.Recipients.Count > 0)
                                {
                                    s_logger.Info(
                                        $"Incoming invite detected, setting MeetingStatus=olMeetingReceived for '{appointment.EntryID}'.");
                                    appointment.MeetingStatus = Outlook.OlMeetingStatus.olMeetingReceived;
                                    appointment.Save();
                                }
                                else
                                {
                                    s_logger.Info(
                                        $"Incoming invite detected but organizer/recipients not ready for '{appointment.EntryID}': " +
                                        $"Organizer='{appointment.Organizer}', Recipients={appointment.Recipients.Count}.");
                                }
                            }
                            catch (System.Exception ex)
                            {
                                s_logger.Warn("Failed to set MeetingStatus for incoming invite.", ex);
                            }
                        }

                        try
                        {
                            var meetingId = ExtractMeetingId(appointment);
                            string operation = null;

                            switch (action)
                            {
                                case ItemAction.Add:
                                    operation = "create";
                                    break;
                                case ItemAction.Change:
                                    operation = "update";
                                    break;
                                case ItemAction.Remove:
                                    operation = "delete";
                                    break;
                            }

                            if (operation != null)
                            {
                                Telemetry.Signal(Telemetry.CalendarEvents, $"event_{operation}", new
                                {
                                    meeting_id = meetingId,
                                    operation,
                                    success = true
                                });
                            }
                        }
                        catch (System.Exception ex)
                        {
                            s_logger.Warn($"Failed to send telemetry for user action '{action}' in Outlook.", ex);
                        }
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

        private string ExtractMeetingId(AppointmentItem appointment)
        {
            if (appointment == null)
            {
                return null;
            }

            var globalAppointmentID = appointment.GlobalAppointmentID;
            if (!string.IsNullOrEmpty(globalAppointmentID))
            {
                var extractedUid = AppointmentItemUtils.ExtractUidFromGlobalId(globalAppointmentID);

                if (extractedUid != globalAppointmentID)
                {
                    return extractedUid;
                }
            }

            //Fallback to EntryId as a more consistent option
            return appointment.EntryID;
        }

        private static object GetPropertySafe(Outlook.PropertyAccessor accessor, string propertyName)
        {
            try
            {
                using (var wrapper = GenericComObjectWrapper.Create(accessor))
                {
                    return wrapper.Inner.GetProperty(propertyName);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
