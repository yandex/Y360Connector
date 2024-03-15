using System.Net.Http;
using System.Threading;
using CalDavSynchronizer.DataAccess;

namespace Y360OutlookConnector.Clients
{
    public interface IHttpClientFactory
    {
        IWebDavClient CreateWebDavClient(CancellationTokenSource cancelTokenSource);
        HttpClient CreateHttpClient();
    }
}
