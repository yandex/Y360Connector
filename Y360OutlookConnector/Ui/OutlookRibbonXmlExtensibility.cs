using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using log4net;
using Office = Microsoft.Office.Core;
using Outlook = Microsoft.Office.Interop.Outlook;
using stdole;
using Y360OutlookConnector.Localization;
using Y360OutlookConnector.Properties;
using Y360OutlookConnector.Synchronization;
using Y360OutlookConnector.Ui.RibbonXml;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Ui
{
    /// Ribbon XML: функциональность <see cref="Y360ConnectorRibbon"/> и <see cref="AppointmentRibbon"/> через <see cref="Office.IRibbonExtensibility"/>.
    /// Включается флагом <c>UsePureXmlRibbon</c> в <see cref="ThisAddIn"/>.
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class OutlookRibbonXmlExtensibility : Office.IRibbonExtensibility
    {
        private static readonly ILog s_logger =
            LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private Office.IRibbonUI _explorerRibbonUi;
        private bool _explorerRibbonWired;
        private EventHandler _componentsCreatedHandler;

        private Office.IRibbonUI _appointmentRibbonUi;
        private bool _appointmentRibbonWired;
        private EventHandler _appointmentComponentsCreatedHandler;

        internal static string GetDiagnostics()
        {
            return "OutlookRibbonXmlExtensibility: Explorer + TabCalendar + appointment inspector + calendar context menus";
        }

        public string GetCustomUI(string ribbonId)
        {
            try
            {
                s_logger?.Debug("GetCustomUI: [" + (ribbonId ?? "null") + "]");
            }
            catch
            {
            }

            if (string.Equals(ribbonId, "Microsoft.Outlook.Explorer", StringComparison.Ordinal))
            {
                return RibbonXmlMarkup.BuildExplorerCustomUi();
            }

            if (string.Equals(ribbonId, "Microsoft.Outlook.Appointment", StringComparison.Ordinal))
            {
                return RibbonXmlMarkup.BuildAppointmentCustomUi();
            }

            return string.Empty;
        }

        public void OnExplorerRibbonLoad(Office.IRibbonUI ribbonUi)
        {
            _explorerRibbonUi = ribbonUi;
            TryWireExplorerRibbonModel();
        }

        private void TryWireExplorerRibbonModel()
        {
            if (ThisAddIn.Components != null)
            {
                WireExplorerRibbon(ThisAddIn.Components);
            }
            else
            {
                if (_componentsCreatedHandler == null)
                {
                    _componentsCreatedHandler = OnComponentsCreatedForExplorerRibbon;
                    ThisAddIn.ComponentsCreated += _componentsCreatedHandler;
                }
            }
        }

        private void OnComponentsCreatedForExplorerRibbon(object sender, EventArgs e)
        {
            if (_componentsCreatedHandler != null)
            {
                ThisAddIn.ComponentsCreated -= _componentsCreatedHandler;
                _componentsCreatedHandler = null;
            }

            WireExplorerRibbon(ThisAddIn.Components);
        }

        private void WireExplorerRibbon(ComponentContainer components)
        {
            if (_explorerRibbonWired)
            {
                return;
            }

            if (components == null)
            {
                return;
            }

            _explorerRibbonWired = true;

            var loginController = components.LoginController;
            if (loginController != null)
            {
                loginController.LoginStateChanged += ExplorerRibbon_LoginStateChanged;
            }
            else
            {
                try
                {
                    s_logger.Warn("[RibbonXml] WireExplorerRibbon: LoginController is null");
                }
                catch
                {
                }
            }

            var syncStatus = components.SyncStatus;
            if (syncStatus != null)
            {
                syncStatus.SyncStateChanged += ExplorerRibbon_SyncStateChanged;
                syncStatus.CriticalErrorChanged += ExplorerRibbon_CriticalErrorChanged;
            }

            InvalidateExplorerRibbonUi();
        }

        private void ExplorerRibbon_LoginStateChanged(object sender, LoginStateEventArgs e)
        {
            InvalidateExplorerRibbonUi();
        }

        private void ExplorerRibbon_SyncStateChanged(object sender, SyncStateChangedEventArgs e)
        {
            InvalidateExplorerRibbonUi();
        }

        private void ExplorerRibbon_CriticalErrorChanged(object sender, CriticalErrorChangedEventArgs e)
        {
            InvalidateExplorerRibbonUi();
        }

        private void InvalidateExplorerRibbonUi()
        {
            try
            {
                if (_explorerRibbonUi != null)
                {
                    _explorerRibbonUi.Invalidate();
                }
            }
            catch
            {
            }
        }

        public void OnAppointmentRibbonLoad(Office.IRibbonUI ribbonUi)
        {
            _appointmentRibbonUi = ribbonUi;
            TryWireAppointmentRibbonModel();
        }

        private void TryWireAppointmentRibbonModel()
        {
            if (ThisAddIn.Components != null)
            {
                WireAppointmentRibbon(ThisAddIn.Components);
            }
            else
            {
                if (_appointmentComponentsCreatedHandler == null)
                {
                    _appointmentComponentsCreatedHandler = OnComponentsCreatedForAppointmentRibbon;
                    ThisAddIn.ComponentsCreated += _appointmentComponentsCreatedHandler;
                }
            }
        }

        private void OnComponentsCreatedForAppointmentRibbon(object sender, EventArgs e)
        {
            if (_appointmentComponentsCreatedHandler != null)
            {
                ThisAddIn.ComponentsCreated -= _appointmentComponentsCreatedHandler;
                _appointmentComponentsCreatedHandler = null;
            }

            WireAppointmentRibbon(ThisAddIn.Components);
        }

        private void WireAppointmentRibbon(ComponentContainer components)
        {
            if (_appointmentRibbonWired)
            {
                return;
            }

            if (components == null)
            {
                return;
            }

            _appointmentRibbonWired = true;

            var loginController = components.LoginController;
            if (loginController != null)
            {
                loginController.LoginStateChanged += AppointmentRibbon_LoginStateChanged;
            }

            InvalidateAppointmentRibbonUi();
        }

        private void AppointmentRibbon_LoginStateChanged(object sender, LoginStateEventArgs e)
        {
            InvalidateAppointmentRibbonUi();
        }

        private void InvalidateAppointmentRibbonUi()
        {
            try
            {
                if (_appointmentRibbonUi != null)
                {
                    _appointmentRibbonUi.Invalidate();
                    InvalidateAppointmentRibbonEditControls();
                }
            }
            catch
            {
            }
        }

        private void InvalidateAppointmentRibbonEditControls()
        {
            if (_appointmentRibbonUi == null)
            {
                return;
            }

            try
            {
                _appointmentRibbonUi.InvalidateControl(RibbonXmlIds.ApptYcEdit);
            }
            catch
            {
            }

            try
            {
                _appointmentRibbonUi.InvalidateControl(RibbonXmlIds.SchedYcEdit);
            }
            catch
            {
            }

            try
            {
                _appointmentRibbonUi.InvalidateControl(RibbonXmlIds.ApptMenuYc);
            }
            catch
            {
            }

            try
            {
                _appointmentRibbonUi.InvalidateControl(RibbonXmlIds.SchedMenuYc);
            }
            catch
            {
            }
        }

        private static bool ExplorerIsLoggedIn()
        {
            var lc = ThisAddIn.Components != null ? ThisAddIn.Components.LoginController : null;
            return lc != null && lc.IsUserLoggedIn;
        }

        private static bool ToolsLayerHasErrors()
        {
            var syncStatus = ThisAddIn.Components != null ? ThisAddIn.Components.SyncStatus : null;
            if (syncStatus == null)
            {
                return false;
            }

            if (syncStatus.CriticalError != CriticalError.None)
            {
                return true;
            }

            return syncStatus.GetTotalSyncResult() == SyncResult.HasErrors;
        }

        public string GetExplorerRibbonLabel(Office.IRibbonControl control)
        {
            if (control == null)
            {
                return string.Empty;
            }

            string id = control.Id ?? string.Empty;
            var syncStatus = ThisAddIn.Components != null ? ThisAddIn.Components.SyncStatus : null;
            bool running = syncStatus != null && syncStatus.State == SyncState.Running;

            if (running)
            {
                if (string.Equals(id, RibbonXmlIds.SyncNow, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, RibbonXmlIds.SyncAll, StringComparison.OrdinalIgnoreCase))
                {
                    return Strings.Toolbar_SyncNowButtonRunning;
                }
            }

            if (string.Equals(id, RibbonXmlIds.MainTab, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Toolbar_RibbonTab;
            }

            if (string.Equals(id, RibbonXmlIds.MainGroup, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Toolbar_RibbonGroup;
            }

            if (string.Equals(id, RibbonXmlIds.Login, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Toolbar_LoginButton;
            }

            if (string.Equals(id, RibbonXmlIds.SyncNow, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Toolbar_SyncNowButton;
            }

            if (string.Equals(id, RibbonXmlIds.SyncAll, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Toolbar_SyncAllNowButton;
            }

            if (string.Equals(id, RibbonXmlIds.Tools, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Toolbar_SyncTargetsButton;
            }

            if (string.Equals(id, RibbonXmlIds.Settings, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Toolbar_SettingsButton;
            }

            if (string.Equals(id, RibbonXmlIds.About, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Toolbar_AboutButton;
            }

            if (string.Equals(id, RibbonXmlIds.Help, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Toolbar_HelpButton;
            }

            if (string.Equals(id, RibbonXmlIds.HomeCreateGroup, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.YandexCalendar_Toolbar_RibbonToolbarButton;
            }

            if (string.Equals(id, RibbonXmlIds.HomeYandexMenu, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.YandexCalendar_Toolbar_RibbonToolbarButton;
            }

            if (string.Equals(id, RibbonXmlIds.HomeYandexCreateItem, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, RibbonXmlIds.ContextMenuCalendarViewButton, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, RibbonXmlIds.ContextMenuCalendarNewItem, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.YandexCalendar_Toolbar_CreateEventButton;
            }

            if (string.Equals(id, RibbonXmlIds.HomeYandexNavigateItem, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.YandexCalendar_Toolbar_NavigateToCalendarButton;
            }

            return string.Empty;
        }

        public bool GetExplorerRibbonControlVisible(Office.IRibbonControl control)
        {
            if (control == null)
            {
                return true;
            }

            string id = control.Id ?? string.Empty;
            bool loggedIn = ExplorerIsLoggedIn();

            if (string.Equals(id, RibbonXmlIds.Login, StringComparison.OrdinalIgnoreCase))
            {
                return !loggedIn;
            }

            if (string.Equals(id, RibbonXmlIds.SyncNow, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, RibbonXmlIds.SyncAll, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, RibbonXmlIds.Tools, StringComparison.OrdinalIgnoreCase))
            {
                return loggedIn;
            }

            return true;
        }

        public bool GetExplorerRibbonControlEnabled(Office.IRibbonControl control)
        {
            if (control == null)
            {
                return true;
            }

            string id = control.Id ?? string.Empty;
            var syncStatus = ThisAddIn.Components != null ? ThisAddIn.Components.SyncStatus : null;
            bool running = syncStatus != null && syncStatus.State == SyncState.Running;

            if (string.Equals(id, RibbonXmlIds.SyncNow, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, RibbonXmlIds.SyncAll, StringComparison.OrdinalIgnoreCase))
            {
                return !running;
            }

            return true;
        }

        public IPictureDisp GetExplorerRibbonControlImage(Office.IRibbonControl control)
        {
            if (control == null)
            {
                return null;
            }

            try
            {
                string id = control.Id ?? string.Empty;

                if (string.Equals(id, RibbonXmlIds.Login, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicLogin, Resources.Login);
                }

                if (string.Equals(id, RibbonXmlIds.SyncNow, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, RibbonXmlIds.SyncAll, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicSyncNow, Resources.SyncNow);
                }

                if (string.Equals(id, RibbonXmlIds.Tools, StringComparison.OrdinalIgnoreCase))
                {
                    bool err = ToolsLayerHasErrors();
                    if (err)
                    {
                        return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicToolsErr, Resources.Attention);
                    }

                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicToolsOk, Resources.Profiles);
                }

                if (string.Equals(id, RibbonXmlIds.Settings, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicSettings, Resources.Settings);
                }

                if (string.Equals(id, RibbonXmlIds.About, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicAbout, Resources.About);
                }

                if (string.Equals(id, RibbonXmlIds.Help, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicHelp, Resources.Help);
                }

                if (string.Equals(id, RibbonXmlIds.HomeYandexMenu, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicYandexCalendar, Resources.YandexCalendar);
                }

                if (string.Equals(id, RibbonXmlIds.HomeYandexCreateItem, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicEdit, Resources.Edit);
                }

                if (string.Equals(id, RibbonXmlIds.HomeYandexNavigateItem, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicCalendar, Resources.Calendar);
                }

                if (string.Equals(id, RibbonXmlIds.ContextMenuCalendarViewButton, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, RibbonXmlIds.ContextMenuCalendarNewItem, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicYandexCalendar, Resources.YandexCalendar);
                }

                return null;
            }
            catch (Exception ex)
            {
                try
                {
                    s_logger.Warn("[RibbonXml] GetExplorerRibbonControlImage failed", ex);
                }
                catch
                {
                }

                return null;
            }
        }

        public void OnExplorerRibbonAction(Office.IRibbonControl control)
        {
            if (control == null)
            {
                return;
            }

            string id = control.Id ?? string.Empty;

            try
            {
                if (string.Equals(id, RibbonXmlIds.HomeYandexCreateItem, StringComparison.OrdinalIgnoreCase))
                {
                    RibbonXmlCalendarExplorerActions.OpenYandexCreateFromExplorerSelection("create_event_from_home_calendar_button_direct");
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.HomeYandexNavigateItem, StringComparison.OrdinalIgnoreCase))
                {
                    RibbonXmlCalendarExplorerActions.OpenYandexCalendarFromExplorer();
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.ContextMenuCalendarViewButton, StringComparison.OrdinalIgnoreCase))
                {
                    RibbonXmlCalendarExplorerActions.OpenYandexCreateFromExplorerSelection("create_event_from_calendar_context_menu_calendar_view");
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.ContextMenuCalendarNewItem, StringComparison.OrdinalIgnoreCase))
                {
                    RibbonXmlCalendarExplorerActions.OpenYandexCreateFromExplorerSelection("create_event_from_calendar_context_menu_new_item");
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.Login, StringComparison.OrdinalIgnoreCase))
                {
                    Telemetry.Signal(Telemetry.ToolbarEvents, "login_button");
                    if (ThisAddIn.Components != null)
                    {
                        ThisAddIn.Components.StartLogin();
                    }

                    return;
                }

                if (string.Equals(id, RibbonXmlIds.SyncNow, StringComparison.OrdinalIgnoreCase))
                {
                    var syncManager = ThisAddIn.Components != null ? ThisAddIn.Components.SyncManager : null;
                    if (syncManager == null)
                    {
                        return;
                    }

                    Telemetry.Signal(Telemetry.ToolbarEvents, "sync_now_button");
                    _ = syncManager.RunSynchronization(true, false);
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.SyncAll, StringComparison.OrdinalIgnoreCase))
                {
                    var syncManager = ThisAddIn.Components != null ? ThisAddIn.Components.SyncManager : null;
                    if (syncManager == null)
                    {
                        return;
                    }

                    syncManager.AutoSyncDisabled = true;
                    MessageBoxResult result = MessageBox.Show(
                        Strings.Messages_SyncAllMessageDescription,
                        Strings.Messages_SyncAllMessageTitle,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    syncManager.AutoSyncDisabled = false;

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }

                    Telemetry.Signal(Telemetry.ToolbarEvents, "sync_all_now_button");
                    _ = syncManager.RunSynchronization(true, true);
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.Tools, StringComparison.OrdinalIgnoreCase))
                {
                    Telemetry.Signal(Telemetry.ToolbarEvents, "tools_and_layers_button");
                    if (ThisAddIn.Components != null)
                    {
                        ThisAddIn.Components.ShowSyncConfigWindow();
                    }

                    return;
                }

                if (string.Equals(id, RibbonXmlIds.Settings, StringComparison.OrdinalIgnoreCase))
                {
                    Telemetry.Signal(Telemetry.ToolbarEvents, "settings_button");
                    SettingsWindow.ShowOrActivate();
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.About, StringComparison.OrdinalIgnoreCase))
                {
                    Telemetry.Signal(Telemetry.ToolbarEvents, "about_button");
                    if (ThisAddIn.Components != null)
                    {
                        ThisAddIn.Components.ShowAboutWindow();
                    }

                    return;
                }

                if (string.Equals(id, RibbonXmlIds.Help, StringComparison.OrdinalIgnoreCase))
                {
                    Telemetry.Signal(Telemetry.ToolbarEvents, "help_button");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = EndpointConfig.HelpUrl,
                        UseShellExecute = true
                    });
                    return;
                }

                try
                {
                    s_logger.Warn("[RibbonXml] OnExplorerRibbonAction unhandled: " + id);
                }
                catch
                {
                }
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        public string GetAppointmentRibbonLabel(Office.IRibbonControl control)
        {
            if (control == null)
            {
                return string.Empty;
            }

            string id = control.Id ?? string.Empty;

            if (string.Equals(id, RibbonXmlIds.ApptMenuTele, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Telemost_Toolbar_RibbonMenuButton;
            }

            if (string.Equals(id, RibbonXmlIds.ApptTeleInt, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Telemost_Toolbar_InternalMeetingButton;
            }

            if (string.Equals(id, RibbonXmlIds.ApptTeleExt, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Telemost_Toolbar_ExternalMeetingButton;
            }

            if (string.Equals(id, RibbonXmlIds.ApptTeleSettings, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.Telemost_Toolbar_SettingsButton;
            }

            if (string.Equals(id, RibbonXmlIds.ApptMenuYc, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, RibbonXmlIds.SchedMenuYc, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.YandexCalendar_Toolbar_RibbonToolbarButton;
            }

            if (string.Equals(id, RibbonXmlIds.ApptYcCreate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, RibbonXmlIds.SchedYcCreate, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.YandexCalendar_Toolbar_CreateEventButton;
            }

            if (string.Equals(id, RibbonXmlIds.ApptYcEdit, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, RibbonXmlIds.SchedYcEdit, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.YandexCalendar_Toolbar_EditEventButton;
            }

            if (string.Equals(id, RibbonXmlIds.ApptYcNav, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, RibbonXmlIds.SchedYcNav, StringComparison.OrdinalIgnoreCase))
            {
                return Strings.YandexCalendar_Toolbar_NavigateToCalendarButton;
            }

            return string.Empty;
        }

        public bool GetAppointmentYandexEditEnabled(Office.IRibbonControl control)
        {
            try
            {
                var lc = ThisAddIn.Components != null ? ThisAddIn.Components.LoginController : null;
                if (lc == null || !lc.IsUserLoggedIn)
                {
                    return false;
                }

                if (control == null)
                {
                    return false;
                }

                var inspector = control.Context as Outlook.Inspector;
                if (inspector == null)
                {
                    return false;
                }

                return RibbonXmlAppointmentEditUrl.RefreshCacheAndReturnEnabled(inspector, lc);
            }
            catch
            {
                return false;
            }
        }

        public IPictureDisp GetAppointmentRibbonControlImage(Office.IRibbonControl control)
        {
            if (control == null)
            {
                return null;
            }

            try
            {
                string id = control.Id ?? string.Empty;

                if (string.Equals(id, RibbonXmlIds.ApptMenuTele, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicTelemost2022, Resources.Yandex_telemost_2022);
                }

                if (string.Equals(id, RibbonXmlIds.ApptTeleInt, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicTelemostInternal, Resources.TelemostInternalMeeting);
                }

                if (string.Equals(id, RibbonXmlIds.ApptTeleExt, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicTelemostExternal, Resources.TelemostExternalMeeting);
                }

                if (string.Equals(id, RibbonXmlIds.ApptTeleSettings, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicTelemostSettings, Resources.TelemostSettings);
                }

                if (string.Equals(id, RibbonXmlIds.ApptMenuYc, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, RibbonXmlIds.SchedMenuYc, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicYandexCalendar, Resources.YandexCalendar);
                }

                if (string.Equals(id, RibbonXmlIds.ApptYcCreate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, RibbonXmlIds.SchedYcCreate, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicYandexCalendar, Resources.YandexCalendar);
                }

                if (string.Equals(id, RibbonXmlIds.ApptYcEdit, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, RibbonXmlIds.SchedYcEdit, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicEdit, Resources.Edit);
                }

                if (string.Equals(id, RibbonXmlIds.ApptYcNav, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, RibbonXmlIds.SchedYcNav, StringComparison.OrdinalIgnoreCase))
                {
                    return RibbonXmlImageCache.GetOrCreateCachedImage(ref RibbonXmlImageCache.PicCalendar, Resources.Calendar);
                }

                return null;
            }
            catch (Exception ex)
            {
                try
                {
                    s_logger.Warn("[RibbonXml] GetAppointmentRibbonControlImage failed", ex);
                }
                catch
                {
                }

                return null;
            }
        }

        public void OnAppointmentRibbonAction(Office.IRibbonControl control)
        {
            if (control == null)
            {
                return;
            }

            var inspector = control.Context as Outlook.Inspector;
            string id = control.Id ?? string.Empty;

            try
            {
                if (string.Equals(id, RibbonXmlIds.ApptTeleInt, StringComparison.OrdinalIgnoreCase))
                {
                    Telemetry.Signal(Telemetry.ToolbarEvents, "telemost_internal_meeting_button");
                    RibbonXmlTaskHelpers.ObserveFaultedTask(YandexCalendarAppointmentActions.XmlTelemostLoginAndMeetingAsync(inspector, true));
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.ApptTeleExt, StringComparison.OrdinalIgnoreCase))
                {
                    Telemetry.Signal(Telemetry.ToolbarEvents, "telemost_external_meeting_button");
                    RibbonXmlTaskHelpers.ObserveFaultedTask(YandexCalendarAppointmentActions.XmlTelemostLoginAndMeetingAsync(inspector, false));
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.ApptTeleSettings, StringComparison.OrdinalIgnoreCase))
                {
                    RibbonXmlTaskHelpers.ObserveFaultedTask(YandexCalendarAppointmentActions.XmlTelemostOpenSettingsAsync(inspector));
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.ApptYcCreate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, RibbonXmlIds.SchedYcCreate, StringComparison.OrdinalIgnoreCase))
                {
                    YandexCalendarAppointmentActions.XmlCreateEventInYandexCalendar(inspector);
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.ApptYcEdit, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, RibbonXmlIds.SchedYcEdit, StringComparison.OrdinalIgnoreCase))
                {
                    if (RibbonXmlAppointmentEditUrl.TryOpenCached(inspector))
                    {
                        return;
                    }

                    if (RibbonXmlAppointmentEditUrl.TryOpenFresh(inspector))
                    {
                        return;
                    }

                    RibbonXmlTaskHelpers.ObserveFaultedTask(YandexCalendarAppointmentActions.XmlOpenEditEventUrlAsync(inspector));
                    return;
                }

                if (string.Equals(id, RibbonXmlIds.ApptYcNav, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, RibbonXmlIds.SchedYcNav, StringComparison.OrdinalIgnoreCase))
                {
                    YandexCalendarAppointmentActions.XmlNavigateToYandexCalendar();
                    return;
                }

                try
                {
                    s_logger.Warn("[RibbonXml] OnAppointmentRibbonAction unhandled: " + id);
                }
                catch
                {
                }
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }
    }
}
