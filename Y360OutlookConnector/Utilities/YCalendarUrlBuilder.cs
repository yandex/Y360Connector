using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Utilities
{
    public static class YCalendarUrlBuilder
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public const string EventTypeUser = "user";

        public static string BuildCreateEventUrl(
            DateTime start,
            DateTime end,
            string userId = null,
            string title = null,
            string description = null,
            bool isAllDay = false,
            string location = null,
            IEnumerable<string> attendees = null,
            IEnumerable<string> resources = null,
            string eventType = null)
        {
            var parameters = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(userId))
            {
                parameters["uid"] = userId;
            }

            parameters["start"] = ToUnixMilliseconds(start).ToString();

            var endForUrl = end;
            if (isAllDay && end.Date > start.Date)
            {
                endForUrl = end.AddDays(-1);
            }

            parameters["end"] = ToUnixMilliseconds(endForUrl).ToString();

            if (!string.IsNullOrWhiteSpace(title))
            {
                parameters["name"] = title;
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                parameters["description"] = description;
            }

            if (isAllDay)
            {
                parameters["isAllDay"] = "1";
            }

            if (!string.IsNullOrWhiteSpace(location))
            {
                parameters["location"] = location.Trim();
            }

            var attendeesJoined = CommaJoinDistinct(attendees);
            if (!string.IsNullOrEmpty(attendeesJoined))
            {
                parameters["attendees"] = attendeesJoined;
            }

            var resourcesJoined = CommaJoinDistinct(resources);
            if (!string.IsNullOrEmpty(resourcesJoined))
            {
                parameters["resources"] = resourcesJoined;
            }

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                parameters["eventType"] = eventType.Trim();
            }

            var query = string.Join("&", parameters.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));

            return $"{EndpointConfig.CalendarCreateEventBaseUrl}?{query}";
        }

        private static long ToUnixMilliseconds(DateTime dt)
        {
            return (long)(dt.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
        }

        private static string CommaJoinDistinct(IEnumerable<string> items)
        {
            if (items == null)
            {
                return null;
            }

            var list = items
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return list.Count == 0 ? null : string.Join(",", list);
        }
    }
}
