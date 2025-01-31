﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Ui.Login
{
    public class LoginInfo
    {
        [JsonProperty("default_email")]
        public string DefaultEmail { get; set; }

        [JsonProperty("login")]
        public string UserName { get; set; }

        [JsonProperty("real_name")]
        public string RealName { get; set; }

        [JsonProperty("is_avatar_empty")]
        public bool IsAvatarEmpty { get; set; }

        [JsonProperty("default_avatar_id")]
        public string DefaultAvatarId { get; set; }

        [JsonProperty("id")]
        public string UserId { get; set; }
    }


    public class LogonSession
    {
        public const string ClientId = "4e20b574e4974457904d9daef7bc41b6";
        public const string OriginAppId = "outlook_y360_sync";

        private readonly HttpClient _httpClient;
        private readonly string  _codeVerifier;
        private readonly string  _tld;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public LogonSession(HttpClient httpClient)
        {
            _codeVerifier = GenerateRandomString(64);
            _httpClient = httpClient;
            _tld = GetTopLevelDomain(Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName);
        }

        private static Uri CreateUri(string uri, Dictionary<string,string> parameters)
        {
            var urlBuilder = new UriBuilder(uri);

            var extraQueryString = string.Join("&", parameters.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));

            urlBuilder.Query = extraQueryString;

            return urlBuilder.Uri;
        }

        public Uri GetOAuthUrl()
        {
            var codeChallenge = CalcCodeChallenge(_codeVerifier);

            var extraParameters = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["origin"] = $"{OriginAppId}",
                ["client_id"] = $"{ClientId}",
                ["code_challenge"] = $"{codeChallenge}",
                ["code_challenge_method"] = "S256"
            };

            if (AppConfig.IsStrongCodeConfirmationUsed)
            {
                extraParameters["use_strong_code"] = "1";
            }

            return CreateUri($"https://oauth.yandex.{_tld}/authorize", extraParameters);
        }

        public Uri GetPassportUrl()
        {
            var oauthUrl = GetOAuthUrl();
           
            var extraParameters = new Dictionary<string, string>
            {
                ["origin"] = $"{OriginAppId}",
                ["retpath"] = $"{oauthUrl}"
            };

            return CreateUri($"https://passport.yandex.{_tld}/auth", extraParameters);
        }

        public async Task<string> RequestTokenAsync(string code)
        {
            var tld = GetTopLevelDomain(Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName);
            var url = $"https://oauth.yandex.{tld}/token";

            var content = new Dictionary<string, string>()
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "origin", OriginAppId },
                { "client_id", ClientId },
                { "code_verifier", _codeVerifier },
                { "device_name", Environment.MachineName }
            };

            ThisAddIn.RestoreUiContext();
            var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(content));

            if (!response.IsSuccessStatusCode)
            {
                string result = await response.Content.ReadAsStringAsync();
                s_logger.Error($"Token request failed. Response: {result}");
            }
            else
            {
                string result = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(result);
                if (json.TryGetValue("access_token", out JToken jsonValue))
                {
                    return (string)jsonValue;
                }
            }

            return "";
        }

        public async Task<LoginInfo> QueryLoginInfoAsync(string accessToken)
        {
            const string url = "https://login.yandex.ru/info?format=json";
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, url))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);

                ThisAddIn.RestoreUiContext();
                var response = await _httpClient.SendAsync(httpRequest);
                if (!response.IsSuccessStatusCode)
                {
                    s_logger.Error($"User info query failed. Response: {response}");
                    return null;
                }

                ThisAddIn.RestoreUiContext();
                string responseContent = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<LoginInfo>(responseContent);
            }
        }

        private static string GenerateRandomString(int length)
        {
            // ReSharper disable once StringLiteralTypo
            char[] charSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

            var data = new byte[4 * length];
            using (var randomGenerator = RandomNumberGenerator.Create())
            {
                randomGenerator.GetBytes(data);
            }
            var result = new StringBuilder(length);
            for (var i = 0; i < length; i++)
            {
                var dword = BitConverter.ToUInt32(data, i * 4);
                long index = dword % charSet.Length;

                result.Append(charSet[index]);
            }

            return result.ToString();
        }

        private static string CalcCodeChallenge(string codeVerifier)
        {
            using (var sha256 = new SHA256Managed())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
                string result = Convert.ToBase64String(bytes, Base64FormattingOptions.None);

                // Replace everything that can break URL
                result = result.Replace("+", "-");
                result = result.Replace("/", "_");
                result = result.Replace("=", "");
                return result;
            }
        }

        public static string GetTopLevelDomain(string lang)
        {
            switch (lang)
            {
                case "ru":
                    return "ru";
                case "tr":
                    return "com.tr";
                default:
                    return "com";
            }
        }
    }
}
