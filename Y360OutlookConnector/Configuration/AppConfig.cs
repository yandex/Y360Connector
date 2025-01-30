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

        private const string AlwaysSkipInvitationEmailsValueName = "alwaysSkipInvitationEmails";
        public static readonly bool AlwaysSkipInvitationEmailsDefaultValue = true;

        private const string StrongCodeConfirmationUsedValueName = "useStringCodeConfirmation";

        public static readonly bool StrongCodeConfirmationUsedDefaultValue = true;
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

        public static bool IsAlwaysSkipInvitationEmails => GetAppSettingValue(AlwaysSkipInvitationEmailsValueName, AlwaysSkipInvitationEmailsDefaultValue);

        public static bool IsStrongCodeConfirmationUsed => GetAppSettingValue(StrongCodeConfirmationUsedValueName, StrongCodeConfirmationUsedDefaultValue);
    }
}
