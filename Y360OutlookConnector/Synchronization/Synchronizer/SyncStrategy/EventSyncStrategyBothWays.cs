using System;
using System.Runtime.InteropServices;
using CalDavSynchronizer;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using DDay.iCal;
using GenSync.EntityRelationManagement;
using GenSync.Synchronization.StateCreationStrategies;
using GenSync.Synchronization.StateFactories;
using GenSync.Synchronization.States;
using Y360OutlookConnector.Synchronization.Synchronizer.States;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization.Synchronizer.SyncStrategy
{
    using IEventSyncState = IEntitySyncState<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName,
        string, IICalendar, IEventSynchronizationContext>;

    using IEventSyncStateFactory = IEntitySyncStateFactory<AppointmentId, DateTime, IAppointmentItemWrapper, 
        WebResourceName, string, IICalendar, IEventSynchronizationContext>;

    using IEventRelationData = IEntityRelationData<AppointmentId, DateTime, WebResourceName, string>;

    public class EventSyncStrategyBothWays
        : IInitialSyncStateCreationStrategy<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, 
            IICalendar, IEventSynchronizationContext>
    {
        private readonly IEventSyncStateFactory _factory;
        private readonly InvitesInfoStorage _invitesInfoStorage;
        private readonly IOutlookSession _outlookSession;
        private readonly OutlookEventRepositoryWrapper _outlookRepository;

        public EventSyncStrategyBothWays(IEventSyncStateFactory factory, InvitesInfoStorage incomingInvites, 
            IOutlookSession outlookSession, OutlookEventRepositoryWrapper outlookRepository)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _invitesInfoStorage = incomingInvites ?? throw new ArgumentNullException(nameof(incomingInvites));
            _outlookSession = outlookSession ?? throw new ArgumentNullException(nameof(outlookSession));
            _outlookRepository = outlookRepository ?? throw new ArgumentNullException(nameof(outlookRepository));
        }

        public IEventSyncState CreateFor_Added_NotExisting(AppointmentId aId, DateTime newA)
        {
            if (_factory is EntitySyncStateFactory<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName,
                    string, IICalendar, IEventSynchronizationContext> actualFactory)
            {
                return new CreateInBWith404Fallback(_outlookRepository, actualFactory.Environment, aId, newA);
            }

            return _factory.Create_CreateInB(aId, newA);
        }

        public IEventSyncState CreateFor_Changed_Changed(IEventRelationData knownData, DateTime newA, string newB)
        {
            return _factory.Create_UpdateAtoB(knownData, newA, newB);
        }

        public IEventSyncState CreateFor_Changed_Deleted(IEventRelationData knownData, DateTime newA)
        {
            return _factory.Create_DeleteInA(knownData, newA);
        }

        public IEventSyncState CreateFor_Changed_Unchanged(IEventRelationData knownData, DateTime newA)
        {
            var globalAppointmentId = GetActualGlobalAppointmentId(knownData.AtypeId);
            bool isInvite = _invitesInfoStorage.FindAndSetAppointmentItemOverriden(globalAppointmentId, newA);
            return isInvite
                ? _factory.Create_UpdateBtoA(knownData, knownData.BtypeVersion, knownData.AtypeVersion)
                : _factory.Create_UpdateAtoB(knownData, newA, knownData.BtypeVersion);
        }

        public IEventSyncState CreateFor_Deleted_Changed(IEventRelationData knownData, string newB)
        {
            return _factory.Create_DeleteInB(knownData, newB);
        }

        public IEventSyncState CreateFor_Deleted_Deleted(IEventRelationData knownData)
        {
            return _factory.Create_Discard();
        }

        public IEventSyncState CreateFor_Deleted_Unchanged(IEventRelationData knownData)
        {
            return _factory.Create_DeleteInB(knownData, knownData.BtypeVersion);
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
            var globalAppointmentId = GetActualGlobalAppointmentId(knownData.AtypeId);
            bool isInvite = _invitesInfoStorage.FindAndSetAppointmentItemOverriden(globalAppointmentId, knownData.AtypeVersion);
            return isInvite
                ? _factory.Create_UpdateBtoA(knownData, knownData.BtypeVersion, knownData.AtypeVersion) 
                : _factory.Create_DoNothing(knownData);
        }

        private string GetActualGlobalAppointmentId(AppointmentId appointmentId)
        {
            Outlook.AppointmentItem appointment = null;
            try
            {
                appointment = _outlookSession.GetAppointmentItem(appointmentId.EntryId);
                return appointment.GlobalAppointmentID;
            }
            catch (Exception)
            {
                return appointmentId.GlobalAppointmentId;
            }
            finally
            {
                if (appointment != null)
                    Marshal.FinalReleaseComObject(appointment);
            }
        }
    }
}
