using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Ui;
using log4net;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Synchronization;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Ui
{
    /// <summary>
    /// Interaction logic for SyncConfigWindow.xaml
    /// </summary>
    public partial class SyncConfigWindow
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private readonly SyncManager _syncManager;
        private readonly Outlook.NameSpace _session;
        private SyncConfigModel _model;

        private static SyncConfigWindow s_instance;

        public SyncConfigWindow(Outlook.NameSpace session, SyncManager syncManager)
        {
            _session = session;
            _syncManager = syncManager;

            InitializeComponent();

            Closed += SyncConfigWindow_Closed;
            IsVisibleChanged += SyncConfigWindow_IsVisibleChanged;
            DisplayLastSyncResults();

            _syncManager.Status.SyncStateChanged += SyncStatus_StateChanged;

             var task = _syncManager.GetSyncTargets();
            if (!task.IsCompleted)
            {
                ShowThrobber(true);
                Dispatcher.BeginInvoke(new Action(() => _ = LoadModelAsync()));
            }
            else
            {
                try
                {
                    SetModel(task.Result);
                }
                catch (Exception e)
                {
                    SyncErrorHandler.HandleException(e, false);
                    throw;
                }
            }
        }

        private void SyncStatus_StateChanged(object sender, SyncStateChangedEventArgs e)
        {
            DisplayLastSyncResults();
        }

        public static void ShowOrActivate(Outlook.Application application, SyncManager syncManager)
        {
            var criticalError = syncManager.Status.CriticalError;
            if (criticalError != CriticalError.None)
            {
                ErrorWindow.ShowError(criticalError);
                return;
            }

            if (s_instance == null)
            {
                s_instance = new SyncConfigWindow(application.Session, syncManager);

                var owner = OutlookWin32Window.GetHandle(application.ActiveExplorer());
                if (owner != IntPtr.Zero)
                {
                    var windowInteropHelper = new WindowInteropHelper(s_instance);
                    windowInteropHelper.Owner = owner;
                }

                s_instance.Closed += (o, e) => s_instance = null;
                s_instance.Show();
            }
            else
            {
                s_instance.Activate();
            }
        }

        private void SyncConfigWindow_Closed(object sender, EventArgs e)
        {
            _syncManager.Status.SyncStateChanged -= SyncStatus_StateChanged;
        }

        private void SyncConfigWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
                Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "shown");
        }

        private void DisplayLastSyncResults()
        {
            if (_syncManager.Status.State == SyncState.Idle)
            {
                switch (_syncManager.Status.GetTotalSyncResult())
                {
                    case SyncResult.HasErrors:
                        SyncSuccessPanel.Visibility = Visibility.Collapsed;
                        SyncFailurePanel.Visibility = Visibility.Visible;
                        break;
                    case SyncResult.Success:
                        SyncSuccessPanel.Visibility = Visibility.Visible;
                        SyncFailurePanel.Visibility = Visibility.Collapsed;
                        break;
                    default:
                        SyncSuccessPanel.Visibility = Visibility.Collapsed;
                        SyncFailurePanel.Visibility = Visibility.Collapsed;
                        break;
                }
            }
        }

        private async Task LoadModelAsync()
        {
            ThisAddIn.RestoreUiContext();

            List<SyncTargetInfo> syncTargets;

            try
            {
                syncTargets = await _syncManager.GetSyncTargets();
            }
            catch (Exception exc)
            {
                SyncErrorHandler.HandleException(exc, false);
                throw;
            }

            
            ShowThrobber(false);

            SetModel(syncTargets);
        }

        private void SetModel(List<SyncTargetInfo> syncTargets)
        {
            _model = new SyncConfigModel(_session, syncTargets, _syncManager.UserEmail);
            ItemsList.ItemsSource = _model.Items;

            CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(ItemsList.ItemsSource);
            PropertyGroupDescription groupDescription = new PropertyGroupDescription("TargetTypeString");
            view.GroupDescriptions?.Clear();
            view.GroupDescriptions?.Add(groupDescription);

            SetModified(_model.IsModified());
        }

        private void ShowThrobber(bool value)
        {
            Throbber.Visibility =  value ? Visibility.Visible : Visibility.Collapsed;
            ContentPanel.Visibility =  value ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.DataContext as SyncTargetModel;
            if (item == null) return;

            var outlookFolder = PickFolderOrNull();
            if (outlookFolder == null) return;

            if (ValidateFolder(outlookFolder, item))
            {
                item.OutlookFolder = outlookFolder;
                item.Enabled = true;
                SetModified(_model.IsModified());
            }
        }

        private bool ValidateFolder(OutlookFolderDescriptor folder, SyncTargetModel syncTarget)
        {
            if (ToOlItemType(syncTarget.Info.TargetType) != folder.DefaultItemType)
            {
                ShowWrongFolderTypeMessage(syncTarget.Info.TargetType);
                return false;
            }

            var conflictedSyncTarget = _model.FindTargetByFolder(folder);
            if (conflictedSyncTarget != null && conflictedSyncTarget != syncTarget)
            {
                if (conflictedSyncTarget.Enabled)
                {
                    ShowFolderAlreadyInUseMessage(conflictedSyncTarget);
                    return false;
                }
                conflictedSyncTarget.OutlookFolder = null;
            }

            return true;
        }

        private static void ShowWrongFolderTypeMessage(SyncTargetType requiredTargetType)
        {
            string messageText;
            switch (requiredTargetType)
            {
                case SyncTargetType.Tasks:
                    messageText = Localization.Strings.SyncConfigWindow_WrongFolderTypeForTasksMessage;
                    break;
                case SyncTargetType.Contacts:
                    messageText = Localization.Strings.SyncConfigWindow_WrongFolderTypeForContactsMessage;
                    break;
                default:
                    messageText = Localization.Strings.SyncConfigWindow_WrongFolderTypeForCalendarMessage;
                    break;
            }
            Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "wrong_folder_type_message");
            MessageBox.Show(messageText, Localization.Strings.Messages_ProductName, 
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static void ShowFolderAlreadyInUseMessage(SyncTargetModel conflictedSyncTarget)
        {
            string messageText;
            switch (conflictedSyncTarget.Info.TargetType)
            {
                case SyncTargetType.Tasks:
                    messageText = String.Format(
                        Localization.Strings.SyncConfigWindow_FolderAlreadyUsedByTasksMessage,
                        conflictedSyncTarget.Name);
                    break;
                case SyncTargetType.Contacts:
                    messageText = String.Format(
                        Localization.Strings.SyncConfigWindow_FolderAlreadyUsedByContacsMessage,
                        conflictedSyncTarget.Name);
                    break;
                default:
                    messageText = String.Format(
                        Localization.Strings.SyncConfigWindow_FolderAlreadyUsedByCalendarMessage,
                        conflictedSyncTarget.Name);
                    break;
            }
            Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "folder_already_used_message");
            MessageBox.Show(messageText, Localization.Strings.Messages_ProductName, 
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private OutlookFolderDescriptor PickFolderOrNull()
        {
            OutlookFolderDescriptor result = null;
            var folder = _session.PickFolder();
            if (folder != null)
            {
                using (var wrapper = GenericComObjectWrapper.Create(folder))
                    result = new OutlookFolderDescriptor(wrapper.Inner);
            }
            Activate();
            return result;
        }

        private void SetModified(bool value)
        {
            ButtonsPanel.Visibility = value ? Visibility.Visible : Visibility.Hidden;
        }

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            var syncTargets = _model.ApplyChanges();
            SetModified(false);

            Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "apply_button");

            _syncManager.ApplySyncConfig(syncTargets);

            if (_syncManager.Status.State != SyncState.Running)
            {
                s_logger.Info("Sync triggered by config update");
                _ = _syncManager.RunSynchronization(true);
            }
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            _model.Restore();
            SetModified(false);

            Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "cancel_button");
        }

        private void SyncCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SetModified(_model.IsModified());
        }

        private static Outlook.OlItemType ToOlItemType(SyncTargetType targetType)
        {
            switch (targetType)
            {
                case SyncTargetType.Calendar:
                    return Outlook.OlItemType.olAppointmentItem;
                case SyncTargetType.Tasks:
                    return Outlook.OlItemType.olTaskItem;
                case SyncTargetType.Contacts:
                    return Outlook.OlItemType.olContactItem;
                default:
                    throw new Exception($"Unknown sync target type: {targetType}");
            }
        }
    }
}
