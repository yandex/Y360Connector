using System;
using System.Windows;

namespace Y360OutlookConnector.Ui
{
    /// <summary>
    /// Interaction logic for AutoUpdateWindow.xaml
    /// </summary>
    public partial class AutoUpdateWindow
    {
        private static AutoUpdateWindow s_activeInstance;

        private Action _startUpdateCallback;

        public AutoUpdateWindow()
        {
            InitializeComponent();

            IsVisibleChanged += AutoUpdateWindow_IsVisibleChanged;
        }

        private void AutoUpdateWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
                Telemetry.Signal(Telemetry.AutoUpdateEvents, "window_shown");
        }

        public static void ShowOrActivate(Action callback)
        {
            if (s_activeInstance == null)
            {
                s_activeInstance = new AutoUpdateWindow();
                s_activeInstance.Closed += (o, e) => s_activeInstance = null;
                s_activeInstance.Show();
            }
            else
            {
                s_activeInstance.Activate();
            }
            s_activeInstance._startUpdateCallback = callback;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Telemetry.Signal(Telemetry.AutoUpdateEvents, "window_restart_button");
            Close();
            _startUpdateCallback?.Invoke();
        }
    }
}
