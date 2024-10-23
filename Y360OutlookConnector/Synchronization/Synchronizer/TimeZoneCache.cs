using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using DDay.iCal;
using CalDavSynchronizer.Implementation.TimeZones;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    public class TimeZoneCache : ITimeZoneCache
    {
        private readonly bool _includeHistoricalData;
        private readonly HttpClient _httpClient;
        private readonly GlobalTimeZoneCache _globalTimeZoneCache;

        public TimeZoneCache(HttpClient httpClient, bool includeHistoricalData, GlobalTimeZoneCache globalTimeZoneCache)
        {
            _httpClient = httpClient;
            _includeHistoricalData = includeHistoricalData;
            _globalTimeZoneCache = globalTimeZoneCache;
        }

        public async Task<ITimeZone> GetByTzIdOrNull(string tzId)
        {
            return await _globalTimeZoneCache.GetTimeZoneById(tzId, _includeHistoricalData, _httpClient);
        }
    }
}
