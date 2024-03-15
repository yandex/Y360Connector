using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using DDay.iCal;
using GenSync.Synchronization;
using GenSync.Synchronization.StateFactories;
using GenSync.Synchronization.States;
using log4net;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    using IEventSyncStateContext =
        IEntitySyncStateContext<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
            IEventSynchronizationContext>;
    using IEventSyncStateFactory =
        IEntitySyncStateFactory<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
            IEventSynchronizationContext>;

    public class EventSyncInterceptor :
        ISynchronizationInterceptor<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
            IICalendar, IEventSynchronizationContext>,
        ISynchronizationStateVisitor<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
            IICalendar, IEventSynchronizationContext>
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private Dictionary<string, ContextWithDeleteInB> _deletesInB;
        private Dictionary<string, ContextWithCreateInB> _createsInB;

        private readonly InvitesInfoStorage _invitesInfo;

        public EventSyncInterceptor(InvitesInfoStorage invitesInfo)
        {
            _invitesInfo = invitesInfo ?? throw new ArgumentNullException(nameof(invitesInfo));
        }

        public void TransformInitialCreatedStates(IReadOnlyList<IEventSyncStateContext> syncStateContexts,
            IEventSyncStateFactory stateFactory)
        {
            _deletesInB = new Dictionary<string, ContextWithDeleteInB>();
            _createsInB = new Dictionary<string, ContextWithCreateInB>();

            foreach (var state in syncStateContexts)
                state.Accept(this);

            var alreadyDeleted = new List<string>();
            foreach (var kvpCreate in _createsInB)
            {
                if (_invitesInfo.FindMarkedForDeletion(kvpCreate.Key))
                {
                    kvpCreate.Value.Context.SetState(stateFactory.Create_DeleteInA(
                        new OutlookEventRelationData
                        {
                            AtypeId = kvpCreate.Value.State.AId,
                            AtypeVersion = kvpCreate.Value.State.AVersion,
                            BtypeId = new WebResourceName(""),
                            BtypeVersion = String.Empty
                        },
                        kvpCreate.Value.State.AVersion));

                    s_logger.Info($"Removing from Outlook already deleted event (id: {kvpCreate.Value.State.AId})");
                    alreadyDeleted.Add(kvpCreate.Key);
                }
            }
            alreadyDeleted.ForEach(x => _createsInB.Remove(x));

            foreach (var kvpDelete in _deletesInB)
            {
                if (_createsInB.TryGetValue(kvpDelete.Key, out var create))
                {
                    s_logger.Info($"Converting deletion of " +
                                  $"'{kvpDelete.Value.State.KnownData.BtypeId.OriginalAbsolutePath}' " +
                                  $"and creation of new from '{create.State.AId}' into an update.");

                    kvpDelete.Value.Context.SetState(stateFactory.Create_Discard());

                    create.Context.SetState(stateFactory.Create_UpdateAtoB(
                        new OutlookEventRelationData
                        {
                            AtypeId = create.State.AId,
                            AtypeVersion = create.State.AVersion,
                            BtypeId = kvpDelete.Value.State.KnownData.BtypeId,
                            BtypeVersion = kvpDelete.Value.State.KnownData.BtypeVersion
                        },
                        create.State.AVersion,
                        kvpDelete.Value.State.KnownData.BtypeVersion));
                }
            }

            _deletesInB = null;
            _createsInB = null;
        }

        public void Dispose()
        {
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            RestoreInA<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state)
        {
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            UpdateBToA<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state)
        {
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            UpdateAToB<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state)
        {
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            RestoreInB<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state)
        {
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            DeleteInBWithNoRetry<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state)
        {
        }

        public void Visit(IEventSyncStateContext context,
            DeleteInB<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state)
        {
            var fileName = state.KnownData.BtypeId.GetServerFileName();
            var uid = Path.GetFileNameWithoutExtension(fileName);
            if (!String.IsNullOrEmpty(uid) && _deletesInB != null)
                _deletesInB[uid] = new ContextWithDeleteInB(context, state);
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            DeleteInAWithNoRetry<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state)
        {
        }

        public void Visit(IEventSyncStateContext context,
            DeleteInA<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state)
        {
        }

        public void Visit(IEventSyncStateContext context,
            CreateInB<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state)
        {
            var uid = AppointmentItemUtils.ExtractUidFromGlobalId(state.AId.GlobalAppointmentId);
            if (!String.IsNullOrEmpty(uid))
                _createsInB[uid] = new ContextWithCreateInB(context, state);
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            CreateInA<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state)
        {
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            DoNothing<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> doNothing)
        {
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            Discard<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> discard)
        {
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            UpdateFromNewerToOlder<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
                IICalendar, IEventSynchronizationContext> updateFromNewerToOlder)
        {
        }

        struct ContextWithDeleteInB
        {
            public readonly IEventSyncStateContext Context;

            public readonly DeleteInB<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
                IICalendar, IEventSynchronizationContext> State;

            public ContextWithDeleteInB(IEventSyncStateContext context,
                DeleteInB<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                    IEventSynchronizationContext> state)
            {
                Context = context ?? throw new ArgumentNullException(nameof(context));
                State = state ?? throw new ArgumentNullException(nameof(state));
            }
        }

        struct ContextWithCreateInB
        {
            public readonly IEventSyncStateContext Context;

            public readonly CreateInB<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
                IICalendar, IEventSynchronizationContext> State;

            public ContextWithCreateInB(IEventSyncStateContext context,
                CreateInB<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                    IEventSynchronizationContext> state)
            {
                Context = context ?? throw new ArgumentNullException(nameof(context));
                State = state ?? throw new ArgumentNullException(nameof(state));
            }
        }
    }

    public class EventSyncInterceptorFactory :
        ISynchronizationInterceptorFactory<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
            IICalendar, IEventSynchronizationContext>
    {
        private readonly InvitesInfoStorage _invitesInfo;

        public EventSyncInterceptorFactory(InvitesInfoStorage invitesInfo)
        {
            _invitesInfo = invitesInfo ?? throw new ArgumentNullException(nameof(invitesInfo));
        }

        public ISynchronizationInterceptor<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
            IICalendar, IEventSynchronizationContext> Create()
        {
            return new EventSyncInterceptor(_invitesInfo);
        }
    }
}
