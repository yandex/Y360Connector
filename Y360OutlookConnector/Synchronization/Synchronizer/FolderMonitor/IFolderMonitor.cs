using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CalDavSynchronizer.ChangeWatching;

namespace Y360OutlookConnector.Synchronization.Synchronizer.FolderMonitor
{
    public class FolderMonitorItemChangedEventArgs : EventArgs
    {
        public IOutlookId EntryId { get; }

        public FolderMonitorItemChangedEventArgs(IOutlookId entryId)
        {
            EntryId = entryId;
        }
    }

    public interface IFolderMonitor : IDisposable
    {
        event EventHandler<FolderMonitorItemChangedEventArgs> ItemChanged;
    }
}
