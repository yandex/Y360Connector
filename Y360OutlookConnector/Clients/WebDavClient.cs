using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CalDavSynchronizer.DataAccess;
using log4net;

namespace Y360OutlookConnector.Clients
{
    public class WebDavClient : WebDavClientBase, IWebDavClient
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private readonly Func<Task<HttpClient>> _httpClientProvider;
        private readonly CancellationToken _cancelToken;

        public WebDavClient(
            Func<Task<HttpClient>> httpClientProvider,
            bool acceptInvalidChars,
            CancellationTokenSource cancelTokenSource)
            : base(acceptInvalidChars)
        {
            _httpClientProvider = httpClientProvider ?? throw new ArgumentNullException(nameof(httpClientProvider));
            if (cancelTokenSource == null) throw new ArgumentNullException(nameof(cancelTokenSource));
            _cancelToken = cancelTokenSource.Token;
        }

        public async Task<XmlDocumentWithNamespaceManager> ExecuteWebDavRequestAndReadResponse(
            Uri url,
            string httpMethod,
            int? depth,
            string ifMatch,
            string ifNoneMatch,
            string mediaType,
            string requestBody)
        {
            try
            {
                var response = await ExecuteWebDavRequest(url, httpMethod, depth, ifMatch, ifNoneMatch, mediaType, requestBody);
                using (response.Item2)
                {
                    using (var responseStream = await response.Item2.Content.ReadAsStreamAsync())
                    {
                        return CreateXmlDocument(responseStream, response.Item3);
                    }
                }
            }
            catch (HttpRequestException x)
            {
                throw MapToWebDavClientException(x);
            }
        }

        public async Task<IHttpHeaders> ExecuteWebDavRequestAndReturnResponseHeaders(
            Uri url,
            string httpMethod,
            int? depth,
            string ifMatch,
            string ifNoneMatch,
            string mediaType,
            string requestBody)
        {
            try
            {
                var result = await ExecuteWebDavRequest(url, httpMethod, depth, ifMatch, ifNoneMatch, mediaType, requestBody);
                using (var response = result.Item2)
                {
                    return TinyCalDavSynchronizer.Factories.CreateHttpResponseHeadersAdapter(result.Item1, response.Headers);
                }
            }
            catch (HttpRequestException x)
            {
                throw MapToWebDavClientException(x);
            }
        }

        private async Task<Tuple<HttpResponseHeaders, HttpResponseMessage, Uri>> ExecuteWebDavRequest(
            Uri url,
            string httpMethod,
            int? depth,
            string ifMatch,
            string ifNoneMatch,
            string mediaType,
            string requestBody,
            HttpResponseHeaders headersFromFirstCall = null)
        {
            _cancelToken.ThrowIfCancellationRequested();

            HttpResponseMessage response;

            using (var requestMessage = new HttpRequestMessage())
            {
                requestMessage.RequestUri = RebuildUri(url);
                requestMessage.Method = new HttpMethod(httpMethod);

                if (depth != null)
                    requestMessage.Headers.Add("Depth", depth.ToString());

                if (!String.IsNullOrEmpty(ifMatch))
                    requestMessage.Headers.Add("If-Match", ifMatch); 

                if (!String.IsNullOrEmpty(ifNoneMatch))
                    requestMessage.Headers.Add("If-None-Match", ifNoneMatch);

                if (!String.IsNullOrEmpty(requestBody))
                    requestMessage.Content = new StringContent(requestBody, Encoding.UTF8, mediaType);

                var httpClient = await _httpClientProvider();
                response = await httpClient.SendAsync(requestMessage, _cancelToken);
            }

            try
            {
                if (response.StatusCode == HttpStatusCode.Moved 
                    || response.StatusCode == HttpStatusCode.Redirect 
                    || response.StatusCode == HttpStatusCode.TemporaryRedirect 
                    || response.StatusCode == HttpStatusCode.SeeOther)
                {
                    if (response.Headers.Location != null)
                    {
                        var location = response.Headers.Location;
                        response.Dispose();
                        var effectiveLocation = location.IsAbsoluteUri ? location : new Uri(url, location);
                        return await ExecuteWebDavRequest(effectiveLocation, httpMethod, depth, ifMatch, ifNoneMatch, mediaType, requestBody, headersFromFirstCall ?? response.Headers);
                    }

                    s_logger.Warn("Ignoring Redirection without Location header.");
                }

                await EnsureSuccessStatusCode(response);

                return Tuple.Create(headersFromFirstCall ?? response.Headers, response, url);
            }
            catch (Exception)
            {
                response?.Dispose();
                throw;
            }
        }

        private static async Task EnsureSuccessStatusCode(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string responseMessage = null;

                try
                {
                    using (var responseStream = await response.Content.ReadAsStreamAsync())
                    {
                        using (var reader = new StreamReader(responseStream, Encoding.UTF8))
                        {
                            responseMessage = await reader.ReadToEndAsync();
                        }
                    }
                }
                catch (Exception x)
                {
                    s_logger.Error("Exception while trying to read the error message.", x);
                }

                throw new WebDavClientException(response.StatusCode, 
                    response.ReasonPhrase ?? response.StatusCode.ToString(), responseMessage, null);
            }
        }

        internal static Exception MapToWebDavClientException(HttpRequestException x)
        {
            var match = Regex.Match(x.Message, @"'(?<code>\d{3})'\s+\('(?<description>.*?)'\)");
            if (!match.Success)
            {
                return new WebDavClientException(x, null, null, null);
            }

            var httpStatusCode = (HttpStatusCode)Int32.Parse(match.Groups["code"].Value);
            return new WebDavClientException(
                x,
                httpStatusCode,
                match.Groups["description"].Value,
                null);
        }

        // The Uri class does not allow the use of some escaped values in the url path.
        // The following method allows to fix this problem
        // (although it is an ugly hack that depends on the version of the dotnet)
        // See also: https://stackoverflow.com/a/784937
        internal static Uri RebuildUri(Uri srcUrl)
        {
            try
            {
                var schemeAndHost = srcUrl.GetLeftPart(UriPartial.Authority);
                var originalString = srcUrl.OriginalString;
                if (!originalString.StartsWith(schemeAndHost) || originalString.Length <= schemeAndHost.Length + 1)
                    return srcUrl;

                var resultUrl = new UriBuilder(srcUrl) { Path = originalString.Substring(schemeAndHost.Length) }.Uri;
                var flagsFieldInfo = typeof(Uri).GetField("m_Flags", BindingFlags.Instance | BindingFlags.NonPublic);
                if (flagsFieldInfo == null) return srcUrl;

                ulong flags = (ulong) flagsFieldInfo.GetValue(resultUrl);
                flags &= ~((ulong) 0x30);

                flagsFieldInfo.SetValue(resultUrl, flags);

                return resultUrl;
            }
            catch (Exception exc)
            {
                s_logger.Warn("ForceCanonicalPathAndQuery error:", exc);
                return srcUrl;
            }
        }
    }
}
