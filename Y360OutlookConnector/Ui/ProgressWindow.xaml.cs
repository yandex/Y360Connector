using System;
using System.Windows;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Ui
{
    /// <summary>
    /// Interaction logic for ProgressWindow.xaml
    /// </summary>
    public partial class ProgressWindow
    {
        public ProgressWindow()
        {
            InitializeComponent();
        }

        public void SetCurrentSyncKind(SyncTargetType kind)
        {
            switch (kind)
            {
                case SyncTargetType.Calendar:
                    Message.Text = Localization.Strings.Messages_SyncProgressCalendars;
                    break;
                case SyncTargetType.Contacts:
                    Message.Text = Localization.Strings.Messages_SyncProgressContacts;
                    break;
                case SyncTargetType.Tasks:
                    Message.Text = Localization.Strings.Messages_SyncProgressTasks;
                    break;
                default:
                    Message.Text = String.Empty;
                    break;
            }
        }

        public void SetProgressValue(int value)
        {
            if (value >= 0)
            {
                ProgressBar.Value = value;
                ProgressBar.IsIndeterminate = false;
            }
            else
            {
                ProgressBar.IsIndeterminate = true;
            }
        }

        public void SetProgressMaximum(int maximum)
        {
            if (maximum > 0)
            {
                ProgressBar.Maximum = maximum;
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 0;
            }
            else
            {
                ProgressBar.IsIndeterminate = true;
            }
        }
    }
}
