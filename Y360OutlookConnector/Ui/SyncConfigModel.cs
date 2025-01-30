using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using CalDavSynchronizer.Ui;
using log4net;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Synchronization;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Ui
{
    public sealed class SyncTargetModel : INotifyPropertyChanged
    {
        private OutlookFolderDescriptor _outlookFolder;
        private bool _enabled;
        private string _folderName;

        public readonly SyncTargetInfo Info;

        public string Name => Info.Name;
        public bool IsPrimary => Info.IsPrimary;
        public bool IsReadOnly => Info.IsReadOnly;
        public bool FolderExist => _outlookFolder != null;
        public string TargetTypeString => ToFriendlyString(Info.TargetType);
        public string FolderPath { get; set; }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled != value)
                {
                    _enabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FolderName));
                }
            }
        }

        public string FolderName
        {
            get
            { 
                if (!Enabled && !FolderExist)
                    return String.Empty;
                return _folderName;
            }
            set
            {
                if (_folderName != value)
                {
                    _folderName = value;
                    OnPropertyChanged();
                }
            }
        }

        public OutlookFolderDescriptor OutlookFolder
        {
            get => _outlookFolder;
            set
            {
                _outlookFolder = value;
                OnPropertyChanged();
                if (_outlookFolder != null)
                    FolderName = _outlookFolder.Name;
                OnPropertyChanged(nameof(FolderExist));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public SyncTargetModel(SyncTargetInfo syncTargetInfo)
        {
            Info = syncTargetInfo ?? throw new ArgumentNullException(nameof(syncTargetInfo));
            _enabled = Info.Config.Active;
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static string ToFriendlyString(SyncTargetType targetType)
        {
            switch (targetType)
            {
                case SyncTargetType.Calendar:
                    return Localization.Strings.SyncConfigWindow_CalendarsFolderType;
                case SyncTargetType.Contacts:
                    return Localization.Strings.SyncConfigWindow_ContactsFolderType;
                case SyncTargetType.Tasks:
                    return Localization.Strings.SyncConfigWindow_TasksFolderType;
                default:
                    return "";
            }
        }
        public override string ToString()
        {
            return $"SyncConfigModel: Name={Name}, IsPrimary={IsPrimary}, IsReadOnly={IsReadOnly}, FolderExists={FolderExist}, TargetType={TargetTypeString}, Enabled={Enabled}, FolderName={FolderName}, FolderPath={FolderPath}";
        }
    }

    public class SyncConfigModel
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public List<SyncTargetModel> Items;

        private readonly Outlook.NameSpace _session;
        private readonly AccountFolders _accountFolders;

        public SyncConfigModel(Outlook.NameSpace session, List<SyncTargetInfo> syncTargets, string userEmail)
        {
            _session = session;
            _accountFolders = new AccountFolders(userEmail, _session);

            Items = syncTargets.ConvertAll(x => new SyncTargetModel(x.Clone()));

            AssignDefaultFolders();
        }

        public SyncTargetModel FindTargetByFolder(OutlookFolderDescriptor folder)
        {
            if (folder == null)
                return null;

            foreach (var item in Items)
            {
                if (item.Info.Config.OutlookFolderEntryId == folder.EntryId
                    && item.Info.Config.OutlookFolderStoreId == folder.StoreId)
                    return item;
            }

            return null;
        }

        private Outlook.MAPIFolder GetOutlookFolder(SyncTargetConfig target)
        {
            try
            {
                if (String.IsNullOrEmpty(target.OutlookFolderEntryId))
                    return null;
                return _session.GetFolderFromID(target.OutlookFolderEntryId, target.OutlookFolderStoreId);
            }
            catch (Exception exc)
            {
                s_logger.Error("Failed to get outlook folder by id", exc);
                return null;
            }
        }

        public OutlookFolderDescriptor GetOutlookFolderDescriptor(SyncTargetConfig target)
        {
            var mapiFolder = GetOutlookFolder(target);
            if (mapiFolder != null)
                return new OutlookFolderDescriptor(mapiFolder);
            return null;
        }

        public bool IsModified()
        {
            return Items.Any(IsModified);
        }

        public void Restore()
        {
            Items.ForEach(Restore);
            AssignDefaultFolders();
        }

        private void AssignDefaultFolders()
        {
            foreach (var item in Items)
            {
                try
                {
                    OutlookFolderDescriptor folderDescriptor = null;
                    var folder = GetOutlookFolder(item.Info.Config);
                    if (folder != null && !AccountFolders.IsFolderTrashed(folder))
                        folderDescriptor = new OutlookFolderDescriptor(folder);

                    item.OutlookFolder = folderDescriptor;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error when trying to find Outlook folder for {item}", ex);
                }

                if (item.OutlookFolder == null)
                {
                    s_logger.Info($"Outlook folder for {item} was not found. Sync target disabled.");
                    item.Info.Config.OutlookFolderEntryId = String.Empty;
                    item.Info.Config.OutlookFolderStoreId = String.Empty;
                    item.Info.Config.Active = false;
                    item.Enabled = false;
                }
            }
                        
            foreach (var item in Items)
            {
                if (item.OutlookFolder == null && item.IsPrimary)
                {
                    var folder = _accountFolders.GetDefaultFolderDescriptor(item.Info.TargetType);
                    if (FindTargetByFolder(folder) == null)
                        item.OutlookFolder = folder;
                }

                if (item.OutlookFolder == null)
                {
                    item.FolderName = _accountFolders.CreateNewFolderName(item.Info.TargetType, item.Name);
                }
            }
        }

        public List<SyncTargetInfo> ApplyChanges()
        {
            var result = new List<SyncTargetInfo>();

            foreach (var item in Items)
            {
                var targetTypeString = item.Info.TargetType.ToString().ToLower();

                if (item.Enabled != item.Info.Config.Active)
                {
                    item.Info.Config.Active = item.Enabled;
                    var eventName = item.Enabled ? "sync_on" : "sync_off";
                    Telemetry.Signal(Telemetry.SyncConfigWindowEvents, eventName, targetTypeString);
                }

                if (item.Info.Config.OutlookFolderEntryId != item.OutlookFolder?.EntryId
                    || item.Info.Config.OutlookFolderStoreId != item.OutlookFolder?.StoreId)
                {
                    item.Info.Config.OutlookFolderEntryId = item.OutlookFolder?.EntryId;
                    item.Info.Config.OutlookFolderStoreId = item.OutlookFolder?.StoreId;

                    Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "folder_changed", targetTypeString);
                }

                CreateFolderIfNeeded(item);

                result.Add(item.Info);
            }

            return result;
        }

        private bool IsModified(SyncTargetModel syncTarget)
        {
            var targetConfig = syncTarget.Info.Config;
            if (targetConfig.Active != syncTarget.Enabled)
                return true;

            var outlookFolderEntryId = syncTarget.OutlookFolder?.EntryId ?? "";
            var outlookFolderStoreId = syncTarget.OutlookFolder?.StoreId ?? "";

            if (outlookFolderEntryId != targetConfig.OutlookFolderEntryId
                || outlookFolderStoreId != targetConfig.OutlookFolderStoreId)
                return true;

            return false;
        }

        private void Restore(SyncTargetModel syncTarget)
        {
            syncTarget.Enabled = syncTarget.Info.Config.Active;
            syncTarget.OutlookFolder = GetOutlookFolderDescriptor(syncTarget.Info.Config);
            if (syncTarget.OutlookFolder == null)
            {
                syncTarget.Enabled = false;
                syncTarget.Info.Config.Active = false;
            }
        }

        private void CreateFolderIfNeeded(SyncTargetModel syncTarget)
        {
            if (syncTarget.Enabled && syncTarget.OutlookFolder == null)
            {
                var folder = _accountFolders.CreateNewFolder(syncTarget.Info.TargetType, syncTarget.FolderName);

                if (folder != null)
                    syncTarget.OutlookFolder = new OutlookFolderDescriptor(folder);

                if (syncTarget.OutlookFolder == null && !String.IsNullOrEmpty(syncTarget.FolderName))
                {
                    var message = String.Format(
                        Localization.Strings.SyncConfigWindow_СreatingFolderErrorMessage,
                        syncTarget.FolderName);

                    Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "create_folder_error_message");
                    MessageBox.Show(message, Localization.Strings.Messages_ProductName,
                        MessageBoxButton.OK, MessageBoxImage.Error);

                    syncTarget.Enabled = false;
                    syncTarget.Info.Config.Active = false;
                }
                else
                {
                    syncTarget.Info.Config.OutlookFolderEntryId = syncTarget.OutlookFolder?.EntryId;
                    syncTarget.Info.Config.OutlookFolderStoreId = syncTarget.OutlookFolder?.StoreId;
                }
            }
        }
    }
}
