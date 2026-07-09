using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Reflection;
using DDay.iCal;
using log4net;
using System.IO;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    public class GlobalTimeZoneCache
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodInfo.GetCurrentMethod().DeclaringType);

        private readonly Dictionary<string, ITimeZone> _tzOutlookMap;
        private readonly Dictionary<string, ITimeZone> _tzHistoricalMap;

        public GlobalTimeZoneCache()
        {
            _tzOutlookMap = new Dictionary<string, ITimeZone>();
            _tzHistoricalMap = new Dictionary<string, ITimeZone>();
        }

        public async Task<ITimeZone> GetTimeZoneById(string tzId, bool includeHistoricalData, HttpClient httpClient)
        {
            ITimeZone tz = GetTzOrNull(tzId, includeHistoricalData);
            if (tz == null)
            {
                var uri = new Uri(EndpointConfig.GetTimeZoneDefinitionUrl(tzId, includeHistoricalData));
                var col = await LoadFromUriOrNull(httpClient, uri);
                if (col != null)
                {
                    tz = col[0].TimeZones[0];
                    AddTz(tzId, tz, includeHistoricalData);
                }
            }

            return tz;
        }

        private ITimeZone GetTzOrNull(string tzId, bool includeHistoricalData)
        {
            if (includeHistoricalData)
            {
                return _tzHistoricalMap.ContainsKey(tzId) ? _tzHistoricalMap[tzId] : null;
            }
            else
            {
                return _tzOutlookMap.ContainsKey(tzId) ? _tzOutlookMap[tzId] : null;
            }
        }

        private void AddTz(string tzId, ITimeZone timeZone, bool includeHistoricalData)
        {
            if (includeHistoricalData)
            {
                _tzHistoricalMap.Add(tzId, timeZone);
            }
            else
            {
                _tzOutlookMap.Add(tzId, timeZone);
            }
        }

        private async Task<IICalendarCollection> LoadFromUriOrNull(HttpClient httpClient, Uri uri)
        {
            try
            {
                using (var response = await httpClient.GetAsync(uri))
                {
                    try
                    {
                        response.EnsureSuccessStatusCode();
                    }
                    catch (Exception)
                    {
                        s_logger.ErrorFormat("Can't access timezone data from '{0}'", uri);
                        return null;
                    }

                    try
                    {
                        var result = await response.Content.ReadAsStringAsync();
                        using (var reader = new StringReader(result))
                        {
                            var collection = iCalendar.LoadFromStream(reader);
                            return collection;
                        }
                    }
                    catch (Exception)
                    {
                        s_logger.ErrorFormat("Can't parse timezone data from '{0}'", uri);
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                // Returning null is safe: callers treat null as "timezone unavailable" and fall back to UTC,
                // so event creation/update continues without losing data.
                s_logger.Warn($"Network error loading timezone data from '{uri}', continuing without tz file", ex);
                return null;
            }
        }
    }
}
