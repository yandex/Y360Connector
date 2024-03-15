using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace Y360OutlookConnector.Clients
{
    class HttpClientLoggingHandler : MessageProcessingHandler 
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public HttpClientLoggingHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        protected override HttpRequestMessage ProcessRequest(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var logMessage = new StringBuilder();
            logMessage.Append($"Request {request.Method.ToString().ToUpper()} {request.RequestUri}");
            if (request.Content != null)
            {
                logMessage.AppendLine();

                var stream = request.Content.ReadAsStreamAsync().Result;
                var reader = new StreamReader(stream);
                var body = reader.ReadToEnd();
                body = body.TrimEnd('\r', '\n', ' ');
                logMessage.Append(body);
                stream.Seek(0, SeekOrigin.Begin);
            }
            s_logger.Debug(logMessage.ToString());

            return request;
        }

        protected override HttpResponseMessage ProcessResponse(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.Content != null)
            {
                response.Content.LoadIntoBufferAsync().Wait();
                var request = response.RequestMessage;

                var logMessage = new StringBuilder();
                logMessage.Append($"Response for {request.Method.ToString().ToUpper()} {request.RequestUri}");
                logMessage.AppendLine();
                var stream = response.Content.ReadAsStreamAsync().Result;
                var reader = new StreamReader(stream);
                var body = reader.ReadToEnd();
                body = body.TrimEnd('\r', '\n', ' ');
                logMessage.Append(body);
                stream.Seek(0, SeekOrigin.Begin);

                s_logger.Debug(logMessage.ToString());
            }

            return response;
        }
    }
}
