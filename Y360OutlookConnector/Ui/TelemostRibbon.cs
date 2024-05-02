using System;
using System.Reflection;
using System.Threading.Tasks;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Microsoft.Office.Tools.Ribbon;
using Y360OutlookConnector.Ui.Extensions;

namespace Y360OutlookConnector.Ui
{
    public partial class TelemostRibbon
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private LoginController _loginController;

        private void TelemostRibbon_Load(object sender, RibbonUIEventArgs e)
        {
            if (ThisAddIn.Components == null)
            {
                ThisAddIn.ComponentsCreated += ThisAddIn_ComponentsCreated;
            }
            else
            {
                OnStartup(ThisAddIn.Components);
            }
        }

        private void ThisAddIn_ComponentsCreated(object sender, EventArgs e)
        {
            ThisAddIn.ComponentsCreated -= ThisAddIn_ComponentsCreated;
            OnStartup(ThisAddIn.Components);
        }

        private void OnStartup(ComponentContainer componentContainer)
        {
            UpdateStrings();

            _loginController = componentContainer?.LoginController;
        }

        private void UpdateStrings()
        {
            TelemostSettings.Label = Localization.Strings.Telemost_Toolbar_SettingsButton; ;
            TelemostInternalMeeting.Label = Localization.Strings.Telemost_Toolbar_InternalMeetingButton;
            TelemostExternalMeeting.Label = Localization.Strings.Telemost_Toolbar_ExternalMeetingButton;
            TelemostRibbonMenu.Label = Localization.Strings.Telemost_Toolbar_RibbonMenuButton;           
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

                //Telemetry.Signal(Telemetry.ToolbarEvents, "login_button");
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
    }
}
