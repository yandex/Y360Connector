using System;

namespace Y360OutlookConnector.Ui.Login
{
    public interface IConfirmationCodePage
    {
        event EventHandler<CodeEnteredArgs> CodeEntered;
        bool IsAlarmed { get; set; }
    }
}
