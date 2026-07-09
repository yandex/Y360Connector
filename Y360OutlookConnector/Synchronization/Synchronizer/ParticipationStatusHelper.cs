using System;
using System.Linq;
using DDay.iCal;
using Y360OutlookConnector.Utilities;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    internal static class ParticipationStatusHelper
    {
        public static string MapOutlookResponseToParticipation(Outlook.OlResponseStatus status)
        {
            switch (status)
            {
                case Outlook.OlResponseStatus.olResponseAccepted:
                    return "ACCEPTED";
                case Outlook.OlResponseStatus.olResponseDeclined:
                    return "DECLINED";
                case Outlook.OlResponseStatus.olResponseTentative:
                    return "TENTATIVE";
                case Outlook.OlResponseStatus.olResponseOrganized:
                    return "ACCEPTED";
                case Outlook.OlResponseStatus.olResponseNone:
                case Outlook.OlResponseStatus.olResponseNotResponded:
                default:
                    return "NEEDS-ACTION";
            }
        }

        public static string GetOwnParticipationFromCalendar(IICalendar calendar, string outlookEmailAddress)
        {
            if (calendar == null || string.IsNullOrEmpty(outlookEmailAddress))
            {
                return null;
            }

            var evt = calendar.Events.FirstOrDefault();
            if (evt == null)
            {
                return null;
            }

            if (evt.Attendees == null)
            {
                return null;
            }

            var outlookMailUri = new Uri("mailto:" + outlookEmailAddress);
            var ownAttendee = evt.Attendees.FirstOrDefault(a => EmailAddress.AreSame(a.Value, outlookMailUri));
            if (ownAttendee == null)
            {
                return null;
            }

            return string.IsNullOrEmpty(ownAttendee.ParticipationStatus) ? "NEEDS-ACTION" : ownAttendee.ParticipationStatus;
        }

        public static bool HasExplicitOutlookResponse(Outlook.AppointmentItem appointment)
        {
            if (appointment == null)
            {
                return false;
            }

            return appointment.ResponseStatus != Outlook.OlResponseStatus.olResponseNone
                   && appointment.ResponseStatus != Outlook.OlResponseStatus.olResponseNotResponded
                   && appointment.ResponseStatus != Outlook.OlResponseStatus.olResponseOrganized;
        }

        public static bool IsIncomingMeeting(Outlook.AppointmentItem appointment)
        {
            if (appointment == null)
            {
                return false;
            }

            return appointment.MeetingStatus == Outlook.OlMeetingStatus.olMeetingReceived
                   || appointment.MeetingStatus == Outlook.OlMeetingStatus.olMeetingReceivedAndCanceled;
        }

        public static bool IsIncomingReplyOnlyChange(Outlook.AppointmentItem appointment)
        {
            if (!IsIncomingMeeting(appointment))
            {
                return false;
            }

            var lastChangeSource = AppointmentItemUtils.GetLastChangeTimeWithSource(appointment).Source;
            return string.Equals(lastChangeSource, "AppointmentReplyTime", StringComparison.Ordinal);
        }

        public static bool ShouldUseServerAttendeesForIncomingParticipationPush(
            Outlook.AppointmentItem appointment,
            IICalendar existingServerCalendarOrNull,
            bool isRecurrenceException)
        {
            if (appointment == null || isRecurrenceException)
            {
                return false;
            }

            if (!IsIncomingMeeting(appointment) || !HasExplicitOutlookResponse(appointment))
            {
                return false;
            }

            var lastChangeSource = AppointmentItemUtils.GetLastChangeTimeWithSource(appointment).Source;
            if (string.Equals(lastChangeSource, "OwnerCriticalChangeTime", StringComparison.Ordinal))
            {
                return false;
            }

            if (existingServerCalendarOrNull == null)
            {
                return false;
            }

            var serverEvent = existingServerCalendarOrNull.Events?.FirstOrDefault(e => e.RecurrenceID == null);
            if (serverEvent?.Attendees == null || serverEvent.Attendees.Count == 0)
            {
                return false;
            }

            return true;
        }

        public static bool ParticipationDiffers(string outlookParticipation, string serverParticipation)
        {
            if (string.IsNullOrEmpty(outlookParticipation) || string.IsNullOrEmpty(serverParticipation))
            {
                return false;
            }

            return !string.Equals(
                NormalizeParticipation(outlookParticipation),
                NormalizeParticipation(serverParticipation),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeParticipation(string participation)
        {
            if (string.IsNullOrEmpty(participation))
            {
                return "NEEDS-ACTION";
            }

            return participation;
        }

        public static int GetOutlookResponseRank(Outlook.OlResponseStatus status)
        {
            switch (status)
            {
                case Outlook.OlResponseStatus.olResponseAccepted:
                case Outlook.OlResponseStatus.olResponseOrganized:
                    return 4;
                case Outlook.OlResponseStatus.olResponseTentative:
                    return 3;
                case Outlook.OlResponseStatus.olResponseDeclined:
                    return 3;
                case Outlook.OlResponseStatus.olResponseNone:
                case Outlook.OlResponseStatus.olResponseNotResponded:
                default:
                    return 1;
            }
        }
    }
}
