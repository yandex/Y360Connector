using System;
using System.Reflection;
using log4net;
using Microsoft.Win32;

namespace Y360OutlookConnector.Configuration
{
    public static class RegistrySettings
    {
        public const string SettingsKeyPath = "SOFTWARE\\Yandex\\Y360.OutlookConnector";

        const string DisableAutomaticUpdatesValueName = "DisableAutomaticUpdates";
        const string AutomaticUpdatesChannelValueName = "AutomaticUpdatesChannel";
        const string FirstTimeValueName = "FirstTime";

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public enum UpdateChannel
        {
            Alpha,
            Beta,
            Stable
        }

        public static UpdateChannel GetUpdateChannel()
        {
            var result = UpdateChannel.Stable;
            try
            {
                using (var regKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath))
                {
                    object value = regKey?.GetValue(AutomaticUpdatesChannelValueName);
                    if (value is string str)
                    {
                        result = (UpdateChannel)Enum.Parse(typeof(UpdateChannel), str, true);
                    }
                }
            }
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to retrieve {AutomaticUpdatesChannelValueName} " +
                              $"from HKCU\\{SettingsKeyPath}", ex);
            }
            return result;
        }

        public static bool IsAutomaticUpdatesDisabled()
        {
            var result = false;
            try
            {
                using (var regKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath))
                {
                    object value = regKey?.GetValue(DisableAutomaticUpdatesValueName);
                    if (value is string stringValue)
                    {
                        Boolean.TryParse(stringValue, out result);
                    }
                    else
                    {
                        result = Convert.ToBoolean(value);
                    }
                }
            }
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to retrieve {DisableAutomaticUpdatesValueName} " +
                              $"from HKCU\\{SettingsKeyPath}", ex);
            }

            return result;
        }

        public static bool IsFirstTimeRun()
        {
            bool result = false;
            try
            {
                using (var regKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath))
                {
                    object value = regKey?.GetValue(FirstTimeValueName);
                    if (value is string stringValue)
                    {
                        Boolean.TryParse(stringValue, out result);
                    }
                    else
                    {
                        result = Convert.ToBoolean(value);
                    }
                }
            }
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to retrieve {FirstTimeValueName} " +
                              $"from HKCU\\{SettingsKeyPath}", ex);
            }
            return result;
        }

        public static void DeleteFirstTimeValue()
        {
            try
            {
                using (var regKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, true))
                {
                    regKey?.DeleteValue(FirstTimeValueName);
                }
            }
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to delete {FirstTimeValueName} " +
                              $"from HKCU\\{SettingsKeyPath}", ex);
            }
        }
    }
}
