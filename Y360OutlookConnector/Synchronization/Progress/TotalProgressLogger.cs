using System;

namespace Y360OutlookConnector.Synchronization.Progress
{
    class TotalProgressLogger : GenSync.ProgressReport.ITotalProgressLogger
    {
        private readonly SyncSessionProgress _syncSession;

        public TotalProgressLogger(SyncSessionProgress syncSession)
        {
            _syncSession = syncSession;
            _syncSession?.SyncRunnerStarted();
        }

        public void Dispose()
        {
            _syncSession?.SyncRunnerFinished();
        }

        public void NotifyWork(int totalEntitiesBeingLoaded, int chunkCount)
        {
            _syncSession?.SyncRunnerProgressStarted(totalEntitiesBeingLoaded, chunkCount);
        }

        public GenSync.ProgressReport.IChunkProgressLogger StartChunk()
        {
            return new ChunkProgress(this);
        }

        public void NotifyChunkOneThirdFinished()
        {
            _syncSession?.SyncRunnerChunkPartFinished();
        }


        class ChunkProgress : GenSync.ProgressReport.IChunkProgressLogger, IDisposable
        {
            private readonly TotalProgressLogger _parent;

            public ChunkProgress(TotalProgressLogger totalProgress)
            {
                _parent = totalProgress;
            }

            public IDisposable StartARepositoryLoad(int entityCount)
            {
                // first part of the chunk
                return this;
            }

            public IDisposable StartBRepositoryLoad(int entityCount)
            {
                // second part of the chunk
                return this;
            }

            public GenSync.ProgressReport.IProgressLogger StartProcessing(int jobCount)
            {
                // third part of the chunk
                return new JobProgress(this);
            }

            public void Dispose()
            {
                _parent.NotifyChunkOneThirdFinished();
            }

            public void NotifyJobsFinished()
            {
                _parent.NotifyChunkOneThirdFinished();
            }
        }


        class JobProgress : GenSync.ProgressReport.IProgressLogger
        {
            private readonly ChunkProgress _parent;

            public JobProgress(ChunkProgress chunkProgress)
            {
                _parent = chunkProgress;
            }

            public void Dispose()
            {
                _parent.NotifyJobsFinished();
            }

            public void Increase()
            {
            }

            public void IncreaseBy(int value)
            {
            }
        }
    }
}
