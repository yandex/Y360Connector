using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using CalDavSynchronizer.Implementation.Common;
using GenSync.Logging;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Y360OutlookConnector;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Localization;
using Y360OutlookConnector.Ui.Extensions;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnector.Ui
{
    /// <summary>
    /// Действия Яндекс Календаря и Телемоста для встречи: общая логика для VSTO-ленты и Ribbon XML.
    /// </summary>
    internal static class YandexCalendarAppointmentActions
    {
        private static readonly ILog s_logger =
            LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private const int MaxCreateEventDescriptionLength = 2000;

        internal static async Task CreateOrUpdateMeetingForInspectorAsync(Inspector inspector, bool isMeetingInternal)
        {
            s_logger.Info(isMeetingInternal ? "CreateOrUpdateInternalMeeting" : "CreateOrUpdateExternalMeeting");

            if (inspector == null)
            {
                return;
            }

            if (!(inspector.CurrentItem is AppointmentItem currentAppointment))
            {
                return;
            }

            await currentAppointment.CreateOrUpdateMeetingAsync(isMeetingInternal);
        }

        private static bool IsUserOrganizer(AppointmentItem currentAppointment, string outlookEmail)
        {
            if (currentAppointment == null)
            {
                return false;
            }

            var organizerEmail = currentAppointment.GetOrganizerEmailAddress(NullEntitySynchronizationLogger.Instance);

            if (EmailAddress.AreSame(organizerEmail, outlookEmail))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Получить ссылку на событие в календаре по встрече (VSTO и Ribbon XML).
        /// </summary>
        internal static async Task<string> GetCalendarEditUrlAsync(AppointmentItem currentAppointment, LoginController loginController)
        {
            if (loginController == null || !loginController.IsUserLoggedIn)
            {
                s_logger.Info("User is not logged in. Can not get event url.");
                return null;
            }

            if (currentAppointment == null)
            {
                return null;
            }

            var uid = AppointmentItemUtils.ExtractUidFromGlobalId(currentAppointment.GlobalAppointmentID);
            if (string.IsNullOrEmpty(uid))
            {
                return null;
            }

            var syncFolder = currentAppointment.GetFolder();
            if (syncFolder == null)
            {
                s_logger.Info($"Fail to get folder for appointment with id={uid}");
                return null;
            }

            var outlookEmail = syncFolder.GetAccount();
            if (string.IsNullOrEmpty(outlookEmail))
            {
                s_logger.Info($"Fail to get account for folder={syncFolder.Name}");
                return null;
            }
            var recState = currentAppointment.GetRecurrenceState();

            var isException = recState == OlRecurrenceState.olApptException;

            var isEventSequence = recState == OlRecurrenceState.olApptMaster;

            var config = ThisAddIn.Components.SyncManager.GetSyncTargetConfig(syncFolder.EntryID);
            var layerId = config?.GetLayerId();
            if (string.IsNullOrEmpty(layerId))
            {
                s_logger.Info($"Fail to get layerId for appointment with id={uid}");
                return null;
            }

            var webDavClient = ThisAddIn.Components.SyncManager.CreateWebDavClient();

            var entity = await webDavClient.GetEntityAsync(uid, config.Url);

            if (entity == null)
            {
                return null;
            }

            Uri eventUrl;

            if (isEventSequence)
            {
                eventUrl = entity.GetMasterEventUrl();
            }
            else
            {
                if (isException)
                {
                    eventUrl = entity.GetEventExceptionByStartDateUrl(currentAppointment.StartUTC);
                }
                else
                {
                    eventUrl = entity.GetMasterEventUrl();
                }
            }

            if (eventUrl == null)
            {
                s_logger.Info($"Fail to get event url for appointment {uid}");
                return null;
            }

            var isUserOrganizer = IsUserOrganizer(currentAppointment, outlookEmail);
            if (!isUserOrganizer)
            {
                if (!AppConfig.IsAlwaysEnableEditEventButton)
                {
                    if (!entity.CanParticipantsEditEvent())
                    {
                        s_logger.Info($"User is not the organizer and participants can not edit event. Edit event is not allowed. Appointment id = {uid}");
                        return null;
                    }
                }
            }

            return currentAppointment.CreateCalendarUrl(eventUrl, loginController.UserInfo.UserId, layerId, isEventSequence);
        }

        internal static string TruncateForCreateEventUrl(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var text = body.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            if (text.Length <= MaxCreateEventDescriptionLength)
            {
                return text;
            }
            return text.Substring(0, MaxCreateEventDescriptionLength);
        }

        internal static void CollectAttendeesAndResources(
            AppointmentItem appointment,
            out List<string> attendees,
            out List<string> resources)
        {
            attendees = new List<string>();
            resources = new List<string>();
            var seenAtt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenRes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Recipient r in appointment.Recipients)
            {
                var rType = (OlMeetingRecipientType)r.Type;
                if (rType == OlMeetingRecipientType.olOrganizer)
                {
                    continue;
                }

                var email = r.AddressEntry != null
                    ? OutlookUtility.GetEmailAdressOrNull(r.AddressEntry, NullEntitySynchronizationLogger.Instance, s_logger)
                    : null;
                if (string.IsNullOrWhiteSpace(email))
                {
                    email = r.Address;
                }
                if (string.IsNullOrWhiteSpace(email))
                {
                    continue;
                }

                email = email.Trim();
                if (rType == OlMeetingRecipientType.olResource)
                {
                    if (seenRes.Add(email))
                    {
                        resources.Add(email);
                    }
                }
                else if (seenAtt.Add(email))
                {
                    attendees.Add(email);
                }
            }
        }

        /// <summary>
        /// Открывает в браузере форму создания события в Календаре по данным встречи Outlook.
        /// </summary>
        internal static void CreateEventInYandexCalendarForAppointment(AppointmentItem appointment, LoginController loginController)
        {
            if (appointment == null)
            {
                return;
            }

            System.Windows.Forms.Application.DoEvents();

            var userId = loginController != null && loginController.UserInfo != null ? loginController.UserInfo.UserId : null;
            var title = string.IsNullOrWhiteSpace(appointment.Subject) ? null : appointment.Subject.Trim();
            var description = TruncateForCreateEventUrl(appointment.Body);
            var location = string.IsNullOrWhiteSpace(appointment.Location) ? null : appointment.Location.Trim();
            CollectAttendeesAndResources(appointment, out var attendees, out var resources);

            var url = YCalendarUrlBuilder.BuildCreateEventUrl(
                appointment.Start,
                appointment.End,
                userId,
                title,
                description,
                appointment.AllDayEvent,
                location,
                attendees,
                resources,
                YCalendarUrlBuilder.EventTypeUser);

            Telemetry.Signal(Telemetry.YandexCalendarEvents, "create_event_from_appointment");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (System.Exception exc)
            {
                s_logger.Warn("CreateEventInYandexCalendar: failed to open URL", exc);
            }
        }

        internal static void XmlCreateEventInYandexCalendar(Inspector inspector)
        {
            var appointment = OutlookInspectorAppointmentHelper.TryGetAppointmentItemFromInspector(inspector);
            var lc = ThisAddIn.Components != null ? ThisAddIn.Components.LoginController : null;
            CreateEventInYandexCalendarForAppointment(appointment, lc);
        }

        internal static void XmlNavigateToYandexCalendar()
        {
            var url = EndpointConfig.CalendarWebBaseUrl;
            var lc = ThisAddIn.Components != null ? ThisAddIn.Components.LoginController : null;
            var userId = lc != null && lc.UserInfo != null ? lc.UserInfo.UserId : null;

            if (!string.IsNullOrEmpty(userId))
            {
                url += "?uid=" + userId;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (System.Exception exc)
            {
                s_logger.Warn("XmlNavigateToYandexCalendar failed", exc);
            }
        }

        internal static async Task XmlOpenEditEventUrlAsync(Inspector inspector)
        {
            var appointment = OutlookInspectorAppointmentHelper.TryGetAppointmentItemFromInspector(inspector);
            var lc = ThisAddIn.Components != null ? ThisAddIn.Components.LoginController : null;
            var editUrl = await GetCalendarEditUrlAsync(appointment, lc);
            if (string.IsNullOrEmpty(editUrl))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = editUrl,
                    UseShellExecute = true
                });
            }
            catch (System.Exception exc)
            {
                s_logger.Warn("XmlOpenEditEventUrlAsync: failed to open URL", exc);
            }
        }

        internal static async Task XmlTelemostLoginAndMeetingAsync(Inspector inspector, bool isMeetingInternal)
        {
            try
            {
                var lc = ThisAddIn.Components != null ? ThisAddIn.Components.LoginController : null;
                if (lc == null)
                {
                    s_logger.Warn("Login controller is null");
                    return;
                }

                if (lc.IsUserLoggedIn)
                {
                    await CreateOrUpdateMeetingForInspectorAsync(inspector, isMeetingInternal);
                    return;
                }

                ThisAddIn.Components.StartLogin();

                if (lc.IsUserLoggedIn)
                {
                    s_logger.Info("User login ok");
                    await CreateOrUpdateMeetingForInspectorAsync(inspector, isMeetingInternal);
                }
                else if (inspector != null)
                {
                    inspector.UpdateStatusLine(Strings.Telemost_Messages_AuthorizeInTelemostMessage);
                    s_logger.Info("User login fail");
                }
            }
            catch (System.Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        internal static async Task XmlTelemostOpenSettingsAsync(Inspector inspector)
        {
            s_logger.Info("ShowSettings (XML ribbon)");

            if (inspector == null)
            {
                return;
            }

            if (!(inspector.CurrentItem is AppointmentItem currentAppointment))
            {
                return;
            }

            if (ThisAddIn.Components == null)
            {
                return;
            }

            var customTaskPane = await ThisAddIn.Components.PaneController.GetOrCreateSettingsPaneAsync(inspector);

            if (customTaskPane == null)
            {
                return;
            }

            var settingsControl = customTaskPane.Control as ITelemostSettingsControl;
            settingsControl?.UpdateMeetingInfo(currentAppointment.GetMeetingInfo());

            customTaskPane.Visible = true;
        }
    }
}
