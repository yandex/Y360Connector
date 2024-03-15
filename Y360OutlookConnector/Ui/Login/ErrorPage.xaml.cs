using System;
using System.Windows;

namespace Y360OutlookConnector.Ui.Login
{
    /// <summary>
    /// Interaction logic for ErrorPage.xaml
    /// </summary>
    public partial class ErrorPage
    {
        public EventHandler RetryClicked;
        public EventHandler AnotherWayClicked;

        public ErrorPage()
        {
            InitializeComponent();

            IsVisibleChanged += ErrorPage_IsVisibleChanged;
        }

        private void ErrorPage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
                Telemetry.Signal(Telemetry.LoginWindowEvents, "error_page_shown");
        }

        private void RetryButton_OnClick(object sender, RoutedEventArgs e)
        {
            RetryClicked?.Invoke(this, EventArgs.Empty);
            Telemetry.Signal(Telemetry.LoginWindowEvents, "error_page_try_again");
        }

        private void AnotherWayButton_OnClick(object sender, RoutedEventArgs e)
        {
            AnotherWayClicked?.Invoke(this, EventArgs.Empty);
            Telemetry.Signal(Telemetry.LoginWindowEvents, "error_page_open_browser");
        }
    }
}
