using System;
using System.Configuration;

namespace Y360OutlookConnector.Configuration
{
    public static class AppConfig
    {
        private const string EnableAutoSyncValueName = "enableAutoSync";
        public static readonly bool EnableAutoSyncDefaultValue = true;

        private const string AlwaysEnableEditEventButtonValueName = "alwaysAllowEditEvent";
        public static readonly bool AlwaysEnableEditEventButtonDefaultValue = false;

        private static bool GetAppSettingValue(string settingName, bool defaultValue)
        {
            var result = defaultValue;
            var str = ConfigurationManager.AppSettings[settingName] ?? String.Empty;
            if (!String.IsNullOrEmpty(str) && Boolean.TryParse(str, out var parsedValue))
            {
                result = parsedValue;
            }

            return result;
        }

        public static bool IsAutoSyncEnabled => GetAppSettingValue(EnableAutoSyncValueName, EnableAutoSyncDefaultValue);

        public static bool IsAlwaysEnableEditEventButton => GetAppSettingValue(AlwaysEnableEditEventButtonValueName, AlwaysEnableEditEventButtonDefaultValue);
    }
}
