using System;
using System.Diagnostics;
using Microsoft.Office.Interop.Outlook;
using Application = Microsoft.Office.Interop.Outlook.Application;
using Explorer = Microsoft.Office.Interop.Outlook.Explorer;
using Y360OutlookConnector.Utilities;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Ui.RibbonXml
{
    /// <summary>Создание события из Explorer и переход в календарь (паритет с <see cref="Y360ConnectorRibbon"/>).</summary>
    internal static class RibbonXmlCalendarExplorerActions
    {
        internal static void OpenYandexCreateFromExplorerSelection(string telemetryEventId)
        {
            Application app =
                Globals.ThisAddIn != null ? Globals.ThisAddIn.Application : null;
            if (app == null)
            {
                return;
            }

            DateTime start;
            DateTime end;
            bool allDay;
            if (!TryGetSelectedCalendarSlot(app, out start, out end, out allDay))
            {
                GetDefaultCalendarSlot(out start, out end, out allDay);
            }

            string userId = ThisAddIn.Components != null &&
                             ThisAddIn.Components.LoginController != null &&
                             ThisAddIn.Components.LoginController.UserInfo != null
                ? ThisAddIn.Components.LoginController.UserInfo.UserId
                : null;

            string url = YCalendarUrlBuilder.BuildCreateEventUrl(
                start,
                end,
                userId,
                title: null,
                description: null,
                isAllDay: allDay,
                location: null,
                attendees: null,
                resources: null,
                eventType: YCalendarUrlBuilder.EventTypeUser);

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            Telemetry.Signal(Telemetry.YandexCalendarEvents, telemetryEventId);
        }

        internal static void OpenYandexCalendarFromExplorer()
        {
            string url = EndpointConfig.CalendarWebBaseUrl;

            string userId = ThisAddIn.Components != null &&
                            ThisAddIn.Components.LoginController != null &&
                            ThisAddIn.Components.LoginController.UserInfo != null
                ? ThisAddIn.Components.LoginController.UserInfo.UserId
                : null;

            if (!string.IsNullOrEmpty(userId))
            {
                url += "?uid=" + userId;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        /// <summary>
        /// Активный календарь и выделенный слот (как в <see cref="Y360ConnectorRibbon"/>).
        /// </summary>
        internal static bool TryGetSelectedCalendarSlot(
            Application app,
            out DateTime start,
            out DateTime end,
            out bool allDay)
        {
            start = default(DateTime);
            end = default(DateTime);
            allDay = false;

            if (app == null)
            {
                return false;
            }

            Explorer explorer = null;
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
                var cal = explorer.CurrentView as CalendarView;
                if (cal == null)
                {
                    return false;
                }

                DateTime s = cal.SelectedStartTime;
                DateTime eTime = cal.SelectedEndTime;
                if (eTime <= s)
                {
                    return false;
                }

                start = s;
                end = eTime;
                allDay = (end - start).TotalHours >= 20;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Округление слота по 30 минутам (как для кнопки на календаре).</summary>
        internal static void GetDefaultCalendarSlot(
            out DateTime start,
            out DateTime end,
            out bool allDay)
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
