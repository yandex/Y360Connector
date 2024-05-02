using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Y360OutlookConnector.Clients.Telemost.Model;

namespace Y360OutlookConnector.Clients.Telemost
{
    internal static class HttpClientExtensions
    {
        private const string XRequestIdHeaderName = "X-Request-ID";
        private const string YandexCloudRequestIdHeaderName = "Yandex-Cloud-Request-ID";

        public static async Task<ApiCallResult<ConferenceShort>> CreateTelemostMeetingAsync(this HttpClient client, bool isInternal)
        {
            var url = $"https://cloud-api.yandex.net/v1/telemost-api/conferences";

            var jsonObject = new ConferenceData { AccessLevel = isInternal ? ConferenceData.AccessLevelEnum.ORGANIZATION : ConferenceData.AccessLevelEnum.PUBLIC };

            var requestId = Guid.NewGuid().ToString();

            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(jsonObject), Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(new HttpMethod("POST"), url) { Content = content };

                request.Headers.Add(XRequestIdHeaderName, requestId);

                var result = await client.SendAsync(request);

                var responseContent = await result.Content.ReadAsStringAsync();
                var yandexRequestId = result.GetYandexRequestId();

                if (result.IsSuccessStatusCode)
                {
                    return ApiCallResult<ConferenceShort>.FromData(JsonConvert.DeserializeObject<ConferenceShort>(responseContent), 
                        requestId, 
                        yandexRequestId);
                }
                
                return ApiCallResult<ConferenceShort>.FromError(JsonConvert.DeserializeObject<Error>(responseContent), 
                    requestId, 
                    yandexRequestId);                
            }
            catch (Exception ex)
            {
                return ApiCallResult<ConferenceShort>.FromException(ex, requestId);
            }            
        }

        private static readonly Regex ValidId = new Regex(@"^\d+$", RegexOptions.Compiled);

        private static void VerifyMeetingId(string id)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (!ValidId.IsMatch(id))
            {
                throw new ArgumentException($"Invalid id {id}", nameof(id));
            }
        }

        public static async Task<ApiCallResult<Conference>> GetTelemostMeetingByIdAsync(this HttpClient client, string id)
        {
            var requestId = Guid.NewGuid().ToString();

            try
            {
                VerifyMeetingId(id);

                var url = $"https://cloud-api.yandex.net/v1/telemost-api/conferences/{id}";

                var request = new HttpRequestMessage(new HttpMethod("GET"), url);

                request.Headers.Add(XRequestIdHeaderName, requestId);

                var result = await client.SendAsync(request);

                var responseContent = await result.Content.ReadAsStringAsync();
                var yandexRequestId = result.GetYandexRequestId();

                if (result.IsSuccessStatusCode)
                {
                    return ApiCallResult<Conference>.FromData(JsonConvert.DeserializeObject<Conference>(responseContent), 
                        requestId,
                        yandexRequestId);
                }

                return ApiCallResult<Conference>.FromError(JsonConvert.DeserializeObject<Error>(responseContent), 
                    requestId, 
                    yandexRequestId);
            }
            catch (Exception ex)
            {
                return ApiCallResult<Conference>.FromException(ex, requestId);
            }
        }

        private static string GetYandexRequestId(this HttpResponseMessage result)
        {
            if (!result.Headers.TryGetValues(YandexCloudRequestIdHeaderName, out var values))
            {
                return null;
            }

            return values.FirstOrDefault();
        }

        public static async Task<ApiCallResult<ConferenceShort>> UpdateTelemostMeetingAsync(this HttpClient client, string id, bool isInternal)
        {
            var requestId = Guid.NewGuid().ToString();

            try
            {
                VerifyMeetingId(id);

                var url = $"https://cloud-api.yandex.net/v1/telemost-api/conferences/{id}";

                var jsonObject = new ConferenceData { AccessLevel = isInternal ? ConferenceData.AccessLevelEnum.ORGANIZATION : ConferenceData.AccessLevelEnum.PUBLIC };


                var content = new StringContent(JsonConvert.SerializeObject(jsonObject), Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };

                request.Headers.Add(XRequestIdHeaderName, requestId);

                var result = await client.SendAsync(request);

                var responseContent = await result.Content.ReadAsStringAsync();
                var yandexRequestId = result.GetYandexRequestId();

                if (result.IsSuccessStatusCode)
                {
                    return ApiCallResult<ConferenceShort>.FromData(JsonConvert.DeserializeObject<ConferenceShort>(responseContent), 
                        requestId,
                        yandexRequestId);
                }

                return ApiCallResult<ConferenceShort>.FromError(JsonConvert.DeserializeObject<Error>(responseContent), 
                    requestId,
                    yandexRequestId);
            }
            catch (Exception ex)
            {
                return ApiCallResult<ConferenceShort>.FromException(ex, requestId);
            }
        }
        
    }
}
