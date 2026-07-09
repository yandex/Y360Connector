using System.Collections.Concurrent;

namespace Y360OutlookConnector.Utilities
{
    public static class SyncWriteTracker
    {
        private static readonly ConcurrentDictionary<string, byte> s_entryIdsInSyncWrite = new ConcurrentDictionary<string, byte>();

        public static void RegisterSyncWrite(string entryId)
        {
            if (string.IsNullOrEmpty(entryId))
            {
                return;
            }
            s_entryIdsInSyncWrite.TryAdd(entryId, 0);
        }

        public static void UnregisterSyncWrite(string entryId)
        {
            if (string.IsNullOrEmpty(entryId))
            {
                return;
            }
            s_entryIdsInSyncWrite.TryRemove(entryId, out _);
        }

        public static bool IsSyncWrite(string entryId)
        {
            return !string.IsNullOrEmpty(entryId) && s_entryIdsInSyncWrite.ContainsKey(entryId);
        }
    }
}

