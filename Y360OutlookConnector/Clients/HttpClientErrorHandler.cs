using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace Y360OutlookConnector.Clients
{
    class ProxyConnectionException : Exception
    {
        public ProxyConnectionException()
            : base("Failed to connect to the proxy server")
        {
        }
    };

    class ProxyAuthException : Exception
    {
        public ProxyAuthException()
            : base("Proxy authentication error")
        {
        }
    };

    class NoInternetException : Exception
    {
        public NoInternetException()
            : base("Unable to establish a connection with the server")
        {
        }
    };


    class HttpClientErrorHandler : HttpClientHandler
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri;
            var task = base.SendAsync(request, cancellationToken);
            return task.ContinueWith(x => HandleAndRethrow(x, uri), cancellationToken);
        }

        private HttpResponseMessage HandleAndRethrow(Task<HttpResponseMessage> task, Uri uri)
        {
            try
            {
                return task.Result;
            }
            catch (AggregateException exc)
            {
                var proxyUrl = Proxy?.GetProxy(uri);
                bool isProxyUsed = proxyUrl != null && !String.Equals(proxyUrl.Authority, uri.Authority);

                Exception flatten = exc.Flatten();
                for (var x = flatten; x != null; x = x.InnerException)
                {
                    if (isProxyUsed)
                    {
                        if (x is System.Net.WebException webException)
                        {
                            if (webException.Response != null 
                                && webException.Response is System.Net.HttpWebResponse httpResponse)
                            {
                                if (httpResponse.StatusCode == System.Net.HttpStatusCode.ProxyAuthenticationRequired)
                                {
                                    s_logger.Error("Proxy auth error", x);
                                    throw new ProxyAuthException();
                                }

                                var targetUrl = httpResponse.ResponseUri;
                                if (String.Equals(targetUrl.Authority, proxyUrl.Authority, 
                                    StringComparison.OrdinalIgnoreCase))
                                {
                                    s_logger.Error("Proxy connect error", x);
                                    throw new ProxyConnectionException();
                                }
                            }

                            if (webException.Status == System.Net.WebExceptionStatus.NameResolutionFailure
                                || webException.Status == System.Net.WebExceptionStatus.ProxyNameResolutionFailure)
                            {
                                s_logger.Error("Proxy connect error", x);
                                throw new ProxyConnectionException();
                            }
                        }

                        if (x is SocketException)
                        {
                            s_logger.Error("Proxy connect error", x);
                            throw new ProxyConnectionException();
                        }
                    }
                    else
                    {
                        if (x is System.Net.WebException webException
                            && webException.Status == System.Net.WebExceptionStatus.NameResolutionFailure)
                        {
                            s_logger.Error("Server connection error", x);
                            throw new NoInternetException();
                        }


                        if (x is SocketException)
                        {
                            s_logger.Error("Server connection error", x);
                            throw new NoInternetException();
                        }
                    }
                }
                throw;
            }
        }
    }
}
