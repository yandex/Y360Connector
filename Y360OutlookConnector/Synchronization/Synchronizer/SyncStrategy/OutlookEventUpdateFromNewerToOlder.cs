using System;
using System.Linq;
using System.Reflection;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using DDay.iCal;
using GenSync.EntityRelationManagement;
using GenSync.Synchronization;
using GenSync.Synchronization.States;
using log4net;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnector.Synchronization.Synchronizer.SyncStrategy
{
    internal class OutlookEventUpdateFromNewerToOlder
        : UpdateFromNewerToOlder<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar, IEventSynchronizationContext>
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        private readonly DateTime _newA;
        private readonly string _newB;

        public OutlookEventUpdateFromNewerToOlder(
            EntitySyncStateEnvironment<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar, IEventSynchronizationContext> environment,
            IEntityRelationData<AppointmentId, DateTime, WebResourceName, string> knownData,
            DateTime newA,
            string newB)
            : base(environment, knownData, newA, newB)
        {
            _newA = newA;
            _newB = newB;
        }

        protected override DateTime ModificationTimeA
        {
            get
            {
                var lastChangeTime = AppointmentItemUtils.GetLastChangeTime(_aEntity.Inner);
                var lastChangeTimeUtc = ToUtcSafe(lastChangeTime);

                s_logger.Info(
                    $"Conflict resolution - Outlook modification time for '{KnownData.AtypeId}': LastModificationTime={_aEntity.Inner.LastModificationTime:o} " +
                    $"(Kind={_aEntity.Inner.LastModificationTime.Kind}), GetLastChangeTime={lastChangeTime:o} (Kind= {lastChangeTime.Kind})," +
                    $" GetLastChangeTimeUtc={lastChangeTimeUtc:o}");

                return lastChangeTimeUtc;
            }
        }

        protected override DateTime? ModificationTimeB
        {
            get
            {
                var lastModified = _bEntity.Events.FirstOrDefault()?.LastModified;
                DateTime? lastModifiedUtc = null;

                if (lastModified != null)
                {
                    var value = lastModified.Value;
                    lastModifiedUtc = ToUtcSafe(value);
                    s_logger.Info(
                        $"Conflict resolution - Server modification time for '{KnownData.BtypeId.OriginalAbsolutePath}': " +
                        $"LAST-MODIFIED={value:o} (Kind={value.Kind}), " +
                        $"LAST-MODIFIED UTC={lastModifiedUtc:o}");
                }
                else
                {
                    s_logger.Warn(
                        $"Conflict resolution - Server modification time for '{KnownData.BtypeId.OriginalAbsolutePath}': " +
                        "LAST-MODIFIED is NULL (event may be new or server doesn't provide this property)");
                }

                return lastModifiedUtc;
            }
        }

        public override IEntitySyncState<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar, IEventSynchronizationContext> Resolve()
        {
            var modificationTimeA = ModificationTimeA;
            var modificationTimeB = ModificationTimeB;

            s_logger.Info(
                $"Conflict resolution for event '{KnownData.AtypeId}' (Outlook) <-> '{KnownData.BtypeId.OriginalAbsolutePath}' (Server): " +
                $"Outlook modified at {modificationTimeA:o}, Server modified at {(modificationTimeB?.ToString("o") ?? "NULL")}");

            if (modificationTimeB == null)
            {
                s_logger.Info(
                    $"Conflict resolution result: Server Last-Modified missing, prefer Outlook: {modificationTimeA:o}");
                return _environment.StateFactory.Create_UpdateAtoB(KnownData, _newA, _newB);
            }

            if (modificationTimeA >= modificationTimeB)
            {
                var timeDifference = modificationTimeA - modificationTimeB.Value;
                s_logger.Info(
                    $"Conflict resolution result: Using Outlook version (newer by {timeDifference.TotalSeconds:F1} seconds). " +
                    $"Outlook: {modificationTimeA:o}, Server: {modificationTimeB.Value:o}");
                return _environment.StateFactory.Create_UpdateAtoB(KnownData, _newA, _newB);
            }

            var diff = modificationTimeB.Value - modificationTimeA;

            s_logger.Info(
                $"Conflict resolution result: Using Server version (newer by {diff.TotalSeconds:F1} seconds). " +
                $"Server: {modificationTimeB.Value:o}, Outlook: {modificationTimeA:o}");

            return _environment.StateFactory.Create_UpdateBtoA(KnownData, _newB, _newA);
        }

        private static DateTime ToUtcSafe(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
            }

            return dateTime.ToUniversalTime();
        }
    }
}
