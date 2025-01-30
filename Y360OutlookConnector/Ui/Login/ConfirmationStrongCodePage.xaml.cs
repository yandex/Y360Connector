using System;
using System.Windows;

namespace Y360OutlookConnector.Ui.Login
{
    /// <summary>
    /// Interaction logic for ConfirmationStrongCodePage.xaml
    /// </summary>
    public partial class ConfirmationStrongCodePage : IConfirmationCodePage
    {
        private bool _isAlarmed;

        public event EventHandler<CodeEnteredArgs> CodeEntered;

        public bool IsAlarmed { get => _isAlarmed; set => SetAlarmed(value); }

        public ConfirmationStrongCodePage()
        {
            InitializeComponent();

            Loaded += AuthCodePage_Loaded;
            IsVisibleChanged += ConfirmationCodePage_IsVisibleChanged;
        }

        private void ConfirmationCodePage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Telemetry.Signal(Telemetry.LoginWindowEvents, "sixteen_chars_page_shown");
        }

        private void AuthCodePage_Loaded(object sender, RoutedEventArgs e)
        {
            SixteenCharsBox.TextEntered += SixteenCharsBox_TextEntered;
            SixteenCharsBox.TextChanged += SixteenCharsBox_TextChanged;
        }

        private void SixteenCharsBox_TextEntered(object sender, TextEnteredArgs e)
        {
            CodeEntered?.Invoke(null, new CodeEnteredArgs{ Code = e.Text });
        }

        private void SixteenCharsBox_TextChanged(object sender, EventArgs e)
        {
            if (IsAlarmed)
                IsAlarmed = false;
        }

        public void SetAlarmed(bool value)
        {
            SixteenCharsBox.SetAlarmed(value);
            ErrorMessage.Visibility = value ? Visibility.Visible : Visibility.Hidden;
            _isAlarmed = value;
        }
    }
}
