using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using CalDavSynchronizer.Contracts;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Utilities;
using log4net;
using log4net.Repository.Hierarchy;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Clients
{
    public class HttpClientFactory : IHttpClientFactory
    {
        private const string UserAgentName = "Y360OutlookConnector";

        private readonly ProxyOptionsProvider _proxyOptionsProvider;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private HttpClient _authorizedHttpClient;
        private bool _wasDebugEnabledForAuthHttpClient;
        private SecureString _oauthToken;

        public HttpClientFactory(ProxyOptionsProvider proxyOptionsProvider)
        {
            _proxyOptionsProvider = proxyOptionsProvider;
            _proxyOptionsProvider.ProxyOptionsChanged += ProxyOptionsChanged;
            ((Hierarchy)LogManager.GetRepository()).ConfigurationChanged += HttpClientFactory_LoggerConfigurationChanged;
        }

        private void HttpClientFactory_LoggerConfigurationChanged(object sender, EventArgs e)
        {
            if (_wasDebugEnabledForAuthHttpClient != s_logger.IsDebugEnabled)
            {
                // Поменялся уровень логгирования. Необходимо пересоздать auth http client, так как при его создании указывается обработчик
                // или без логгирования или с логгированием
                _authorizedHttpClient = null;
            }
        }

        private void ProxyOptionsChanged(object sender, EventArgs e)
        {
            _authorizedHttpClient = null;
        }

        public void SetAccessToken(SecureString accessToken)
        {
            _oauthToken = accessToken;
            _authorizedHttpClient = null;
        }

        public HttpClient CreateHttpClient()
        {
            var proxy = CreateProxy(_proxyOptionsProvider.GetProxyOptions());
            var httpClientHandler = new HttpClientErrorHandler
            {
                Proxy = proxy,
                UseProxy = proxy != null
            };

            var httpClient = new HttpClient(httpClientHandler);
            httpClient.DefaultRequestHeaders.UserAgent.Add(GetProductInfoHeaderValue());

            return httpClient;
        }

        public IWebDavClient CreateWebDavClient(CancellationTokenSource cancelTokenSource)
        {
            return new WebDavClient(CreateAuthorizedHttpClient, true, cancelTokenSource);
        }

        public Task<HttpClient> CreateAuthorizedHttpClient()
        {
            if (_oauthToken == null)
                throw new ApplicationException("No access token");

            if (_authorizedHttpClient != null)
                return Task.FromResult(_authorizedHttpClient);

            var proxy = CreateProxy(_proxyOptionsProvider.GetProxyOptions());
            var httpClientHandler = new HttpClientErrorHandler
            {
                AllowAutoRedirect = false,
                PreAuthenticate = false,
                Proxy = proxy,
                UseProxy = proxy != null
            };

            _wasDebugEnabledForAuthHttpClient = s_logger.IsDebugEnabled;
            var httpClient = _wasDebugEnabledForAuthHttpClient
                ? new HttpClient(new HttpClientLoggingHandler(httpClientHandler))
                : new HttpClient(httpClientHandler);

            httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("OAuth", SecureStringUtility.ToUnsecureString(_oauthToken));

            httpClient.DefaultRequestHeaders.UserAgent.Add(GetProductInfoHeaderValue());

            httpClient.Timeout = TimeSpan.FromSeconds(90);

            _authorizedHttpClient = httpClient;

            return Task.FromResult(httpClient);
        }

        private static ProductInfoHeaderValue GetProductInfoHeaderValue()
        {
            return new ProductInfoHeaderValue(UserAgentName, GetProductVersion());
        }

        private static string GetProductVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return $"{version.Major}.{version.Minor}";
        }

        public static IWebProxy CreateProxy(ProxyOptions proxyOptions)
        {
            IWebProxy proxy = null;
            if (proxyOptions == null || proxyOptions.ProxyUseDefault)
            {
                proxy = WebRequest.DefaultWebProxy;
                proxy.Credentials = CredentialCache.DefaultCredentials;
            }
            else if (proxyOptions.ProxyUseManual && !String.IsNullOrEmpty(proxyOptions.ProxyUrl))
            {
                proxy = new WebProxy(proxyOptions.ProxyUrl, false);
                proxy.Credentials = !String.IsNullOrEmpty(proxyOptions.ProxyUserName) 
                    ? new NetworkCredential(proxyOptions.ProxyUserName, proxyOptions.ProxyPassword) 
                    : CredentialCache.DefaultCredentials;
            }

            return proxy;
        }
    }
}
