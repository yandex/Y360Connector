using System.Reflection;
using Yandex.Metrica;
using Newtonsoft.Json.Linq;
using log4net;
using Newtonsoft.Json;

namespace Y360OutlookConnector
{
    public static class Telemetry
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public const string ToolbarEvents = "toolbar";
        public const string AboutWindowEvents = "about_window";
        public const string AutoUpdateEvents = "auto_updates";
        public const string SettingsWindowEvents = "settings_window";
        public const string LoginWindowEvents = "login_window";
        public const string AppStartEvents = "app_start";
        public const string SyncConfigWindowEvents = "sync_config_window";
        public const string ErrorWindowEvents = "error_window";
        public const string SyncReportsEvents = "sync_reports";

        public static void Initialize(string dataFolder)
        {
            YandexMetricaFolder.SetCurrent(dataFolder);

            YandexMetrica.Config.CrashTracking = false;
            YandexMetrica.Config.LocationTracking = false;
            YandexMetrica.Config.OfflineMode = false;
            YandexMetrica.Config.CustomAppVersion = Assembly.GetExecutingAssembly().GetName().Version;
            YandexMetrica.Config.CustomAppId = "4427623";
            YandexMetrica.Activate("783f9c79-3d57-4ed1-b17c-a5527a5ba363");
        }

        public static void Shutdown()
        {
            YandexMetrica.Snapshot();
        }

        public static void Signal(string eventGroup, string eventName, object value)
        {
            try
            {
                var payload = new JObject
                {
                    [eventName] = value != null ? JToken.FromObject(value) : JValue.CreateNull()
                };

#if !DEBUG
                YandexMetrica.ReportEvent(eventGroup, JsonConvert.SerializeObject(payload));
#endif
            }
            catch (System.Exception exc)
            {
                s_logger.Warn($"Send event {eventGroup}.{eventName} error:", exc);
            }
        }

        public static void Signal(string eventGroup, string eventName)
        {
            try
            {
                var payload = new JObject
                {
                    [eventName] = new JObject()
                };

#if !DEBUG
                YandexMetrica.ReportEvent(eventGroup, JsonConvert.SerializeObject(payload));
#endif
            }
            catch (System.Exception exc)
            {
                s_logger.Warn($"Send event {eventGroup}.{eventName} error:", exc);
            }
        }

        public static void SignalError(string errorType, System.Exception error)
        {
            try
            {
                var payload = new JObject
                {
                    [errorType] = error.ToString()
                };

#if !DEBUG
                YandexMetrica.ReportEvent("errors", JsonConvert.SerializeObject(payload));
#endif
            }
            catch (System.Exception exc)
            {
                s_logger.Warn($"Send unhandled exception error:", exc);
            }
        }
    }
}
