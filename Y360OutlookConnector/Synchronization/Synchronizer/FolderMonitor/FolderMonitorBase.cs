using System;
using System.Runtime.InteropServices;
using CalDavSynchronizer.ChangeWatching;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization.Synchronizer.FolderMonitor
{
    public abstract class FolderMonitorBase : IFolderMonitor
    {
        public event EventHandler<FolderMonitorItemChangedEventArgs> ItemChanged;

        private readonly Outlook.Items _folderItems;
        private readonly Outlook.Folder _folder;

        protected enum ItemAction
        {
            Add,
            Change,
            Remove
        }

        protected FolderMonitorBase(Outlook.Folder folder)
        {
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));

            _folderItems = folder.Items;

            _folder.BeforeItemMove += Folder_BeforeItemMove;
            _folderItems.ItemAdd += FolderItems_ItemAdd;
            _folderItems.ItemChange += FolderItems_ItemChange;
        }

        public void Dispose()
        {
            _folder.BeforeItemMove -= Folder_BeforeItemMove;
            _folderItems.ItemAdd -= FolderItems_ItemAdd;
            _folderItems.ItemChange -= FolderItems_ItemChange;

            Marshal.FinalReleaseComObject(_folderItems);
            Marshal.FinalReleaseComObject(_folder);
        }

        private void FolderItems_ItemChange(object item)
        {
            HandleItem(item, ItemAction.Change);
        }

        private void FolderItems_ItemAdd(object item)
        {
            HandleItem(item, ItemAction.Add);
        }

        private void Folder_BeforeItemMove(object item, Outlook.MAPIFolder moveTo, ref bool cancel)
        {
            HandleItem(item, ItemAction.Remove);
        }

        protected void OnItemChanged(IOutlookId entryId)
        {
            ItemChanged?.Invoke(this, new FolderMonitorItemChangedEventArgs(entryId));
        }

        protected abstract void HandleItem(object item, ItemAction action);
    }
}
