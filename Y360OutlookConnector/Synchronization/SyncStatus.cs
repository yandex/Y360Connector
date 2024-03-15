using System;
using System.Collections.Generic;
using System.Linq;
using GenSync.Logging;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Synchronization
{
    public enum SyncState
    {
        Unknown,
        Running,
        Idle,
        CriticalError
    }
    public enum SyncResult
    {
        None,
        Success,
        HasErrors,
    }

    public enum CriticalError
    {
        None,
        ProxyConnectFailure,
        ProxyAuthFailure,
        NoInternet,
        ServerError
    }

    public class SyncStateChangedEventArgs : EventArgs
    {
        public SyncState State { get; set; }
    }

    public class CriticalErrorChangedEventArgs : EventArgs
    {
        public CriticalError Error { get; set; }
    }

    public class SyncStatus : ISynchronizationReportSink
    {
        public SyncState State { get; set; }
        public CriticalError CriticalError { get; set; }
        public bool AreAllSyncsSuccessful {  get => _reports.Find(x => x.HasErrors) == null; }
        public bool HasAnySyncReport { get => _reports.Count > 0; }

        public event EventHandler<SyncStateChangedEventArgs> SyncStateChanged;
        public event EventHandler<CriticalErrorChangedEventArgs> CriticalErrorChanged;

        private List<SynchronizationReport> _reports = new List<SynchronizationReport>();
        private Dictionary<Guid, SyncResult> _syncResults = new Dictionary<Guid, SyncResult>();

        public SyncStatus()
        {
            State = SyncState.Unknown;
        }

        public void Reset()
        {
            _reports.Clear();
            ChangeState(SyncState.Unknown);
        }

        public SyncResult GetTotalSyncResult()
        {
            if (_syncResults.Count == 0)
                return SyncResult.None;

            foreach (var item in _syncResults)
            {
                if (item.Value == SyncResult.HasErrors)
                    return SyncResult.HasErrors;
                if (item.Value == SyncResult.None)
                    return SyncResult.None;
            }

            return SyncResult.Success;
        }

        public void OnSynchronizationStarted(List<Guid> targetIds)
        {
            _reports = new List<SynchronizationReport>();
            var syncResults = _syncResults.Keys
                .Intersect(targetIds)
                .ToDictionary(x => x, x => _syncResults[x]);
            foreach (var id in targetIds)
            {
                if (!syncResults.ContainsKey(id))
                    syncResults[id] = SyncResult.None;
            }
            _syncResults = syncResults;

            ChangeState(SyncState.Running);
        }

        public void OnSynchronizationFinished()
        {
            ChangeState(CriticalError == CriticalError.None 
                ? SyncState.Idle 
                : SyncState.CriticalError);
        }

        public void SetCriticalError(CriticalError error)
        {
            ChangeCriticalError(error);
        }

        public IReadOnlyCollection<SynchronizationReport> GetReports()
        {
            return _reports;
        }

        public void SendReportsTelemetry(List<SyncTargetInfo> syncTargets)
        {
            foreach (var report in _reports)
            {
                var syncTargetInfo = syncTargets.Find(x => x.Id == report.ProfileId);
                if (syncTargetInfo == null)
                    continue;

                switch (syncTargetInfo.TargetType)
                {
                    case SyncTargetType.Calendar:
                        SendCalendarReportTelemetry(report);
                        break;
                    case SyncTargetType.Contacts:
                        SendContactsReportTelemetry(report);
                        break;
                    case SyncTargetType.Tasks:
                        SendTasksReportTelemetry(report);
                        break;
                }
            }
        }

        public void PostReport(SynchronizationReport report)
        {
            if (report == null) return;

            ThisAddIn.UiContext.Send(_ =>
            {
                _reports.Add(report);
                _syncResults[report.ProfileId] = report.HasErrors
                    ? SyncResult.HasErrors 
                    : SyncResult.Success;
            },
            null);
        }

        private void ChangeState(SyncState value)
        {
            if (State == value) return;

            State = value;
            SyncStateChanged?.Invoke(this, new SyncStateChangedEventArgs { State = value });
        }

        private void ChangeCriticalError(CriticalError value)
        {
            if (CriticalError == value) return;

            CriticalError = value;
            CriticalErrorChanged?.Invoke(this, new CriticalErrorChangedEventArgs { Error = value });

            if (value != CriticalError.None)
                ChangeState(SyncState.CriticalError);
            else if (State == SyncState.CriticalError)
                ChangeState(SyncState.Unknown);
        }

        private static void SendCalendarReportTelemetry(SynchronizationReport report)
        {
            Telemetry.Signal(Telemetry.SyncReportsEvents,
                report.HasErrors ? "calendar_sync_failure" : "calendar_sync_success");
        }

        private static void SendContactsReportTelemetry(SynchronizationReport report)
        {
            Telemetry.Signal(Telemetry.SyncReportsEvents,
                report.HasErrors ? "contacts_sync_failure" : "contacts_sync_success");
        }

        private static void SendTasksReportTelemetry(SynchronizationReport report)
        {
            Telemetry.Signal(Telemetry.SyncReportsEvents,
                report.HasErrors ? "tasks_sync_failure" : "tasks_sync_success");
        }
    }
}
