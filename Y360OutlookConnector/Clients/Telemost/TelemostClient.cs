using System.Threading.Tasks;
using Y360OutlookConnector.Clients.Telemost.Model;

namespace Y360OutlookConnector.Clients.Telemost
{
    public class TelemostClient
    {
        private readonly HttpClientFactory _httpClientFactory;

        public TelemostClient(HttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<ApiCallResult<ConferenceShort>> CreateTelemostMeetingAsync(bool isInternal)
        {
            var client = await _httpClientFactory.CreateAuthorizedHttpClient();

            return await client.CreateTelemostMeetingAsync(isInternal);
        }

        public async Task<ApiCallResult<ConferenceShort>> UpdateTelemostMeetingAsync(string id, bool isInternal)
        {
            var client = await _httpClientFactory.CreateAuthorizedHttpClient();

            return await client.UpdateTelemostMeetingAsync(id, isInternal);
        }

        public async Task<ApiCallResult<Conference>> GetTelemostMeetingByIdAsync(string id)
        {
            var client = await _httpClientFactory.CreateAuthorizedHttpClient();

            return await client.GetTelemostMeetingByIdAsync(id);
        }
    }
}
