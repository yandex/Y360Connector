using System;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using DDay.iCal;
using GenSync.EntityRelationManagement;
using GenSync.Synchronization.StateCreationStrategies;
using GenSync.Synchronization.StateFactories;
using GenSync.Synchronization.States;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    using IEventSyncState = IEntitySyncState<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName,
        string, IICalendar, IEventSynchronizationContext>;

    using IEventSyncStateFactory = IEntitySyncStateFactory<AppointmentId, DateTime, IAppointmentItemWrapper, 
        WebResourceName, string, IICalendar, IEventSynchronizationContext>;

    using IEventRelationData = IEntityRelationData<AppointmentId, DateTime, WebResourceName, string>;

    public class EventSyncStrategyServerToOutlook
        : IInitialSyncStateCreationStrategy<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, 
            IICalendar, IEventSynchronizationContext>
    {
        private readonly IEventSyncStateFactory _factory;

        public EventSyncStrategyServerToOutlook(IEventSyncStateFactory factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public IEventSyncState CreateFor_Added_NotExisting(AppointmentId aId, DateTime newA)
        {
            return _factory.Create_DeleteInAWithNoRetry(aId, newA);
        }

        public IEventSyncState CreateFor_Changed_Changed(IEventRelationData knownData, DateTime newA, string newB)
        {
            return _factory.Create_UpdateBtoA(knownData, newB, newA);
        }

        public IEventSyncState CreateFor_Changed_Deleted(IEventRelationData knownData, DateTime newA)
        {
            return _factory.Create_DeleteInA(knownData, newA);
        }

        public IEventSyncState CreateFor_Changed_Unchanged(IEventRelationData knownData, DateTime newA)
        {
            return _factory.Create_RestoreInA(knownData, newA);
        }

        public IEventSyncState CreateFor_Deleted_Changed(IEventRelationData knownData, string newB)
        {
            return _factory.Create_CreateInA(knownData.BtypeId, newB);
        }

        public IEventSyncState CreateFor_Deleted_Deleted(IEventRelationData knownData)
        {
            return _factory.Create_Discard();
        }

        public IEventSyncState CreateFor_Deleted_Unchanged(IEventRelationData knownData)
        {
            return _factory.Create_CreateInA(knownData.BtypeId, knownData.BtypeVersion);
        }

        public IEventSyncState CreateFor_NotExisting_Added(WebResourceName bId, string newB)
        {
            return _factory.Create_CreateInA(bId, newB);
        }

        public IEventSyncState CreateFor_Unchanged_Changed(IEventRelationData knownData, string newB)
        {
            return _factory.Create_UpdateBtoA(knownData, newB, knownData.AtypeVersion);
        }

        public IEventSyncState CreateFor_Unchanged_Deleted(IEventRelationData knownData)
        {
            return _factory.Create_DeleteInA(knownData, knownData.AtypeVersion);
        }

        public IEventSyncState CreateFor_Unchanged_Unchanged(IEventRelationData knownData)
        {
            return _factory.Create_DoNothing(knownData);
        }
    }
}
