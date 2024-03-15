using CalDavSynchronizer.DataAccess;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using CalDavSynchronizer.Contracts;
using System.Net.Http.Headers;
using CalDavSynchronizer.Utilities;

namespace Y360OutlookConnector.Synchronization
{
    public class HttpClientFactory : TinyCalDavSynchronizer.IHttpClientFactory
    {
        private readonly SecureString _oauthToken;
        private readonly ProxyOptions _proxyOptions;

        public HttpClientFactory(SecureString oauthToken, ProxyOptions proxyOptions)
        {
            _oauthToken = oauthToken;
            _proxyOptions = proxyOptions;
        }

        public HttpClient CreateHttpClient()
        {
            var proxy = CreateProxy(_proxyOptions);
            var httpClientHandler = new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = proxy != null
            };
            return new HttpClient(httpClientHandler);
        }

        public IWebDavClient CreateWebDavClient()
        {
            return new CalDavSynchronizer.DataAccess.HttpClientBasedClient.WebDavClient(
                CreateHttpClientWithOAuthToken,
                GetProductName(),
                GetProductVersion(),
                false,
                true,
                false);
        }

        private Task<HttpClient> CreateHttpClientWithOAuthToken()
        {
            IWebProxy proxy = CreateProxy(_proxyOptions);

            var httpClientHandler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                PreAuthenticate = false,
                Proxy = proxy,
                UseProxy = (proxy != null)
            };

            var httpClient = new HttpClient(httpClientHandler);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", SecureStringUtility.ToUnsecureString(_oauthToken));

            httpClient.Timeout = TimeSpan.FromSeconds(90);
            return Task.FromResult(httpClient);
        }

        private static string GetProductName()
        {
            return "Y360OutlookConnector";
        }

        private static string GetProductVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return $"{version.Major}.{version.Minor}";
        }

        public static IWebProxy CreateProxy(ProxyOptions proxyOptions)
        {
            if (proxyOptions == null) return null;

            IWebProxy proxy = null;
            if (proxyOptions.ProxyUseDefault)
            {
                proxy = WebRequest.DefaultWebProxy;
                proxy.Credentials = CredentialCache.DefaultCredentials;
            }
            else if (proxyOptions.ProxyUseManual)
            {
                proxy = new WebProxy(proxyOptions.ProxyUrl, false);
                if (!string.IsNullOrEmpty(proxyOptions.ProxyUserName))
                {
                    proxy.Credentials = new NetworkCredential(proxyOptions.ProxyUserName, proxyOptions.ProxyPassword);
                }
                else
                {
                    proxy.Credentials = CredentialCache.DefaultCredentials;
                }
            }

            return proxy;
        }
    }
}
