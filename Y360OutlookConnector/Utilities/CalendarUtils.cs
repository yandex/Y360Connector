using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.DDayICalWorkaround;
using DDay.iCal;
using DDay.iCal.Serialization;
using DDay.iCal.Serialization.iCalendar;
using log4net;

namespace Y360OutlookConnector.Utilities
{
    public static class CalendarUtils
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public static readonly string ParticipantsPermissionPropertyName = "X-YANDEX-PARTICIPANT-PERMISSION";
        public static bool CanParticipantsEditEvent(this IICalendar calendar)
        {
            var propCanEdit = calendar.Properties.FirstOrDefault(p => p.Name == ParticipantsPermissionPropertyName);
            if (propCanEdit == null)
            {
                return false;
            }
            var propValue = propCanEdit.Value as string;
            if (propValue == null || propValue != "EDIT")
            {
                return false;
            }

            return true;
        }

        public static Uri GetMasterEventUrl(this IICalendar calendar)
        {
            var events = calendar.Events;
            if (events == null || events.Count == 0)
            {
                return null;
            }

            return events.FirstOrDefault(e => e.RecurrenceID == null)?.Url;
        }

        public static Uri GetEventExceptionByStartDateUrl(this IICalendar calendar, DateTime startUtc)
        {
            var events = calendar.Events;
            if (events == null || events.Count == 0)
            {
                return null;
            }

            return events.FirstOrDefault(e => e.RecurrenceID != null && e.Start.UTC == startUtc)?.Url;
        }

        public static IICalendar DeserializeEntityData(string entityData, WebResourceName webResourceName)
        {
            string normalizedICalData, fixedICalData;

            // fix some linebreak issues with Open-Xchange
            if (entityData.Contains("\r\r\n"))
            {
                normalizedICalData = CalendarDataPreprocessor.NormalizeLineBreaks(entityData);
            }
            else
            {
                normalizedICalData = entityData;
            }

            // emClient sets DTSTART in VTIMEZONE to year 0001, which causes a 90 sec delay in DDay.iCal to evaluate the recurrence rule.
            // If we find such a DTSTART we replace it 0001 with 1970 since the historic data is not valid anyway and avoid the performance issue.
            if (normalizedICalData.Contains("DTSTART:00010101"))
            {
                fixedICalData = CalendarDataPreprocessor.FixInvalidDTSTARTInTimeZoneNoThrow(normalizedICalData);
            }
            else
            {
                fixedICalData = normalizedICalData;
            }

            IICalendar calendar;

            var calendarSerializer = new iCalendarSerializer();

            if (TryDeserializeCalendar(fixedICalData, out calendar, webResourceName, calendarSerializer))
            {
                // Add only if there is atleast one vevent or vtodo to avoid Nullreference Exceptions when processing
                if (calendar.Events.Count > 0)
                {
                    return calendar;
                }
            }
            else
            {
                // maybe deserialization failed because of the iCal-TimeZone-Bug =>  try to fix it
                var fixedICalData2 = CalendarDataPreprocessor.FixTimeZoneComponentOrderNoThrow(fixedICalData);
                if (TryDeserializeCalendar(fixedICalData2, out calendar, webResourceName, calendarSerializer))
                {
                    // Add only if there is atleast one vevent or vtodo to avoid Nullreference Exceptions when processing
                    if (calendar.Events.Count > 0)
                    {
                        s_logger.Info(string.Format("Deserialized ICalData with reordering of TimeZone data '{0}'.", webResourceName.Id));
                        return calendar;
                    }
                }
            }

            return null;
        }

        private static bool TryDeserializeCalendar(
            string iCalData,
            out IICalendar calendar,
            WebResourceName uriOfCalendarForLogging,
            IStringSerializer calendarSerializer)
        {
            calendar = null;
            try
            {
                calendar = DeserializeCalendar(iCalData, calendarSerializer);
                return true;
            }
            catch (Exception x)
            {
                s_logger.Error(string.Format("Could not deserialize ICalData of '{0}'.", uriOfCalendarForLogging.OriginalAbsolutePath));
                s_logger.Debug(string.Format("ICalData:\r\n{0}", iCalData), x);
                return false;
            }
        }

        private static IICalendar DeserializeCalendar(string iCalData, IStringSerializer calendarSerializer)
        {
            using (var reader = new StringReader(iCalData))
            {
                var calendarCollection = (iCalendarCollection)calendarSerializer.Deserialize(reader);
                return calendarCollection[0];
            }
        }
    }
}
