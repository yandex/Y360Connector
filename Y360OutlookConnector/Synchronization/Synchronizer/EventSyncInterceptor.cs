using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CalDavSynchronizer;
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
using Outlook = Microsoft.Office.Interop.Outlook;

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
        private readonly IOutlookSession _outlookSession;
        private readonly DeferredRespondStorage _deferredRespondStorage;

        public EventSyncInterceptor(InvitesInfoStorage invitesInfo, IOutlookSession outlookSession, DeferredRespondStorage deferredRespondStorage)
        {
            _invitesInfo = invitesInfo ?? throw new ArgumentNullException(nameof(invitesInfo));
            _outlookSession = outlookSession ?? throw new ArgumentNullException(nameof(outlookSession));
            _deferredRespondStorage = deferredRespondStorage ?? throw new ArgumentNullException(nameof(deferredRespondStorage));
        }

        public void TransformInitialCreatedStates(IReadOnlyList<IEventSyncStateContext> syncStateContexts,
            IEventSyncStateFactory stateFactory)
        {
            //XXX Причина дублей: Outlook может "пересоздавать" встречу (delete+create) с новым EntryID.
            //XXX Исправление в этом файле: сводим такие пары к update-by-UID и переносим marker на новый item, чтобы не терять финализацию.
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

            //XXX Здесь "склеиваем" delete+create в update-by-UID:
            //XXX если Outlook удалил старый item и тут же создал новый, не считаем это реальным удалением события.
            //XXX Вместо этого сохраняем связь с тем же серверным UID и переводим поток в update-ветку.
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
                if (!String.IsNullOrEmpty(decodedUid) && _createsInB.TryGetValue(decodedUid, out var createByDecoded))
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
                    if (TryHandleUpdateByUid(stateFactory, kvpDelete.Value, decodedUid, extractedUid, knownData.AtypeId.GlobalAppointmentId))
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
                if (TryHandleUpdateByUid(stateFactory, kvpDoNothing.Value, decodedUid, extractedUid, knownData.AtypeId.GlobalAppointmentId))
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
            string extractedUid,
            string fallbackUid)
        {
            if (!TryGetUpdateByUidCandidate(decodedUid, extractedUid, fallbackUid, out var candidate))
            {
                return false;
            }

            var knownData = deleteContext.State.KnownData;

            if (TryDeleteStaleGhostIfNeeded(stateFactory, candidate, knownData))
            {
                deleteContext.Context.SetState(stateFactory.Create_DoNothing(knownData));
                return true;
            }

            //XXX Анти-даунгрейд в UID-конвертации (delete -> update):
            //XXX даже если найдена пара по UID, при ambiguous-состоянии нельзя отправлять A->B,
            //XXX иначе UID-путь обойдет основной guard стратегии и откатит серверный статус участия.
            //XXX Поэтому оставляем сервер как источник истины и не выполняем A->B конвертацию.
            if (ShouldPreferServerState(candidate.State.AId))
            {
                deleteContext.Context.SetState(stateFactory.Create_DoNothing(knownData));
                candidate.Context.SetState(stateFactory.Create_DeleteInAWithNoRetry(candidate.State.AId, candidate.State.AVersion));
                s_logger.Info($"Preferring server state and deleting ambiguous local duplicate '{candidate.State.AId}' via UID guard.");
                return true;
            }

            deleteContext.Context.SetState(stateFactory.Create_Discard());
            //XXX Outlook часто делает delete+create (новый EntryID). В этот момент легко потерять точку, где нужно
            //XXX повторно вызвать Respond() для нормализации UI встречи. Поэтому помечаем UID как pending в памяти,
            //XXX чтобы стратегия форсировала B->A и маппер завершил Respond().
            //XXX Ранее здесь вызывался MarkDeferredRespondPending с Save(), что создавало новый EntryID и бесконечный цикл.
            _deferredRespondStorage.MarkPending(candidate.State.Uid);

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
               string extractedUid,
               string fallbackUid)
        {
            if (!TryGetUpdateByUidCandidate(decodedUid, extractedUid, fallbackUid, out var candidate))
            {
                return false;
            }

            var knownData = doNothingContext.KnownData;

            if (TryDeleteStaleGhostIfNeeded(stateFactory, candidate, knownData))
            {
                return true;
            }

            //XXX Анти-даунгрейд в UID-конвертации (do-nothing -> update):
            //XXX логика та же, что и выше: ambiguous-состояние Outlook считаем ненадежным,
            //XXX поэтому не разрешаем этому пути превратиться в A->B и перезаписать серверный PARTSTAT.
            if (ShouldPreferServerState(candidate.State.AId))
            {
                candidate.Context.SetState(stateFactory.Create_DeleteInAWithNoRetry(candidate.State.AId, candidate.State.AVersion));
                s_logger.Info($"Preferring server state and deleting ambiguous local duplicate '{candidate.State.AId}' (do-nothing UID path).");
                return true;
            }

            doNothingContext.Context.SetState(stateFactory.Create_Discard());
            //XXX Та же логика для do-nothing -> update: помечаем UID как pending в памяти,
            //XXX чтобы стратегия форсировала B->A и маппер завершил Respond() без Save()-петли.
            _deferredRespondStorage.MarkPending(candidate.State.Uid);

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
            string fallbackUid,
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
            else if (!String.IsNullOrEmpty(fallbackUid) && _updateByUidCandidates.TryGetValue(fallbackUid, out var byFallback))
            {
                candidate = byFallback;
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

        private bool ShouldPreferServerState(AppointmentId appointmentId)
        {
            try
            {
                using (var appointmentWrapper = GenericComObjectWrapper.Create(_outlookSession.GetAppointmentItem(appointmentId.EntryId)))
                {
                    return ShouldPreferServerState(appointmentWrapper.Inner);
                }
            }
            catch (COMException ex)
            {
                s_logger.Warn($"COM error while evaluating UID-conversion guard for '{appointmentId}'.", ex);
                return false;
            }
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to evaluate UID-conversion guard for '{appointmentId}'.", ex);
                return false;
            }
        }

        //XXX Определение ambiguous-состояния:
        //XXX 1) встреча входящая (MeetingStatus = received или receivedAndCanceled),
        //XXX 2) ResponseStatus = None или NotResponded.
        //XXX Это не считаем надежным пользовательским действием; при таком сочетании
        //XXX не допускаем даунгрейд серверного статуса участия через Outlook->Server.
        private static bool ShouldPreferServerState(Outlook.AppointmentItem appointment)
        {
            return appointment != null
                   && (appointment.MeetingStatus == Outlook.OlMeetingStatus.olMeetingReceived
                       || appointment.MeetingStatus == Outlook.OlMeetingStatus.olMeetingReceivedAndCanceled)
                   && (appointment.ResponseStatus == Outlook.OlResponseStatus.olResponseNone
                       || appointment.ResponseStatus == Outlook.OlResponseStatus.olResponseNotResponded);
        }

        private bool TryDeleteStaleGhostIfNeeded(IEventSyncStateFactory stateFactory, ContextWithUpdateByUidCandidate candidate,
            IEntityRelationData<AppointmentId, DateTime, WebResourceName, string> knownData)
        {
            if (candidate.State.AId == null || knownData?.AtypeId == null
                || String.Equals(candidate.State.AId.EntryId, knownData.AtypeId.EntryId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using (var candidateWrapper = GenericComObjectWrapper.Create(_outlookSession.GetAppointmentItem(candidate.State.AId.EntryId)))
                using (var knownWrapper = GenericComObjectWrapper.Create(_outlookSession.GetAppointmentItem(knownData.AtypeId.EntryId)))
                {
                    var candidateAppointment = candidateWrapper.Inner;
                    var knownAppointment = knownWrapper.Inner;

                    if (!ParticipationStatusHelper.IsIncomingMeeting(candidateAppointment) || !ParticipationStatusHelper.IsIncomingMeeting(knownAppointment))
                    {
                        return false;
                    }

                    var candidateRank = ParticipationStatusHelper.GetOutlookResponseRank(candidateAppointment.ResponseStatus);
                    var knownRank = ParticipationStatusHelper.GetOutlookResponseRank(knownAppointment.ResponseStatus);

                    if (candidateRank >= knownRank || candidate.State.AVersion >= knownData.AtypeVersion)
                    {
                        return false;
                    }

                    candidate.Context.SetState(stateFactory.Create_DeleteInAWithNoRetry(candidate.State.AId, candidate.State.AVersion));
                    s_logger.Info($"Deleting stale ghost appointment '{candidate.State.AId}' " +
                        $"(ResponseStatus={candidateAppointment.ResponseStatus}, AVersion={candidate.State.AVersion:o}) " +
                        $"in favour of '{knownData.AtypeId}' (ResponseStatus={knownAppointment.ResponseStatus}, AVersion={knownData.AtypeVersion:o}), " +
                        $"UID={candidate.State.Uid}.");
                    return true;
                }
            }
            catch (COMException ex)
            {
                s_logger.Warn($"COM error while evaluating stale ghost for UID '{candidate.State.Uid}'.", ex);
                return false;
            }
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to evaluate stale ghost for UID '{candidate.State.Uid}'.", ex);
                return false;
            }
        }

    }

    public class EventSyncInterceptorFactory :
        ISynchronizationInterceptorFactory<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
            IICalendar, IEventSynchronizationContext>
    {
        private readonly InvitesInfoStorage _invitesInfo;
        private readonly IOutlookSession _outlookSession;
        private readonly DeferredRespondStorage _deferredRespondStorage;

        public EventSyncInterceptorFactory(InvitesInfoStorage invitesInfo, IOutlookSession outlookSession, DeferredRespondStorage deferredRespondStorage)
        {
            _invitesInfo = invitesInfo ?? throw new ArgumentNullException(nameof(invitesInfo));
            _outlookSession = outlookSession ?? throw new ArgumentNullException(nameof(_outlookSession));
            _deferredRespondStorage = deferredRespondStorage ?? throw new ArgumentNullException(nameof(deferredRespondStorage));
        }

        public ISynchronizationInterceptor<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
            IICalendar, IEventSynchronizationContext> Create()
        {
            return new EventSyncInterceptor(_invitesInfo, _outlookSession, _deferredRespondStorage);
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
