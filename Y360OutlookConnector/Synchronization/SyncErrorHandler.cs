using System;
using CalDavSynchronizer.DataAccess;
using Y360OutlookConnector.Clients;

namespace Y360OutlookConnector.Synchronization
{
    public static class SyncErrorHandler
    {
        public static void HandleException(Exception exception, bool silent = true)
        {
            if (IsUnauthorizedError(exception))
            {
                ThisAddIn.UiContext.Post((o) =>
                    {
                        if (!silent)
                            Ui.ErrorWindow.ShowError(Ui.ErrorWindow.ErrorType.Unauthorized);
                        ThisAddIn.Components?.LoginController.Logout();
                    },
                    null);
            }
            else if (IsProxyConnectionException(exception))
            {
                ThisAddIn.UiContext.Post((o) =>
                    {
                        if (!silent)
                            Ui.ErrorWindow.ShowError(Ui.ErrorWindow.ErrorType.ProxyError);
                        ThisAddIn.Components?.SyncStatus.SetCriticalError(CriticalError.ProxyConnectFailure);
                    },
                    null);
            }
            else if (IsProxyAuthException(exception))
            {
                ThisAddIn.UiContext.Post((o) =>
                    {
                        if (!silent)
                            Ui.ErrorWindow.ShowError(Ui.ErrorWindow.ErrorType.ProxyError);
                        ThisAddIn.Components?.SyncStatus.SetCriticalError(CriticalError.ProxyAuthFailure);
                    },
                    null);
            }
            else if (IsNoInternetException(exception))
            {
                ThisAddIn.UiContext.Post((o) =>
                    {
                        if (!silent)
                            Ui.ErrorWindow.ShowError(Ui.ErrorWindow.ErrorType.NoInternet);
                        ThisAddIn.Components?.SyncStatus.SetCriticalError(CriticalError.NoInternet);
                    },
                    null);
            }
            else if (IsServerError(exception))
            {
                ThisAddIn.UiContext.Post((o) =>
                    {
                        if (!silent)
                            Ui.ErrorWindow.ShowError(Ui.ErrorWindow.ErrorType.ServerError);
                        ThisAddIn.Components?.SyncStatus.SetCriticalError(CriticalError.ServerError);
                    },
                    null);
            }

            ExceptionHandler.Instance.Unexpected(exception);
        }

        public static bool IsUnauthorizedError(Exception exception)
        {
            for (var x = exception; x != null; x = x.InnerException)
            {
                if (x is WebDavClientException webDavException
                    && webDavException.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return true;
            }

            return false;
        }

        public static bool IsProxyConnectionException(Exception exception)
        {
            for (var x = exception; x != null; x = x.InnerException)
            {
                if (x is ProxyConnectionException)
                    return true;
            }

            return false;
        }

        public static bool IsProxyAuthException(Exception exception)
        {
            for (var x = exception; x != null; x = x.InnerException)
            {
                if (x is ProxyAuthException)
                    return true;
            }

            return false;
        }

        public static bool IsNoInternetException(Exception exception)
        {
            for (var x = exception; x != null; x = x.InnerException)
            {
                if (x is NoInternetException)
                    return true;
            }

            return false;
        }

        public static bool IsServerError(Exception exception)
        {
            for (var x = exception; x != null; x = x.InnerException)
            {
                if (x is WebDavClientException)
                    return true;
            }

            return false;
        }
    }
}
