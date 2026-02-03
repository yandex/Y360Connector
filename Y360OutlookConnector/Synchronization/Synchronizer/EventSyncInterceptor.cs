using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using DDay.iCal;
using GenSync.EntityRelationManagement;
using GenSync.Synchronization;
using GenSync.Synchronization.StateFactories;
using GenSync.Synchronization.States;
using log4net;
using Y360OutlookConnector.Synchronization.Synchronizer.States;
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
        private Dictionary<string, ContextWithUpdateByUidCandidate> _updateByUidCandidates;
        private Dictionary<string, ContextWithDoNothing> _doNothingByUid;

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
            _updateByUidCandidates = new Dictionary<string, ContextWithUpdateByUidCandidate>();
            _doNothingByUid = new Dictionary<string, ContextWithDoNothing>();

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

            var processedDeletes = new HashSet<IEventSyncStateContext>();
            foreach (var kvpDelete in _deletesInB)
            {
                if (!processedDeletes.Add(kvpDelete.Value.Context))
                {
                    continue;
                }

                var knownData = kvpDelete.Value.State.KnownData;
                var fileName = knownData.BtypeId.GetServerFileName();
                var uid = Path.GetFileNameWithoutExtension(fileName);
                var decodedUid = Uri.UnescapeDataString(uid);
                var extractedUid = String.Empty;

                if (!String.IsNullOrEmpty(decodedUid) && AppointmentItemUtils.IsGlobalAppointmentId(decodedUid))
                {
                    extractedUid = AppointmentItemUtils.ExtractUidFromGlobalId(decodedUid);
                }

                var hasCreate = false;
                ContextWithCreateInB create = default(ContextWithCreateInB);
                if (!String.IsNullOrEmpty(decodedUid) && _createsInB.TryGetValue(extractedUid, out var createByDecoded))
                {
                    create = createByDecoded;
                    hasCreate = true;
                }
                else if (!String.IsNullOrEmpty(extractedUid) && _createsInB.TryGetValue(extractedUid, out var createByExtracted))
                {
                    create = createByExtracted;
                    hasCreate = true;
                }

                if (hasCreate)
                {
                    s_logger.Info($"Converting deletion of " +
                                  $"'{knownData.BtypeId.OriginalAbsolutePath}' " +
                                  $"and creation of new from '{create.State.AId}' into an update.");

                    kvpDelete.Value.Context.SetState(stateFactory.Create_Discard());

                    create.Context.SetState(stateFactory.Create_UpdateAtoB(
                        new OutlookEventRelationData
                        {
                            AtypeId = create.State.AId,
                            AtypeVersion = create.State.AVersion,
                            BtypeId = knownData.BtypeId,
                            BtypeVersion = knownData.BtypeVersion
                        },
                        create.State.AVersion,
                        knownData.BtypeVersion));
                }
                else
                {
                    if (TryHandleUpdateByUid(stateFactory, kvpDelete.Value, decodedUid, extractedUid))
                    {
                        continue;
                    }

                    var isIncomingInvite = false;
                    if (!String.IsNullOrEmpty(decodedUid))
                    {
                        isIncomingInvite = _invitesInfo.IsIncomingInviteByUid(decodedUid);
                        if (!isIncomingInvite && !String.IsNullOrEmpty(extractedUid))
                        {
                            isIncomingInvite = _invitesInfo.IsIncomingInviteByUid(extractedUid);
                        }

                        if (!isIncomingInvite && AppointmentItemUtils.IsGlobalAppointmentId(decodedUid))
                        {
                            isIncomingInvite = _invitesInfo.IsIncomingInvite(decodedUid);
                        }
                    }

                    if (isIncomingInvite)
                    {
                        s_logger.Info($"Skipping deletion for incoming invite (uid: {decodedUid}, path: {knownData.BtypeId.OriginalAbsolutePath})");
                        kvpDelete.Value.Context.SetState(stateFactory.Create_DoNothing(knownData));
                    }
                }
            }

            var processedDoNothings = new HashSet<IEventSyncStateContext>();
            foreach (var kvpDoNothing in _doNothingByUid)
            {
                if (!processedDoNothings.Add(kvpDoNothing.Value.Context))
                {
                    continue;
                }

                var knownData = kvpDoNothing.Value.KnownData;
                var fileName = knownData.BtypeId.GetServerFileName();
                var uid = Path.GetFileNameWithoutExtension(fileName);
                var decodedUid = Uri.UnescapeDataString(uid);
                var extractedUid = String.Empty;
                if (!String.IsNullOrEmpty(decodedUid) && AppointmentItemUtils.IsGlobalAppointmentId(decodedUid))
                {
                    extractedUid = AppointmentItemUtils.ExtractUidFromGlobalId(decodedUid);
                }
                if (TryHandleUpdateByUid(stateFactory, kvpDoNothing.Value, decodedUid, extractedUid))
                {
                    continue;
                }
            }

            _deletesInB = null;
            _createsInB = null;
            _updateByUidCandidates = null;
            _doNothingByUid = null;
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
            var decodedUid = Uri.UnescapeDataString(uid);
            if (!String.IsNullOrEmpty(decodedUid) && _deletesInB != null)
            {
                _deletesInB[decodedUid] = new ContextWithDeleteInB(context, state);
                if (AppointmentItemUtils.IsGlobalAppointmentId(decodedUid))
                {
                    var extractedUid = AppointmentItemUtils.ExtractUidFromGlobalId(decodedUid);
                    if (!String.IsNullOrEmpty(extractedUid) && 
                        !String.Equals(extractedUid, decodedUid, StringComparison.OrdinalIgnoreCase) && 
                        !_deletesInB.ContainsKey(extractedUid))
                    {
                        _deletesInB[extractedUid] = new ContextWithDeleteInB(context, state);
                    }
                }
            }
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
            if (_doNothingByUid == null)
            {
                return;
            }

            IEntityRelationData<AppointmentId, DateTime, WebResourceName, string> knownData = null;
            doNothing.AddNewRelationNoThrow(data => knownData = data);
            if (knownData == null)
            {
                return;
            }

            var fileName = knownData.BtypeId.GetServerFileName();
            var uid = Path.GetFileNameWithoutExtension(fileName);
            var decodedUid = Uri.UnescapeDataString(uid);
            if (!String.IsNullOrEmpty(decodedUid))
            {
                _doNothingByUid[decodedUid] = new ContextWithDoNothing(syncStateContext, doNothing, knownData);
                if (AppointmentItemUtils.IsGlobalAppointmentId(decodedUid))
                {
                    var extractedUid = AppointmentItemUtils.ExtractUidFromGlobalId(decodedUid);
                    if (!String.IsNullOrEmpty(extractedUid) &&
                        !String.Equals(extractedUid, decodedUid, StringComparison.OrdinalIgnoreCase) &&
                        !_doNothingByUid.ContainsKey(extractedUid))
                    {
                        _doNothingByUid[extractedUid] = new ContextWithDoNothing(syncStateContext, doNothing, knownData);
                    }
                }
            }
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            Discard<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> discard)
        {
            if (discard is UpdateByUidCandidate candidate &&
                !String.IsNullOrEmpty(candidate.Uid) &&
                _updateByUidCandidates != null)
            {
                _updateByUidCandidates[candidate.Uid] = new ContextWithUpdateByUidCandidate(syncStateContext, candidate);
            }
        }

        public void Visit(IEventSyncStateContext syncStateContext,
            UpdateFromNewerToOlder<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
                IICalendar, IEventSynchronizationContext> updateFromNewerToOlder)
        {
        }

        private bool TryHandleUpdateByUid(
            IEventSyncStateFactory stateFactory,
            ContextWithDeleteInB deleteContext,
            string decodedUid,
            string extractedUid)
        {
            if (!TryGetUpdateByUidCandidate(decodedUid, extractedUid, out var candidate))
            {
                return false;
            }

            var knownData = deleteContext.State.KnownData;
            deleteContext.Context.SetState(stateFactory.Create_Discard());

            candidate.Context.SetState(stateFactory.Create_UpdateAtoB(
                new OutlookEventRelationData
                {
                    AtypeId = candidate.State.AId,
                    AtypeVersion = candidate.State.AVersion,
                    BtypeId = knownData.BtypeId,
                    BtypeVersion = knownData.BtypeVersion
                },
                candidate.State.AVersion,
                knownData.BtypeVersion));

            s_logger.Info($"Converting deletion of '{knownData.BtypeId.OriginalAbsolutePath}' into update by UID for '{candidate.State.AId}'.");
            return true;
        }
        
        private bool TryHandleUpdateByUid(
               IEventSyncStateFactory stateFactory,
               ContextWithDoNothing doNothingContext,
               string decodedUid,
               string extractedUid)
        {
            if (!TryGetUpdateByUidCandidate(decodedUid, extractedUid, out var candidate))
            {
                return false;
            }

            var knownData = doNothingContext.KnownData;
            doNothingContext.Context.SetState(stateFactory.Create_Discard());

            candidate.Context.SetState(stateFactory.Create_UpdateAtoB(
                new OutlookEventRelationData
                {
                    AtypeId = candidate.State.AId,
                    AtypeVersion = candidate.State.AVersion,
                    BtypeId = knownData.BtypeId,
                    BtypeVersion = knownData.BtypeVersion
                },
                candidate.State.AVersion,
                knownData.BtypeVersion));

            s_logger.Info($"Converting do-nothing of '{knownData.BtypeId.OriginalAbsolutePath}' into update by UID for '{candidate.State.AId}'.");
            return true;
        }

        private bool TryGetUpdateByUidCandidate(
            string decodedUid,
            string extractedUid,
            out ContextWithUpdateByUidCandidate candidate)
        {
            candidate = default(ContextWithUpdateByUidCandidate);
            if (_updateByUidCandidates == null)
            {
                return false;
            }

            if (!String.IsNullOrEmpty(decodedUid) && _updateByUidCandidates.TryGetValue(decodedUid, out var byDecoded))
            {
                candidate = byDecoded;
            }
            else if (!String.IsNullOrEmpty(extractedUid) && _updateByUidCandidates.TryGetValue(extractedUid, out var byExtracted))
            {
                candidate = byExtracted;
            }
            else
            {
                return false;
            }
            _updateByUidCandidates.Remove(candidate.State.Uid);
            return true;
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

    struct ContextWithUpdateByUidCandidate
    {
        public readonly IEventSyncStateContext Context;
        public readonly UpdateByUidCandidate State;

        public ContextWithUpdateByUidCandidate(IEventSyncStateContext context, UpdateByUidCandidate state)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            State = state ?? throw new ArgumentNullException(nameof(state));
        }
    }

    struct ContextWithDoNothing
    {
        public readonly IEventSyncStateContext Context;
        public readonly DoNothing<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
            IICalendar, IEventSynchronizationContext> State;
        public readonly IEntityRelationData<AppointmentId, DateTime, WebResourceName, string> KnownData;

        public ContextWithDoNothing(IEventSyncStateContext context,
            DoNothing<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> state,
            IEntityRelationData<AppointmentId, DateTime, WebResourceName, string> knownData)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            State = state ?? throw new ArgumentNullException(nameof(state));
            KnownData = knownData ?? throw new ArgumentNullException(nameof(knownData));
        }
    }
}
