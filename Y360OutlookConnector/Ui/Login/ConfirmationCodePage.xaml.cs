using System;
using System.Windows;

namespace Y360OutlookConnector.Ui.Login
{
    /// <summary>
    /// Interaction logic for ConfirmationCodePage.xaml
    /// </summary>
    public partial class ConfirmationCodePage
    {
        private bool _isAlarmed;

        public class CodeEnteredArgs : EventArgs
        {
            public string Code { get; set; }
        }

        public event EventHandler<CodeEnteredArgs> CodeEntered;

        public bool IsAlarmed { get => _isAlarmed; set => SetAlarmed(value); }

        public ConfirmationCodePage()
        {
            InitializeComponent();

            Loaded += AuthCodePage_Loaded;
            IsVisibleChanged += ConfirmationCodePage_IsVisibleChanged;
        }

        private void ConfirmationCodePage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Telemetry.Signal(Telemetry.LoginWindowEvents, "seven_digits_page_shown");
        }

        private void AuthCodePage_Loaded(object sender, RoutedEventArgs e)
        {
            SevenDigitsBox.TextEntered += SevenDigitsBox_TextEntered;
            SevenDigitsBox.TextChanged += SevenDigitsBox_TextChanged;
        }

        private void SevenDigitsBox_TextEntered(object sender, SevenDigitsBox.TextEnteredArgs e)
        {
            CodeEntered?.Invoke(null, new CodeEnteredArgs{ Code = e.Text });
        }

        private void SevenDigitsBox_TextChanged(object sender, EventArgs e)
        {
            if (IsAlarmed)
                IsAlarmed = false;
        }

        public void SetAlarmed(bool value)
        {
            SevenDigitsBox.SetAlarmed(value);
            ErrorMessage.Visibility = value ? Visibility.Visible : Visibility.Hidden;
            _isAlarmed = value;
        }
    }
}
