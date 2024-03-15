using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Y360OutlookConnector.Configuration
{
    public static class AppConfig
    {
        private const string EnableAutoSyncValueName = "enableAutoSync";
        public const bool EnableAutoSyncDefaultValue = true;

        public static bool IsAutoSyncEnabled
        {
            get
            {
                var result = EnableAutoSyncDefaultValue;
                string str = ConfigurationManager.AppSettings[EnableAutoSyncValueName] ?? "";
                if (!String.IsNullOrEmpty(str) && Boolean.TryParse(str, out var parsedValue))
                    result = parsedValue;
                return result;
            }
        }
    }
}
