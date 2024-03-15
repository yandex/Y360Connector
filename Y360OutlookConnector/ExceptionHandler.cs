using log4net;
using log4net.Core;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Y360OutlookConnector
{
    public class ExceptionHandler : GenSync.IExceptionLogger
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public static readonly ExceptionHandler Instance = new ExceptionHandler();

        private ExceptionHandler()
        {
        }

        public void Unexpected(Exception exception)
        {
            if (exception is TaskCanceledException)
                return;

            s_logger.Error("Unexpected exception:", exception);
            Telemetry.SignalError("unexpected", exception);
        }

        // GenSync.IExceptionLogger implementation

        public void LogException(Exception exception, ILog logger)
        {
            LogException(String.Empty, exception, logger);
        }

        public void LogException(string message, Exception exception, ILog logger)
        {
            if (exception is TaskCanceledException)
                return;

            logger.Logger.Log(typeof(ExceptionHandler), Level.Error, message, exception);
            Telemetry.SignalError("sync_failure", exception);
        }
    }
}
