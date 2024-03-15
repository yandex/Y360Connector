using System;
using System.Windows;
using System.Windows.Media.Imaging;
using Y360OutlookConnector.Synchronization;

namespace Y360OutlookConnector.Ui
{
    /// <summary>
    /// Interaction logic for ErrorWindow.xaml
    /// </summary>
    public partial class ErrorWindow
    {
        public enum ErrorType
        {
            None,
            ProxyError,
            Unauthorized,
            NoInternet,
            ServerError,
        }

        private static ErrorWindow s_instance;

        private ErrorType _errorType = ErrorType.None;
        private Action _buttonAction;

        public static void ShowError(CriticalError criticalError)
        {
            var errorType = ErrorType.None;
            switch (criticalError)
            {
                case CriticalError.ProxyConnectFailure:
                case CriticalError.ProxyAuthFailure:
                    errorType = ErrorType.ProxyError;
                    break;
                case CriticalError.NoInternet:
                    errorType = ErrorType.NoInternet;
                    break;
                case CriticalError.ServerError:
                    errorType = ErrorType.ServerError;
                    break;
            }
            ShowError(errorType);
        }

        public static void ShowError(ErrorType errorType)
        {
            if (errorType == ErrorType.None)
            {
                s_instance?.Close();
            }
            else if (s_instance == null)
            {
                s_instance = new ErrorWindow(errorType);
                s_instance.Closed += (o, e) => s_instance = null;
                s_instance.Show();
            }
            else
            {
                s_instance.SetErrorType(errorType);
                s_instance.Activate();
            }
        }

        public static void HideError(ErrorType errorType)
        {
            if (s_instance != null && s_instance._errorType == errorType)
            {
                s_instance.Close();
            }
        }

        private ErrorWindow(ErrorType errorType)
        {
            InitializeComponent();
            SetErrorType(errorType);
        }

        private void SetErrorType(ErrorType errorType)
        {
            if (_errorType != errorType)
            {
                _errorType = errorType;

                UpdateLogoBitmap();
                UpdateTitle();
                UpdateDescription();
                UpdateButton();

                SignalTelemetry(errorType);
            }
        }

        private void UpdateLogoBitmap()
        {
            switch (_errorType)
            {
                case ErrorType.Unauthorized:
                    LogoImage.Source = new BitmapImage(
                        new Uri("pack://application:,,,/Y360OutlookConnector;component/Resources/Ya.png"));
                    break;
                default:
                    LogoImage.Source = new BitmapImage(
                        new Uri("pack://application:,,,/Y360OutlookConnector;component/Resources/ExclamationSign.png"));
                    break;
            }
        }

        private void UpdateTitle()
        {
            string title;
            switch (_errorType)
            {
                case ErrorType.ProxyError:
                    title = Localization.Strings.Messages_ProxyErrorMessageTitle;
                    break;
                case ErrorType.NoInternet:
                    title = Localization.Strings.Messages_NoInternetMessageTitle;
                    break;
                case ErrorType.ServerError:
                    title = Localization.Strings.Messages_SyncFailureMessageTitle;
                    break;
                default:
                    title = String.Empty;
                    break;
            }
            TitleTextBox.Text = title;
            TitleTextBox.Visibility = String.IsNullOrEmpty(title) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateDescription()
        {
            string description;
            switch (_errorType)
            {
                case ErrorType.ProxyError:
                    description = Localization.Strings.Messages_ProxyErrorMessageDescription;
                    break;
                case ErrorType.NoInternet:
                    description = Localization.Strings.Messages_NoInternetMessageDesc;
                    break;
                case ErrorType.ServerError:
                    description = Localization.Strings.Messages_SyncFailureMessageDescription;
                    break;
                case ErrorType.Unauthorized:
                    description = Localization.Strings.Messages_UnauthorizedMessageDescription;
                    break;
                default:
                    description = String.Empty;
                    break;
            }
            DescriptionTextBox.Text = description;
            DescriptionTextBox.Visibility = String.IsNullOrEmpty(description)
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateButton()
        {
            switch (_errorType)
            {
                case ErrorType.ProxyError:
                    ActionButton.Visibility = Visibility.Visible;
                    ActionButton.Content = Localization.Strings.Messages_ProxyErrorMessageButton;
                    _buttonAction = ProxySettingsAction;
                    break;
                case ErrorType.Unauthorized:
                    ActionButton.Visibility = Visibility.Visible;
                    ActionButton.Content = Localization.Strings.Messages_UnauthorizedMessageButton;
                    _buttonAction = StartLoginAction;
                    break;
                default:
                    ActionButton.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void ProxySettingsAction()
        {
            Close();
            SettingsWindow.ShowOrActivate();
        }

        private void StartLoginAction()
        {
            Close();
            ThisAddIn.Components.StartLogin();
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            _buttonAction?.Invoke();
        }

        private static void SignalTelemetry(ErrorType errorType)
        {
            switch (errorType)
            {
                case ErrorType.ProxyError:
                    Telemetry.Signal(Telemetry.ErrorWindowEvents, "proxy_error");
                    break;
                case ErrorType.Unauthorized:
                    Telemetry.Signal(Telemetry.ErrorWindowEvents, "unauthorized");
                    break;
                case ErrorType.NoInternet:
                    Telemetry.Signal(Telemetry.ErrorWindowEvents, "no_internet");
                    break;
                case ErrorType.ServerError:
                    Telemetry.Signal(Telemetry.ErrorWindowEvents, "server_error");
                    break;
            }
        }
    }
}
