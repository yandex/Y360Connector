
namespace Y360OutlookConnector.Synchronization.Progress
{
    class TotalProgressFactory : GenSync.ProgressReport.ITotalProgressFactory
    {
        private SyncSessionProgress _syncSession;

        public GenSync.ProgressReport.ITotalProgressLogger Create()
        {
            return _syncSession == null
                    ? GenSync.ProgressReport.NullTotalProgressFactory.Instance.Create()
                    : new TotalProgressLogger(_syncSession);
        }

        public void OnSyncSessionStarted(SyncSessionProgress syncSession)
        {
            _syncSession = syncSession;
        }

        public void OnSyncSessionFinished(SyncSessionProgress syncSession)
        {
            if (ReferenceEquals(syncSession, _syncSession))
                _syncSession = null;
        }
    }
}
