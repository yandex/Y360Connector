using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CalDavSynchronizer;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using DDay.iCal;
using GenSync.EntityRelationManagement;
using GenSync.Logging;
using GenSync.Synchronization;
using GenSync.Synchronization.StateCreationStrategies;
using GenSync.Synchronization.StateFactories;
using GenSync.Synchronization.States;
using log4net;
using Y360OutlookConnector.Synchronization.Synchronizer.States;
using Y360OutlookConnector.Utilities;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization.Synchronizer.SyncStrategy
{
    using IEventRelationData = IEntityRelationData<AppointmentId, DateTime, WebResourceName, string>;
    using IEventSyncState = IEntitySyncState<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName,
        string, IICalendar, IEventSynchronizationContext>;
    using IEventSyncStateFactory = IEntitySyncStateFactory<AppointmentId, DateTime, IAppointmentItemWrapper, 
        WebResourceName, string, IICalendar, IEventSynchronizationContext>;

    public class EventSyncStrategyBothWays
        : IInitialSyncStateCreationStrategy<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
            IICalendar, IEventSynchronizationContext>
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        private static readonly ConcurrentDictionary<string, Outlook.OlResponseStatus> s_lastResponseByUid
            = new ConcurrentDictionary<string, Outlook.OlResponseStatus>(StringComparer.OrdinalIgnoreCase);

        private readonly IEventSyncStateFactory _factory;
        private readonly EntitySyncStateEnvironment<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar, IEventSynchronizationContext> _environment;
        private readonly InvitesInfoStorage _invitesInfoStorage;
        private readonly IOutlookSession _outlookSession;
        private readonly OutlookEventRepositoryWrapper _outlookRepository;
        private readonly string _outlookEmailAddress;
        private readonly DeferredRespondStorage _deferredRespondStorage;
        private readonly CalDavRepository<IEventSynchronizationContext> _calDavRepository;

        public EventSyncStrategyBothWays(IEventSyncStateFactory factory,
            EntitySyncStateEnvironment<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar, IEventSynchronizationContext> environment,
            InvitesInfoStorage incomingInvites, IOutlookSession outlookSession, OutlookEventRepositoryWrapper outlookRepository, string outlookEmailAddress, DeferredRespondStorage deferredRespondStorage, CalDavRepository<IEventSynchronizationContext> calDavRepository)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _invitesInfoStorage = incomingInvites ?? throw new ArgumentNullException(nameof(incomingInvites));
            _outlookSession = outlookSession ?? throw new ArgumentNullException(nameof(outlookSession));
            _outlookRepository = outlookRepository ?? throw new ArgumentNullException(nameof(outlookRepository));
            _outlookEmailAddress = outlookEmailAddress ?? throw new ArgumentNullException(nameof(outlookEmailAddress));
            _deferredRespondStorage = deferredRespondStorage ?? throw new ArgumentNullException(nameof(deferredRespondStorage));
            _calDavRepository = calDavRepository ?? throw new ArgumentNullException(nameof(calDavRepository));
        }

        public IEventSyncState CreateFor_Added_NotExisting(AppointmentId aId, DateTime newA)
        {
            try
            {
                using (var appointmentWrapper = GenericComObjectWrapper.Create(_outlookSession.GetAppointmentItem(aId.EntryId)))
                {
                    var appointment = appointmentWrapper.Inner;
                    var organizerEmail = appointment.GetOrganizerEmailAddress(NullEntitySynchronizationLogger.Instance);
                    bool isOrganizer = !string.IsNullOrEmpty(organizerEmail) && EmailAddress.AreSame(organizerEmail, _outlookEmailAddress);

                    if (isOrganizer)
                    {
                        if (_factory is EntitySyncStateFactory<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName,
                            string, IICalendar, IEventSynchronizationContext> actualFactory)
                        {
                            return new CreateInBWith404Fallback(_outlookRepository, actualFactory.Environment, aId, newA, _outlookSession, _outlookEmailAddress);
                        }

                        return _factory.Create_CreateInB(aId, newA);
                    }

                    string globalAppointmentId = null;
                    string uid = null;
                    bool isIncomingInvite = false;
                    try
                    {
                        globalAppointmentId = appointment.GlobalAppointmentID;
                        if (!string.IsNullOrEmpty(globalAppointmentId))
                        {
                            isIncomingInvite = _invitesInfoStorage.IsIncomingInvite(globalAppointmentId);
                            uid = AppointmentItemUtils.ExtractUidFromGlobalId(globalAppointmentId);
                        }
                    }
                    catch (Exception ex)
                    {
                        s_logger.ErrorFormat($"Failed to get GlobalAppointmentID for appointment id {aId}. Exception: {ex}");
                    }

                    try
                    {
                        var lastChangeTime = AppointmentItemUtils.GetLastChangeTime(appointment);
                        s_logger.Debug($"Appointment diagnostics: A={aId}, ResponseStatus={appointment.ResponseStatus}, " +
                            $"MeetingStatus={appointment.MeetingStatus}, " +
                            $"LastModificationTime={appointment.LastModificationTime:o}, " +
                            $"LastChangeTime={lastChangeTime:o}, " +
                            $"GlobalAppointmentId={globalAppointmentId ?? "null"}");
                        LogResponseStatusTransition(aId, appointment, uid, lastChangeTime);
                    }
                    catch (Exception ex)
                    {
                        s_logger.Warn($"Failed to read appointment diagnostics for {aId}", ex);
                    }

                    if (!String.IsNullOrEmpty(uid))
                    {
                        var shouldPreferServerState = ShouldPreferServerState(appointment);
                        if (shouldPreferServerState)
                        {
                            s_logger.Info($"Capturing ambiguous A entity for UID-based handling'{aId}'" +
                                $"(ResponseStatus={appointment.ResponseStatus}, MeetingStatus={appointment.MeetingStatus}).");
                        }

                        if (!shouldPreferServerState)
                        {
                            if (!String.Equals(uid, globalAppointmentId, StringComparison.OrdinalIgnoreCase))
                            {
                                s_logger.Info($"Capturing new A entity for UID-based update: {aId}");
                            }
                            else 
                            {
                                s_logger.Info($"Capturing new A entity for GlobalAppointmentId-based fallback: {aId}");
                            }
                            
                        }

                        return new UpdateByUidCandidate(aId, newA, uid);
                    }

                    if (isIncomingInvite)
                    {
                        s_logger.Info($"Skipping creation in user's calendar from an invitation (not an organizer): {aId}");
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(organizerEmail) && !String.IsNullOrEmpty(globalAppointmentId))
                        {
                            _invitesInfoStorage.AddIncomingInvite(globalAppointmentId, newA);
                            s_logger.Info($"Added incoming invite to the storage (not an organizer): {aId}");
                        }
                        s_logger.Info($"Skipping creation in user's calendar (not an organizer): {aId}");
                    }

                    return new Discard<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar, IEventSynchronizationContext>();
                }
            }
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to check appointment properties for {aId}", ex);
                return new Discard<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar, IEventSynchronizationContext>();
            }
        }

        public IEventSyncState CreateFor_Changed_Changed(IEventRelationData knownData, DateTime newA, string newB)
        {
            //XXX Причина дублей/потери опций: после recreate Outlook может остаться в промежуточном состоянии, даже если сервер уже корректный.
            //XXX Исправление в стратегии: при active defer-marker в спорных состояниях форсим B->A, чтобы повторно зайти в Map2To1 и дожать Respond().
            //XXX Ключевая идея этой ветки: если у встречи активен defer-marker, приоритет всегда у B->A.
            //XXX Это нужно, чтобы Outlook гарантированно дошел до корректного "финального" состояния встречи
            //XXX после restart/recreate, а не пытался раньше времени отправлять локальное состояние на сервер.
            //XXX Outlook после restart может пересоздать meeting item (новый EntryID), и мы тогда не всегда успеваем корректно
            //XXX довести состояние кнопок/визуала. Если на item стоит defer-marker, это значит "Respond() еще нужно дожать".
            //XXX Поэтому в этом состоянии принудительно выбираем B->A, чтобы снова пройти через маппинг и завершить Respond().
            if (HasPendingDeferredRespond(knownData.AtypeId))
            {
                s_logger.Debug($"Forcing UpdateBtoA for deferred respond marker in Changed/Changed: A={knownData.AtypeId}, B={knownData.BtypeId.OriginalAbsolutePath}");
                return _factory.Create_UpdateBtoA(knownData, newB, newA);
            }

            //XXX Анти-даунгрейд статуса участия:
            //XXX ambiguous-состояние = received/receivedAndCanceled + ResponseStatus(None|NotResponded).
            //XXX Такое состояние Outlook не считаем явным действием пользователя (часто это артефакт локального состояния),
            //XXX поэтому не даем Outlook понизить серверный статус и выбираем сервер как источник истины (B->A).
            if (ShouldPreferServerState(knownData.AtypeId))
            {
                s_logger.Info($"Conflict resolution override: preferring server version for '{knownData.AtypeId}' due to ambiguous Outlook response status.");
                return _factory.Create_UpdateBtoA(knownData, newB, newA);
            }

            s_logger.Info($"Conflict detected: both Outlook and server have changed event '{knownData.AtypeId}' (Outlook version: {newA:o}, Server version: {newB}). " +
                $"Using time-based conflict resolution");
            return new OutlookEventUpdateFromNewerToOlder(_environment, knownData, newA, newB);
        }

        public IEventSyncState CreateFor_Changed_Deleted(IEventRelationData knownData, DateTime newA)
        {
            LogDeleteInAIdentifiers(knownData, "Btype deleted, A changed");
            return _factory.Create_DeleteInA(knownData, newA);
        }

        public IEventSyncState CreateFor_Changed_Unchanged(IEventRelationData knownData, DateTime newA)
        {
            //XXX Это такой же сценарий "item пересоздан, но сервер не менялся": без переопределения сюда легко попадает A->B.
            //XXX Нам это не подходит, пока defer-marker активен, потому что нужно не отправлять данные в сервер, а
            //XXX повторно применить серверное состояние в Outlook и завершить отложенный Respond().
            if (HasPendingDeferredRespond(knownData.AtypeId))
            {
                s_logger.Debug($"Forcing UpdateBtoA for deferred respond marker in Changed/Changed: A={knownData.AtypeId}, B={knownData.BtypeId.OriginalAbsolutePath}");
                return _factory.Create_UpdateBtoA(knownData, knownData.BtypeVersion, knownData.AtypeVersion);
            }

            var globalAppointmentId = GetActualGlobalAppointmentId(knownData.AtypeId);
            bool isInvite = _invitesInfoStorage.FindAndSetAppointmentItemOverriden(globalAppointmentId, newA);

            //XXX Анти-даунгрейд в changed/unchanged:
            //XXX если Outlook выглядит как "не ответил", но критерии ambiguous выполнены, это ненадежный сигнал.
            //XXX Без этой ветки Коннектор может отправить A->B и откатить на сервере ACCEPTED/TENTATIVE/DECLINED в NEEDS-ACTION.
            //XXX Поэтому для ambiguous мы блокируем A->B и подтягиваем серверное состояние в Outlook (B->A).
            if (!isInvite && ShouldPreferServerState(knownData.AtypeId))
            {
                s_logger.Info($"Changed/Unchanged override: updating B->A for '{knownData.AtypeId}' due to ambiguous Outlook response status.");
                return _factory.Create_UpdateBtoA(knownData, knownData.BtypeVersion, knownData.AtypeVersion);
            }

            if (!isInvite)
            {
                try
                {
                    using (var appointmentWrapper = GenericComObjectWrapper.Create(_outlookSession.GetAppointmentItem(knownData.AtypeId.EntryId)))
                    {
                        var appt = appointmentWrapper.Inner;
                        var (lastChangeTime, lastChangeSource) = AppointmentItemUtils.GetLastChangeTimeWithSource(appt);
                        s_logger.Info($"Changed/Unchanged → UpdateAtoB diagnostics: " +
                            $"A={knownData.AtypeId}, " +
                            $"LastModificationTime={appt.LastModificationTime:o}, " +
                            $"LastChangeTime={lastChangeTime:o}, " +
                            $"LastChangeSource={lastChangeSource}, " +
                            $"MeetingStatus={appt.MeetingStatus}, " +
                            $"ResponseStatus={appt.ResponseStatus}");

                        if (ParticipationStatusHelper.IsIncomingReplyOnlyChange(appt))
                        {
                            if (ShouldPushParticipationStatusToServer(knownData))
                            {
                                s_logger.Info(
                                    $"Changed/Unchanged override: incoming reply-only change, pushing participation to server for '{knownData.AtypeId}'.");
                                return _factory.Create_UpdateAtoB(knownData, newA, knownData.BtypeVersion);
                            }

                            s_logger.Info(
                                $"Changed/Unchanged override: incoming reply-only change without participation diff, updating B->A for '{knownData.AtypeId}'.");
                            return _factory.Create_UpdateBtoA(knownData, knownData.BtypeVersion, knownData.AtypeVersion);
                        }
                    }
                }
                catch (Exception ex)
                {
                    s_logger.Warn($"Changed/Unchanged diagnostics failed for A={knownData.AtypeId}", ex);
                }
            }

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
            s_logger.Debug($"CreateFor_Deleted_Unchanged: A={knownData.AtypeId}, B={knownData.BtypeId.OriginalAbsolutePath}, GlobalAppointmentId={knownData.AtypeId.GlobalAppointmentId ?? "null"}");

            var fileName = knownData.BtypeId.GetServerFileName();
            var uidFromFileName = Path.GetFileNameWithoutExtension(fileName);
            var decodedUid = Uri.UnescapeDataString(uidFromFileName);

            s_logger.Debug($"CreateFor_Deleted_Unchanged: fileName={fileName}, decodedUid={decodedUid}");


            if (!string.IsNullOrEmpty(decodedUid))
            {
                bool isIncomingByUid = _invitesInfoStorage.IsIncomingInviteByUid(decodedUid);
                s_logger.Debug($"CreateFor_Deleted_Unchanged: IsIncomingInviteByUid({decodedUid}) = {isIncomingByUid}");
                if (isIncomingByUid)
                {
                    s_logger.Debug($"Skipping deletion of server event for incoming invite (uid : {decodedUid}, path: {knownData.BtypeId.OriginalAbsolutePath}");
                    return _factory.Create_DoNothing(knownData);
                }
            }

            string extractedUid = null;
            if (!string.IsNullOrEmpty(decodedUid) && AppointmentItemUtils.IsGlobalAppointmentId(decodedUid))
            {
                extractedUid = AppointmentItemUtils.ExtractUidFromGlobalId(decodedUid);

                s_logger.Debug($"CreateFor_Deleted_Unchanged: extractedUId={extractedUid}");

                if (!string.IsNullOrEmpty(extractedUid))
                {
                    bool isIncomingByExtractedUid = _invitesInfoStorage.IsIncomingInviteByUid(extractedUid);
                    s_logger.Debug($"CreateFor_Deleted_Unchanged: IsIncomingInviteByUid({extractedUid})={isIncomingByExtractedUid}");
                    if (isIncomingByExtractedUid)
                    {
                        s_logger.Debug($"Skipping deletion of server event for incoming invite (extracted uid : {extractedUid}, path: {knownData.BtypeId.OriginalAbsolutePath}");
                        return _factory.Create_DoNothing(knownData);
                    }
                }
            }

            if (!string.IsNullOrEmpty(knownData.AtypeId.GlobalAppointmentId))
            {
                bool isIncomingByGlobalId = _invitesInfoStorage.IsIncomingInvite(knownData.AtypeId.GlobalAppointmentId);
                s_logger.Debug($"CreateFor_Deleted_Unchanged: IsIncomingInvite({knownData.AtypeId.GlobalAppointmentId}) = {isIncomingByGlobalId}");
                if (isIncomingByGlobalId)
                {
                    s_logger.Debug($"Skipping deletion of server event for incoming invite (globalAppointmentId : {knownData.AtypeId.GlobalAppointmentId})");
                    return _factory.Create_DoNothing(knownData);
                }

                if (AppointmentItemUtils.IsGlobalAppointmentId(decodedUid) && !String.IsNullOrEmpty(extractedUid) && !String.Equals(extractedUid, decodedUid, StringComparison.OrdinalIgnoreCase))
                {
                    s_logger.Debug($"Skipping deletion of server event - GlobalAppointmentID has embedded external UID: decodedUid={decodedUid}, extractedUid={extractedUid}");
                    return _factory.Create_DoNothing(knownData);
                }
            }
            else
            {
                s_logger.Debug("CreateFor_Deleted_Unchanged: knownData.AtypeId.GlobalAppointmentId is null or empty");
            }

            s_logger.Debug($"Deleting server event (not found in InvitesInfoStorage): A={knownData.AtypeId}, B={knownData.BtypeId.OriginalAbsolutePath}");
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
            LogDeleteInAIdentifiers(knownData, "Btype not in CalDav query");
            return _factory.Create_DeleteInA(knownData, knownData.AtypeVersion);
        }

        public IEventSyncState CreateFor_Unchanged_Unchanged(IEventRelationData knownData)
        {
            //XXX Даже если обе стороны "без изменений", marker означает: встречу нужно еще раз прогнать через Respond().
            //XXX Без этого синк сделает DoNothing, и пользователь увидит, что UI встречи не до конца нормализовался.
            //XXX Поэтому при marker всегда запускаем B->A.
            if (HasPendingDeferredRespond(knownData.AtypeId))
            {
                s_logger.Debug($"Forcing UpdateBtoA for deferred respond marker: A={knownData.AtypeId}, B={knownData.BtypeId.OriginalAbsolutePath}");
                return _factory.Create_UpdateBtoA(knownData, knownData.BtypeVersion, knownData.AtypeVersion);
            }

            //XXX LastModificationTime может совпадать со snapshot, но PARTSTAT на сервере ещё NEEDS-ACTION,
            //XXX а в Outlook пользователь уже ответил (Accepted/Tentative/Declined). В этом случае DoNothing
            //XXX оставляет расхождение навсегда - форсируем A->B.
            if (ShouldPushParticipationStatusToServer(knownData))
            {
                s_logger.Info($"Unchanged/Unchanged override: pushing Outlook participation status to server for '{knownData.AtypeId}'.");
                return _factory.Create_UpdateAtoB(knownData, knownData.AtypeVersion, knownData.BtypeVersion);
            }


            var globalAppointmentId = GetActualGlobalAppointmentId(knownData.AtypeId);
            bool isInvite = _invitesInfoStorage.FindAndSetAppointmentItemOverriden(globalAppointmentId, knownData.AtypeVersion);
            return isInvite
                ? _factory.Create_UpdateBtoA(knownData, knownData.BtypeVersion, knownData.AtypeVersion)
                : _factory.Create_DoNothing(knownData);
        }

        private string GetActualGlobalAppointmentId(AppointmentId appointmentId)
        {
            try
            {
                using (var appointmentWrapper = GenericComObjectWrapper.Create(_outlookSession.GetAppointmentItem(appointmentId.EntryId)))
                {
                    return appointmentWrapper.Inner.GlobalAppointmentID;
                }
            }
            catch (Exception)
            {
                return appointmentId.GlobalAppointmentId;
            }
        }

        private void LogResponseStatusTransition(
            AppointmentId aId,
            Outlook.AppointmentItem appointment,
            string uid,
            DateTime lastChangeTime)
        {
            if (String.IsNullOrEmpty(uid))
            {
                return;
            }

            var current = appointment.ResponseStatus;
            if (s_lastResponseByUid.TryGetValue(uid, out var previous))
            {
                if (previous != current)
                {
                    s_logger.Info($"ResponseStatus transition for UID {uid}: {previous} -> {current}, A={aId}, MeetingStatus={appointment.MeetingStatus}, LastChangeTime={lastChangeTime:o}");
                }
            }
            else
            {
                s_logger.Info($"ResponseStatus snapshot for UID {uid}: {current}, A={aId}, MeetingStatus={appointment.MeetingStatus}, LastChangeTime={lastChangeTime:o}");
            }

            s_lastResponseByUid[uid] = current;
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
                s_logger.Warn($"COM error while evaluating response-status guard for '{appointmentId}'.", ex);
                return false;
            }
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to evaluate response-status guard for '{appointmentId}'.", ex);
                return false;
            }
        }

        //XXX Определение ambiguous-состояния:
        //XXX 1) встреча входящая (MeetingStatus = received или receivedAndCanceled),
        //XXX 2) ResponseStatus = None или NotResponded.
        //XXX В этом сочетании Outlook не дает надежного признака пользовательского ответа,
        //XXX поэтому серверный статус участия защищаем от даунгрейда.
        private static bool ShouldPreferServerState(Outlook.AppointmentItem appointment)
        {
            return appointment != null
                   && (appointment.MeetingStatus == Outlook.OlMeetingStatus.olMeetingReceived
                       || appointment.MeetingStatus == Outlook.OlMeetingStatus.olMeetingReceivedAndCanceled)
                   && (appointment.ResponseStatus == Outlook.OlResponseStatus.olResponseNone
                       || appointment.ResponseStatus == Outlook.OlResponseStatus.olResponseNotResponded);
        }

        //XXX Проверка: нужно ли форсировать B->A для финализации Respond() на этом item.
        //XXX Маркер хранится в DeferredRespondStorage по UID события (не по EntryID),
        //XXX что позволяет находить его даже после пересоздания Outlook item с новым EntryID.
        private bool HasPendingDeferredRespond(AppointmentId appointmentId)
        {
            var globalId = appointmentId?.GlobalAppointmentId;
            if (string.IsNullOrEmpty(globalId))
            {
                return false;
            }

            var uid = AppointmentItemUtils.ExtractUidFromGlobalId(globalId);
            if (string.IsNullOrEmpty(uid))
            {
                uid = globalId;
            }

            return _deferredRespondStorage.IsPending(uid);
        }

        private bool ShouldPushParticipationStatusToServer(IEventRelationData knownData)
        {
            try
            {
                using (var appointmentWrapper = GenericComObjectWrapper.Create(_outlookSession.GetAppointmentItem(knownData.AtypeId.EntryId)))
                {
                    var appointment = appointmentWrapper.Inner;

                    if (!ParticipationStatusHelper.IsIncomingMeeting(appointment)
                        || !ParticipationStatusHelper.HasExplicitOutlookResponse(appointment))
                    {
                        return false;
                    }

                    var organizerEmail = appointment.GetOrganizerEmailAddress(NullEntitySynchronizationLogger.Instance);
                    if (!string.IsNullOrEmpty(organizerEmail)
                        && EmailAddress.AreSame(organizerEmail, _outlookEmailAddress))
                    {
                        return false;
                    }

                    if (HasPendingDeferredRespond(knownData.AtypeId) || ShouldPreferServerState(appointment))
                        return false;

                    var serverCalendar = TryLoadServerCalendar(knownData.BtypeId);
                    if (serverCalendar == null)
                        return false;

                    var serverParticipation = ParticipationStatusHelper.GetOwnParticipationFromCalendar(
                        serverCalendar, _outlookEmailAddress);
                    if (serverParticipation == null)
                        return false;

                    var outlookParticipation = ParticipationStatusHelper.MapOutlookResponseToParticipation(
                        appointment.ResponseStatus);

                    return ParticipationStatusHelper.ParticipationDiffers(outlookParticipation, serverParticipation);
                }
            }
            catch (COMException ex)
            {
                s_logger.Warn($"COM error while evaluating participation-status push for '{knownData.AtypeId}'.", ex);
                return false;
            }
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to evaluate participation-status push for '{knownData.AtypeId}'.", ex);
                return false;
            }
        }

        private IICalendar TryLoadServerCalendar(WebResourceName bTypeId)
        {
            var entities = _calDavRepository
                .Get(new[] { bTypeId }, NullLoadEntityLogger.Instance, null)
                .GetAwaiter()
                .GetResult();

            foreach (var entityWithId in entities)
            {
                return entityWithId.Entity;
            }

            return null;
        }

        private static void LogDeleteInAIdentifiers(IEventRelationData knownData, string reason)
        {
            var calDavUrl = knownData.BtypeId != null ? knownData.BtypeId.OriginalAbsolutePath : null;
            string uidFromFileName = null;
            if (knownData.BtypeId != null)
            {
                var fileName = knownData.BtypeId.GetServerFileName();
                uidFromFileName = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(fileName));
            }

            string uidFromGlobalId = null;
            if (!string.IsNullOrEmpty(knownData.AtypeId.GlobalAppointmentId))
            {
                uidFromGlobalId = AppointmentItemUtils.ExtractUidFromGlobalId(knownData.AtypeId.GlobalAppointmentId);
            }

            s_logger.Info($"DeleteInA ({reason}): calDavUrl={calDavUrl ?? "null"}, " +
                $"uidFromFileName={uidFromFileName ?? "null"}, " +
                $"uidFromGlobalId={uidFromGlobalId ?? "null"}, " +
                $"entryId={knownData.AtypeId.EntryId ?? "null"}");
        }
    }
}
