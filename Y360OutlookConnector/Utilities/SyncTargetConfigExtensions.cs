using System;
using System.Linq;
using System.Text.RegularExpressions;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Utilities
{
    public static class SyncTargetConfigExtensions
    {
        private static Regex EventRegExp = new Regex("^events-(\\d+)/{0,1}$", RegexOptions.Compiled);
        public static string GetLayerId(this SyncTargetConfig config) 
        {
            Uri url;

            try
            {
                url = new Uri(config.Url);
            }
            catch(Exception)
            {
                return null;
            }
            
            var lastSegment = url.Segments.LastOrDefault();

            if (string.IsNullOrEmpty(lastSegment))
            {
                return null;
            }

            var match = EventRegExp.Match(lastSegment);

            if (!match.Success)
            {
                return null;
            }

            return match.Groups[1].Value;
        }

    }
}
