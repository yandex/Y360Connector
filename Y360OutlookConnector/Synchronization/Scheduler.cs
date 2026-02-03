using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CalDavSynchronizer.Implementation.TimeRangeFiltering;
using TinyCalDavSynchronizer;
using Y360OutlookConnector.Clients;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Synchronization.Progress;
using Y360OutlookConnector.Synchronization.Synchronizer;
using Y360OutlookConnector.Synchronization.Synchronizer.FolderMonitor;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization
{
    public class Scheduler
    {
        private readonly FolderMonitorFactory _folderMonitorFactory;
        private readonly SynchronizerFactory _synchronizerFactory;
        private readonly GenSync.Logging.ISynchronizationReportSink _reportSink;
        private readonly TotalProgressFactory _totalProgressFactory = new TotalProgressFactory();

        private Dictionary<Guid, SyncTargetRunner> _runnersById = new Dictionary<Guid, SyncTargetRunner>();
        private bool _isFullSyncRunning;

        private readonly CustomDateTimeRangeProvider _dateTimeRangeProvider;

        private class CustomDateTimeRangeProvider : IDateTimeRangeProvider
        {
            private readonly IDateTimeRangeProvider _dateRangeProvider;
            public CustomDateTimeRangeProvider()
            {
                _dateRangeProvider = Factories.CreateDateTimeRangeProvider(60, 365);
            }

            public DateTimeRange? GetRange()
            {
                return NoDateRangeApplied ? null : _dateRangeProvider.GetRange();
            }

            public bool NoDateRangeApplied { get; set; }
        }

        public Scheduler(Outlook.NameSpace nameSpace, IHttpClientFactory httpClientFactory, 
            string profileDataDir, GenSync.Logging.ISynchronizationReportSink reportSink, 
            InvitesInfoStorage invitesInfo)
        {
            var httpClientFactory1 = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

            _folderMonitorFactory = new FolderMonitorFactory(nameSpace, invitesInfo);

            var outlookSession = new CalDavSynchronizer.OutlookSession(nameSpace);

            _dateTimeRangeProvider = new CustomDateTimeRangeProvider();

            _synchronizerFactory = new SynchronizerFactory(outlookSession, httpClientFactory1,
                profileDataDir, _totalProgressFactory, _dateTimeRangeProvider, invitesInfo);

            _reportSink = reportSink;
        }

        public void ApplySettings(IReadOnlyCollection<SyncTargetInfo> syncTargets, string userEmail, string userCommonName)
        {
            if (syncTargets == null)
                throw new ArgumentNullException(nameof(syncTargets));

            var workersById = new Dictionary<Guid, SyncTargetRunner>();
            var sortedSyncTargets = OrderBySyncType(syncTargets);
            foreach (var syncTarget in sortedSyncTargets)
            {
                try
                {
                    if (!_runnersById.TryGetValue(syncTarget.Id, out var syncTargetRunner))
                    {
                        syncTargetRunner = new SyncTargetRunner(
                            _synchronizerFactory,
                            _folderMonitorFactory,
                            syncTarget.Id,
                            _reportSink);
                    }

                    syncTargetRunner.UpdateSettings(syncTarget, userEmail, userCommonName);
                    workersById.Add(syncTarget.Id, syncTargetRunner);
                }
                catch (Exception exc)
                {
                    ExceptionHandler.Instance.Unexpected(exc);
                }
            }
            var obsoleteIds = _runnersById.Keys.Except(workersById.Keys);
            foreach (var profileId in obsoleteIds)
            {
                var runner = _runnersById[profileId];
                runner?.Cancel();
            }

            _runnersById = workersById;
        }

        public void ClearSettings()
        {
            foreach (var runner in _runnersById.Values)
            {
                try
                {
                    runner?.Cancel();
                }
                catch (Exception exc)
                {
                    ExceptionHandler.Instance.Unexpected(exc);
                }
            }
                
            _runnersById = new Dictionary<Guid, SyncTargetRunner>();
        }

        public SyncTargetRunner GetSyncTargetRunner(Guid profileId)
        {
            if (_runnersById.TryGetValue(profileId, out var runner))
                return runner;
            return null;
        }

        public async Task<bool> RunSynchronization(bool wasManuallyTriggered, bool noDateConstraint, Dictionary<Guid,string> ctags)
        {
            bool result = false;

            if (_isFullSyncRunning) return false;

            _isFullSyncRunning = true;
            try
            {
                if (noDateConstraint)
                {
                    _dateTimeRangeProvider.NoDateRangeApplied = true;
                }
                using (var syncSession = new SyncSessionProgress(_totalProgressFactory, wasManuallyTriggered))
                {
                    var alreadyRan = new HashSet<Guid>();
                    while (true)
                    {
                        DateTime? timePoint = null;
                        if (!wasManuallyTriggered)
                            timePoint = DateTime.UtcNow;

                        var runner = GetNextRunner(alreadyRan, _runnersById, timePoint);
                        if (runner == null)
                            break;

                        if (!ctags.TryGetValue(runner.ProfileId, out var ctag))
                            ctag = String.Empty;

                        syncSession.OnBeforeSyncRunnerStart(runner);
                        ThisAddIn.RestoreUiContext();
                        bool syncStarted = await runner.RunAndRescheduleNoThrow(wasManuallyTriggered, ctag);
                        if (syncStarted)
                            result = true;

                        alreadyRan.Add(runner.ProfileId);
                    }
                }
            }
            catch (Exception exc)
            {
                SyncErrorHandler.HandleException(exc);
            }
            finally
            {
                _isFullSyncRunning = false;
                if (noDateConstraint)
                {
                    _dateTimeRangeProvider.NoDateRangeApplied = false;
                }
            }

            return result;
        }

        private static List<SyncTargetInfo> OrderBySyncType(IReadOnlyCollection<SyncTargetInfo> syncTarget)
        {
            var syncOrderMap = new Dictionary<SyncTargetType, int>() {
                { SyncTargetType.Contacts, 0 },
                { SyncTargetType.Calendar, 1 },
                { SyncTargetType.Tasks, 2 }
            };

            return syncTarget.OrderBy(x => 
            {
                if (syncOrderMap.TryGetValue(x.TargetType, out var syncOrder))
                    return syncOrder;
                return syncOrderMap.Count + 1;
            }).ToList();
        }

        private static SyncTargetRunner GetNextRunner(HashSet<Guid> alreadyRan, 
            Dictionary<Guid, SyncTargetRunner> runnersById, DateTime? timePoint)
        {
            foreach(var item in runnersById)
            {
                var runner = item.Value;

                if (runner == null)
                    continue;
                if (!runner.IsEnabled)
                    continue;
                if (alreadyRan.Contains(item.Key))
                    continue;

                if (timePoint.HasValue && runner.LastAutoSyncTime != DateTime.MinValue
                    && (runner.LastAutoSyncTime + GetAutoSyncTimeout(runner.TargetKind)) > timePoint)
                    continue;

                return runner;
            }

            return null;
        }

        private static TimeSpan GetAutoSyncTimeout(SyncTargetType targetKind)
        {
            switch (targetKind)
            {
                case SyncTargetType.Calendar:
                case SyncTargetType.Tasks:
                    return TimeSpan.FromMinutes(1);
                default:
                    return TimeSpan.FromMinutes(30);
            }
        }
    }
}
