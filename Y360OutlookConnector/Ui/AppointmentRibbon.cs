using System;
using System.Reflection;
using System.Threading.Tasks;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Microsoft.Office.Tools.Ribbon;
using Y360OutlookConnector.Ui.Extensions;

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

            btnCreateEventInYandexInAppointment.Label = Localization.Strings.YandexCalendar_Toolbar_CreateEventButton;
            btnNavigateToYandexCalendarInAppointment.Label = Localization.Strings.YandexCalendar_Toolbar_NavigateToCalendarButton;
            btnEditEventInAppointment.Label = Localization.Strings.YandexCalendar_Toolbar_EditEventButton;
            YandexCalendarRibbonMenu.Label = Localization.Strings.YandexCalendar_Toolbar_RibbonToolbarButton;

            btnCreateEventInYandexInSchedulingAssistant.Label = Localization.Strings.YandexCalendar_Toolbar_CreateEventButton;
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
            await YandexCalendarAppointmentActions.CreateOrUpdateMeetingForInspectorAsync(inspector, isMeetingInternal);
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

        private async Task<string> GetEventUrlAsync()
        {
            return await YandexCalendarAppointmentActions.GetCalendarEditUrlAsync(CurrentAppointment, _loginController);
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


            var inspector = e.Control.Context as Inspector;
            if (inspector == null)
            {
                return;
            }

            await YandexCalendarAppointmentActions.XmlTelemostOpenSettingsAsync(inspector);
        }

        private void NavigateToYandexCalendar_Click(object sender, RibbonControlEventArgs e)
        {
           YandexCalendarAppointmentActions.XmlNavigateToYandexCalendar();
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

        private void CreateEventInYandexCalendar_Click(object sender, RibbonControlEventArgs e)
        {
            var inspector = e.Control.Context as Inspector;
            var appointment = OutlookInspectorAppointmentHelper.TryGetAppointmentItemFromInspector(inspector);
            YandexCalendarAppointmentActions.CreateEventInYandexCalendarForAppointment(appointment, _loginController);
        }
        #endregion
    }
}
