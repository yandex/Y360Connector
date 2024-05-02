using System;

namespace Y360OutlookConnector.Clients.Telemost.Model
{
    public class ApiCallResult<T> where T : class
    {
        private ApiCallResult() 
        {
        }

        public static ApiCallResult<T> FromData(T data, string requestId, string yandexRequestId)
        {
            return new ApiCallResult<T> { Data = data, RequestId = requestId, YandexRequestId = yandexRequestId };
        }

        public static ApiCallResult<T> FromError(Error error, string requestId, string yandexRequestId)
        {
            return new ApiCallResult<T> { Error = error, RequestId = requestId, YandexRequestId = yandexRequestId };
        }

        public static ApiCallResult<T> FromException(Exception exception, string requestId)
        {
            return new ApiCallResult<T> { Exception = exception, RequestId = requestId };
        }

        public string YandexRequestId { get; private set; }
        public string RequestId { get; private set; }
        public T Data { get; private set; }
        public Error Error { get; private set; }
        public Exception Exception { get; private set; }
    }
}
