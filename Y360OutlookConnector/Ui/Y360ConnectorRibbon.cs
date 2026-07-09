using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using log4net;
using Microsoft.Office.Tools.Ribbon;
using Y360OutlookConnector.Synchronization;
using Y360OutlookConnector.Utilities;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Ui
{
    public partial class Y360ConnectorRibbon
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private void Y360ConnectorRibbon_Load(object sender, RibbonUIEventArgs e)
        {
            s_logger.Info("Y360ConnectorRibbon Load");

            SyncNowButton.Visible = false;
            SyncAllNowButton.Visible = false;
            ToolsAndLayersButton.Visible = false;
            LoginButton.Visible = true;

            if (ThisAddIn.Components == null)
                ThisAddIn.ComponentsCreated += ThisAddIn_ComponentsCreated;
            else
                OnStartup(ThisAddIn.Components);
        }

        private void ThisAddIn_ComponentsCreated(object sender, EventArgs e)
        {
            ThisAddIn.ComponentsCreated -= ThisAddIn_ComponentsCreated;
            OnStartup(ThisAddIn.Components);
        }

        private void UpdateStrings()
        {
            tab1.Label = Localization.Strings.Toolbar_RibbonTab;
            LoginButton.Label = Localization.Strings.Toolbar_LoginButton;
            MainGroup.Label = Localization.Strings.Toolbar_RibbonGroup;
            SyncNowButton.Label = Localization.Strings.Toolbar_SyncNowButton;
            SyncAllNowButton.Label = Localization.Strings.Toolbar_SyncAllNowButton;
            ToolsAndLayersButton.Label = Localization.Strings.Toolbar_SyncTargetsButton;
            SettingsButton.Label = Localization.Strings.Toolbar_SettingsButton;
            AboutButton.Label = Localization.Strings.Toolbar_AboutButton;
            HelpButton.Label = Localization.Strings.Toolbar_HelpButton;
            HomeCreateGroup.Label = Localization.Strings.YandexCalendar_Toolbar_RibbonToolbarButton;
            CreateYandexMeetingFromHomeButton.Label = Localization.Strings.YandexCalendar_Toolbar_CreateEventButton;
        }

        private void OnStartup(ComponentContainer componentContainer)
        {
            UpdateStrings();

            var loginController = componentContainer?.LoginController;
            if (loginController != null)
            {
                loginController.LoginStateChanged += LoginController_LoginStateChanged;
                SetUserLoggedIn(loginController.IsUserLoggedIn);
            }
            else
            {
                s_logger.Warn("Login controller in null");
            }

            var syncStatus = componentContainer?.SyncStatus;
            if (syncStatus != null)
            {
                syncStatus.SyncStateChanged += SyncStatus_SyncStateChanged;
            }
        }

        private void LoginController_LoginStateChanged(object sender, LoginStateEventArgs e)
        {
            SetUserLoggedIn(e.IsUserLoggedIn);
        }

        private void SyncStatus_SyncStateChanged(object sender, SyncStateChangedEventArgs e)
        {
            var syncStatus = ThisAddIn.Components?.SyncStatus;
            if (syncStatus == null) return;

            if (e.State == SyncState.Running)
            {
                SyncNowButton.Enabled = false;
                SyncAllNowButton.Enabled = false;
                SyncNowButton.Label = Localization.Strings.Toolbar_SyncNowButtonRunning;
                SyncAllNowButton.Label = Localization.Strings.Toolbar_SyncNowButtonRunning;
            }
            else
            {
                SyncNowButton.Enabled = true;
                SyncAllNowButton.Enabled = true;
                SyncNowButton.Label = Localization.Strings.Toolbar_SyncNowButton;
                SyncAllNowButton.Label = Localization.Strings.Toolbar_SyncAllNowButton;
            }

            bool hasErrors = syncStatus.CriticalError != CriticalError.None;
            if (!hasErrors)
            {
                hasErrors = syncStatus.GetTotalSyncResult() == SyncResult.HasErrors;
            }

            ToolsAndLayersButton.Image = hasErrors
                ? Properties.Resources.Attention
                : Properties.Resources.Profiles;
        }

        private void SetUserLoggedIn(bool isUserLoggedIn)
        {
            SyncNowButton.Visible = isUserLoggedIn;
            SyncAllNowButton.Visible = isUserLoggedIn;
            ToolsAndLayersButton.Visible = isUserLoggedIn;
            LoginButton.Visible = !isUserLoggedIn;
        }

        private void LoginButton_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                Telemetry.Signal(Telemetry.ToolbarEvents, "login_button");
                ThisAddIn.Components?.StartLogin();
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        private void SyncAllNowButton_Click(object sender, RibbonControlEventArgs e)
        {
            var syncManager = ThisAddIn.Components?.SyncManager;

            if (syncManager == null)
            {
                return;
            }

            // Запрещаем синхронизацию по таймеру, пока пользователь не закроет диалоговое окно
            syncManager.AutoSyncDisabled = true;
            var result = MessageBox.Show(Localization.Strings.Messages_SyncAllMessageDescription,
                            Localization.Strings.Messages_SyncAllMessageTitle,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
            syncManager.AutoSyncDisabled = false;

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
            try
            {
                Telemetry.Signal(Telemetry.ToolbarEvents, "sync_all_now_button");
                _ = syncManager.RunSynchronization(true, true);
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        private void SyncNowButton_Click(object sender, RibbonControlEventArgs e)
        {
            var syncManager = ThisAddIn.Components?.SyncManager;

            if (syncManager == null)
            {
                return;
            }

            try
            {
                Telemetry.Signal(Telemetry.ToolbarEvents, "sync_now_button");
                _ = syncManager.RunSynchronization(true, false);
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        private void ToolsAndLayersButton_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                Telemetry.Signal(Telemetry.ToolbarEvents, "tools_and_layers_button");
                ThisAddIn.Components?.ShowSyncConfigWindow();
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        private void SettingsButton_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                Telemetry.Signal(Telemetry.ToolbarEvents, "settings_button");
                SettingsWindow.ShowOrActivate();
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        private void AboutButton_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                Telemetry.Signal(Telemetry.ToolbarEvents, "about_button");
                ThisAddIn.Components?.ShowAboutWindow();
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        private void HelpButton_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                Telemetry.Signal(Telemetry.ToolbarEvents, "help_button");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://yandex.ru/support/calendar-business/plug-in.html",
                    UseShellExecute = true
                });
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        private void CreateYandexMeetingFromHomeButton_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                Outlook.Application app = Globals.ThisAddIn != null ? Globals.ThisAddIn.Application : null;
                if (app == null)
                {
                    return;
                }

                DateTime start;
                DateTime end;
                bool allDay;
                if (!TryGetSelectedSlot(app, out start, out end, out allDay))
                {
                    GetDefaultSlot(out start, out end, out allDay);
                }

                var userId = ThisAddIn.Components != null &&
                             ThisAddIn.Components.LoginController != null &&
                             ThisAddIn.Components.LoginController.UserInfo != null
                    ? ThisAddIn.Components.LoginController.UserInfo.UserId
                    : null;

                string url = YCalendarUrlBuilder.BuildCreateEventUrl(
                    start,
                    end,
                    userId,
                    eventType: YCalendarUrlBuilder.EventTypeUser);

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                Telemetry.Signal(Telemetry.YandexCalendarEvents, "create_event_from_home_calendar_button_direct");
            }
            catch (Exception exc)
            {
                s_logger.Warn("CreateYandexMeetingFromHomeButton_Click failed", exc);
            }
        }

        private static bool TryGetSelectedSlot(Outlook.Application app, out DateTime start, out DateTime end, out bool allDay)
        {
            start = default(DateTime);
            end = default(DateTime);
            allDay = false;

            if (app == null)
            {
                return false;
            }

            Outlook.Explorer explorer;
            try
            {
                explorer = app.ActiveExplorer();
            }
            catch
            {
                return false;
            }

            if (explorer == null)
            {
                return false;
            }

            try
            {
                var cal = explorer.CurrentView as Outlook.CalendarView;
                if (cal == null)
                {
                    return false;
                }

                var s = cal.SelectedStartTime;
                var e = cal.SelectedEndTime;
                if (e <= s)
                {
                    return false;
                }

                start = s;
                end = e;
                allDay = (end - start).TotalHours >= 20;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void GetDefaultSlot(out DateTime start, out DateTime end, out bool allDay)
        {
            allDay = false;
            DateTime now = DateTime.Now;
            const int alignMinutes = 30;

            double totalMinutes = now.TimeOfDay.TotalMinutes;
            double nextAligned = Math.Ceiling(totalMinutes / alignMinutes) * alignMinutes;
            if (nextAligned >= 24.0 * 60.0 - 0.1)
            {
                start = now.Date.AddDays(1);
            }
            else
            {
                start = now.Date.AddMinutes((int)nextAligned);
            }

            if (start <= now)
            {
                start = start.AddMinutes(alignMinutes);
            }

            end = start.AddMinutes(30);
        }
    }
}
