using System;
using System.Collections.Concurrent;
using System.Reflection;
using log4net;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    /// In-memory store that tracks which calendar event UIDs need a deferred Respond() call
    /// after a UID-based item conversion in the sync interceptor.
    ///
    /// Replaces the previous approach of storing the marker as a MAPI property with Save(),
    /// which caused Outlook to recreate the appointment item with a new EntryID on every sync cycle,
    /// creating an infinite sync loop.
    public class DeferredRespondStorage
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private readonly TimeSpan _ttl;
        private readonly ConcurrentDictionary<string, DateTime> _pendingByUid =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public DeferredRespondStorage(TimeSpan ttl)
        {
            _ttl = ttl;
        }

        /// Marks the UID as pending a deferred Respond(). If already marked, the existing
        /// timestamp is preserved (TTL is not renewed) to prevent indefinite forced B->A updates.
        public void MarkPending(string uid)
        {
            if (string.IsNullOrEmpty(uid))
            {
                return;
            }

            if (_pendingByUid.TryAdd(uid, DateTime.UtcNow))
            {
                s_logger.Debug($"Deferred respond marked pending for UID '{uid}'.");
            }
            else
            {
                s_logger.Debug($"Deferred respond already pending for UID '{uid}', preserving original timestamp.");
            }
        }

        /// Returns true if the UID has a pending deferred respond that has not yet expired.
        /// Expired entries are removed lazily on first check.
        public bool IsPending(string uid)
        {
            return IsPending(uid, out _);
        }

        /// Returns true if the UID has a pending deferred respond that has not yet expired.
        public bool IsPending(string uid, out DateTime markedAt)
        {
            markedAt = default(DateTime);

            if (string.IsNullOrEmpty(uid))
            {
                return false;
            }

            if (!_pendingByUid.TryGetValue(uid, out markedAt))
            {
                return false;
            }

            if (DateTime.UtcNow - markedAt > _ttl)
            {
                _pendingByUid.TryRemove(uid, out var _);
                s_logger.Debug($"Deferred respond marker expired for UID '{uid}'.");
                markedAt = default(DateTime);
                return false;
            }

            return true;
        }

        /// Clears the pending deferred respond marker for the UID after Respond() has been applied.
        public void ClearPending(string uid)
        {
            if (string.IsNullOrEmpty(uid))
                return;

            if (_pendingByUid.TryRemove(uid, out var _))
                s_logger.Debug($"Deferred respond marker cleared for UID '{uid}'.");
        }
    }
}
