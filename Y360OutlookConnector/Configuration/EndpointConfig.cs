using System;

namespace Y360OutlookConnector.Configuration
{
    /// <summary>
    /// Единая точка доступа к URL и OAuth-константам Коннектора (пока значения по умолчанию).
    /// </summary>
    public static class EndpointConfig
    {
        public const string DefaultOAuthClientId = "4e20b574e4974457904d9daef7bc41b6";
        public const string DefaultOAuthOriginAppId = "outlook_y360_sync";

        private const string DefaultLoginInfoUrl = "https://login.yandex.ru/info?format=json";
        private const string DefaultCalDavBaseUrl = "https://caldav.yandex.ru/";
        private const string DefaultCardDavBaseUrl = "https://carddav.yandex.ru/";
        private const string DefaultCloudApiBaseUrl = "https://cloud-api.yandex.net";
        private const string DefaultCalendarApiBaseUrl = "https://cloud-api.yandex.ru";
        private const string DefaultCalendarWebBaseUrl = "https://calendar.yandex.ru";
        private const string DefaultAvatarsBaseUrl = "https://avatars.yandex.net";
        private const string DefaultHelpUrl = "https://yandex.ru/support/calendar-business/plug-in";
        private const string DefaultHelpUrlBusiness = "https://yandex.ru/support/yandex-360/business/calendar/ru/plug-in#inst";
        private const string DefaultHelpUrlCustomers = "https://yandex.ru/support/yandex-360/customers/calendar/web/ru/plug-in#inst";
        private const string DefaultTimeZoneOutlookBaseUrl = "https://www.tzurl.org/zoneinfo-outlook/";
        private const string DefaultTimeZoneHistoricalBaseUrl = "https://www.tzurl.org/zoneinfo/";

        public static string OAuthClientId
        {
            get { return DefaultOAuthClientId; }
        }

        public static string OAuthOriginAppId
        {
            get { return DefaultOAuthOriginAppId; }
        }

        public static string LoginInfoUrl
        {
            get { return DefaultLoginInfoUrl; }
        }

        public static string CalDavBaseUrl
        {
            get { return DefaultCalDavBaseUrl; }
        }

        public static string CardDavBaseUrl
        {
            get { return DefaultCardDavBaseUrl; }
        }

        public static string CalendarWebBaseUrl
        {
            get { return DefaultCalendarWebBaseUrl; }
        }

        public static string HelpUrl
        {
            get { return DefaultHelpUrl; }
        }

        public static string HelpUrlBusiness
        {
            get { return DefaultHelpUrlBusiness; }
        }

        public static string HelpUrlCustomers
        {
            get { return DefaultHelpUrlCustomers; }
        }

        public static string CalendarCreateEventBaseUrl
        {
            get { return TrimTrailingSlash(DefaultCalendarWebBaseUrl) + "/event/new"; }
        }

        public static string GetOAuthAuthorizeUrl(string tld)
        {
            return "https://oauth.yandex." + tld + "/authorize";
        }

        public static string GetOAuthTokenUrl(string tld)
        {
            return "https://oauth.yandex." + tld + "/token";
        }

        public static string GetPassportAuthUrl(string tld)
        {
            return "https://passport.yandex." + tld + "/auth";
        }

        public static string GetCalendarUserInfoUrl()
        {
            return TrimTrailingSlash(DefaultCalendarApiBaseUrl) + "/v1/calendar/user-info";
        }

        public static string GetTelemostConferencesUrl()
        {
            return TrimTrailingSlash(DefaultCloudApiBaseUrl) + "/v1/telemost-api/conferences";
        }

        public static string GetTelemostConferenceUrl(string id)
        {
            return TrimTrailingSlash(DefaultCloudApiBaseUrl) + "/v1/telemost-api/conferences/" + id;
        }

        public static string GetAutoUpdateInstallerUrl()
        {
            return TrimTrailingSlash(DefaultCloudApiBaseUrl) + "/v1/calendar/outlook-extensions/win86/installer";
        }

        public static string GetAvatarUrl(string avatarId)
        {
            return TrimTrailingSlash(DefaultAvatarsBaseUrl) + "/get-yapic/" + avatarId + "/islands-75";
        }

        public static string GetTimeZoneDefinitionUrl(string timeZoneId, bool includeHistoricalData)
        {
            var baseUrl = includeHistoricalData ? DefaultTimeZoneHistoricalBaseUrl : DefaultTimeZoneOutlookBaseUrl;
            return TrimTrailingSlash(baseUrl) + "/" + timeZoneId + ".ics";
        }

        private static string TrimTrailingSlash(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            return url.EndsWith("/") ? url.TrimEnd('/') : url;
        }
    }
}
