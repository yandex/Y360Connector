using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnector.Ui.RibbonXml
{
    /// <summary>
    /// Кэш URL "Редактировать в Яндекс Календаре" для Ribbon XML (синхронный resolve в <c>getEnabled</c> и при клике).
    /// </summary>
    internal static class RibbonXmlAppointmentEditUrl
    {
        private static readonly ILog s_logger =
            LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private static readonly ConditionalWeakTable<Inspector, EditUrlCacheInfo> s_cache =
            new ConditionalWeakTable<Inspector, EditUrlCacheInfo>();

        private sealed class EditUrlCacheInfo
        {
            internal string EditUrl;
        }

        internal static bool RefreshCacheAndReturnEnabled(Inspector inspector, LoginController lc)
        {
            if (inspector == null || lc == null || !lc.IsUserLoggedIn)
            {
                return false;
            }

            var appointment = OutlookInspectorAppointmentHelper.TryGetAppointmentItemFromInspector(inspector);
            if (appointment == null)
            {
                return false;
            }

            string uid = AppointmentItemUtils.ExtractUidFromGlobalId(appointment.GlobalAppointmentID);
            if (string.IsNullOrEmpty(uid))
            {
                return false;
            }

            EditUrlCacheInfo info = s_cache.GetValue(inspector, _ => new EditUrlCacheInfo());

            string url = YandexCalendarAppointmentActions.GetCalendarEditUrlAsync(appointment, lc)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            info.EditUrl = string.IsNullOrEmpty(url) ? null : url;
            return !string.IsNullOrEmpty(info.EditUrl);
        }

        internal static bool TryOpenCached(Inspector inspector)
        {
            if (inspector == null)
            {
                return false;
            }

            EditUrlCacheInfo info;
            if (!s_cache.TryGetValue(inspector, out info))
            {
                return false;
            }

            var url = info.EditUrl;
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return true;
            }
            catch (System.Exception exc)
            {
                try
                {
                    s_logger.Warn("[RibbonXml] TryOpenCachedAppointmentEditUrl failed", exc);
                }
                catch
                {
                }

                return false;
            }
        }

        internal static bool TryOpenFresh(Inspector inspector)
        {
            if (inspector == null)
            {
                return false;
            }

            try
            {
                var appointment = OutlookInspectorAppointmentHelper.TryGetAppointmentItemFromInspector(inspector);
                var lc = ThisAddIn.Components != null ? ThisAddIn.Components.LoginController : null;
                if (appointment == null || lc == null || !lc.IsUserLoggedIn)
                {
                    return false;
                }

                string url = YandexCalendarAppointmentActions.GetCalendarEditUrlAsync(appointment, lc)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                if (string.IsNullOrEmpty(url))
                {
                    return false;
                }

                EditUrlCacheInfo info = s_cache.GetValue(inspector, _ => new EditUrlCacheInfo());
                info.EditUrl = url;

                return TryOpenCached(inspector);
            }
            catch (System.Exception exc)
            {
                try
                {
                    s_logger.Warn("[RibbonXml] TryOpenFreshAppointmentEditUrl failed", exc);
                }
                catch
                {
                }

                return false;
            }
        }
    }
}
