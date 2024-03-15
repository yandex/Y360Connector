using System;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Synchronization.Progress
{
    class SyncSessionProgress : IDisposable
    {
        private readonly TotalProgressFactory _progressFactory;

        private bool _disposed;
        private SyncTargetType _currentSyncRunnerKind;

        private Ui.ProgressWindow _progressWindow;
        private System.Windows.Forms.Timer _timer;

        private int _progressMaximum = 0;
        private int _progressValue = 0;

        public SyncSessionProgress(TotalProgressFactory progressFactory)
        {
            _progressFactory = progressFactory;
            _progressFactory?.OnSyncSessionStarted(this);
        }

        public void OnBeforeSyncRunnerStart(SyncTargetRunner runner)
        {
            if (_disposed) return;

            _currentSyncRunnerKind = runner.TargetKind;
        }

        public void Dispose()
        {
            _progressFactory?.OnSyncSessionFinished(this);
            _progressWindow?.Close();
            _progressWindow = null;

            _timer?.Dispose();
            _timer = null;

            _disposed = true;
        }

        public void SyncRunnerStarted()
        {
            if (_disposed) return;

            ThisAddIn.UiContext.Post(_ =>
                {
                    ResetProgress();
                    _progressWindow?.SetCurrentSyncKind(_currentSyncRunnerKind);
                },
                null);
        }

        public void SyncRunnerFinished()
        {
            // no-op
        }

        public void SyncRunnerProgressStarted(int totalEntitiesBeingLoaded, int chunkCount)
        {
            if (_disposed) return;

            ThisAddIn.UiContext.Post(_ =>
                {
                    _progressValue = 0;
                    _progressMaximum = chunkCount * 3;

                    if (totalEntitiesBeingLoaded >= 25 && _progressWindow == null)
                    {
                        _progressWindow = new Ui.ProgressWindow();
                        _progressWindow.SetCurrentSyncKind(_currentSyncRunnerKind);
                        _progressWindow.Show();
                    }
                    _progressWindow?.SetProgressMaximum(_progressMaximum);
                },
                null);
        }

        public void SyncRunnerChunkPartFinished()
        {
            if (_disposed) return;
            
            ThisAddIn.UiContext.Post(_ =>
                {
                    _progressValue += 1;
                    _progressWindow?.SetProgressValue(_progressValue);
                },
                null);
        }

        private void ResetProgress()
        {
            _progressMaximum = 0;
            _progressValue = 0;

            _progressWindow?.SetProgressMaximum(_progressMaximum);
        }
    }
}
