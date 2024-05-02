using CalDavSynchronizer.ChangeWatching;
using GenSync.Logging;
using log4net;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Synchronization.Synchronizer;
using Y360OutlookConnector.Synchronization.Synchronizer.FolderMonitor;

namespace Y360OutlookConnector.Synchronization
{
    public class SyncTargetRunner
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public Guid ProfileId { get; }
        public bool IsEnabled { get => _data.Active; }
        public DateTime LastAutoSyncTime { get; private set; }
        public SyncTargetType TargetKind { get => _data.TargetKind; }

        private readonly TimeSpan _partialSyncDelay = TimeSpan.FromSeconds(5);
        private readonly System.Windows.Forms.Timer _partialSyncTimer;
        private readonly SynchronizerFactory _synchronizerFactory;
        private readonly FolderMonitorFactory _folderMonitorFactory;
        private readonly ISynchronizationReportSink _reportSink;
        private ConfigData _data;
        private string _prevCTag;

        private volatile bool _fullSyncPending;

        private readonly ConcurrentDictionary<string, IOutlookId> _pendingOutlookItems =
            new ConcurrentDictionary<string, IOutlookId>();

        private int _isRunning;

        public SyncTargetRunner(
            SynchronizerFactory synchronizerFactory,
            FolderMonitorFactory folderMonitorFactory,
            Guid profileId,
            ISynchronizationReportSink reportSink)
        {
            _reportSink = reportSink ?? throw new ArgumentNullException(nameof(reportSink));
            _synchronizerFactory = synchronizerFactory ?? throw new ArgumentNullException(nameof(synchronizerFactory));
            _folderMonitorFactory = folderMonitorFactory ?? throw new ArgumentNullException(nameof(folderMonitorFactory));

            ProfileId = profileId;
            LastAutoSyncTime = DateTime.MinValue;

            _partialSyncTimer = new System.Windows.Forms.Timer();
            _partialSyncTimer.Tick += PartialSyncTimerTickAsync;
            _partialSyncTimer.Interval = (int) _partialSyncDelay.TotalMilliseconds;
        }

        public void UpdateSettings(SyncTargetInfo info, string userEmail, string userCommonName)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            if (ProfileId != info.Id)
                throw new ArgumentException($"Cannot update runner for profile '{ProfileId}' " +
                                            $"with options of profile '{info.Id}'");

            if (!_data.IsModified(info))
                return;

            if (_isRunning == 1)
                s_logger.Info($"Applying options to profile '{info.Name}' ({ProfileId}) which is currently running.");

            _pendingOutlookItems.Clear();
            _fullSyncPending = false;

            if (!_data.IsEmpty)
                _data.FolderMonitor.ItemChanged -= FolderMonitor_ItemChanged;
            _data.Reset();

            bool isActive = info.Config.Active;
            IFolderMonitor folderMonitor = null;
            if (isActive && !String.IsNullOrEmpty(info.Config.OutlookFolderEntryId))
            {
                folderMonitor = _folderMonitorFactory.Create(
                    info.Config.OutlookFolderEntryId, info.Config.OutlookFolderStoreId);
                if (folderMonitor != null)
                    folderMonitor.ItemChanged += FolderMonitor_ItemChanged;
            }
            else
            {
                isActive = false;
            }

            var synchronizer = isActive
                ? _synchronizerFactory.CreateSynchronizer(info, userEmail, userCommonName)
                : null;

