using System;
using System.Reflection;
using System.Threading.Tasks;
using GenSync.Logging;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Microsoft.Office.Tools.Ribbon;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Ui.Extensions;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnector.Ui
{
    public partial class AppointmentRibbon
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private LoginController _loginController;

        private string _editEventUrl;

        private void ClearEventUrl()
        {
            btnEditEventInAppointment.Enabled = false;
            btnEditEventInSchedulingAssistant.Enabled = false;
            _editEventUrl = null;
        }

        private async Task OnStartup(ComponentContainer componentContainer)
        {
            _loginController = componentContainer.LoginController;
            _loginController.LoginStateChanged += LoginController_LoginStateChanged;

            if (_loginController.IsUserLoggedIn)
            {
                await UpdateEventUrlAsync();
            }
        }

        private async Task UpdateEventUrlAsync()
        {
            _editEventUrl = await GetEventUrlAsync();

            btnEditEventInAppointment.Enabled = !string.IsNullOrEmpty(_editEventUrl);
            btnEditEventInSchedulingAssistant.Enabled = !string.IsNullOrEmpty(_editEventUrl);
        }

        private void UpdateStrings()
        {
            btnTelemostSettings.Label = Localization.Strings.Telemost_Toolbar_SettingsButton; ;
            btnTelemostInternalMeeting.Label = Localization.Strings.Telemost_Toolbar_InternalMeetingButton;
            btnTelemostExternalMeeting.Label = Localization.Strings.Telemost_Toolbar_ExternalMeetingButton;
            TelemostRibbonMenu.Label = Localization.Strings.Telemost_Toolbar_RibbonMenuButton;

            btnNavigateToYandexCalendarInAppointment.Label = Localization.Strings.YandexCalendar_Toolbar_NavigateToCalendarButton;
            btnEditEventInAppointment.Label = Localization.Strings.YandexCalendar_Toolbar_EditEventButton;
            YandexCalendarRibbonMenu.Label = Localization.Strings.YandexCalendar_Toolbar_RibbonToolbarButton;

            SchedulingAssistantTabYandexCalendarMenu.Label = Localization.Strings.YandexCalendar_Toolbar_RibbonToolbarButton;
            btnNavigateToYandexCalendarInSchedulingAssistant.Label = Localization.Strings.YandexCalendar_Toolbar_NavigateToCalendarButton;
            btnEditEventInSchedulingAssistant.Label = Localization.Strings.YandexCalendar_Toolbar_EditEventButton;
        }

        private async Task LoginIfRequiredAndCreateOrUpdateMeetingAsync(Inspector inspector, bool isMeetingInternal)
        {
            try
            {
                if (_loginController == null)
                {
                    s_logger.Warn("Login controller in null");
                    return;
                }

                if (_loginController.IsUserLoggedIn)
                {
                    await CreateOrUpdateMeetingAsync(inspector, isMeetingInternal);
                    return;
                }

                ThisAddIn.Components?.StartLogin();

                if (_loginController.IsUserLoggedIn)
                {
                    s_logger.Info("User login ok");
                    await CreateOrUpdateMeetingAsync(inspector, isMeetingInternal);
                }
                else
                {
                    inspector.UpdateStatusLine(Localization.Strings.Telemost_Messages_AuthorizeInTelemostMessage);
                    s_logger.Info("User login fail");
                }
            }
            catch (System.Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        private async Task CreateOrUpdateMeetingAsync(Inspector inspector, bool isMeetingInternal)
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

        private AppointmentItem CurrentAppointment
        {
            get
            {
                if (!(Context is Inspector currentInspector))
                {
                    return null;
                }

                if (!(currentInspector.CurrentItem is AppointmentItem currentAppointment))
                {
                    return null;
                }

                return currentAppointment;
            }
        }

        private bool IsUserOrganizer(string outlookEmail)
        {
            var currentAppointment = CurrentAppointment;

            if (currentAppointment == null)
            {
                return false;
            }

            var organizerEmail = currentAppointment.GetOrganizerEmailAddress(NullEntitySynchronizationLogger.Instance);

            if (EmailAddress.AreSame(organizerEmail, outlookEmail, EmailAddress.KnownDomainsAliases))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Получить ссылку на событие в календаре по текущей встрече
        /// </summary>
        /// <returns></returns>
        private async Task<string> GetEventUrlAsync()
        {
            if (!_loginController.IsUserLoggedIn)
            {
                s_logger.Info("User is not logged in. Can not get event url.");
                return null;
            }

            var currentAppointment = CurrentAppointment;
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
            if (String.IsNullOrEmpty(outlookEmail))
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

            // Получаем данные по встрече из календаря, чтобы получить event url
            var webDavClient = ThisAddIn.Components.SyncManager.CreateWebDavClient();

            var entity = await webDavClient.GetEntityAsync(uid, config.Url);

            if (entity == null)
            {
                return null;
            }

            Uri eventUrl;

            if (isEventSequence)
            {
                // Ищем мастер событие
                eventUrl = entity.GetMasterEventUrl();
            }
            else
            {
                if (isException)
                {
                    // Ищем исключение по дате
                    eventUrl = entity.GetEventExceptionByStartDateUrl(currentAppointment.StartUTC);
                }
                else
                {
                    // Ищем мастер событие
                    eventUrl = entity.GetMasterEventUrl();
                }
            }

            if (eventUrl == null)
            {
                s_logger.Info($"Fail to get event url for appointment {uid}");
                return null;
            }

            var isUserOrganizer = IsUserOrganizer(outlookEmail);
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

            return currentAppointment.CreateCalendarUrl(eventUrl, _loginController.UserInfo.UserId, layerId, isEventSequence);           
        }

        #region Event handlers

        private async void LoginController_LoginStateChanged(object sender, LoginStateEventArgs e)
        {
            if (e.IsUserLoggedIn)
            {
                await UpdateEventUrlAsync();
            }
            else
            {
                ClearEventUrl();
            }
        }

        private async void AppointmentRibbon_Load(object sender, RibbonUIEventArgs e)
        {
            UpdateStrings();
            ClearEventUrl();

            if (ThisAddIn.Components == null)
            {
                ThisAddIn.ComponentsCreated += ThisAddIn_ComponentsCreated;
            }
            else
            {
                await OnStartup(ThisAddIn.Components);
            }
        }

        private async void ThisAddIn_ComponentsCreated(object sender, EventArgs e)
        {
            ThisAddIn.ComponentsCreated -= ThisAddIn_ComponentsCreated;
            await OnStartup(ThisAddIn.Components);
        }

        private async void TelemostInternalMeeting_Click(object sender, RibbonControlEventArgs e)
        {
            Telemetry.Signal(Telemetry.ToolbarEvents, "telemost_internal_meeting_button");
            await LoginIfRequiredAndCreateOrUpdateMeetingAsync(e.Control.Context as Inspector, true);
        }

        private async void TelemostExternalMeeting_Click(object sender, RibbonControlEventArgs e)
        {
            Telemetry.Signal(Telemetry.ToolbarEvents, "telemost_external_meeting_button");
            await LoginIfRequiredAndCreateOrUpdateMeetingAsync(e.Control.Context as Inspector, false);           
        }

        private async void TelemostSettings_Click(object sender, RibbonControlEventArgs e)
        {
            s_logger.Info("ShowSettings");


            if (!(e.Control.Context is Inspector currentInspector))
            {
                return;
            }

            if (!(currentInspector.CurrentItem is AppointmentItem currentAppointment))
            {
                return;
            }

            var customTaskPane = await ThisAddIn.Components.PaneController.GetOrCreateSettingsPaneAsync(currentInspector);

            if (customTaskPane == null)
            {
                return;
            }

            var settingsControl = customTaskPane.Control as ITelemostSettingsControl;
            settingsControl?.UpdateMeetingInfo(currentAppointment.GetMeetingInfo());

            customTaskPane.Visible = true;
        }

        private void NavigateToYandexCalendar_Click(object sender, RibbonControlEventArgs e)
        {
            var url = "https://calendar.yandex.ru";

            var userId = _loginController?.UserInfo?.UserId;

            if (!string.IsNullOrEmpty(userId))
            {
                url += $"?uid={userId}";
            }
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };

            System.Diagnostics.Process.Start(startInfo);
        }

        private void EditEvent_Click(object sender, RibbonControlEventArgs e)
        {
            if (string.IsNullOrEmpty(_editEventUrl))
            {
                return;
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _editEventUrl,
                UseShellExecute = true
            };

            System.Diagnostics.Process.Start(startInfo);
        }

        #endregion
    }
}
