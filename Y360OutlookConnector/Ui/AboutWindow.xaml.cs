using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Navigation;

namespace Y360OutlookConnector.Ui
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow
    {
        private const string LicenseAgreementUrl = "https://github.com/yandex/Y360Connector/blob/main/LICENSE.txt";

        private readonly AutoUpdateManager _autoUpdateManager;

        public AboutWindow()
        {
            InitializeComponent();

            LicenseHyperlink.NavigateUri = new Uri(LicenseAgreementUrl);

            var thisVersion = Assembly.GetExecutingAssembly().GetName().Version;
            VersionLabel.Text = String.Format(Localization.Strings.AboutWindow_VersionString, 
                thisVersion.ToString(3), thisVersion.Revision);

            Closed += AboutWindow_Closed;
            IsVisibleChanged += AboutWindow_IsVisibleChanged;

            _autoUpdateManager = ThisAddIn.Components?.AutoUpdateManager;
            if (_autoUpdateManager != null)
            {
                _autoUpdateManager.UpdateStateChanged += AutoUpdateManager_UpdateStateChanged;
                HandleAutoUpdateState();
            }
        }

        public bool? ShowDialog(object parentWindow)
        {
            var owner = OutlookWin32Window.GetHandle(parentWindow);
            if (owner != IntPtr.Zero)
            {
                var windowInteropHelper = new WindowInteropHelper(this);
                windowInteropHelper.Owner = owner;
            }

            return ShowDialog();
        }

        private void AutoUpdateManager_UpdateStateChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(HandleAutoUpdateState);
        }

        private void HandleAutoUpdateState()
        {
            var autoUpdateSate = _autoUpdateManager.State;
            if (autoUpdateSate == AutoUpdateManager.UpdateState.WaitingForRestart)
            {
                var version = _autoUpdateManager.AvailableVersion;

                AutoUpdateVersionLabel.Text = String.Format(Localization.Strings.AboutWindow_NewVersionString, 
                    version.ToString(3), version.Revision);
                AutoUpdatePanel.Visibility = Visibility.Visible;
            }
            else if (autoUpdateSate == AutoUpdateManager.UpdateState.Restarting)
            {
                Close();
            }
            else
            {
                AutoUpdatePanel.Visibility = Visibility.Collapsed;
            }
        }

        private void AboutWindow_Closed(object sender, EventArgs e)
        {
            if (_autoUpdateManager != null)
                _autoUpdateManager.UpdateStateChanged -= AutoUpdateManager_UpdateStateChanged;
        }

        private void AboutWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
                Telemetry.Signal(Telemetry.AboutWindowEvents, "shown");
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Telemetry.Signal(Telemetry.AboutWindowEvents, "eula_link");
            Process.Start(new ProcessStartInfo
            { 
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Telemetry.Signal(Telemetry.AboutWindowEvents, "restart_button");
            Close();
            _autoUpdateManager?.RestartOutlook();
        }
    }
}