            _data = new ConfigData(
                isActive,
                info,
                synchronizer,
                folderMonitor);
        }

        public void Cancel()
        {
            if (!_data.IsEmpty)
                _data.FolderMonitor.ItemChanged -= FolderMonitor_ItemChanged;

            _data.Reset();
        }

        private void FolderMonitor_ItemChanged(object sender, FolderMonitorItemChangedEventArgs e)
        {
            try
            {
                FolderMonitor_ItemSavedOrDeletedAsync(e);
            }
            catch (Exception x)
            {
                s_logger.Error(null, x);
            }
        }

        private void FolderMonitor_ItemSavedOrDeletedAsync(FolderMonitorItemChangedEventArgs e)
        {
            try
            {
                // If EntryId is null, we don't wont to start partial sync right now, just clear ctag
                _prevCTag = String.Empty;
                if (e.EntryId?.EntryId == null) return;

                _pendingOutlookItems.AddOrUpdate(e.EntryId.EntryId, e.EntryId, (key, existingValue) => e.EntryId.Version > existingValue.Version ? e.EntryId : existingValue);
                if (s_logger.IsDebugEnabled)
                {
                    s_logger.Debug($"Partial sync:  '{_pendingOutlookItems.Count}' items pending after " +
                                   $"registering item '{e.EntryId.EntryId}' as pending sync item.");
                }

                // Restart timer
                _partialSyncTimer.Stop();
                _partialSyncTimer.Start();
            }
            catch (Exception x)
            {
                s_logger.Error(null, x);
            }
        }

        private async void PartialSyncTimerTickAsync(object sender, EventArgs e)
        {
            _partialSyncTimer.Stop();
            await RunAllPendingJobs();
        }

        public async Task<bool> RunAndRescheduleNoThrow(bool wasManuallyTriggered, string ctag)
        {
            bool result = false;
            try
            {
                if (!_data.Active)
                    return false;

                if (!wasManuallyTriggered &&
                    !String.IsNullOrEmpty(_prevCTag) && !String.IsNullOrEmpty(ctag)
                    && _prevCTag == ctag)
                {
                    s_logger.Debug($"Skipping sync of '{_data.Name}' ({_data.Url}): no changes detected");
                    return false;
                }

                if (!wasManuallyTriggered || LastAutoSyncTime == DateTime.MinValue)
                    LastAutoSyncTime = DateTime.UtcNow;

                _fullSyncPending = true;
                result = true;

                string syncReason = wasManuallyTriggered ? "triggered manually" : "autosync";
                s_logger.Info($"Syncing '{_data.Name}' - {_data.Url} ({syncReason})");
                await RunAllPendingJobs();
                _prevCTag = ctag;
            }
            catch (Exception exc)
            {
                SyncErrorHandler.HandleException(exc);
                _prevCTag = String.Empty;
            }
            return result;
        }

        private async Task RunAllPendingJobs()
        {
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0)
            {
                try
                {
                    while (_fullSyncPending || _pendingOutlookItems.Count > 0)
                    {
                        if (_fullSyncPending)
                        {
                            _fullSyncPending = false;
                            Thread.MemoryBarrier();
                            await RunFullNoThrow();
                        }

                        if (_pendingOutlookItems.Count > 0)
                        {
                            var itemsToSync = _pendingOutlookItems.Values.ToArray();
                            _pendingOutlookItems.Clear();
                            if (s_logger.IsDebugEnabled)
                            {
                                s_logger.Debug($"Partial sync: Going to sync '{itemsToSync.Length}' pending " +
                                               $"items ( {string.Join(", ", itemsToSync.Select(id => id.EntryId))} ).");
                            }

                            Thread.MemoryBarrier(); // should not be required because there is just one thread entering multiple times
                            await RunPartialNoThrow(itemsToSync);
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isRunning, 0);
                }
            }
        }

        private async Task RunFullNoThrow()
        {
            try
            {
                if (_data.Synchronizer == null)
                    return;

                using (var logger = new SynchronizationLogger(ProfileId, _data.Name, _reportSink, true))
                {
                    try
                    {
                        await Task.Run(async () =>
                        {
                            await _data.Synchronizer.Synchronize(logger);
                        });
                    }
                    catch (Exception exc)
                    {
                        SyncErrorHandler.HandleException(exc);
                    }

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
            catch (Exception x)
            {
                s_logger.Error(null, x);
            }
        }

        private async Task RunPartialNoThrow(IOutlookId[] itemsToSync)
        {
            try
            {
                if (_data.Synchronizer == null)
                    return;

                using (var logger = new SynchronizationLogger(ProfileId, _data.Name, _reportSink, false))
                {
                    try
                    {
                        await Task.Run(async () =>
                        {
                            await _data.Synchronizer.SynchronizePartial(itemsToSync, logger);
                        });
                    }
                    catch (Exception exc)
                    {
                        SyncErrorHandler.HandleException(exc);
                    }

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
            catch (Exception exc)
            {
                SyncErrorHandler.HandleException(exc);
            }
        }

        private struct ConfigData
        {
            public bool Active;
            public CancellableSynchronizer Synchronizer;
            public IFolderMonitor FolderMonitor;

            public readonly string Name;
            public readonly bool IsReadOnly;
            public readonly string Url;
            public readonly string OutlookFolderEntryId;
            public readonly string OutlookFolderStoreId;
            public readonly SyncTargetType TargetKind;

            public bool IsEmpty => FolderMonitor == null;

            public ConfigData(bool active, SyncTargetInfo info, CancellableSynchronizer synchronizer,
                IFolderMonitor folderMonitor)
            {
                if (info == null)
                    throw new ArgumentNullException(nameof(info));

                Synchronizer = synchronizer;
                FolderMonitor = folderMonitor;
                Active = active;

                Name = info.Name;
                IsReadOnly = info.IsReadOnly;
                Url = info.Config.Url;
                TargetKind = info.TargetType;
                OutlookFolderEntryId = info.Config.OutlookFolderEntryId;
                OutlookFolderStoreId = info.Config.OutlookFolderStoreId;
            }

            public bool IsModified(SyncTargetInfo info)
            {
                return Active != info.Config.Active
                    || IsReadOnly != info.IsReadOnly
                    || Url != info.Config.Url
                    || OutlookFolderEntryId != info.Config.OutlookFolderEntryId
                    || OutlookFolderStoreId != info.Config.OutlookFolderStoreId;
            }

            public void Reset()
            {
                FolderMonitor?.Dispose();
                Synchronizer?.Dispose();

                FolderMonitor = null;
                Synchronizer = null;
                Active = false;
            }
        }
    }
}
