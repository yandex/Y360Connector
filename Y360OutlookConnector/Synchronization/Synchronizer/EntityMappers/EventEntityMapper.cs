using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CalDavSynchronizer.Contracts;
using CalDavSynchronizer.DDayICalWorkaround;
using CalDavSynchronizer.Implementation.Common;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using CalDavSynchronizer.Implementation.TimeZones;
using DDay.iCal;
using GenSync.EntityMapping;
using GenSync.Logging;
using log4net;
using Microsoft.Office.Interop.Outlook;
using NodaTime;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Synchronization.Synchronizer;
using Y360OutlookConnector.Utilities;
using Exception = Microsoft.Office.Interop.Outlook.Exception;
using Period = DDay.iCal.Period;
using RecurrencePattern = DDay.iCal.RecurrencePattern;

namespace Y360OutlookConnector.Synchronization.EntityMappers
{
    public class EventEntityMapper : IEntityMapper<IAppointmentItemWrapper, IICalendar, IEventSynchronizationContext>
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodInfo.GetCurrentMethod().DeclaringType);
        private static readonly Regex _htmlTag = new Regex(@"<[^>\r\n]{1,100}>", RegexOptions.Compiled);
        private static readonly Regex _ctrlChars = new Regex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]", RegexOptions.Compiled);


        private const string PR_SENDER_NAME = "http://schemas.microsoft.com/mapi/proptag/0x0C1A001E";
        private const string PR_SENDER_EMAIL_ADDRESS = "http://schemas.microsoft.com/mapi/proptag/0x0C1F001E";
        private const string PR_SENT_REPRESENTING_NAME = "http://schemas.microsoft.com/mapi/proptag/0x0042001E";
        private const string PR_SENT_REPRESENTING_EMAIL_ADDRESS = "http://schemas.microsoft.com/mapi/proptag/0x0065001E";
        private const string PR_SENT_REPRESENTING_ADDRTYPE = "http://schemas.microsoft.com/mapi/proptag/0x0064001E";
        private const string PR_SENDER_ADDRTYPE = "http://schemas.microsoft.com/mapi/proptag/0x0C1E001E";
        private const string PR_SENT_REPRESENTING_ENTRYID = "http://schemas.microsoft.com/mapi/proptag/0x00410102";
        private const string PR_SENDER_ENTRYID = "http://schemas.microsoft.com/mapi/proptag/0x0C190102";
        private const string PR_GLOBAL_OBJECT_ID = "http://schemas.microsoft.com/mapi/id/{6ED8DA90-450B-101B-98DA-00AA003F1305}/00030102";
        private const string PR_CLEAN_GLOBAL_OBJECT_ID = "http://schemas.microsoft.com/mapi/id/{6ED8DA90-450B-101B-98DA-00AA003F1305}/00230102";
        private const string PR_FINVITED = "http://schemas.microsoft.com/mapi/id/{00062002-0000-0000-C000-000000000046}/8229000B";
        private const string DeferredRespondMarkerPropertyAccessor = "http://schemas.microsoft.com/mapi/string/{14B893D3-5A3E-4D11-93A5-1881742AE0F1}/Y360DeferredRespondUtc";
        private static readonly TimeSpan DeferredRespondMarkerTtl = TimeSpan.FromMinutes(15);

        private readonly int _outlookMajorVersion;

        private readonly string _outlookEmailAddress;
        private readonly string _serverEmailUri;
        private readonly string _serverUserCommonName;
        private readonly string _localTimeZoneId;
        private readonly ITimeZone _configuredEventTimeZoneOrNull;
        private readonly EventMappingConfiguration _configuration;
        private readonly ITimeZoneCache _timeZoneCache;
        private readonly IOutlookTimeZones _outlookTimeZones;
        private readonly ICalendarResourceResolver _calendarResourceResolver;
        private readonly FailedEntityTracker _failedEntityTracker;
        private readonly DeferredRespondStorage _deferredRespondStorage;

        public EventEntityMapper(
            string outlookEmailAddress,
            Uri serverEmailAddress,
            string serverUserCommonName,
            string localTimeZoneId,
            string outlookApplicationVersion,
            ITimeZoneCache timeZoneCache,
            EventMappingConfiguration configuration,
            ITimeZone configuredEventTimeZoneOrNull,
            IOutlookTimeZones outlookTimeZones,
            ICalendarResourceResolver calendarResourceResolver,
            FailedEntityTracker failedEntityTracker,
            DeferredRespondStorage deferredRespondStorage)
        {
            _calendarResourceResolver = calendarResourceResolver ?? throw new ArgumentNullException(nameof(calendarResourceResolver));
            _outlookEmailAddress = outlookEmailAddress;
            _configuration = configuration;
            _configuredEventTimeZoneOrNull = configuredEventTimeZoneOrNull;
            _outlookTimeZones = outlookTimeZones;
            _serverEmailUri = serverEmailAddress.ToString();
            _localTimeZoneId = localTimeZoneId;
            _timeZoneCache = timeZoneCache;
            _serverUserCommonName = serverUserCommonName;

            string outlookMajorVersionString = outlookApplicationVersion.Split(new char[] { '.' })[0];
            _outlookMajorVersion = Convert.ToInt32(outlookMajorVersionString);
            _failedEntityTracker = failedEntityTracker;
            _deferredRespondStorage = deferredRespondStorage;
        }

        public static EventEntityMapper Create(
            string outlookEmailAddress,
            Uri serverEmailAddress,
            string serverUserCommonName,
            string localTimeZoneId,
            string outlookApplicationVersion,
            ITimeZoneCache timeZoneCache,
            EventMappingConfiguration configuration,
            ITimeZone configuredEventTimeZoneOrNull,
            IOutlookTimeZones outlookTimeZones,
            ICalendarResourceResolver calendarResourceResolver,
            FailedEntityTracker failedEntityTracker,
            DeferredRespondStorage deferredRespondStorage)
        {
            return new EventEntityMapper(outlookEmailAddress, serverEmailAddress, serverUserCommonName,
                localTimeZoneId, outlookApplicationVersion, timeZoneCache, configuration,
                configuredEventTimeZoneOrNull, outlookTimeZones, calendarResourceResolver, failedEntityTracker, deferredRespondStorage);
        }

        public async Task<IICalendar> Map1To2(IAppointmentItemWrapper sourceWrapper, IICalendar existingTargetCalendar, IEntitySynchronizationLogger logger, IEventSynchronizationContext context)
        {
            var newTargetCalendar = new iCalendar();

            if (AppConfig.IsAlwaysSkipInvitationEmails && existingTargetCalendar.IsNew())
            {
                var organizerEmail = sourceWrapper.Inner.GetOrganizerEmailAddress(logger);

                if (EmailAddress.AreSame(organizerEmail, _outlookEmailAddress))
                {
                    // Это создание новой встречи. Организатором является сам пользователь.
                    // Событие отсутствует в календаре, но присутствует в Outlook и это новое событие
                    // Добавляем свойство, что не требуется посылать приглашение
                    newTargetCalendar.AddSkipInvitationProperty();
                }                
            }
            

            ITimeZone startIcalTimeZone = null;
            ITimeZone endIcalTimeZone = null;

            if (!_configuration.CreateEventsInUTC)
            {
                string startTimeZoneID;
                string endTimeZoneID;

                try
                {
                    using (var startTimeZone = GenericComObjectWrapper.Create(sourceWrapper.Inner.StartTimeZone))
                    {
                        startTimeZoneID = startTimeZone.Inner.ID;
                    }

                    using (var endTimeZone = GenericComObjectWrapper.Create(sourceWrapper.Inner.EndTimeZone))
                    {
                        endTimeZoneID = endTimeZone.Inner.ID;
                    }

                    if (_configuration.UseIanaTz)
                    {
                        if (_localTimeZoneId == startTimeZoneID && _configuredEventTimeZoneOrNull != null)
                        {
                            newTargetCalendar.TimeZones.Add(_configuredEventTimeZoneOrNull);
                            startIcalTimeZone = _configuredEventTimeZoneOrNull;
                        }
                        else
                        {
                            var startIanaTzId = TimeZoneMapper.WindowsToIanaOrNull(startTimeZoneID);
                            if (startIanaTzId != null)
                                startIcalTimeZone = await _timeZoneCache.GetByTzIdOrNull(startIanaTzId);
                            if (startIcalTimeZone != null)
                                newTargetCalendar.TimeZones.Add(startIcalTimeZone);
                        }
                    }
                    else
                    {
                        var startTimeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(startTimeZoneID);
                        startIcalTimeZone = iCalTimeZone.FromSystemTimeZone(startTimeZoneInfo, new DateTime(1970, 1, 1), false);
                        CalendarDataPreprocessor.FixTimeZoneDSTRRules(startTimeZoneInfo, startIcalTimeZone);
                        newTargetCalendar.TimeZones.Add(startIcalTimeZone);
                    }

                    if (endTimeZoneID != startTimeZoneID)
                    {
                        if (_configuration.UseIanaTz)
                        {
                            if (_localTimeZoneId == endTimeZoneID && _configuredEventTimeZoneOrNull != null)
                            {
                                newTargetCalendar.TimeZones.Add(_configuredEventTimeZoneOrNull);
                                endIcalTimeZone = _configuredEventTimeZoneOrNull;
                            }
                            else
                            {
                                var endIanaTzId = TimeZoneMapper.WindowsToIanaOrNull(endTimeZoneID);
                                if (endIanaTzId != null)
                                    endIcalTimeZone = await _timeZoneCache.GetByTzIdOrNull(endIanaTzId);
                                if (endIcalTimeZone != null)
                                    newTargetCalendar.TimeZones.Add(endIcalTimeZone);
                            }
                        }
                        else
                        {
                            var endTimeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(endTimeZoneID);

                            endIcalTimeZone = iCalTimeZone.FromSystemTimeZone(endTimeZoneInfo, new DateTime(1970, 1, 1), false);
                            CalendarDataPreprocessor.FixTimeZoneDSTRRules(endTimeZoneInfo, endIcalTimeZone);
                            newTargetCalendar.TimeZones.Add(endIcalTimeZone);
                        }
                    }
                    else
                    {
                        endIcalTimeZone = startIcalTimeZone;
                    }
                }
                catch (COMException ex)
                {
                    s_logger.Warn("Can't get Timezone of AppointmentItem, using UTC", ex);
                    logger.LogWarning("Can't get Timezone of AppointmentItem, using UTC");
                }
            }

            var existingTargetEvent = existingTargetCalendar.Events.FirstOrDefault(e => e.RecurrenceID == null);

            var newTargetEvent = new Event();
           
            if (existingTargetEvent != null)
                newTargetEvent.UID = existingTargetEvent.UID;
            else if (_configuration.UseGlobalAppointmentID)
                newTargetEvent.UID = AppointmentItemUtils.ExtractUidFromGlobalId(sourceWrapper.Inner.GlobalAppointmentID);

            LogOutgoingAttendeeDiagnostics("master", sourceWrapper.Inner, existingTargetEvent, newTargetEvent);

            newTargetCalendar.Events.Add(newTargetEvent);

            await Map1To2(sourceWrapper.Inner, newTargetEvent, false, startIcalTimeZone, endIcalTimeZone, logger, context, existingTargetCalendar);

            LogSequenceDiagnostics(newTargetCalendar, existingTargetCalendar);

            for (int i = 0, newSequenceNumber = existingTargetCalendar.Events.Count > 0 ? existingTargetCalendar.Events.Max(e => e.Sequence) + 1 : 0;
                i < newTargetCalendar.Events.Count;
                i++, newSequenceNumber++)
            {
                newTargetCalendar.Events[i].Sequence = newSequenceNumber;
            }

            return newTargetCalendar;
        }

        private async Task Map1To2(AppointmentItem source, IEvent target, bool isRecurrenceException, ITimeZone startIcalTimeZone, ITimeZone endIcalTimeZone, IEntitySynchronizationLogger logger, IEventSynchronizationContext context, IICalendar existingServerCalendarOrNull)
        {
            if (source.AllDayEvent)
            {
                // Outlook's AllDayEvent relates to Start and not not StartUtc!!!
                target.Start = new iCalDateTime(source.Start);
                target.Start.HasTime = false;
                target.End = new iCalDateTime(source.End);
                target.End.HasTime = false;
                target.IsAllDay = true;
            }
            else
            {
                if (_configuration.CreateEventsInUTC || startIcalTimeZone == null || endIcalTimeZone == null)
                {
                    target.Start = new iCalDateTime(source.StartUTC) {IsUniversalTime = true};
                    target.DTEnd = new iCalDateTime(source.EndUTC) {IsUniversalTime = true};
                }
                else if (_configuration.UseIanaTz)
                {
                    // StartUTC и EndUTC имеют тип Unspecified. Необходимо явно установить тип Utc
                    var startInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(source.StartUTC, DateTimeKind.Utc));
                    var startTimeZone = DateTimeZoneProviders.Tzdb[startIcalTimeZone.TZID];
                    var zonedStart = startInstant.InZone(startTimeZone);
                    target.Start = new iCalDateTime(zonedStart.ToDateTimeUnspecified());
                    target.Start.SetTimeZone(startIcalTimeZone);
                    var endInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(source.EndUTC, DateTimeKind.Utc));
                    var endTimeZone = DateTimeZoneProviders.Tzdb[endIcalTimeZone.TZID];
                    var zonedEnd = endInstant.InZone(endTimeZone);
                    target.End = new iCalDateTime(zonedEnd.ToDateTimeUnspecified());
                    target.End.SetTimeZone(endIcalTimeZone);
                }
                else
                {
                    target.Start = new iCalDateTime(source.StartInStartTimeZone);
                    target.Start.SetTimeZone(startIcalTimeZone);
                    target.DTEnd = new iCalDateTime(source.EndInEndTimeZone);
                    target.End.SetTimeZone(endIcalTimeZone);
                }

                target.IsAllDay = false;
            }

            target.Summary = CalendarDataPreprocessor.EncodeString(source.Subject);
            if (!string.IsNullOrEmpty(target.Summary) &&
                target.Summary.StartsWith("Cancelled: "))
                target.Status = EventStatus.Cancelled;

            target.Location = CalendarDataPreprocessor.EncodeString(source.Location);

            if (_configuration.MapBody)
            {
                target.Description = CalendarDataPreprocessor.EncodeString(source.Body);
                if (IsBodyBroken(source.Body, target.Description))
                {
                    s_logger.Error($"Error on mapping Description from Outlook to Calendar. \r\n Outlook: {source.Body}  \r\n Calendar: {target.Description}");
                    Telemetry.Signal(Telemetry.ConfirmedBugEvent, "error_mapping_description_1To2");
                }
            }

            target.Priority = CommonEntityMapper.MapPriority1To2(source.Importance);


            if (_configuration.MapAttendees)
            {
                if (ParticipationStatusHelper.ShouldUseServerAttendeesForIncomingParticipationPush(
                    source, existingServerCalendarOrNull, isRecurrenceException))
                {
                    MapAttendeesFromServerWithOwnParticipationPatch(source, target, existingServerCalendarOrNull, logger);
                }
                else
                {
                    var organizerSet = await MapAttendees1To2(source, target, logger);
                    if (!organizerSet)
                        MapOrganizer1To2(source, target, logger);
                    EnsureOrganizerInAttendees(target);
                    MapOwnAttendeeOutlookToServer(target);
                }
            }

            if (!isRecurrenceException)
                await MapRecurrance1To2(source, target, startIcalTimeZone, endIcalTimeZone, logger, context, existingServerCalendarOrNull);


            target.Class = CommonEntityMapper.MapPrivacy1To2(source.Sensitivity, _configuration.MapSensitivityPrivateToClassConfidential, _configuration.MapSensitivityPublicToDefault);

            MapReminder1To2(source, target, isRecurrenceException);

            MapCategories1To2(source, target, context);

            target.Properties.Add(MapTransparency1To2(source.BusyStatus));
            target.Properties.Add(MapBusyStatus1To2(source.BusyStatus));
        }

        private static CalendarProperty MapBusyStatus1To2(OlBusyStatus value)
        {
            switch (value)
            {
                case OlBusyStatus.olTentative:
                    return new CalendarProperty("X-MICROSOFT-CDO-BUSYSTATUS", "TENTATIVE");
                case OlBusyStatus.olOutOfOffice:
                    return new CalendarProperty("X-MICROSOFT-CDO-BUSYSTATUS", "OOF");
                case OlBusyStatus.olFree:
                    return new CalendarProperty("X-MICROSOFT-CDO-BUSYSTATUS", "FREE");
                case OlBusyStatus.olWorkingElsewhere:
                    return new CalendarProperty("X-MICROSOFT-CDO-BUSYSTATUS", "WORKINGELSEWHERE");
                case OlBusyStatus.olBusy:
                default:
                    return new CalendarProperty("X-MICROSOFT-CDO-BUSYSTATUS", "BUSY");
            }
        }

        private static CalendarProperty MapTransparency1To2(OlBusyStatus value)
        {
            switch (value)
            {
                case OlBusyStatus.olBusy:
                case OlBusyStatus.olOutOfOffice:
                case OlBusyStatus.olWorkingElsewhere:
                case OlBusyStatus.olTentative:
                    return new CalendarProperty("TRANSP", "OPAQUE");
                case OlBusyStatus.olFree:
                    return new CalendarProperty("TRANSP", "TRANSPARENT");
            }

            throw new NotImplementedException(string.Format("Mapping for value '{0}' not implemented.", value));
        }


        private static OlBusyStatus MapTransparency2To1(IEvent source)
        {
            if (source.Properties.ContainsKey("X-MICROSOFT-CDO-BUSYSTATUS"))
            {
                switch (source.Properties["X-MICROSOFT-CDO-BUSYSTATUS"].Value.ToString())
                {
                    case "WORKINGELSEWHERE":
                        return OlBusyStatus.olWorkingElsewhere;
                    case "FREE":
                        return OlBusyStatus.olFree;
                    case "TENTATIVE":
                        return OlBusyStatus.olTentative;
                    case "OOF":
                        return OlBusyStatus.olOutOfOffice;
                    case "BUSY":
                    default:
                        return OlBusyStatus.olBusy;
                }
            }
            else
            {
                if (source.Transparency == TransparencyType.Transparent || source.IsAllDay && !source.Properties.ContainsKey("TRANSP"))
                    return OlBusyStatus.olFree;
                else return OlBusyStatus.olBusy;
            }
        }

        private void MapCategories1To2(AppointmentItem source, IEvent target, IEventSynchronizationContext context)
        {
            if (!string.IsNullOrEmpty(source.Categories))
            {
                var useEventCategoryAsFilter = _configuration.UseEventCategoryAsFilter;

                var sourceCategories = CommonEntityMapper.SplitCategoryString(source.Categories)
                    .Where(c => !useEventCategoryAsFilter || string.Compare(c, _configuration.EventCategory, StringComparison.OrdinalIgnoreCase) != 0);

                var wasColorAdded = false;

                foreach (var sourceCategory in sourceCategories)
                {
                    if (_configuration.MapEventColorToCategory && !wasColorAdded)
                    {
                        var htmlColor = context.MapCategoryToHtmlColorOrNull(sourceCategory);
                        if (htmlColor != null)
                        {
                            var color = new CalendarProperty("COLOR", htmlColor);
                            target.Properties.Add(color);
                            wasColorAdded = true;
                        }
                    }

                    if (!(_configuration.MapEventColorToCategory && ColorCategoryMapper.IsNameOfAutoGeneratedColorCategory(sourceCategory)))
                        target.Categories.Add(sourceCategory);
                }
            }
        }

        private void MapReminder1To2(AppointmentItem source, IEvent target, bool isRecurrenceException)
        {
            if (_configuration.MapReminder == ReminderMapping.@false)
                return;

            if (source.ReminderSet)
            {
                var reminderRelativeToStart = TimeSpan.FromMinutes(-source.ReminderMinutesBeforeStart);

                if (_configuration.MapReminder == ReminderMapping.JustUpcoming)
                {
                    if ((!source.IsRecurring || isRecurrenceException) && source.StartUTC.Add(reminderRelativeToStart) <= DateTime.UtcNow)
                        return;
                    if (source.IsRecurring && !isRecurrenceException)
                    {
                        using (var sourceRecurrencePatternWrapper = GenericComObjectWrapper.Create(source.GetRecurrencePattern()))
                        {
                            if (!sourceRecurrencePatternWrapper.Inner.NoEndDate &&
                                sourceRecurrencePatternWrapper.Inner.PatternEndDate.Add(sourceRecurrencePatternWrapper.Inner.EndTime.TimeOfDay).ToUniversalTime() <= DateTime.UtcNow)
                                return;
                        }
                    }
                }

                var trigger = new Trigger(reminderRelativeToStart);

                target.Alarms.Add(
                    new Alarm()
                    {
                        Description = "This is an event reminder"
                    }
                );
                // Fix DDay.iCal TimeSpan 0 serialization
                if (reminderRelativeToStart == TimeSpan.Zero)
                {
                    target.Alarms[0].Properties.Add(new CalendarProperty("TRIGGER", "-P0D"));
                }
                else
                {
                    target.Alarms[0].Trigger = trigger;
                }

                // Fix for google, since Google wants ACTION property DISPLAY in uppercase
                var actionProperty = new CalendarProperty("ACTION", "DISPLAY");
                target.Alarms[0].Properties.Add(actionProperty);
            }
        }


        private void MapReminder2To1(IEvent source, AppointmentItem target, bool isRecurrenceException, IEntitySynchronizationLogger logger)
        {
            if (_configuration.MapReminder == ReminderMapping.@false)
            {
                target.ReminderSet = false;
                return;
            }

            if (source.Alarms.Count == 0)
            {
                return;
            }

            if (source.Alarms.Count > 1)
            {
                s_logger.WarnFormat("Event '{0}' contains multiple alarms. Ignoring all except first alarm with action DISPLAY.", source.UID);
            }

            var alarm = source.Alarms.FirstOrDefault(a => a.Action == AlarmAction.Display);

            if (alarm == null)
            {
                s_logger.WarnFormat("Event '{0}' contains only not supported alarm types. Ignoring alarm.", source.UID);
                target.ReminderSet = false;
                return;
            }

            if (alarm.Trigger == null)
            {
                s_logger.WarnFormat("Event '{0}' contains non RFC-conform alarm. Ignoring alarm.", source.UID);
                logger.LogWarning("Event contains non RFC-conform alarm. Ignoring alarm.");
                target.ReminderSet = false;
                return;
            }

            if (alarm.Trigger.IsRelative && (!alarm.Trigger.Duration.HasValue ||
                                             (alarm.Trigger.Related == TriggerRelation.Start && alarm.Trigger.Duration > TimeSpan.Zero) ||
                                             (alarm.Trigger.Related == TriggerRelation.End && target.EndUTC.Add(alarm.Trigger.Duration.Value) > target.StartUTC)))
            {
                s_logger.WarnFormat("Event '{0}' alarm has an invalid duration or is not before event start. Ignoring.", source.UID);
                logger.LogWarning("Alarm has an invalid duration or is not before event start. Ignoring.");
                target.ReminderSet = false;
                return;
            }

            if (target.IsRecurring && !isRecurrenceException && _configuration.MapReminder == ReminderMapping.JustUpcoming)
            {
                using (var sourceRecurrencePatternWrapper = GenericComObjectWrapper.Create(target.GetRecurrencePattern()))
                {
                    if (!sourceRecurrencePatternWrapper.Inner.NoEndDate &&
                        sourceRecurrencePatternWrapper.Inner.PatternEndDate.Add(sourceRecurrencePatternWrapper.Inner.EndTime.TimeOfDay).ToUniversalTime() <= DateTime.UtcNow)
                    {
                        target.ReminderSet = false;
                        return;
                    }
                }
            }

            if (alarm.Trigger.IsRelative && alarm.Trigger.Duration.HasValue)
            {
                if (_configuration.MapReminder == ReminderMapping.JustUpcoming &&
                    (!target.IsRecurring || isRecurrenceException) &&
                    (alarm.Trigger.Related == TriggerRelation.Start && target.StartUTC.Add(alarm.Trigger.Duration.Value) <= DateTime.UtcNow) ||
                    (alarm.Trigger.Related == TriggerRelation.End && target.EndUTC.Add(alarm.Trigger.Duration.Value) <= DateTime.UtcNow))
                {
                    target.ReminderSet = false;
                    return;
                }

                try
                {
                    target.ReminderSet = true;
                    if (alarm.Trigger.Related == TriggerRelation.Start)
                    {
                        target.ReminderMinutesBeforeStart = -(int) alarm.Trigger.Duration.Value.TotalMinutes;
                    }
                    else
                    {
                        target.ReminderMinutesBeforeStart = -(int) (alarm.Trigger.Duration.Value.TotalMinutes + target.Duration);
                    }
                }
                catch (System.Exception ex)
                {
                    s_logger.WarnFormat("Event '{0}' alarm has an invalid duration which can't be set in Outlook. {1}", source.UID, ex);
                    logger.LogWarning("Alarm has an invalid duration. Ignoring.");
                    target.ReminderSet = false;
                }
            }
            else if (alarm.Trigger.DateTime != null)
            {
                var alarmTimeUtc = alarm.Trigger.DateTime.AsUtc();
                if (_configuration.MapReminder == ReminderMapping.JustUpcoming && alarmTimeUtc < DateTime.UtcNow)
                {
                    target.ReminderSet = false;
                    return;
                }

                var alarmDuration = source.Start.UTC - alarmTimeUtc;
                if (alarmDuration >= TimeSpan.Zero)
                {
                    try
                    {
                        target.ReminderSet = true;
                        target.ReminderMinutesBeforeStart = (int) alarmDuration.TotalMinutes;
                    }
                    catch (System.Exception ex)
                    {
                        s_logger.WarnFormat("Event '{0}' alarm has an invalid duration which can't be set in Outlook. {1}", source.UID, ex);
                        logger.LogWarning("Alarm has an invalid duration. Ignoring.");
                        target.ReminderSet = false;
                    }
                }
                else
                {
                    s_logger.WarnFormat("Event '{0}' alarm is not before event start. Ignoring.", source.UID);
                    logger.LogWarning("Alarm is not before event start. Ignoring.");
                    target.ReminderSet = false;
                }
            }
        }

        private string MapParticipation1To2(OlResponseStatus value)
        {
            switch (value)
            {
                case OlResponseStatus.olResponseAccepted:
                    return "ACCEPTED";
                case OlResponseStatus.olResponseDeclined:
                    return "DECLINED";
                case OlResponseStatus.olResponseOrganized:
                    return "ACCEPTED";
                case OlResponseStatus.olResponseTentative:
                    return "TENTATIVE";
                case OlResponseStatus.olResponseNone:
                case OlResponseStatus.olResponseNotResponded:
                default:
                    return "NEEDS-ACTION";
            }
        }

        private OlResponseStatus MapParticipation2To1(string value)
        {
            switch (value)
            {
                case "NEEDS-ACTION":
                    return OlResponseStatus.olResponseNotResponded;
                case "ACCEPTED":
                    return OlResponseStatus.olResponseAccepted;
                case "DECLINED":
                    return OlResponseStatus.olResponseDeclined;
                case "TENTATIVE":
                    return OlResponseStatus.olResponseTentative;
                case "DELEGATED":
                    return OlResponseStatus.olResponseNotResponded;
                case null:
                    return OlResponseStatus.olResponseNone;
                // according to the RFC 5545 not recognized values must be treated the same way as NEEDS-ACTION
                default:
                    return OlResponseStatus.olResponseNotResponded;
            }
        }


        private OlMeetingResponse? MapParticipation2ToMeetingResponse(string value)
        {
            switch (value)
            {
                case "ACCEPTED":
                    return OlMeetingResponse.olMeetingAccepted;
                case "DECLINED":
                    return OlMeetingResponse.olMeetingDeclined;
                case "TENTATIVE":
                    return OlMeetingResponse.olMeetingTentative;
                case "NEEDS-ACTION":
                case "DELEGATED":
                // according to the RFC 5545 not recognized values must be treated the same way as NEEDS-ACTION
                default:
                    return null;
            }
        }

        private void MapOrganizer1To2(AppointmentItem source, IEvent target, IEntitySynchronizationLogger logger)
        {
            if (source.MeetingStatus != OlMeetingStatus.olNonMeeting)
            {
                using (var organizerWrapper = GenericComObjectWrapper.Create(source.GetOrganizer()))
                {
                    if (organizerWrapper.Inner != null)
                    {
                        if (StringComparer.InvariantCultureIgnoreCase.Compare(organizerWrapper.Inner.Name, source.Organizer) == 0)
                        {
                            SetOrganizer(target, organizerWrapper.Inner, organizerWrapper.Inner.Address, logger);
                        }
                        else
                        {
                            string organizerEmail = OutlookUtility.GetSenderEmailAddressOrNull(source, logger, s_logger);
                            SetOrganizer(target, source.Organizer, organizerEmail, logger);
                        }

                        SetOrganizerSchedulingParameters(source, target, logger);
                    }
                }
            }
        }

        private void SetOrganizer(IEvent target, AddressEntry organizer, string address, IEntitySynchronizationLogger logger)
        {
            string organizerEmail = GetMailUrlOrNull(organizer, address, logger);
            Organizer targetOrganizer;
            if (EmailAddress.AreSame(organizerEmail?.Substring(s_mailtoSchemaLength), _outlookEmailAddress))
            {
                organizerEmail = _serverEmailUri;
                targetOrganizer = new Organizer(_serverEmailUri);

                var attendees = new List<IAttendee>(target.Attendees);
                var organizerAttendees = attendees.Find(x => x.Value == new Uri(organizerEmail));
                if (organizerAttendees != null)
                    targetOrganizer.CommonName = organizerAttendees.CommonName;
                else
                    targetOrganizer.CommonName = _serverUserCommonName;
            }
            else if (_configuration.OrganizerAsDelegate && StringComparer.InvariantCultureIgnoreCase.Compare(organizerEmail, $"mailto:{_outlookEmailAddress}") == 0)
            {
                targetOrganizer = new Organizer(_serverEmailUri);
                if (organizerEmail != null) targetOrganizer.SentBy = new Uri(organizerEmail);
            }
            else
            {
                targetOrganizer = (organizerEmail != null) ? new Organizer(organizerEmail) : new Organizer();
                if (organizer != null)
                    targetOrganizer.CommonName = organizer.Name;
            }

            target.Organizer = targetOrganizer;
        }

        private void SetOrganizer(IEvent target, string organizerCN, string organizerEmail, IEntitySynchronizationLogger logger)
        {
            Organizer targetOrganizer;

            if (organizerEmail != null)
            {
                if (EmailAddress.AreSame(organizerEmail, _outlookEmailAddress))
                    organizerEmail = _serverEmailUri.Substring(s_mailtoSchemaLength);

                var emailAddress = $"mailto:{organizerEmail}";
                if (Uri.IsWellFormedUriString(emailAddress, UriKind.Absolute))
                {
                    if (_configuration.OrganizerAsDelegate && StringComparer.InvariantCultureIgnoreCase.Compare(organizerEmail, _outlookEmailAddress) == 0)
                    {
                        targetOrganizer = new Organizer(_serverEmailUri);
                        targetOrganizer.SentBy = new Uri(emailAddress);
                    }
                    else
                    {
                        targetOrganizer = new Organizer(emailAddress);
                        targetOrganizer.CommonName = organizerCN;
                    }
                }
                else
                {
                    s_logger.WarnFormat("Invalid email address URI {0} for organizer", organizerEmail);
                    logger.LogWarning($"Invalid email address Uri '{organizerEmail}' for organizer");
                    targetOrganizer = new Organizer();
                    targetOrganizer.CommonName = organizerCN;
                }
            }
            else
            {
                targetOrganizer = new Organizer();
                targetOrganizer.CommonName = organizerCN;
            }

            target.Organizer = targetOrganizer;
        }

        private void SetOrganizerSchedulingParameters(AppointmentItem source, IEvent target, IEntitySynchronizationLogger logger)
        {
            if (_configuration.ScheduleAgentClient)
                target.Organizer.Parameters.Add("SCHEDULE-AGENT", "CLIENT");
            if (_configuration.SendNoAppointmentNotifications)
                target.Properties.Add(new CalendarProperty("X-SOGO-SEND-APPOINTMENT-NOTIFICATIONS", "NO"));

            try
            {
                if (GetPropertySafe(source.PropertyAccessor, PR_FINVITED))
                {
                    target.Organizer.Parameters.Add("SCHEDULE-STATUS", "1.1");
                }
            }
            catch (COMException ex)
            {
                s_logger.Warn("Can't access FINVITED property of appointment.", ex);
            }
        }

        private void MapOwnAttendeeOutlookToServer(IEvent target)
        {
            var outlookEmailAddressUrl = new Uri($"mailto:{_outlookEmailAddress}");
            var attendees = new List<IAttendee>(target.Attendees);
            var ownAttendee = attendees.Find(x => EmailAddress.AreSame(x.Value, outlookEmailAddressUrl));
            if (ownAttendee != null)
            {
                ownAttendee.Value = new Uri(_serverEmailUri);
                ownAttendee.Parameters.Remove("EMAIL");
                ownAttendee.CommonName = _serverUserCommonName;
            }
        }

        private void MapAttendeesFromServerWithOwnParticipationPatch(
            AppointmentItem source,
            IEvent target,
            IICalendar existingServerCalendarOrNull,
            IEntitySynchronizationLogger logger)
        {
            var serverEvent = existingServerCalendarOrNull?.Events?.FirstOrDefault(e => e.RecurrenceID == null);
            if (serverEvent == null)
            {
                return;
            }

            int outlookRecipientCount;
            try
            {
                outlookRecipientCount = source.Recipients.Count;
            }
            catch (COMException)
            {
                outlookRecipientCount = -1;
            }

            s_logger.Info($"Map1To2: server-as-base ATTENDEE/ORGANIZER for incoming meeting (Outlook Recipients={outlookRecipientCount}, server ATTENDEE={serverEvent.Attendees.Count})");

            if (serverEvent.Organizer != null)
            {
                target.Organizer = serverEvent.Organizer.Copy<Organizer>();
            }

            foreach (var serverAttendee in serverEvent.Attendees)
            {
                target.Attendees.Add(serverAttendee.Copy<Attendee>());
            }

            PatchOwnParticipationStatusFromOutlook(source, target, logger);
            MapOwnAttendeeOutlookToServer(target);
        }

        private void PatchOwnParticipationStatusFromOutlook(
            AppointmentItem source,
            IEvent target,
            IEntitySynchronizationLogger logger)
        {
            var outlookMailUri = new Uri("mailto:" + _outlookEmailAddress);
            var serverMailUri = new Uri(_serverEmailUri);
            var ownAttendee = target.Attendees.FirstOrDefault(a =>
                EmailAddress.AreSame(a.Value, serverMailUri) || EmailAddress.AreSame(a.Value, outlookMailUri));

            if (ownAttendee == null)
            {
                return;
            }

            string partStat = null;
            foreach (Recipient recipient in source.Recipients)
            {
                if (IsOwnIdentity(recipient, logger))
                {
                    partStat = ResolveOwnParticipationStatus(source, recipient);
                    break;
                }
            }

            if (partStat == null)
            {
                partStat = MapParticipation1To2(source.ResponseStatus);
            }

            ownAttendee.ParticipationStatus = partStat;
        }

        private void EnsureOrganizerInAttendees(IEvent target)
        {
            var organizer = target?.Organizer;
            if (organizer != null)
            {
                var attendees = new List<IAttendee>(target.Attendees);
                if (attendees.Find(x => EmailAddress.AreSame(x.Value, organizer.Value)) == null)
                {
                    var attendee = new Attendee
                    {
                        ParticipationStatus = "ACCEPTED",
                        Role = "REQ-PARTICIPANT",
                        CommonName = organizer.CommonName,
                        Value = organizer.Value
                    };

                    if (_configuration.ScheduleAgentClient)
                        attendee.Parameters.Add("SCHEDULE-AGENT", "CLIENT");

                    target.Attendees.Add(attendee);
                }
            }
        }

        private string GetMailUrlOrNull(AddressEntry addressEntry, string defaultMailAddress, IEntitySynchronizationLogger logger)
        {
            return CreateMailUriOrNull(OutlookUtility.GetEmailAdressOrNull(addressEntry, logger, s_logger) ?? defaultMailAddress, logger);
        }


        private static string CreateMailUriOrNull(string emailAddressOrNull, IEntitySynchronizationLogger logger)
        {
            if (!string.IsNullOrEmpty(emailAddressOrNull))
            {
                var emailAddressUriString = string.Format("mailto:{0}", emailAddressOrNull);
                if (!Uri.IsWellFormedUriString(emailAddressUriString, UriKind.Absolute))
                {
                    s_logger.WarnFormat("Invalid email address URI {0} for attendee.", emailAddressUriString);
                    logger.LogWarning($"Invalid email address Uri '{emailAddressUriString}' for attendee.");
                    return null;
                }

                return emailAddressUriString;
            }
            else
            {
                return null;
            }
        }

        private async Task MapRecurrance1To2(AppointmentItem source, IEvent target, ITimeZone startIcalTimeZone, ITimeZone endIcalTimeZone, IEntitySynchronizationLogger logger, IEventSynchronizationContext context, IICalendar existingServerCalendarOrNull)
        {
            if (source.IsRecurring)
            {
                using (var sourceRecurrencePatternWrapper = GenericComObjectWrapper.Create(source.GetRecurrencePattern()))
                {
                    var sourceRecurrencePattern = sourceRecurrencePatternWrapper.Inner;
                    IRecurrencePattern targetRecurrencePattern = new RecurrencePattern();

                    // Don't set Count if pattern has NoEndDate or invalid Occurences for some reason.
                    if (!sourceRecurrencePattern.NoEndDate && sourceRecurrencePattern.Occurrences > 0)
                    {
                        // Preserve UNTIL from server if it matches Outlook's PatternEndDate, to avoid
                        // spurious UNTIL→COUNT conversion on roundtrip that can trigger iTIP notifications.
                        var serverMasterEvent = existingServerCalendarOrNull?.Events?.FirstOrDefault(e => e.RecurrenceID == null);
                        var serverUntil = serverMasterEvent?.RecurrenceRules != null && serverMasterEvent.RecurrenceRules.Count > 0
                            ? serverMasterEvent.RecurrenceRules[0].Until : default(DateTime);

                        if (serverUntil != default(DateTime) && serverUntil.Date == sourceRecurrencePattern.PatternEndDate.Date)
                        {
                            targetRecurrencePattern.Until = serverUntil;
                            s_logger.Debug($"RRULE: preserving server UNTIL={serverUntil:o} instead of COUNT={sourceRecurrencePattern.Occurrences}");
                        }
                        else
                        {
                            targetRecurrencePattern.Count = sourceRecurrencePattern.Occurrences;
                            s_logger.Debug($"RRULE: writing COUNT={sourceRecurrencePattern.Occurrences}" +
                                (serverUntil != default(DateTime)
                                    ? $", server UNTIL={serverUntil:o} differs from PatternEndDate={sourceRecurrencePattern.PatternEndDate:d}"
                                    : ", no server UNTIL"));
                        }
                    }

                    if (sourceRecurrencePattern.Interval >= 1)
                    {
                        targetRecurrencePattern.Interval = (sourceRecurrencePattern.RecurrenceType == OlRecurrenceType.olRecursYearly ||
                                                            sourceRecurrencePattern.RecurrenceType == OlRecurrenceType.olRecursYearNth)
                            ? sourceRecurrencePattern.Interval / 12
                            : sourceRecurrencePattern.Interval;
                    }

                    switch (sourceRecurrencePattern.RecurrenceType)
                    {
                        case OlRecurrenceType.olRecursDaily:
                            targetRecurrencePattern.Frequency = FrequencyType.Daily;
                            break;
                        case OlRecurrenceType.olRecursWeekly:
                            targetRecurrencePattern.Frequency = FrequencyType.Weekly;
                            CommonEntityMapper.MapDayOfWeek1To2(sourceRecurrencePattern.DayOfWeekMask, targetRecurrencePattern.ByDay);
                            break;
                        case OlRecurrenceType.olRecursMonthly:
                            targetRecurrencePattern.Frequency = FrequencyType.Monthly;
                            targetRecurrencePattern.ByMonthDay.Add(sourceRecurrencePattern.DayOfMonth);
                            break;
                        case OlRecurrenceType.olRecursMonthNth:
                            targetRecurrencePattern.Frequency = FrequencyType.Monthly;

                            if (sourceRecurrencePattern.Instance == 5)
                            {
                                targetRecurrencePattern.BySetPosition.Add(-1);
                                CommonEntityMapper.MapDayOfWeek1To2(sourceRecurrencePattern.DayOfWeekMask, targetRecurrencePattern.ByDay);
                            }
                            else if (sourceRecurrencePattern.Instance > 0)
                            {
                                targetRecurrencePattern.BySetPosition.Add(sourceRecurrencePattern.Instance);
                                CommonEntityMapper.MapDayOfWeek1To2(sourceRecurrencePattern.DayOfWeekMask, targetRecurrencePattern.ByDay);
                            }
                            else
                            {
                                CommonEntityMapper.MapDayOfWeek1To2(sourceRecurrencePattern.DayOfWeekMask, targetRecurrencePattern.ByDay);
                            }

                            break;
                        case OlRecurrenceType.olRecursYearly:
                            targetRecurrencePattern.Frequency = FrequencyType.Yearly;
                            targetRecurrencePattern.ByMonthDay.Add(sourceRecurrencePattern.DayOfMonth);
                            targetRecurrencePattern.ByMonth.Add(sourceRecurrencePattern.MonthOfYear);
                            break;
                        case OlRecurrenceType.olRecursYearNth:
                            targetRecurrencePattern.Frequency = FrequencyType.Yearly;
                            if (sourceRecurrencePattern.Instance == 5)
                            {
                                targetRecurrencePattern.BySetPosition.Add(-1);
                                CommonEntityMapper.MapDayOfWeek1To2(sourceRecurrencePattern.DayOfWeekMask, targetRecurrencePattern.ByDay);
                            }
                            else if (sourceRecurrencePattern.Instance > 0)
                            {
                                targetRecurrencePattern.BySetPosition.Add(sourceRecurrencePattern.Instance);
                                CommonEntityMapper.MapDayOfWeek1To2(sourceRecurrencePattern.DayOfWeekMask, targetRecurrencePattern.ByDay);
                            }
                            else
                            {
                                CommonEntityMapper.MapDayOfWeek1To2(sourceRecurrencePattern.DayOfWeekMask, targetRecurrencePattern.ByDay);
                            }

                            targetRecurrencePattern.ByMonth.Add(sourceRecurrencePattern.MonthOfYear);
                            break;
                    }

                    target.RecurrenceRules.Add(targetRecurrencePattern);

                    Dictionary<DateTime, PeriodList> targetExceptionDatesByDate = new Dictionary<DateTime, PeriodList>();
                    HashSet<DateTime> originalOutlookDatesWithExceptions = new HashSet<DateTime>();
                    var sourceZone = DateTimeZoneProviders.Bcl.GetSystemDefault();

                    foreach (var sourceException in sourceRecurrencePattern.Exceptions.ToSafeEnumerable<Exception>())
                    {
                        if (!sourceException.Deleted)
                        {
                            // calculate Exception OriginalDate in target timezone.
                            var localExDateTime = LocalDateTime.FromDateTime(sourceException.OriginalDate);
                            var zonedExDateTime = sourceZone.AtLeniently(localExDateTime);
                            DateTime targetExDateTime;

                            if (_configuration.CreateEventsInUTC || startIcalTimeZone == null)
                            {
                                targetExDateTime = zonedExDateTime.ToDateTimeUtc();
                            }
                            else
                            {
                                var targetZone = (_configuration.UseIanaTz) ? DateTimeZoneProviders.Tzdb[startIcalTimeZone.TZID] : DateTimeZoneProviders.Bcl[startIcalTimeZone.TZID];
                                targetExDateTime = zonedExDateTime.WithZone(targetZone).LocalDateTime.ToDateTimeUnspecified();
                            }

                            originalOutlookDatesWithExceptions.Add(targetExDateTime);
                        }
                    }

                    foreach (var sourceException in sourceRecurrencePattern.Exceptions.ToSafeEnumerable<Exception>())
                    {
                        if (!sourceException.Deleted)
                        {
                            targetExceptionDatesByDate.Remove(sourceException.OriginalDate.Date);

                            try
                            {
                                using (var wrapper = new AppointmentItemWrapper(sourceException.AppointmentItem, _ => { throw new InvalidOperationException("Cannot reload exception AppointmentITem!"); }))
                                {
                                    var targetException = new Event();
                                    target.Calendar.Events.Add(targetException);
                                    targetException.UID = target.UID;

                                    var serverEx = TryFindServerExceptionForOutlook(
                                                                            existingServerCalendarOrNull,
                                                                            source,
                                                                            sourceException,
                                                                            sourceZone,
                                                                            startIcalTimeZone,
                                                                            _configuration);
                                    LogOutgoingAttendeeDiagnostics("exception", wrapper.Inner, serverEx, targetException);

                                    await Map1To2(wrapper.Inner, targetException, true, startIcalTimeZone, endIcalTimeZone, logger, context, null);

                                    // Organizer must be the same for all components to avoid SameOrganizerForAllComponentsException
                                    if (target.Organizer != null)
                                        targetException.Organizer = target.Organizer;

                                    // check if new exception is already present in target
                                    // if it is found and not already present as exdate then add a new exdate to avoid 2 events
                                    var from = (wrapper.Inner.Start.Date < sourceException.OriginalDate.Date) ? wrapper.Inner.Start.Date : sourceException.OriginalDate.Date;
                                    var to = (wrapper.Inner.Start.Date > sourceException.OriginalDate.Date) ? wrapper.Inner.Start.Date.AddDays(1) : sourceException.OriginalDate.Date.AddDays(1);

                                    var targetContainsExceptionList = target.GetOccurrences(from, to);
                                    foreach (var el in targetContainsExceptionList)
                                    {
                                        if (!originalOutlookDatesWithExceptions.Contains(el.Period.StartTime.Value))
                                        {
                                            PeriodList targetExList = new PeriodList();

                                            if (!el.Period.StartTime.HasTime)
                                            {
                                                iCalDateTime exDate = new iCalDateTime(el.Period.StartTime.Date);
                                                exDate.HasTime = false;
                                                targetExList.Add(exDate);
                                                targetExList.Parameters.Add("VALUE", "DATE");
                                            }
                                            else
                                            {
                                                targetExList.Add(new iCalDateTime(el.Period.StartTime.AsUtc()) {IsUniversalTime = true});
                                            }

                                            if (!targetExceptionDatesByDate.ContainsKey(el.Period.StartTime.Date))
                                                targetExceptionDatesByDate.Add(el.Period.StartTime.Date, targetExList);
                                        }
                                    }

                                    if (source.AllDayEvent)
                                    {
                                        // Outlook's AllDayEvent relates to Start and not not StartUtc!!!
                                        targetException.RecurrenceID = new iCalDateTime(sourceException.OriginalDate);
                                        targetException.RecurrenceID.HasTime = false;
                                    }
                                    else
                                    {
                                        var localExDateTime = LocalDateTime.FromDateTime(sourceException.OriginalDate);
                                        var zonedExDateTime = sourceZone.AtLeniently(localExDateTime);

                                        // Use same value type and tzid for RECURRENCE-ID as for DTSTART to be compliant with RFC 5545
                                        // see https://tools.ietf.org/html/rfc5545#section-3.8.4.4
                                        if (_configuration.CreateEventsInUTC || startIcalTimeZone == null)
                                        {
                                            var originalDateUtc = zonedExDateTime.ToDateTimeUtc();
                                            targetException.RecurrenceID = new iCalDateTime(originalDateUtc) {IsUniversalTime = true};
                                        }
                                        else
                                        {
                                            var targetZone = (_configuration.UseIanaTz) ? DateTimeZoneProviders.Tzdb[startIcalTimeZone.TZID] : DateTimeZoneProviders.Bcl[startIcalTimeZone.TZID];
                                            var targetExDateTime = zonedExDateTime.WithZone(targetZone).LocalDateTime.ToDateTimeUnspecified();
                                            targetException.RecurrenceID = new iCalDateTime(targetExDateTime);
                                            targetException.RecurrenceID.SetTimeZone(startIcalTimeZone);
                                        }
                                    }
                                }
                            }
                            catch (COMException ex)
                            {
                                s_logger.Warn("Can't get AppointmentItem of Exception, scheduling it to sync later!", ex);
                                logger.LogWarning("Can't get AppointmentItem of Exception, scheduling it to sync later!", ex);

                                var entityId = source.EntryID;
                                if (!string.IsNullOrEmpty(entityId))
                                {
                                    _failedEntityTracker.AddFailedEntity(entityId, "Event", ex);
                                }
                                else
                                {
                                    s_logger.Warn("Can't get EntryID of AppointmentItem, can't schedule it to sync later!");
                                    logger.LogWarning("Can't get EntryID of AppointmentItem, can't schedule it to sync later!");
                                }
                                throw;
                            }
                            catch (ArgumentException x)
                            {
                                s_logger.Warn("Can't get AppointmentItem of Exception, ignoring!", x);
                                logger.LogWarning("Can't get AppointmentItem of Exception, ignoring!", x);
                            }
                        }
                        else
                        {
                            if (!originalOutlookDatesWithExceptions.Contains(sourceException.OriginalDate))
                            {
                                PeriodList targetExList = new PeriodList();

                                if (source.AllDayEvent)
                                {
                                    iCalDateTime exDate = new iCalDateTime(sourceException.OriginalDate);
                                    exDate.HasTime = false;
                                    targetExList.Add(exDate);
                                    targetExList.Parameters.Add("VALUE", "DATE");
                                }
                                else
                                {
                                    string startTimeZoneID;
                                    using (var startTimeZone = GenericComObjectWrapper.Create(source.StartTimeZone))
                                    {
                                        startTimeZoneID = startTimeZone.Inner.ID;
                                    }

                                    var timeZone = TimeZoneInfo.FindSystemTimeZoneById(startTimeZoneID);
                                    var originalDateUtc = TimeZoneInfo.ConvertTimeToUtc(sourceException.OriginalDate, timeZone);
                                    iCalDateTime exDate = new iCalDateTime(originalDateUtc.Add(source.StartInStartTimeZone.TimeOfDay)) {IsUniversalTime = true};

                                    targetExList.Add(exDate);
                                }

                                if (!targetExceptionDatesByDate.ContainsKey(sourceException.OriginalDate))
                                    targetExceptionDatesByDate.Add(sourceException.OriginalDate, targetExList);
                            }
                        }
                    }

                    target.ExceptionDates.AddRange(targetExceptionDatesByDate.Values);
                }
            }
        }

        private void MapRecurrance2To1(IEvent source, IReadOnlyCollection<IEvent> exceptions, IAppointmentItemWrapper targetWrapper, IEntitySynchronizationLogger logger, IEventSynchronizationContext context)
        {
            if (source.RecurrenceRules.Count > 0)
            {
                using (var targetRecurrencePatternWrapper = GenericComObjectWrapper.Create(targetWrapper.Inner.GetRecurrencePattern()))
                {
                    var targetRecurrencePattern = targetRecurrencePatternWrapper.Inner;
                    if (source.RecurrenceRules.Count > 1)
                    {
                        s_logger.WarnFormat("Event '{0}' contains more than one recurrence rule. Since outlook supports only one rule, all except the first one will be ignored.", source.UID);
                        logger.LogWarning("Event contains more than one recurrence rule. Since outlook supports only one rule, all except the first one will be ignored.");
                    }

                    var sourceRecurrencePattern = source.RecurrenceRules[0];

                    switch (sourceRecurrencePattern.Frequency)
                    {
                        case FrequencyType.Daily:
                            if (sourceRecurrencePattern.ByDay.Count > 0)
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursWeekly;
                                targetRecurrencePattern.DayOfWeekMask = CommonEntityMapper.MapDayOfWeek2To1(sourceRecurrencePattern.ByDay);
                            }
                            else
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursDaily;
                            }

                            break;
                        case FrequencyType.Weekly:
                            if (sourceRecurrencePattern.ByDay.Count > 0)
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursWeekly;
                                targetRecurrencePattern.DayOfWeekMask = CommonEntityMapper.MapDayOfWeek2To1(sourceRecurrencePattern.ByDay);
                            }
                            else
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursWeekly;
                            }

                            break;
                        case FrequencyType.Monthly:
                            if (sourceRecurrencePattern.ByDay.Count > 0)
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursMonthNth;
                                if (sourceRecurrencePattern.ByWeekNo.Count > 1)
                                {
                                    s_logger.WarnFormat("Event '{0}' contains more than one week in a monthly recurrence rule. Since outlook supports only one week, all except the first one will be ignored.", source.UID);
                                    logger.LogWarning("Event contains more than one week in a monthly recurrence rule. Since outlook supports only one week, all except the first one will be ignored.");
                                }
                                else if (sourceRecurrencePattern.ByWeekNo.Count > 0)
                                {
                                    targetRecurrencePattern.Instance = sourceRecurrencePattern.ByWeekNo[0];
                                }
                                else
                                {
                                    targetRecurrencePattern.Instance = (sourceRecurrencePattern.ByDay[0].Offset >= 0) ? sourceRecurrencePattern.ByDay[0].Offset : 5;
                                }

                                if (sourceRecurrencePattern.BySetPosition.Count > 0)
                                {
                                    targetRecurrencePattern.Instance = (sourceRecurrencePattern.BySetPosition[0] >= 0) ? sourceRecurrencePattern.BySetPosition[0] : 5;
                                }

                                targetRecurrencePattern.DayOfWeekMask = CommonEntityMapper.MapDayOfWeek2To1(sourceRecurrencePattern.ByDay);
                            }
                            else if (sourceRecurrencePattern.ByMonthDay.Count > 0)
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursMonthly;
                                if (sourceRecurrencePattern.ByMonthDay.Count > 1)
                                {
                                    s_logger.WarnFormat("Event '{0}' contains more than one days in a monthly recurrence rule. Since outlook supports only one day, all except the first one will be ignored.", source.UID);
                                    logger.LogWarning("Event contains more than one days in a monthly recurrence rule. Since outlook supports only one day, all except the first one will be ignored.");
                                }

                                try
                                {
                                    targetRecurrencePattern.DayOfMonth = sourceRecurrencePattern.ByMonthDay[0];
                                }
                                catch (COMException ex)
                                {
                                    s_logger.Warn($"Recurring event '{source.UID}' contains invalid BYMONTHDAY '{sourceRecurrencePattern.ByMonthDay[0]}', which will be ignored.", ex);
                                    logger.LogWarning($"Recurring event '{source.UID}' contains invalid BYMONTHDAY '{sourceRecurrencePattern.ByMonthDay[0]}', which will be ignored.", ex);
                                }
                            }
                            else
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursMonthly;
                            }

                            break;
                        case FrequencyType.Yearly:
                            if (sourceRecurrencePattern.ByMonth.Count > 0 && sourceRecurrencePattern.ByWeekNo.Count > 0)
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursYearNth;
                                if (sourceRecurrencePattern.ByMonth.Count > 1)
                                {
                                    s_logger.WarnFormat("Event '{0}' contains more than one months in a yearly recurrence rule. Since outlook supports only one month, all except the first one will be ignored.", source.UID);
                                    logger.LogWarning("Event contains more than one months in a yearly recurrence rule. Since outlook supports only one month, all except the first one will be ignored.");
                                }

                                if (sourceRecurrencePattern.ByMonth[0] < 1 || sourceRecurrencePattern.ByMonth[0] > 12)
                                {
                                    s_logger.Warn($"Recurring event '{source.UID}' contains invalid BYMONTH '{sourceRecurrencePattern.ByMonth[0]}', which will be ignored.");
                                    logger.LogWarning($"Recurring event '{source.UID}' contains invalid BYMONTH '{sourceRecurrencePattern.ByMonth[0]}', which will be ignored.");
                                }
                                else
                                    targetRecurrencePattern.MonthOfYear = sourceRecurrencePattern.ByMonth[0];

                                if (sourceRecurrencePattern.ByWeekNo.Count > 1)
                                {
                                    s_logger.WarnFormat("Event '{0}' contains more than one week in a yearly recurrence rule. Since outlook supports only one week, all except the first one will be ignored.", source.UID);
                                    logger.LogWarning("Event contains more than one week in a yearly recurrence rule. Since outlook supports only one week, all except the first one will be ignored.");
                                }

                                targetRecurrencePattern.Instance = sourceRecurrencePattern.ByWeekNo[0];

                                targetRecurrencePattern.DayOfWeekMask = CommonEntityMapper.MapDayOfWeek2To1(sourceRecurrencePattern.ByDay);
                            }
                            else if (sourceRecurrencePattern.ByMonth.Count > 0 && sourceRecurrencePattern.ByMonthDay.Count > 0)
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursYearly;
                                if (sourceRecurrencePattern.ByMonth.Count > 1)
                                {
                                    s_logger.WarnFormat("Event '{0}' contains more than one months in a yearly recurrence rule. Since outlook supports only one month, all except the first one will be ignored.", source.UID);
                                    logger.LogWarning("Event contains more than one months in a yearly recurrence rule. Since outlook supports only one month, all except the first one will be ignored.");
                                }

                                if (sourceRecurrencePattern.ByMonth[0] != targetRecurrencePattern.MonthOfYear)
                                {
                                    if (sourceRecurrencePattern.ByMonth[0] < 1 || sourceRecurrencePattern.ByMonth[0] > 12)
                                    {
                                        s_logger.Warn($"Recurring event '{source.UID}' contains invalid BYMONTH '{sourceRecurrencePattern.ByMonth[0]}', which will be ignored.");
                                        logger.LogWarning($"Recurring event '{source.UID}' contains invalid BYMONTH '{sourceRecurrencePattern.ByMonth[0]}', which will be ignored.");
                                    }
                                    else
                                        targetRecurrencePattern.MonthOfYear = sourceRecurrencePattern.ByMonth[0];
                                }

                                if (sourceRecurrencePattern.ByMonthDay.Count > 1)
                                {
                                    s_logger.WarnFormat("Event '{0}' contains more than one days in a monthly recurrence rule. Since outlook supports only one day, all except the first one will be ignored.", source.UID);
                                    logger.LogWarning("Event contains more than one days in a monthly recurrence rule. Since outlook supports only one day, all except the first one will be ignored.");
                                }

                                if (sourceRecurrencePattern.ByMonthDay[0] != targetRecurrencePattern.DayOfMonth)
                                {
                                    try
                                    {
                                        targetRecurrencePattern.DayOfMonth = sourceRecurrencePattern.ByMonthDay[0];
                                    }
                                    catch (COMException ex)
                                    {
                                        s_logger.Warn($"Recurring event '{source.UID}' contains invalid BYMONTHDAY '{sourceRecurrencePattern.ByMonthDay[0]}', which will be ignored.", ex);
                                        logger.LogWarning($"Recurring event '{source.UID}' contains invalid BYMONTHDAY '{sourceRecurrencePattern.ByMonthDay[0]}', which will be ignored.", ex);
                                    }
                                }
                            }
                            else if (sourceRecurrencePattern.ByMonth.Count > 0 && sourceRecurrencePattern.ByDay.Count > 0)
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursYearNth;
                                if (sourceRecurrencePattern.ByMonth.Count > 1)
                                {
                                    s_logger.WarnFormat("Event '{0}' contains more than one months in a yearly recurrence rule. Since outlook supports only one month, all except the first one will be ignored.", source.UID);
                                    logger.LogWarning("Event contains more than one months in a yearly recurrence rule. Since outlook supports only one month, all except the first one will be ignored.");
                                }

                                if (sourceRecurrencePattern.ByMonth[0] < 1 || sourceRecurrencePattern.ByMonth[0] > 12)
                                {
                                    s_logger.Warn($"Recurring event '{source.UID}' contains invalid BYMONTH '{sourceRecurrencePattern.ByMonth[0]}', which will be ignored.");
                                    logger.LogWarning($"Recurring event '{source.UID}' contains invalid BYMONTH '{sourceRecurrencePattern.ByMonth[0]}', which will be ignored.");
                                }
                                else
                                    targetRecurrencePattern.MonthOfYear = sourceRecurrencePattern.ByMonth[0];

                                targetRecurrencePattern.Instance = (sourceRecurrencePattern.ByDay[0].Offset >= 0) ? sourceRecurrencePattern.ByDay[0].Offset : 5;
                                if (sourceRecurrencePattern.BySetPosition.Count > 0)
                                {
                                    targetRecurrencePattern.Instance = (sourceRecurrencePattern.BySetPosition[0] >= 0) ? sourceRecurrencePattern.BySetPosition[0] : 5;
                                }

                                targetRecurrencePattern.DayOfWeekMask = CommonEntityMapper.MapDayOfWeek2To1(sourceRecurrencePattern.ByDay);
                            }
                            else
                            {
                                targetRecurrencePattern.RecurrenceType = OlRecurrenceType.olRecursYearly;
                            }

                            break;
                        default:
                            s_logger.WarnFormat("Recurring event '{0}' contains the Frequency '{1}', which is not supported by outlook. Ignoring recurrence rule.", source.UID, sourceRecurrencePattern.Frequency);
                            logger.LogWarning($"Recurring event contains the Frequency '{sourceRecurrencePattern.Frequency}', which is not supported by outlook. Ignoring recurrence rule.");
                            targetWrapper.Inner.ClearRecurrencePattern();
                            break;
                    }

                    try
                    {
                        targetRecurrencePattern.Interval = (targetRecurrencePattern.RecurrenceType == OlRecurrenceType.olRecursYearly ||
                                                            targetRecurrencePattern.RecurrenceType == OlRecurrenceType.olRecursYearNth)
                            ? sourceRecurrencePattern.Interval * 12
                            : sourceRecurrencePattern.Interval;
                    }
                    catch (COMException ex)
                    {
                        s_logger.Warn($"Recurring event '{source.UID}' contains the Interval '{sourceRecurrencePattern.Interval}', which is not supported by outlook. Ignoring interval.", ex);
                        logger.LogWarning($"Recurring event contains the Interval '{sourceRecurrencePattern.Interval}', which is not supported by outlook. Ignoring interval.", ex);
                    }

                    try
                    {
                        if (sourceRecurrencePattern.Count > 0)
                        {
                            targetRecurrencePattern.Occurrences = sourceRecurrencePattern.Count;
                        }
                        else if (sourceRecurrencePattern.Count == 0)
                        {
                            s_logger.Warn($"Recurring event '{source.UID}' contains COUNT=0, which is invalid. Ignoring the occurence count.");
                            logger.LogWarning($"Recurring event '{source.UID}' contains COUNT=0, which is invalid. Ignoring the occurence count.");
                        }

                        if (sourceRecurrencePattern.Until != default(DateTime))
                        {
                            targetRecurrencePattern.PatternEndDate = sourceRecurrencePattern.Until.Date >= targetRecurrencePattern.PatternStartDate
                                ? sourceRecurrencePattern.Until.Date
                                : targetRecurrencePattern.PatternStartDate;
                        }
                        else if (sourceRecurrencePattern.Count <= 0)
                        {
                            // set explicitly NoEndDate only if there is no positive COUNT and no UNTIL set
                            targetRecurrencePattern.NoEndDate = true;
                        }
                    }
                    catch (COMException ex)
                    {
                        s_logger.Warn($"Recurring event '{source.UID}' contains occurence count or end date, which is not supported by outlook. Ignoring.", ex);
                        logger.LogWarning($"Recurring event contains occurence count or end date, which is not supported by outlook. Ignoring.", ex);
                    }
                }
                // Due to limitations out outlook, the Appointment has to be saved here. Otherwise 'targetRecurrencePattern.GetOccurrence ()'
                // will throw an exception

                targetWrapper.SaveAndReload();

                using (var targetRecurrencePatternWrapper = GenericComObjectWrapper.Create(targetWrapper.Inner.GetRecurrencePattern()))
                {
                    var targetRecurrencePattern = targetRecurrencePatternWrapper.Inner;

                    if (source.ExceptionDates != null)
                    {
                        foreach (IPeriodList exdateList in source.ExceptionDates)
                        {
                            foreach (IPeriod exdate in exdateList)
                            {
                                try
                                {
                                    string startTimeZoneID;
                                    using (var startTimeZone = GenericComObjectWrapper.Create(targetWrapper.Inner.StartTimeZone))
                                    {
                                        startTimeZoneID = startTimeZone.Inner.ID;
                                    }

                                    NodaTime.DateTimeZone startZone = NodaTime.DateTimeZoneProviders.Bcl[startTimeZoneID];
                                    DateTime originalStart;

                                    if (exdate.StartTime.IsUniversalTime)
                                    {
                                        originalStart = NodaTime.Instant.FromDateTimeUtc(exdate.StartTime.Value).InZone(startZone).ToDateTimeUnspecified().Date;
                                    }
                                    else
                                    {
                                        originalStart = exdate.StartTime.Date;
                                    }

                                    var originalExDate = NodaTime.LocalDateTime.FromDateTime(originalStart.Add(targetWrapper.Inner.StartInStartTimeZone.TimeOfDay));

                                    NodaTime.ZonedDateTime zonedExDate = originalExDate.InZoneLeniently(startZone);
                                    NodaTime.ZonedDateTime localExDate = zonedExDate.WithZone(NodaTime.DateTimeZoneProviders.Bcl.GetSystemDefault());

                                    using (var wrapper = GenericComObjectWrapper.Create(targetRecurrencePattern.GetOccurrence(localExDate.ToDateTimeUnspecified())))
                                    {
                                        wrapper.Inner.Delete();
                                    }
                                }
                                catch (COMException ex)
                                {
                                    s_logger.Warn("Can't find occurence of exception, ignoring.", ex);
                                    logger.LogWarning("Can't find occurence of exception, ignoring.", ex);
                                }
                            }
                        }
                    }

                    // to prevent skipping of occurences while moving (outlook throws exception when skipping occurences), moving has to be done in two steps
                    // first move all exceptions which are preponed from earliest to latest
                    MapRecurrenceExceptions2To1(
                        exceptions.Where(e => e.Start.AsUtc() < e.RecurrenceID.Date).OrderBy(e => e.Start.AsUtc()),
                        targetWrapper,
                        targetRecurrencePattern,
                        logger,
                        context);
                    // then move all exceptions which are postponed or are not moved from last to first
                    MapRecurrenceExceptions2To1(
                        exceptions.Where(e => e.Start.AsUtc() >= e.RecurrenceID.Date).OrderByDescending(e => e.Start.AsUtc()),
                        targetWrapper,
                        targetRecurrencePattern,
                        logger,
                        context);
                    // HINT: this algorith will only prevent skipping while moving. If the final state contains skipped occurences, outlook will throw an exception anyway
                }
            }
        }

        private void MapRecurrenceExceptions2To1(
            IEnumerable<IEvent> exceptions,
            IAppointmentItemWrapper targetWrapper,
            Microsoft.Office.Interop.Outlook.RecurrencePattern targetRecurrencePattern,
            IEntitySynchronizationLogger logger,
            IEventSynchronizationContext context)
        {
            foreach (var recurranceException in exceptions)
            {
                try
                {
                    string startTimeZoneID;
                    using (var startTimeZone = GenericComObjectWrapper.Create(targetWrapper.Inner.StartTimeZone))
                    {
                        startTimeZoneID = startTimeZone.Inner.ID;
                    }

                    NodaTime.DateTimeZone startZone = NodaTime.DateTimeZoneProviders.Bcl[startTimeZoneID];
                    DateTime originalStart;

                    if (recurranceException.RecurrenceID.IsUniversalTime)
                    {
                        originalStart = NodaTime.Instant.FromDateTimeUtc(recurranceException.RecurrenceID.Value).InZone(startZone).ToDateTimeUnspecified().Date;
                    }
                    else
                    {
                        originalStart = recurranceException.RecurrenceID.Date;
                    }

                    var originalExDate = NodaTime.LocalDateTime.FromDateTime(originalStart.Add(targetWrapper.Inner.StartInStartTimeZone.TimeOfDay));
                    NodaTime.ZonedDateTime zonedExDate = originalExDate.InZoneLeniently(startZone);
                    NodaTime.ZonedDateTime localExDate = zonedExDate.WithZone(NodaTime.DateTimeZoneProviders.Bcl.GetSystemDefault());

                    var targetException = targetRecurrencePattern.GetOccurrence(localExDate.ToDateTimeUnspecified());

                    using (var exceptionWrapper = new AppointmentItemWrapper(targetException, _ => { throw new InvalidOperationException("cannot reload exception item"); }))

                    {
                        Map2To1(recurranceException, new IEvent[] { }, exceptionWrapper, true, false, logger, context);

                        if (_outlookMajorVersion >= 15 && recurranceException.Organizer != null &&
                            recurranceException.Attendees.Count > 0)
                        {
                            using (var pa = GenericComObjectWrapper.Create(targetException.PropertyAccessor))
                            {
                                var uid = recurranceException.UID;
                                byte[] globalId = AppointmentItemUtils.IsGlobalAppointmentId(uid)
                                    ? AppointmentItemUtils.CreateGlobalExceptionIdFromGlobalAppointmentId(uid, originalStart)
                                    : OutlookUtility.MapUidToGlobalExceptionId(uid, originalStart);
                                try
                                {
                                    pa.Inner.SetProperty(PR_GLOBAL_OBJECT_ID, globalId);
                                }
                                catch (COMException ex)
                                {
                                    s_logger.Warn($"Can't set GlobalAppointmentID of meeting exception'{targetException.EntryID}'.", ex);
                                }
                            }
                        }

                        exceptionWrapper.Inner.Save();
                    }
                }
                catch (COMException ex)
                {
                    s_logger.Warn("Can't find occurence of exception or exception can't be saved, ignoring.", ex);
                    logger.LogWarning("Can't find occurence of exception or exception can't be saved, ignoring.", ex);
                }
            }
        }

        private async Task<bool> MapAttendees1To2(AppointmentItem source, IEvent target, IEntitySynchronizationLogger logger)
        {
            var organizerSet = false;
            var ownAttendeeSet = false;

            foreach (var recipient in source.Recipients.ToSafeEnumerable<Recipient>())
            {
                string recipientMailAddressOrNull = null;
                var resolveSuccess = false;
                try
                {
                    resolveSuccess = recipient.Resolve();
                    if (resolveSuccess)
                    {
                        using (var entryWrapper = GenericComObjectWrapper.Create(recipient.AddressEntry))
                        {
                            recipientMailAddressOrNull = OutlookUtility.GetEmailAdressOrNull(entryWrapper.Inner, logger, s_logger);
                        }
                    }
                }
                catch (COMException ex)
                {
                    s_logger.Warn("Can't get AddressEntry of recipient", ex);
                    logger.LogWarning("Can't get AddressEntry of recipient", ex);
                }

                var nameWithoutEmail = OutlookUtility.RemoveEmailFromName(recipient);
                nameWithoutEmail = Regex.Replace(nameWithoutEmail, @"( <[^<>]*>)+$", string.Empty);

                if ((OlMeetingRecipientType) recipient.Type == OlMeetingRecipientType.olResource)
                {
                    var attendee = new Attendee();
                    attendee.Type = "RESOURCE";
                    attendee.ParticipationStatus = "ACCEPTED";
                    attendee.CommonName = nameWithoutEmail;
                    attendee.Role = "REQ-PARTICIPANT";

                    var resourceUri = await _calendarResourceResolver.GetResourceUriOrNull(nameWithoutEmail);

                    if (resourceUri != null)
                    {
                        attendee.Value = resourceUri;
                        if (!string.IsNullOrEmpty(recipientMailAddressOrNull))
                        {
                            attendee.Parameters.Add("EMAIL", recipientMailAddressOrNull);
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(recipient.Address))
                        {
                            var recipientMailUrl = CreateMailUriOrNull(recipientMailAddressOrNull ?? recipient.Address, logger);
                            if (recipientMailUrl != null)
                            {
                                attendee.Value = new Uri(recipientMailUrl);
                            }
                        }
                    }

                    target.Attendees.Add(attendee);
                }
                else if (!IsOwnIdentity(recipientMailAddressOrNull))
                {
                    // Guard: if this is the organizer of own meeting but IsOwnIdentity returned false
                    // (e.g. email cache miss), don't add as regular attendee to avoid duplicating the
                    // organizer in ATTENDEE. EnsureOrganizerInAttendees will add them correctly.
                    // We still allow the loop to continue so SetOrganizer can be called below.
                    var skipAttendee = (OlMeetingRecipientType)recipient.Type == OlMeetingRecipientType.olOrganizer && source.MeetingStatus == OlMeetingStatus.olMeeting;
                    if (skipAttendee)
                    {
                        s_logger.Warn($"Skipping organizer '{recipient.Address}' as regular attendee (IsOwnIdentity=false likely due to cache miss, MeetingStatus=olMeeting).");
                    }

                    var attendee = new Attendee();

                    if (!string.IsNullOrEmpty(recipient.Address))
                    {
                        var recipientMailUrl = CreateMailUriOrNull(recipientMailAddressOrNull ?? recipient.Address, logger);
                        if (recipientMailUrl != null)
                        {
                            attendee.Value = new Uri(recipientMailUrl);
                        }
                    }

                    if (attendee.Value == null)
                    {
                        var emailFromName = TryExtractEmailFromRecipientName(recipient.Name);
                        if (emailFromName != null)
                        {
                            var mailUrl = CreateMailUriOrNull(emailFromName, logger);
                            if (mailUrl != null)
                            {
                                attendee.Value = new Uri(mailUrl);
                                s_logger.Info($"Recovered attendee email from recipient name '{emailFromName}'.");
                            }
                        }
                    }

                    // Second fallback: try to extract email from recipient.Address if it is in
                    // "Name <email>" format (Outlook may store it this way when Resolve() fails).
                    if (attendee.Value == null && !string.IsNullOrEmpty(recipient.Address))
                    {
                        var emailFromAddress = TryExtractEmailFromRecipientName(recipient.Address);
                        if (emailFromAddress != null)
                        {
                            var mailUrl = CreateMailUriOrNull(emailFromAddress, logger);
                            if (mailUrl != null)
                            {
                                attendee.Value = new Uri(mailUrl);
                                s_logger.Info($"Recovered attendee email from recipient address field '{emailFromAddress}'.");
                            }
                        }
                    }

                    if (attendee.Value == null)
                    {
                        if (!skipAttendee)
                        {
                            s_logger.Warn($"Can't determine mail address: Name='{recipient.Name}', Address='{recipient.Address}', Type={recipient.Type}, Resolve={resolveSuccess}.");
                            logger.LogWarning($"Can't determine mail address of attendee '{recipient.Name}' and no fallback email found in recipient name.");
                        }
                        continue;
                    }

                    attendee.ParticipationStatus = MapParticipation1To2(recipient.MeetingResponseStatus);
                    attendee.CommonName = nameWithoutEmail;
                    attendee.Role = MapAttendeeType1To2((OlMeetingRecipientType) recipient.Type);

                    attendee.RSVP = true;
                    if (_configuration.ScheduleAgentClient)
                    {
                        attendee.Parameters.Add("SCHEDULE-AGENT", "CLIENT");
                    }
                    if (!skipAttendee)
                    {
                        target.Attendees.Add(attendee);
                    }


                }
                else
                {
                    if ((source.MeetingStatus == OlMeetingStatus.olMeetingReceived || source.MeetingStatus == OlMeetingStatus.olMeetingReceivedAndCanceled) && (!ownAttendeeSet))
                    {
                        var ownAttendee = new Attendee();

                        if (!string.IsNullOrEmpty(recipient.Address))
                        {
                            var recipientMailUrl = CreateMailUriOrNull(recipientMailAddressOrNull ?? recipient.Address, logger);
                            if (recipientMailUrl != null)
                            {
                                ownAttendee.Value = new Uri(recipientMailUrl);
                            }
                        }

                        ownAttendee.CommonName = nameWithoutEmail;
                        ownAttendee.ParticipationStatus = ResolveOwnParticipationStatus(source, recipient);
                        ownAttendee.Role = MapAttendeeType1To2((OlMeetingRecipientType) recipient.Type);
                        if (_configuration.ScheduleAgentClient)
                            ownAttendee.Parameters.Add("SCHEDULE-AGENT", "CLIENT");
                        target.Attendees.Add(ownAttendee);
                        ownAttendeeSet = true;
                    }
                }

                if (((OlMeetingRecipientType) recipient.Type) == OlMeetingRecipientType.olOrganizer)
                {
                    if (!string.IsNullOrEmpty(recipient.Address))
                    {
                        using (var entryWrapper = GenericComObjectWrapper.Create(recipient.AddressEntry))
                        {
                            SetOrganizer(target, entryWrapper.Inner, recipient.Address, logger);
                        }
                    }
                    else
                    {
                        SetOrganizer(target, recipient.Name, null, logger);
                    }

                    SetOrganizerSchedulingParameters(source, target, logger);
                    organizerSet = true;
                }
            }

            return organizerSet;
        }

        private bool IsOwnIdentity(Recipient recipient, IEntitySynchronizationLogger logger)
        {
            try
            {
                if (recipient.Resolve())
                {
                    string mailAddress;
                    using (var wrapper = GenericComObjectWrapper.Create(recipient.AddressEntry))
                        mailAddress = OutlookUtility.GetEmailAdressOrNull(wrapper.Inner, NullEntitySynchronizationLogger.Instance,
                            s_logger);
                    return IsOwnIdentity(mailAddress);
                }
                else
                    return false;
            }
            catch (COMException ex)
            {
                s_logger.Warn("Can't get AddressEntry of recipient", ex);
                logger.LogWarning("Can't get AddressEntry of recipient", ex);
                return false;
            }
        }

        private bool IsOwnIdentity(string mailAddress)
        {
            return EmailAddress.AreSame(mailAddress, _outlookEmailAddress);
        }

        public string MapAttendeeType1To2(OlMeetingRecipientType recipientType)
        {
            switch (recipientType)
            {
                case OlMeetingRecipientType.olOptional:
                    return "OPT-PARTICIPANT";
                case OlMeetingRecipientType.olRequired:
                case OlMeetingRecipientType.olResource:
                    return "REQ-PARTICIPANT";
                case OlMeetingRecipientType.olOrganizer:
                    return "CHAIR";
            }

            throw new NotImplementedException(string.Format("Mapping for value '{0}' not implemented.", recipientType));
        }

        public OlMeetingRecipientType MapAttendeeType2To1(string recipientType)
        {
            switch (recipientType)
            {
                case null:
                case "NON-PARTICIPANT":
                case "OPT-PARTICIPANT":
                    return OlMeetingRecipientType.olOptional;
                case "REQ-PARTICIPANT":
                    return OlMeetingRecipientType.olRequired;
                case "CHAIR":
                    return OlMeetingRecipientType.olOrganizer;
                case "X-LOCATION":
                    return OlMeetingRecipientType.olResource;
                // according to the RFC 5545 unknown values must be treated as REQ-PARTICIPANT
                default:
                    return OlMeetingRecipientType.olRequired;
            }
        }


        private const int s_mailtoSchemaLength = 7; // length of "mailto:"

        public Task<IAppointmentItemWrapper> Map2To1(IICalendar sourceCalendar, IAppointmentItemWrapper target, IEntitySynchronizationLogger logger, IEventSynchronizationContext context)
        {
            //XXX Причина дублей: ранний Respond() на свежесозданном Outlook item может спровоцировать повторное пересоздание встречи.
            //XXX Исправление в маппере: для Accept используем defer+marker и позже выполняем безопасный дожим Respond().
            //XXX В этом маппинге происходит "финализация" invite в Outlook:
            //XXX здесь мы решаем, вызывать ли Respond() сразу или отложить его через marker,
            //XXX чтобы одновременно убрать дубликаты и сохранить корректные кнопки/визуальный статус встречи.
            IEvent sourceMasterEvent = null;
            IReadOnlyCollection<IEvent> sourceExceptionEvents;
            var isFreshlyCreatedTarget = target.Inner.EntryID == null;

            var sourceEvents = sourceCalendar.Events;

            target.Inner.ResponseRequested = true;

            if (sourceEvents.Count == 1)
            {
                sourceMasterEvent = sourceEvents[0];
                sourceExceptionEvents = new IEvent[] { };
            }
            else
            {
                var sourceExceptionEventsList = new List<IEvent>();
                sourceExceptionEvents = sourceExceptionEventsList;

                foreach (var sourceEvent in sourceEvents)
                {
                    if (sourceEvent.RecurrenceID == null)
                        sourceMasterEvent = sourceEvent;
                    else
                        sourceExceptionEventsList.Add(sourceEvent);
                }

                // TODO
                // Maybe it is a good idea to sort the exception events here by RecurrenceId
            }

            if (sourceMasterEvent == null)
            {
                s_logger.Warn("Detected CalDav Event which contains only exceptions. Reconstructing master event.");
                logger.LogWarning("Detected CalDav Event which contains only exceptions. Reconstructing master event.");
                AddMasterEvent(sourceCalendar);
                return Map2To1(sourceCalendar, target, logger, context);
            }

            // Map UID to GlobalAppointmentID for new meetings to avoid double events from Mail invites
            // only for the master Appointment and only for Outlook >= 2013
            if (target.Inner.EntryID == null && _outlookMajorVersion >= 15)
            {
                using (var pa = GenericComObjectWrapper.Create(target.Inner.PropertyAccessor))
                {
                    target.Inner.Save();

                    var uid = sourceMasterEvent.UID;
                    byte[] globalId = AppointmentItemUtils.IsGlobalAppointmentId(uid) 
                        ? AppointmentItemUtils.ConvertHexStringToByteArray(uid)
                        : OutlookUtility.MapUidToGlobalId(uid);

                    try
                    {
                        pa.Inner.SetProperty(PR_GLOBAL_OBJECT_ID, globalId);
                        pa.Inner.SetProperty(PR_CLEAN_GLOBAL_OBJECT_ID, globalId);
                        target.SaveAndReload();
                    }
                    catch (COMException ex)
                    {
                        s_logger.Warn($"Can't set GlobalAppointmentID of meeting '{target.Inner.EntryID}'.", ex);
                    }
                }
            }

            return Task.FromResult(Map2To1(sourceMasterEvent, sourceExceptionEvents, target, false, isFreshlyCreatedTarget, logger, context));
        }

        private void AddMasterEvent(IICalendar calendar)
        {
            if (calendar.Events.Count < 2)
                throw new ArgumentException("Calendar has to contain at least two events", nameof(calendar));

            var sortedEvents = calendar.Events.OrderBy(e => e.RecurrenceID).ToArray();

            var masterEvent = new Event();
            var firstException = sortedEvents[0];
            masterEvent.Start = firstException.RecurrenceID;
            masterEvent.Summary = firstException.Summary;
            masterEvent.Location = firstException.Location;
            masterEvent.Class = firstException.Class;
            masterEvent.Categories.AddRange(firstException.Categories.ToArray());
            masterEvent.Organizer = firstException.Organizer;
            masterEvent.Attendees = firstException.Attendees;
            masterEvent.UID = firstException.UID;

            var sortedExceptionsWithDistance =
                new[] {new {Event = firstException, DistanceFromMasterInDays = 0}}
                    .Union(
                        sortedEvents
                            .Zip(
                                sortedEvents.Skip(1),
                                (first, second) => new
                                {
                                    Event = second,
                                    DistanceFromMasterInDays = (int) Math.Round((second.RecurrenceID.Value - first.RecurrenceID.Value).TotalDays, MidpointRounding.AwayFromZero)
                                }))
                    .ToArray();

            var intervalInDays = GreatestCommonDivisor(sortedExceptionsWithDistance.Select(d => d.DistanceFromMasterInDays));

            var numberOfEceptions = sortedExceptionsWithDistance.Last().DistanceFromMasterInDays / intervalInDays + 1;
            masterEvent.RecurrenceRules.Add(new RecurrencePattern(FrequencyType.Daily, intervalInDays)
            {
                Count = numberOfEceptions
            });

            var exDates = new PeriodList();

            int currentExceptionIndex = 0;
            for (int occurence = 0; occurence < numberOfEceptions; occurence++)
            {
                var currentDistanceFromMasterInDays = occurence * intervalInDays;
                var originalDate = masterEvent.Start.AddDays(currentDistanceFromMasterInDays);
                if (sortedExceptionsWithDistance[currentExceptionIndex].DistanceFromMasterInDays == currentDistanceFromMasterInDays)
                {
                    // The recurrence Id has to be set, since the original value was rounded to calculate the interval
                    sortedExceptionsWithDistance[currentExceptionIndex].Event.RecurrenceID = originalDate;
                    currentExceptionIndex++;
                }
                else
                {
                    exDates.Add(new Period(originalDate));
                }
            }

            masterEvent.ExceptionDates.Add(exDates);
            calendar.Events.Add(masterEvent);
        }

        static int GreatestCommonDivisor(int a, int b)
        {
            return b == 0 ? a : GreatestCommonDivisor(b, a % b);
        }

        private static int GreatestCommonDivisor(IEnumerable<int> values)
        {
            return values.Aggregate(GreatestCommonDivisor);
        }

        private IAppointmentItemWrapper Map2To1(
            IEvent source,
            IReadOnlyCollection<IEvent> recurrenceExceptionsOrNull,
            IAppointmentItemWrapper targetWrapper,
            bool isRecurrenceException,
            bool isFreshlyCreatedTarget,
            IEntitySynchronizationLogger logger,
            IEventSynchronizationContext context)
        {
            if (!isRecurrenceException && targetWrapper.Inner.IsRecurring)
            {
                targetWrapper.Inner.ClearRecurrencePattern();
                targetWrapper.SaveAndReload();
            }

            if (source.IsAllDay)
            {
                targetWrapper.Inner.Start = source.Start.Value;
                if (source.End == null)
                {
                    targetWrapper.Inner.End = source.Start.Value.AddDays(1);
                }
                else if (source.End.Value <= source.Start.Value)
                {
                    s_logger.Warn("Invalid EndDate of appointment, setting to StartDate + 1 day.");
                    logger.LogWarning("Invalid EndDate of appointment, setting to StartDate + 1 day.");
                    targetWrapper.Inner.End = source.Start.Value.AddDays(1);
                }
                else
                {
                    targetWrapper.Inner.End = source.End.Value;
                }

                targetWrapper.Inner.AllDayEvent = true;
            }
            else
            {
                targetWrapper.Inner.AllDayEvent = false;

                if (!string.IsNullOrEmpty(source.Start.TZID))
                    MapTimeZone2To1(source.Start.TZID, tz => targetWrapper.Inner.StartTimeZone = tz, "set StartTimeZone of appointment", logger);

                if (source.Start.IsUniversalTime)
                {
                    targetWrapper.Inner.StartUTC = source.Start.Value;
                }
                else
                {
                    targetWrapper.Inner.StartInStartTimeZone = source.Start.Value;
                }

                if (source.DTEnd != null)
                {
                    if (!string.IsNullOrEmpty(source.DTEnd.TZID))
                        MapTimeZone2To1(source.DTEnd.TZID, tz => targetWrapper.Inner.EndTimeZone = tz, "set EndTimeZone of appointment", logger);

                    try
                    {
                        if (source.DTEnd.IsUniversalTime)
                        {
                            targetWrapper.Inner.EndUTC = source.DTEnd.Value;
                        }
                        else
                        {
                            targetWrapper.Inner.EndInEndTimeZone = source.DTEnd.Value;
                        }
                    }
                    catch (COMException ex)
                    {
                        s_logger.Warn("Invalid EndTime of appointment, setting StartTime.", ex);
                        logger.LogWarning("Invalid EndTime of appointment, setting StartTime.", ex);
                        if (source.Start.HasTime)
                        {
                            targetWrapper.Inner.EndTimeZone = targetWrapper.Inner.StartTimeZone;
                            targetWrapper.Inner.End = targetWrapper.Inner.Start;
                        }
                        else
                        {
                            targetWrapper.Inner.EndUTC = source.Start.AddDays(1).AsUtc();
                        }
                    }
                }
                else if (source.Start.HasTime)
                {
                    targetWrapper.Inner.EndTimeZone = targetWrapper.Inner.StartTimeZone;
                    targetWrapper.Inner.End = targetWrapper.Inner.Start;
                }
                else
                {
                    targetWrapper.Inner.EndUTC = source.Start.AddDays(1).AsUtc();
                }
            }

            targetWrapper.Inner.Subject = source.Summary;
            if (source.Status == EventStatus.Cancelled)
            {
                if (string.IsNullOrEmpty(targetWrapper.Inner.Subject))
                    targetWrapper.Inner.Subject = "Cancelled: ";
                else if (!targetWrapper.Inner.Subject.StartsWith("Cancelled: "))
                    targetWrapper.Inner.Subject = "Cancelled: " + targetWrapper.Inner.Subject;
            }

            targetWrapper.Inner.Location = source.Location;

            MapBody2To1(source, targetWrapper.Inner, logger);

            targetWrapper.Inner.Importance = CommonEntityMapper.MapPriority2To1(source.Priority);

            if (_configuration.MapAttendees)
                MapAttendeesAndOrganizer2To1(source, targetWrapper.Inner, logger);

            if (!isRecurrenceException)
                MapRecurrance2To1(source, recurrenceExceptionsOrNull, targetWrapper, logger, context);

            if (!isRecurrenceException)
            {
                try
                {
                    var sensitivity = CommonEntityMapper.MapPrivacy2To1(source.Class,
                        _configuration.MapClassConfidentialToSensitivityPrivate, _configuration.MapClassPublicToSensitivityPrivate);
                    targetWrapper.Inner.Sensitivity = sensitivity;
                }
                catch (System.Exception exc)
                {
                    s_logger.Warn($"Failed to update AppointmentItem.Sensitivity: {exc.Message}");
                }
            }


            MapReminder2To1(source, targetWrapper.Inner, isRecurrenceException, logger);

            if (!isRecurrenceException)
                MapCategories2To1(source, targetWrapper.Inner, context, logger);

            targetWrapper.Inner.BusyStatus = MapTransparency2To1(source);

            if (_configuration.MapAttendees && source.Organizer != null)
            {
                var ownSourceAttendee = source.Attendees.FirstOrDefault((a) =>
                    {
                        try
                        {
                            return StringComparer.InvariantCultureIgnoreCase.Compare(a.Value != null ? a.Value.ToString() : null, _serverEmailUri) == 0;
                        }
                        catch (UriFormatException)
                        {
                            return false;
                        }
                    }
                );

                if (source.Status == EventStatus.Cancelled)
                {
                    targetWrapper.Inner.MeetingStatus = OlMeetingStatus.olMeetingReceivedAndCanceled;
                }
                else if (ownSourceAttendee != null && targetWrapper.Inner.ResponseStatus != OlResponseStatus.olResponseOrganized && !_configuration.OrganizerAsDelegate)
                {
                    var response = MapParticipation2ToMeetingResponse(ownSourceAttendee.ParticipationStatus);
                    var mappedResponseStatus = MapParticipation2To1(ownSourceAttendee.ParticipationStatus);
                    //XXX hasDeferredRespond проверяет два источника:
                    //XXX 1) MAPI-свойство на item - для случаев, когда mapper сам поставил маркер (freshly created Accept);
                    //XXX 2) DeferredRespondStorage по UID - для случаев UID-конвертации из интерцептора (без Save() и без MAPI-свойства).
                    var hasDeferredRespond = IsDeferredRespondPending(targetWrapper.Inner, out var deferredRespondAtUtc);
                    if (!hasDeferredRespond && _deferredRespondStorage != null && !string.IsNullOrEmpty(source.UID))
                    {
                        DateTime storageMarkedAt;
                        if (_deferredRespondStorage.IsPending(source.UID, out storageMarkedAt))
                        {
                            hasDeferredRespond = true;
                            deferredRespondAtUtc = storageMarkedAt;
                        }
                    }

                    // show received meetings without response as tentative
                    if (response == null && !source.Properties.ContainsKey("X-MICROSOFT-CDO-BUSYSTATUS"))
                    {
                        targetWrapper.Inner.BusyStatus = OlBusyStatus.olTentative;
                    }

                    if (response == null)
                    {
                        if (hasDeferredRespond)
                        {
                            ClearDeferredRespondPending(targetWrapper.Inner, source.UID);
                        }
                        s_logger.Debug($"Skip meeting response (no mapping) for UID '{source.UID}'");
                    }
                    //XXX Если marker активен, просто "already matches" недостаточно: нужно все равно пройти Respond(),
                    //XXX иначе Outlook может оставить старый визуальный статус/кнопки даже при правильном ResponseStatus.
                    else if (mappedResponseStatus == targetWrapper.Inner.ResponseStatus && !hasDeferredRespond)
                    {
                        s_logger.Debug($"Skip meeting response (already matches) for UID '{source.UID}'");
                    }
                    //XXX Деферим только Accept на только что созданном Outlook item.
                    //XXX Это снижает риск дубликатов от раннего Respond(), но не ломает сценарии Tentative/Decline.
                    else if (isFreshlyCreatedTarget && !isRecurrenceException && response == OlMeetingResponse.olMeetingAccepted && MarkDeferredRespondPending(targetWrapper.Inner))
                    {
                        s_logger.Debug($"Defer applying accepted meeting response for UID '{source.UID}' because the target is freshly created in this sync pass.");
                    }
                    else
                    {
                        if (hasDeferredRespond)
                        {
                            s_logger.Debug($"Applying deferred meeting response for UID '{source.UID}' (deferrad at {deferredRespondAtUtc:o}.");
                        }
                        s_logger.Debug($"Applying meeting response for UID '{source.UID}'");

                        if (response == OlMeetingResponse.olMeetingDeclined)
                        {
                            targetWrapper.Inner.MeetingStatus = OlMeetingStatus.olMeetingReceivedAndCanceled;
                            if (hasDeferredRespond)
                            {
                                ClearDeferredRespondPending(targetWrapper.Inner, source.UID);
                            }
                        }
                        else
                        {
                            if (targetWrapper.Inner.MeetingStatus == OlMeetingStatus.olNonMeeting)
                            {
                                s_logger.Debug($"Skip meeting response for UID '{source.UID}' because target is non-meeting");
                            }
                            else
                            {
                                try
                                {
                                    using (var newMeetingItem = GenericComObjectWrapper.Create(targetWrapper.Inner.Respond(response.Value)))
                                    {
                                        var newAppointment = newMeetingItem.Inner.GetAssociatedAppointment(false);
                                        if (newAppointment != null)
                                        {
                                            targetWrapper.Replace(newAppointment);
                                        }
                                    }
                                    if (hasDeferredRespond)
                                    {
                                        ClearDeferredRespondPending(targetWrapper.Inner, source.UID);
                                    }
                                    s_logger.Debug($"Applied meeting response for UID '{source.UID}'");
                                }
                                catch (System.Exception ex)
                                {
                                    s_logger.Warn("Can't respond to meeting invite.", ex);
                                    logger.LogWarning("Can't respond to meeting invite.", ex);
                                }
                            }
                        }
                    }
                }
                else if (ownSourceAttendee == null)
                {
                    s_logger.Info(
                        $"No own attendee found for UID '{source.UID}'");
                }
            }

            return targetWrapper;
        }

        void MapTimeZone2To1(string timeZoneId, Action<Microsoft.Office.Interop.Outlook.TimeZone> actionWithMappedValue, string actionNameForLogging, IEntitySynchronizationLogger logger)
        {
            try
            {
                var timeZone = _outlookTimeZones[timeZoneId];
                if (timeZone != null)
                {
                    actionWithMappedValue(timeZone);
                }
                else
                {
                    var logMessage = $"Could not {actionNameForLogging}, because a local timezone '{timeZoneId}' did not exist";
                    s_logger.Warn(logMessage);
                    logger.LogWarning(logMessage);
                }
            }
            catch (COMException ex)
            {
                var logMessage = $"Can't {actionNameForLogging}.";
                s_logger.Warn(logMessage, ex);
                logger.LogWarning(logMessage, ex);
            }
        }

        private void MapCategories2To1(IEvent source, AppointmentItem target, IEventSynchronizationContext context, IEntitySynchronizationLogger logger)
        {
            var targetCategorySortOrderByCategory = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);
            for (var i = 0; i < source.Categories.Count; i++)
                targetCategorySortOrderByCategory[source.Categories[i]] = i;

            if (_configuration.UseEventCategoryAsFilter && !_configuration.InvertEventCategoryFilter && !targetCategorySortOrderByCategory.ContainsKey(_configuration.EventCategory))
            {
                targetCategorySortOrderByCategory.Add(_configuration.EventCategory, targetCategorySortOrderByCategory.Count);
            }

            if (_configuration.MapEventColorToCategory && source.Properties.ContainsKey("COLOR"))
            {
                var htmlColor = source.Properties["COLOR"].Value.ToString();
                var category = context.MapHtmlColorToCategoryOrNull(htmlColor, logger);
                if (category != null)
                    targetCategorySortOrderByCategory[category] = -1;
            }

            target.Categories = string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator, targetCategorySortOrderByCategory.OrderBy(e => e.Value).Select(e => e.Key));
        }

        private void MapBody2To1(IEvent source, AppointmentItem target, IEntitySynchronizationLogger logger)
        {
            if (_configuration.MapBody)
            {
                target.Body = source.Description;
                if (!string.IsNullOrWhiteSpace(source.Description) && IsBodyBroken(source.Description, target.Body))
                {
                    s_logger.Info($"Error on mapping Description, using RTF bypass. \r\n Calendar: {source.Description} \r\n Outlook: {target.Body}");
                    target.RTFBody = ConvertTextToRtf(source.Description);
                    Telemetry.Signal(Telemetry.ConfirmedBugEvent, "error_mapping_description_2To1");
                }
            }
            else
            {
                target.Body = string.Empty;
            }
        }

        private byte[] ConvertTextToRtf(string text)
        {
            using (var rtb = new System.Windows.Forms.RichTextBox())
            {
                rtb.Font = new System.Drawing.Font("Calibri", 12f);
                rtb.Text = text;
                return System.Text.Encoding.GetEncoding(1251).GetBytes(rtb.Rtf);
            }
        }
        private bool IsBodyBroken(string original, string encoded)
        {
            try
            {
                if (string.IsNullOrEmpty(original) && string.IsNullOrEmpty(encoded)) return false;

                if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(encoded)) return true;

                // Символ �
                if (encoded.Contains('\uFFFD') && !original.Contains('\uFFFD')) return true;

                // Проверяю html и др. рабочие теги
                if (_htmlTag.IsMatch(encoded) && !_htmlTag.IsMatch(original)) return true;
                if (_ctrlChars.IsMatch(encoded) && !_ctrlChars.IsMatch(original)) return true;

                // На всякий случай фиксирую, если аномально увеличилась длина строки
                if (encoded.Length > original.Length * 1.3) return true;

                // Хвостовые «пустые» символы (пробелы, \t, \0, NBSP), которых нет в оригинале
                if (EndsWithPadding(encoded) && !EndsWithPadding(original))
                {
                    var trimmed = encoded.TrimEnd();
                    var padding = encoded.Substring(trimmed.Length);

                    //Игнорирование нормального паддинга от Outlook: пробел + \r\n
                    if (padding == " \r\n" || padding == " \r" || padding == " \n")
                    {
                        return false;
                    }
                    return true;
                }

            }
            catch (System.Exception ex)
            {
                s_logger.Error($"Unexpected error on attemp to check IsBodyBroken. Original: {original} \r\n Encoded: {encoded}", ex);
                return true;
            }
           
            return false;
        }

        private static bool EndsWithPadding(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;

            int i = s.Length;
            while (i > 0 && (s[i - 1] == '\n' ||  s[i - 1] == '\r'))
                i--;

            if (i == 0) return false;

            //Проверяем символ, идущий сразу после CR/LF-хвоста
            char last = s[i - 1];

            return last == '\0' || // null-byte
                    last == '\u00A0' ||   // NBSP
                    last == ' ' || 
                    last == '\t';

        }

        private void MapAttendeesAndOrganizer2To1(IEvent source, AppointmentItem target, IEntitySynchronizationLogger logger)
        {
            var recipientsToDispose = new HashSet<Recipient>();
            int recipientsBefore;
            try 
            {
                recipientsBefore = target.Recipients.Count;
            }
            catch (COMException)
            {
                recipientsBefore = -1;
            }

            try
            {
                var targetRecipientsWhichShouldRemain = new HashSet<Recipient>();
                var indexByEmailAddresses = GetOutlookRecipientsByEmailAddressesOrName(target, recipientsToDispose, logger);

                // Fix some issues in the attendees list, before processing.

                string sourceOrganizerEmail = string.Empty;
                if (source.Organizer != null && source.Organizer.Value != null)
                {
                    try
                    {
                        sourceOrganizerEmail = source.Organizer.Value.ToString().Substring(s_mailtoSchemaLength);
                    }
                    catch (UriFormatException ex)
                    {
                        s_logger.Warn("Ignoring invalid Uri in organizer email.", ex);
                        logger.LogWarning("Ignoring invalid Uri in organizer email.", ex);
                    }
                }

                var sourceAttendeesSnapshot = source.Attendees.Select(x => x.Copy<Attendee>()).ToList();
                var attendees = PrepareAttendeesList(sourceAttendeesSnapshot, source, logger, out var distinctEmailsAfterOrganizerDedup);
                WarnIfPrepLostDistinctAttendees(distinctEmailsAfterOrganizerDedup, attendees, logger);

                var isIncomingInvite = source.Organizer != null
                                       && source.Organizer.Value != null
                                       && !EmailAddress.AreSame(sourceOrganizerEmail, _outlookEmailAddress);

                if (isIncomingInvite)
                {
                    target.MeetingStatus = OlMeetingStatus.olMeetingReceived;
                    SetupIncomingMeetingOrganizer(
                        source,
                        target,
                        sourceOrganizerEmail,
                        targetRecipientsWhichShouldRemain,
                        recipientsToDispose,
                        logger);
                    indexByEmailAddresses = GetOutlookRecipientsByEmailAddressesOrName(target, recipientsToDispose, logger);

                    foreach (var attendee in attendees)
                    {
                        TryMapServerAttendeeToOutlookRecipient(
                            attendee,
                            target,
                            indexByEmailAddresses,
                            targetRecipientsWhichShouldRemain,
                            recipientsToDispose,
                            logger,
                            logActions: false,
                            actionPrefix: string.Empty);
                    }

                    indexByEmailAddresses = GetOutlookRecipientsByEmailAddressesOrName(target, recipientsToDispose, logger);
                    VerifyAndRepairIncomingAttendees(
                        attendees,
                        target,
                        indexByEmailAddresses,
                        targetRecipientsWhichShouldRemain,
                        recipientsToDispose,
                        logger);
                }
                else
                {
                    foreach (var attendee in attendees)
                    {
                        TryMapServerAttendeeToOutlookRecipient(
                            attendee,
                            target,
                            indexByEmailAddresses,
                            targetRecipientsWhichShouldRemain,
                            recipientsToDispose,
                            logger,
                            logActions: false,
                            actionPrefix: string.Empty);
                    }

                    if (source.Organizer != null && source.Organizer.Value != null)
                    {
                        if (target.Recipients.Count > 0)
                        {
                            target.MeetingStatus = OlMeetingStatus.olMeeting;

                            using (var oPa = GenericComObjectWrapper.Create(target.PropertyAccessor))
                            {
                                if (oPa.Inner != null)
                                {
                                    try
                                    {
                                        oPa.Inner.SetProperty(PR_FINVITED, true);
                                    }
                                    catch (COMException ex)
                                    {
                                        s_logger.Warn("Could not set property PR_FINVITED for appointment", ex);
                                    }
                                }
                            }
                        }
                        else
                        {
                            target.MeetingStatus = OlMeetingStatus.olNonMeeting;
                        }
                    }
                    else
                    {
                        target.MeetingStatus = OlMeetingStatus.olNonMeeting;
                    }
                }

                if (!ParticipationStatusHelper.IsIncomingMeeting(target))
                {
                    for (int i = target.Recipients.Count; i > 0; i--)
                    {
                        var recipient = target.Recipients[i];
                        recipientsToDispose.Add(recipient);
                        if (!IsOwnIdentity(recipient, logger))
                        {
                            if (!targetRecipientsWhichShouldRemain.Contains(recipient))
                                target.Recipients.Remove(i);
                        }
                    }
                }

                int recipientsAfter;
                try
                {
                    recipientsAfter = target.Recipients.Count;
                }
                catch (COMException)
                {
                    recipientsAfter = -1;
                }

                s_logger.Info(
                    $"MapAttendeesAndOrganizer2To1: Recipients before={recipientsBefore} after={recipientsAfter}, " +
                    $"incomingMeeting={ParticipationStatusHelper.IsIncomingMeeting(target)}");
            }
            finally
            {
                recipientsToDispose.ToSafeEnumerable().ToArray();
            }
        }

        private void SetupIncomingMeetingOrganizer(
                    IEvent source,
                    AppointmentItem target,
                    string sourceOrganizerEmail,
                    HashSet<Recipient> targetRecipientsWhichShouldRemain,
                    HashSet<Recipient> recipientsToDispose,
                    IEntitySynchronizationLogger logger)
        {
            Recipient organizerRecipient = null;

            if (!string.IsNullOrEmpty(sourceOrganizerEmail))
            {
                organizerRecipient = TryFindRecipientByEmail(target, sourceOrganizerEmail, logger);
                if (organizerRecipient == null)
                {
                    var recipientName = CreateOutlookRecipientName(
                        sourceOrganizerEmail, source.Organizer.CommonName);

                    organizerRecipient = target.Recipients.Add(recipientName);
                    s_logger.Info($"MapAttendeesAndOrganizer2To1: organizer added, email={sourceOrganizerEmail}");
                }
                else
                {
                    s_logger.Info($"MapAttendeesAndOrganizer2To1: organizer reused, email={sourceOrganizerEmail}");
                }
            }
            else if (!string.IsNullOrEmpty(source.Organizer.CommonName))
            {
                organizerRecipient = target.Recipients.Add(source.Organizer.CommonName);
                s_logger.Info($"MapAttendeesAndOrganizer2To1: organizer added, cn={source.Organizer.CommonName}");
            }

            if (organizerRecipient == null)
            {
                s_logger.Warn("MapAttendeesAndOrganizer2To1: incoming organizer skipped (no email and no CN).");
                logger.LogWarning("Incoming organizer skipped (no email and no CN).");
                return;
            }

            recipientsToDispose.Add(organizerRecipient);
            organizerRecipient.Type = (int)OlMeetingRecipientType.olOrganizer;

            var organizerSenderSetViaPrSender = false;

            using (var oPa = GenericComObjectWrapper.Create(target.PropertyAccessor))
                            {
                                string organizerID = null;

                try
                {
                    if (organizerRecipient.Resolve())
                    {
                        using (var organizerAddressEntry = GenericComObjectWrapper.Create(organizerRecipient.AddressEntry))
                        {
                            organizerID = organizerAddressEntry.Inner != null ? organizerAddressEntry.Inner.ID : null;
                        }
                    }
                }
                catch (COMException ex)
                {
                    s_logger.Warn("Can't resolve organizer recipient in Server→Outlook mapping, PR_SENDER_* will not be set.", ex);
                    logger.LogWarning("Can't resolve organizer recipient in Server→Outlook mapping", ex);
                }

                if (organizerID != null && oPa.Inner != null && !string.IsNullOrEmpty(sourceOrganizerEmail))
                {
                    var propertyTagsSentRepresenting = new object[] { PR_SENT_REPRESENTING_NAME, PR_SENT_REPRESENTING_EMAIL_ADDRESS, PR_SENT_REPRESENTING_ADDRTYPE, PR_SENT_REPRESENTING_ENTRYID };
                    var propertyTagsSender = new object[] { PR_SENDER_NAME, PR_SENDER_EMAIL_ADDRESS, PR_SENT_REPRESENTING_ADDRTYPE, PR_SENDER_ENTRYID };
                    var propertyValues = new object[] { organizerRecipient.Name, sourceOrganizerEmail, "SMTP", oPa.Inner.StringToBinary(organizerID) };

                    try
                    {
                                        oPa.Inner.SetProperties(propertyTagsSentRepresenting, propertyValues);

                        if (_outlookMajorVersion >= 15)
                        {
                            oPa.Inner.SetProperties(propertyTagsSender, propertyValues);
                            organizerSenderSetViaPrSender = true;
                        }
                    }
                    catch (COMException ex)
                    {
                        s_logger.Warn("Could not set property PR_SENDER_* for organizer", ex);
                        logger.LogWarning("Could not set property PR_SENDER_* for organizer", ex);
                    }
                }
            }

            if (organizerSenderSetViaPrSender && !string.IsNullOrEmpty(sourceOrganizerEmail))
            {
                RemoveRecipientByEmail(target, sourceOrganizerEmail, recipientsToDispose, logger);
                s_logger.Info($"MapAttendeesAndOrganizer2To1: organizer removed from Recipients (PR_SENDER set), email={sourceOrganizerEmail}");
            }
            else
            {
                targetRecipientsWhichShouldRemain.Add(organizerRecipient);
            }
        }

        private void VerifyAndRepairIncomingAttendees(
            IList<Attendee> expectedAttendees,
            AppointmentItem target,
            Dictionary<string, Recipient> indexByEmailAddresses,
            HashSet<Recipient> targetRecipientsWhichShouldRemain,
            HashSet<Recipient> recipientsToDispose,
            IEntitySynchronizationLogger logger)
        {
            var missingAttendees = CollectMissingExpectedAttendees(expectedAttendees, target, logger);

            if (missingAttendees.Count == 0)
            {
                return;
            }

            s_logger.Info($"MapAttendeesAndOrganizer2To1: verify/repair missing={missingAttendees.Count}, " +
                $"emails=[{FormatAttendeeEmails(missingAttendees, logger)}]");

            foreach (var attendee in missingAttendees)
            {
                TryMapServerAttendeeToOutlookRecipient(
                    attendee,
                    target,
                    indexByEmailAddresses,
                    targetRecipientsWhichShouldRemain,
                    recipientsToDispose,
                    logger,
                    logActions: true,
                    actionPrefix: "repair-");

                indexByEmailAddresses = GetOutlookRecipientsByEmailAddressesOrName(target, recipientsToDispose, logger);
            }

            var stillMissing = CollectMissingExpectedAttendees(expectedAttendees, target, logger);
            if (stillMissing.Count > 0)
            {
                s_logger.Warn(
                    $"MapAttendeesAndOrganizer2To1: verify/repair incomplete, still missing={stillMissing.Count}, " +
                    $"emails=[{FormatAttendeeEmails(stillMissing, logger)}]");
                logger.LogWarning(
                    $"Incoming attendee repair incomplete, still missing {stillMissing.Count} attendee(s).");
            }
        }

        private List<Attendee> CollectMissingExpectedAttendees(
            IList<Attendee> expectedAttendees,
            AppointmentItem target,
            IEntitySynchronizationLogger logger)
        {
            var missingAttendees = new List<Attendee>();

            foreach (var attendee in expectedAttendees)
            {
                var emailWithoutMailto = TryGetAttendeeEmailWithoutMailto(attendee, logger);
                if (string.IsNullOrEmpty(emailWithoutMailto))
                    continue;

                if (TryFindRecipientByEmail(target, emailWithoutMailto, logger) == null)
                    missingAttendees.Add(attendee);
            }

            return missingAttendees;
        }

        private List<Attendee> PrepareAttendeesList(
            IList<Attendee> sourceAttendeesSnapshot,
            IEvent source,
            IEntitySynchronizationLogger logger,
            out HashSet<string> distinctEmailsAfterOrganizerDedup)
        {
            var attendees = sourceAttendeesSnapshot.Select(x => x.Copy<Attendee>()).ToList();
            RemoveOrganizerDuplicatesFromAttendees(attendees, source, logger);
            distinctEmailsAfterOrganizerDedup = CollectDistinctAttendeeEmails(attendees, logger);

            ReplaceOwnAttendeeEmail(attendees, logger);
            DeduplicateAttendeesByEmail(attendees, logger);

            return attendees;
        }

        private void WarnIfPrepLostDistinctAttendees(
            HashSet<string> distinctEmailsAfterOrganizerDedup,
            IList<Attendee> attendeesAfterPrep,
            IEntitySynchronizationLogger logger)
        {
            var expectedEmails = new HashSet<string>(
                            distinctEmailsAfterOrganizerDedup, StringComparer.OrdinalIgnoreCase);
            NormalizeOwnAttendeeEmailsInSet(expectedEmails);

            var actualEmails = CollectDistinctAttendeeEmails(attendeesAfterPrep, logger);
            var lostEmails = new List<string>();
            foreach (var email in expectedEmails)
            {
                if (!actualEmails.Contains(email))
                    lostEmails.Add(email);
            }

            if (lostEmails.Count == 0)
                return;

            s_logger.Warn(
                $"MapAttendeesAndOrganizer2To1: prep lost attendees, missing=[{string.Join(", ", lostEmails)}], " +
                $"actual=[{FormatAttendeeEmails(attendeesAfterPrep, logger)}]");
            logger.LogWarning(
                $"Attendee prep lost {lostEmails.Count} distinct attendee(s): {string.Join(", ", lostEmails)}.");
        }

        private HashSet<string> CollectDistinctAttendeeEmails(
                    IList<Attendee> attendees,
                    IEntitySynchronizationLogger logger)
        {
            var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var attendee in attendees)
            {
                var email = TryGetAttendeeEmailWithoutMailto(attendee, logger);
                if (!string.IsNullOrEmpty(email))
                    emails.Add(email);
            }

            return emails;
        }

        private void NormalizeOwnAttendeeEmailsInSet(HashSet<string> emails)
        {
            var serverOwnEmails = new List<string>();
            foreach (var email in emails)
            {
                if (IsStrictOwnAttendeeEmail(email)
                    && !string.Equals(email, _outlookEmailAddress, StringComparison.OrdinalIgnoreCase))
                {
                    serverOwnEmails.Add(email);
                }
            }

            foreach (var email in serverOwnEmails)
            {
                emails.Remove(email);
                emails.Add(_outlookEmailAddress);
            }
        }

        private bool IsStrictOwnAttendeeEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
                return false;

            if (string.Equals(email, _outlookEmailAddress, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(_serverEmailUri) && _serverEmailUri.Length > s_mailtoSchemaLength)
            {
                var serverEmail = _serverEmailUri.Substring(s_mailtoSchemaLength);
                if (string.Equals(email, serverEmail, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void RemoveOrganizerDuplicatesFromAttendees(
            IList<Attendee> attendees,
            IEvent source,
            IEntitySynchronizationLogger logger)
        {
            if (source.Organizer == null || source.Organizer.Value == null)
                return;

            for (int i = attendees.Count - 1; i >= 0; i--)
            {
                if (!EmailAddress.AreSame(attendees[i].Value, source.Organizer.Value))
                    continue;

                var email = TryGetAttendeeEmailWithoutMailto(attendees[i], logger);
                attendees.RemoveAt(i);
                s_logger.Debug($"MapAttendeesAndOrganizer2To1: prep removed organizer duplicate, email={email}");
            }
        }

        private void ReplaceOwnAttendeeEmail(IList<Attendee> attendees, IEntitySynchronizationLogger logger)
        {
            var ownIndex = -1;
            for (int i = 0; i < attendees.Count; i++)
            {
                var email = TryGetAttendeeEmailWithoutMailto(attendees[i], logger);
                if (!IsStrictOwnAttendeeEmail(email))
                {
                    continue;
                }

                ownIndex = i;
                break;
            }

            if (ownIndex < 0)
            {
                return;
            }

            var ownAttendee = attendees[ownIndex];
            var outlookMailto = "mailto:" + _outlookEmailAddress;
            var lastIndex = attendees.Count - 1;

            if (ownIndex == lastIndex)
            {
                ownAttendee.Value = new Uri(outlookMailto);
                ownAttendee.Parameters.Remove("EMAIL");
                return;
            }

            var role = ownAttendee.Role;
            var type = ownAttendee.Type;
            var commonName = ownAttendee.CommonName;

            attendees.RemoveAt(ownIndex);
            attendees.Add(new Attendee
            {
                Role = role,
                Value = new Uri(outlookMailto),
                Type = type,
                CommonName = commonName
            });
        }

        private void DeduplicateAttendeesByEmail(IList<Attendee> attendees, IEntitySynchronizationLogger logger)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = attendees.Count - 1; i >= 0; i--)
            {
                var email = TryGetAttendeeEmailWithoutMailto(attendees[i], logger);
                if (string.IsNullOrEmpty(email))
                    continue;

                if (seen.Add(email))
                    continue;

                s_logger.Debug($"MapAttendeesAndOrganizer2To1: prep dedupe removed duplicate, email={email}");
                attendees.RemoveAt(i);
            }
        }

        private string FormatAttendeeEmails(IList<Attendee> attendees, IEntitySynchronizationLogger logger)
        {
            var emails = new List<string>();
            foreach (var attendee in attendees)
            {
                var email = TryGetAttendeeEmailWithoutMailto(attendee, logger);
                emails.Add(string.IsNullOrEmpty(email) ? attendee.CommonName ?? "?" : email);
            }

            return string.Join(", ", emails);
        }

        private string TryGetAttendeeEmailWithoutMailto(Attendee attendee, IEntitySynchronizationLogger logger)
        {
            var attendeeEmail = TryGetAttendeeMailtoUri(attendee, logger);
            if (string.IsNullOrEmpty(attendeeEmail))
                return string.Empty;

            return attendeeEmail.Substring(s_mailtoSchemaLength);
        }

        private bool TryMapServerAttendeeToOutlookRecipient(
            Attendee attendee,
            AppointmentItem target,
            Dictionary<string, Recipient> indexByEmailAddresses,
            HashSet<Recipient> targetRecipientsWhichShouldRemain,
            HashSet<Recipient> recipientsToDispose,
            IEntitySynchronizationLogger logger,
            bool logActions,
            string actionPrefix)
        {
            Recipient targetRecipient = null;
            var attendeeEmail = TryGetAttendeeMailtoUri(attendee, logger);
            string action;
            string logEmail = attendeeEmail;

            if (!string.IsNullOrEmpty(attendeeEmail))
            {
                var emailWithoutMailto = attendeeEmail.Substring(s_mailtoSchemaLength);
                logEmail = emailWithoutMailto;
                targetRecipient = TryFindRecipientByEmail(target, emailWithoutMailto, logger);
                if (targetRecipient != null)
                {
                    action = "reused";
                }
                else if (indexByEmailAddresses.TryGetValue(attendeeEmail, out targetRecipient))
                {
                    action = "reused";
                }
                else
                {
                    var recipientName = CreateOutlookRecipientName(
                        emailWithoutMailto, attendee.CommonName);

                    targetRecipient = target.Recipients.Add(recipientName);
                    action = "added";
                }
            }
            else if (!string.IsNullOrEmpty(attendee.CommonName))
            {
                targetRecipient = target.Recipients.Add(attendee.CommonName);
                action = "added";
                logEmail = attendee.CommonName;
            }
            else
            {
                action = "skipped";
            }

            if (logActions)
            {
                s_logger.Info(
                    $"MapAttendeesAndOrganizer2To1: attendee {actionPrefix}{action}, email={logEmail ?? string.Empty}");
            }

            if (targetRecipient == null)
                return false;

            recipientsToDispose.Add(targetRecipient);
            targetRecipientsWhichShouldRemain.Add(targetRecipient);
            targetRecipient.Type = (int)MapAttendeeType2To1(attendee.Role);
            if (attendee.Type == "RESOURCE" || attendee.Type == "ROOM")
                targetRecipient.Type = (int)OlMeetingRecipientType.olResource;
            try
            {
                targetRecipient.Resolve();
            }
            catch (COMException ex)
            {
                s_logger.Warn("Can't resolve recipient in Server→Outlook mapping, skipping GAL lookup.", ex);
                logger.LogWarning("Can't resolve recipient in Server→Outlook mapping", ex);
            }

            return true;
        }

        private string TryGetAttendeeMailtoUri(Attendee attendee, IEntitySynchronizationLogger logger)
        {
            var attendeeEmail = string.Empty;
            if (attendee.Parameters.ContainsKey("EMAIL"))
            {
                attendeeEmail = attendee.Parameters.Get("EMAIL");
                if (!attendeeEmail.StartsWith("mailto:", StringComparison.InvariantCultureIgnoreCase))
                {
                    attendeeEmail = "mailto:" + attendeeEmail;
                }
            }
            else if (attendee.Value != null && StringComparer.InvariantCultureIgnoreCase.Compare(attendee.Value.Scheme, "mailto") == 0)
            {
                try
                {
                    attendeeEmail = attendee.Value.ToString();
                }
                catch (UriFormatException ex)
                {
                    s_logger.Warn("Ignoring invalid Uri in attendee email.", ex);
                    logger.LogWarning("Ignoring invalid Uri in attendee email.", ex);
                }
            }

            return attendeeEmail;
        }

        private void RemoveRecipientByEmail(
                    AppointmentItem target,
                    string email,
                    HashSet<Recipient> recipientsToDispose,
                    IEntitySynchronizationLogger logger)
        {
            for (int i = target.Recipients.Count; i >= 1; i--)
            {
                var recipient = target.Recipients[i];
                recipientsToDispose.Add(recipient);

                try
                {
                    if (recipient.Resolve())
                    {
                        using (var entryWrapper = GenericComObjectWrapper.Create(recipient.AddressEntry))
                        {
                            var recipientEmail = OutlookUtility.GetEmailAdressOrNull(entryWrapper.Inner, logger, s_logger);
                            if (!string.IsNullOrEmpty(recipientEmail) && EmailAddress.AreSame(recipientEmail, email))
                            {
                                target.Recipients.Remove(i);
                                return;
                            }
                        }
                    }
                }
                catch (COMException)
                {
                    //continue scanning
                }

                var emailFromAddress = TryExtractEmailFromRecipientName(recipient.Address);
                if (!string.IsNullOrEmpty(emailFromAddress) && EmailAddress.AreSame(emailFromAddress, email))
                {
                    target.Recipients.Remove(i);
                    return;
                }

                var emailFromName = TryExtractEmailFromRecipientName(recipient.Name);
                if (!string.IsNullOrEmpty(emailFromName) && EmailAddress.AreSame(emailFromName, email))
                {
                    target.Recipients.Remove(i);
                    return;
                }
            }
        }

        private Dictionary<string, Recipient> GetOutlookRecipientsByEmailAddressesOrName(AppointmentItem appointment, HashSet<Recipient> disposeList, IEntitySynchronizationLogger logger)
        {
            Dictionary<string, Recipient> indexByEmailAddresses = new Dictionary<string, Recipient>(StringComparer.InvariantCultureIgnoreCase);

            foreach (Recipient recipient in appointment.Recipients)
            {
                disposeList.Add(recipient);
                if (!string.IsNullOrEmpty(recipient.Address))
                {
                    using (var entryWrapper = GenericComObjectWrapper.Create(recipient.AddressEntry))
                    {
                        indexByEmailAddresses[GetMailUrlOrNull(entryWrapper.Inner, recipient.Address, logger) ?? recipient.Name] = recipient;
                    }
                }
                else
                {
                    indexByEmailAddresses[recipient.Name] = recipient;
                }
            }

            return indexByEmailAddresses;
        }

        private Recipient TryFindRecipientByEmail(AppointmentItem appointment, string email, IEntitySynchronizationLogger logger)
        {
            if (string.IsNullOrEmpty(email))
            {
                return null;
            }

            foreach (Recipient recipient in appointment.Recipients)
            {
                try
                {
                    if (recipient.Resolve())
                    {
                        using (var entryWrapper = GenericComObjectWrapper.Create(recipient.AddressEntry))
                        {
                            var recipientEmail = OutlookUtility.GetEmailAdressOrNull(entryWrapper.Inner, logger, s_logger);
                            if (!string.IsNullOrEmpty(recipientEmail) && EmailAddress.AreSame(recipientEmail, email))
                            {
                                return recipient;
                            }
                        }
                    }
                }
                catch (COMException)
                {
                    // continue scanning
                }

                var emailFromAddress = TryExtractEmailFromRecipientName(recipient.Address);
                if (!string.IsNullOrEmpty(emailFromAddress) && EmailAddress.AreSame(emailFromAddress, email))
                {
                    return recipient;
                }

                var emailFromName = TryExtractEmailFromRecipientName(recipient.Name);
                if (!string.IsNullOrEmpty(emailFromName) && EmailAddress.AreSame(emailFromName, email))
                {
                    return recipient;
                }
            }

            return null;
        }

        private string CreateOutlookRecipientName(string email, string commonName)
        {
            var fallbackName = !String.IsNullOrEmpty(commonName) ? $"{commonName} <{email}>" : email;
            try
            {
                if (!String.IsNullOrEmpty(commonName)
                    && String.Equals(commonName, email, StringComparison.OrdinalIgnoreCase))
                {
                    return email;
                }

                if (!String.IsNullOrEmpty(commonName) 
                    && !String.Equals(commonName, EmailAddress.Parse(email).NameId, StringComparison.OrdinalIgnoreCase))
                    return fallbackName;

                var outlookApp = Globals.ThisAddIn.Application;
                var outlookSession = outlookApp?.Session;
                using (var recipient = GenericComObjectWrapper.Create(outlookSession?.CreateRecipient(email)))
                {
                    if (recipient.Inner == null)
                        return fallbackName;

                    using (var addressEntry = GenericComObjectWrapper.Create(recipient.Inner.AddressEntry))
                    {
                        if (addressEntry.Inner == null)
                            return fallbackName;

                        using (var contactItem = GenericComObjectWrapper.Create(addressEntry.Inner.GetContact()))
                        {
                            if (contactItem.Inner == null)
                                return fallbackName;

                            var contactName = contactItem.Inner.FullName;
                            return String.IsNullOrEmpty(contactName) ? fallbackName : $"{contactName} <{email}>";
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                s_logger.Warn($"Failed to retrieve Outlook recipient name for {email}");
                return fallbackName;
            }
        }

        private static dynamic GetPropertySafe(PropertyAccessor accessor, string propertyName)
        {
            using (var wrapper = GenericComObjectWrapper.Create(accessor))
            {
                return wrapper.Inner.GetProperty(propertyName);
            }
        }

        private string ResolveOwnParticipationStatus(AppointmentItem source, Recipient recipient)
        {
            if (source.MeetingStatus == OlMeetingStatus.olMeetingReceivedAndCanceled)
            {
                return "DECLINED";
            }

            if (!IsAmbiguousNotRespondedStatus(source.ResponseStatus))
            {
                return MapParticipation1To2(source.ResponseStatus);
            }

            if (!IsAmbiguousNotRespondedStatus(recipient.MeetingResponseStatus))
            {
                s_logger.Debug($"Using recipient meeting response for own attendee because source response is ambiguous: {source.ResponseStatus} -> {recipient.MeetingResponseStatus}");
                return MapParticipation1To2(recipient.MeetingResponseStatus);
            }

            if (source.MeetingStatus == OlMeetingStatus.olMeetingReceived)
            {
                s_logger.Debug($"Own attendee response is ambiguous ({source.ResponseStatus}); mapper does not force participation status");
            }

            return MapParticipation1To2(source.ResponseStatus);
        }

        private static bool IsAmbiguousNotRespondedStatus(OlResponseStatus status)
        {
            return status == OlResponseStatus.olResponseNone || status == OlResponseStatus.olResponseNotResponded;
        }

        //XXX Читаем служебный marker, который говорит: "этот item еще нужно дофинализировать через Respond()".
        //XXX Если marker битый или просроченный, сразу чистим его, чтобы не гонять бесконечные принудительные обновления.
        private bool IsDeferredRespondPending(AppointmentItem appointment, out DateTime deferredRespondAtUtc)
        {
            deferredRespondAtUtc = default(DateTime);
            try
            {
                if (appointment == null)
                {
                    return false;
                }

                string value;
                using (var pa = GenericComObjectWrapper.Create(appointment.PropertyAccessor))
                {
                    try
                    {
                        var rawValue = pa.Inner.GetProperty(DeferredRespondMarkerPropertyAccessor);
                        value = rawValue?.ToString();
                    }
                    catch (COMException)
                    {
                        return false;
                    }
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out deferredRespondAtUtc))
                {
                    ClearDeferredRespondMarker(appointment);
                    return false;
                }

                if (DateTime.UtcNow - deferredRespondAtUtc > DeferredRespondMarkerTtl)
                {
                    s_logger.Debug($"Deferred meeting response marker expired for '{appointment.EntryID}'.");
                    ClearDeferredRespondMarker(appointment);
                    return false;
                }

                return true;
            }
            catch (System.Exception ex)
            {
                s_logger.Warn("Failed to read deferred meeting response marker.", ex);
                return false;
            }
        }

        //XXX Ставим marker через PropertyAccessor, чтобы пережить Outlook recreate и не зависеть от UserProperties.
        //XXX Возвращаем bool: defer включаем только если marker реально записался.
        private bool MarkDeferredRespondPending(AppointmentItem appointment)
        {
            try
            {
                if (appointment == null)
                {
                    return false;
                }

                using (var pa = GenericComObjectWrapper.Create(appointment.PropertyAccessor))
                {
                    pa.Inner.SetProperty(DeferredRespondMarkerPropertyAccessor, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                s_logger.Warn("Failed to set deferred meeting response marker.", ex);
            }

            return false;
        }

        //XXX Marker одноразовый: после успешного (или уже не нужного) Respond() обязательно убираем.
        private void ClearDeferredRespondMarker(AppointmentItem appointment)
        {
            try
            {
                if (appointment == null)
                {
                    return;
                }

                using (var pa = GenericComObjectWrapper.Create(appointment.PropertyAccessor))
                {
                    try
                    {
                        pa.Inner.DeleteProperty(DeferredRespondMarkerPropertyAccessor);
                    }
                    catch (COMException)
                    {
                        //Marker is optional and may not exist yet
                    }
                }
            }
            catch (System.Exception ex)
            {
                s_logger.Warn("Failed to clear deferred meeting response marker.", ex);
            }
        }

        private void ClearDeferredRespondPending(AppointmentItem appointment, string uid)
        {
            ClearDeferredRespondMarker(appointment);
            if (_deferredRespondStorage != null && !string.IsNullOrEmpty(uid))
                _deferredRespondStorage.ClearPending(uid);
        }

        private static void LogOutgoingAttendeeDiagnostics(string scope, AppointmentItem source, IEvent serverEventOrNull, IEvent uidHintEventOrNull)
        {
            int outCnt = -1;
            try
            {
                outCnt = source.Recipients.Count;
            }
            catch (System.Exception ex)
            {
                s_logger.Debug("OutgoingAttendeeDiagnostics: Recipients.Count failed", ex);
            }

            int srvCnt = 0;
            if (serverEventOrNull != null && serverEventOrNull.Attendees != null)
            {
                srvCnt = serverEventOrNull.Attendees.Count;
            }

            if (outCnt >= 0 && srvCnt == outCnt)
            {
                return;
            }

            bool recurring = false;
            try
            {
                recurring = source.IsRecurring;
            }
            catch (System.Exception ex)
            {
                s_logger.Debug("OutgoingAttendeeDiagnostics: IsRecurring failed", ex);
            }

            int meeting = -1;
            try
            {
                meeting = (int)source.MeetingStatus;
            }
            catch (System.Exception ex)
            {
                s_logger.Debug("OutgoingAttendeeDiagnostics: MeetingStatus failed", ex);
            }

            string uid = "-";
            if (uidHintEventOrNull != null && !string.IsNullOrEmpty(uidHintEventOrNull.UID))
            {
                var u = uidHintEventOrNull.UID;
                uid = u.Length > 24 ? u.Substring(0, 24) + "..." : u;
            }

            string outStr = outCnt >= 0 ? outCnt.ToString(CultureInfo.InvariantCulture) : "?";
            string dStr = outCnt >= 0 ? (srvCnt - outCnt).ToString(CultureInfo.InvariantCulture) : "?";

            var msg = string.Format(
                CultureInfo.InvariantCulture,
                "OutgoingAttendeeDiagnostics: scope={0} out={1} server={2} delta_srv_minus_out={3} recurring={4} meetingStatus={5} uid={6}",
                scope, outStr, srvCnt, dStr, recurring, meeting, uid);

            s_logger.Debug(msg);

            if (outCnt >= 0 && srvCnt > outCnt)
            {
                s_logger.Warn(msg);
                try
                {
                    Telemetry.Signal(
                        Telemetry.SyncDiagnostics,
                        "outgoing_attendee_server_richer",
                        new
                        {
                            scope = scope,
                            out_cnt = outCnt,
                            srv_cnt = srvCnt,
                            d_srv_minus_out = srvCnt - outCnt,
                            rec = recurring ? 1 : 0
                        });
                }
                catch (System.Exception ex)
                {
                    s_logger.Debug("OutgoingAttendeeDiagnostics: telemetry failed", ex);
                }
            }
        }

        private static void LogSequenceDiagnostics(IICalendar newCal, IICalendar existingCal)
        {
            try
            {
                var existingSeq = existingCal.Events.Count > 0 ? existingCal.Events.Max(e => e.Sequence) : -1;
                var newSeq = existingSeq + 1;

                foreach (var newEv in newCal.Events)
                {
                    var isException = newEv.RecurrenceID != null;
                    var scope = isException ? $"exception(RID={newEv.RecurrenceID})" : "master";

                    var serverEv = isException
                        ? existingCal.Events.FirstOrDefault(e => e.RecurrenceID != null &&
                            e.RecurrenceID.Value.Date == newEv.RecurrenceID.Value.Date)
                        : existingCal.Events.FirstOrDefault(e => e.RecurrenceID == null);

                    if (serverEv == null)
                    {
                        s_logger.Debug($"SEQUENCE [{scope}]: new event, assigning seq={newSeq}");
                        continue;
                    }

                    var changes = new System.Text.StringBuilder();
                    if (newEv.Start?.Value != serverEv.Start?.Value)
                    {
                        changes.Append("DTSTART ");
                    }

                    if (newEv.DTEnd?.Value != serverEv.DTEnd?.Value)
                    {
                        changes.Append("DTEND ");
                    }

                    if (newEv.Summary != serverEv.Summary)
                    {
                        changes.Append("SUMMARY ");
                    }

                    if (newEv.Location != serverEv.Location)
                    {
                        changes.Append("LOCATION ");
                    }

                    if (newEv.Description != serverEv.Description)
                    {
                        changes.Append("DESCRIPTION ");
                    }

                    var newRrule = newEv.RecurrenceRules.Count > 0 ? newEv.RecurrenceRules[0].ToString() : "";
                    var srvRrule = serverEv.RecurrenceRules.Count > 0 ? serverEv.RecurrenceRules[0].ToString() : "";
                    if (newRrule != srvRrule)
                    {
                        changes.Append("RRULE ");
                    }

                    var newAttendees = string.Join(",", newEv.Attendees.Select(a => a.Value?.ToString() ?? "").OrderBy(x => x));
                    var srvAttendees = string.Join(",", serverEv.Attendees.Select(a => a.Value?.ToString() ?? "").OrderBy(x => x));
                    if (newAttendees != srvAttendees)
                    {
                        changes.Append("ATTENDEES ");
                    }
                    var changedFields = changes.ToString().Trim();
                    var decision = string.IsNullOrEmpty(changedFields) ? "increment(no_content_change)" : $"increment({changedFields})";

                    s_logger.Debug($"SEQUENCE [{scope}]: server_seq={serverEv.Sequence} → new_seq={newSeq}, decision={decision}");
                    if (!string.IsNullOrEmpty(changedFields))
                    {
                        s_logger.Debug($"SEQUENCE [{scope}] changed fields detail: {changedFields}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                s_logger.Debug("LogSequenceDiagnostics failed", ex);
            }
        }

        private static IEvent TryFindServerExceptionForOutlook(
            IICalendar serverCalOrNull,
            AppointmentItem masterSource,
            Exception outlookEx,
            DateTimeZone sourceZone,
            ITimeZone startIcalTimeZone,
            EventMappingConfiguration configuration)
        {
            if (serverCalOrNull == null)
            {
                return null;
            }

            iCalDateTime expectedRid;
            if (masterSource.AllDayEvent)
            {
                expectedRid = new iCalDateTime(outlookEx.OriginalDate);
                expectedRid.HasTime = false;
            }
            else
            {
                var localEx = LocalDateTime.FromDateTime(outlookEx.OriginalDate);
                var zonedEx = sourceZone.AtLeniently(localEx);
                if (configuration.CreateEventsInUTC || startIcalTimeZone == null)
                {
                    var utc = zonedEx.ToDateTimeUtc();
                    expectedRid = new iCalDateTime(utc) { IsUniversalTime = true };
                }
                else
                {
                    var tz = configuration.UseIanaTz
                        ? DateTimeZoneProviders.Tzdb[startIcalTimeZone.TZID]
                        : DateTimeZoneProviders.Bcl[startIcalTimeZone.TZID];
                    var local = zonedEx.WithZone(tz).LocalDateTime.ToDateTimeUnspecified();
                    expectedRid = new iCalDateTime(local);
                    expectedRid.SetTimeZone(startIcalTimeZone);
                }
            }

            foreach (var ev in serverCalOrNull.Events)
            {
                var rid = ev.RecurrenceID;
                if (rid == null)
                {
                    continue;
                }

                bool match = false;
                try
                {
                    match = !rid.HasTime && !expectedRid.HasTime ? rid.Date.Date == expectedRid.Date.Date : rid.AsUtc() == expectedRid.AsUtc();
                }
                catch
                {
                    match = false;
                }
                if (match)
                {
                    return ev;
                }
            }
            return null;
        }

        private static string TryExtractEmailFromRecipientName(string recipientName)
        {
            if (string.IsNullOrEmpty(recipientName))
            {
                return null;
            }

            var angleBracketStart = recipientName.LastIndexOf('<');
            var angleBracketEnd = recipientName.LastIndexOf('>');
            if (angleBracketStart >= 0 && angleBracketEnd > angleBracketStart)
            {
                var extracted = recipientName.Substring(angleBracketStart + 1, angleBracketEnd - angleBracketStart - 1);
                if (extracted.Contains("@"))
                {
                    return extracted;
                }
            }

            if (recipientName.Contains("@") && !recipientName.Contains(" "))
            {
                return recipientName;
            }

            return null;
        }
    }
}
