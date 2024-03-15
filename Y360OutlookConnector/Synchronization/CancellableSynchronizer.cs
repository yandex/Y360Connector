using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CalDavSynchronizer.ChangeWatching;
using CalDavSynchronizer.Synchronization;
using GenSync.Logging;

namespace Y360OutlookConnector.Synchronization
{
    public class CancellableSynchronizer : IOutlookSynchronizer, IDisposable
    {
        private readonly IOutlookSynchronizer _innerSynchronizer;
        private readonly CancellationTokenSource _cancelTokenSource;

        public CancellableSynchronizer(IOutlookSynchronizer synchronizer, CancellationTokenSource cancelTokenSource)
        {
            _cancelTokenSource = cancelTokenSource ?? throw new ArgumentNullException(nameof(cancelTokenSource));
            _innerSynchronizer = synchronizer ?? throw new ArgumentNullException(nameof(synchronizer));
        }

        public Task Synchronize(ISynchronizationLogger logger)
        {
            return _innerSynchronizer.Synchronize(logger);
        }

        public Task SynchronizePartial(IEnumerable<IOutlookId> outlookIds, ISynchronizationLogger logger)
        {
            return _innerSynchronizer.SynchronizePartial(outlookIds, logger);
        }

        public void Cancel()
        {
            _cancelTokenSource.Cancel();
        }

        public void Dispose()
        {
            Cancel();
            _cancelTokenSource.Dispose();
        }
    }
}
