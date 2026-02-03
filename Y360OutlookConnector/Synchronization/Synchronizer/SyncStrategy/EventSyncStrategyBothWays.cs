using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using CalDavSynchronizer;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using DDay.iCal;
using GenSync.EntityRelationManagement;
using GenSync.Logging;
using GenSync.Synchronization.StateCreationStrategies;
using GenSync.Synchronization.StateFactories;
using GenSync.Synchronization.States;
using log4net;
using Y360OutlookConnector.Synchronization.Synchronizer.States;
using Y360OutlookConnector.Utilities;
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
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        private static readonly ConcurrentDictionary<string, Outlook.OlResponseStatus> s_lastResponseByUid
            = new ConcurrentDictionary<string, Outlook.OlResponseStatus>(StringComparer.OrdinalIgnoreCase);

        private readonly IEventSyncStateFactory _factory;
        private readonly InvitesInfoStorage _invitesInfoStorage;
        private readonly IOutlookSession _outlookSession;
        private readonly OutlookEventRepositoryWrapper _outlookRepository;
        private readonly string _outlookEmailAddress;

        public EventSyncStrategyBothWays(IEventSyncStateFactory factory, InvitesInfoStorage incomingInvites,
            IOutlookSession outlookSession, OutlookEventRepositoryWrapper outlookRepository, string outlookEmailAddress)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _invitesInfoStorage = incomingInvites ?? throw new ArgumentNullException(nameof(incomingInvites));
            _outlookSession = outlookSession ?? throw new ArgumentNullException(nameof(outlookSession));
            _outlookRepository = outlookRepository ?? throw new ArgumentNullException(nameof(outlookRepository));
            _outlookEmailAddress = outlookEmailAddress ?? throw new ArgumentNullException(nameof(outlookEmailAddress));
        }

        public IEventSyncState CreateFor_Added_NotExisting(AppointmentId aId, DateTime newA)
        {
            Outlook.AppointmentItem appointment = null;

            try
            {
                appointment = _outlookSession.GetAppointmentItem(aId.EntryId);

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

                if (!String.IsNullOrEmpty(uid) &&
                    !String.Equals(uid, globalAppointmentId, StringComparison.OrdinalIgnoreCase))
                {
                    s_logger.Info($"Capturing nee=w A entity for UID-based update: {aId}");
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
            catch (Exception ex)
            {
                s_logger.Warn($"Failed to check appointment properties for {aId}", ex);
                return new Discard<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar, IEventSynchronizationContext>();
            }
            finally
            {
                if (appointment != null)
                {
                    Marshal.FinalReleaseComObject(appointment);
                }
            }
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

            if (!string.IsNullOrEmpty(decodedUid) && AppointmentItemUtils.IsGlobalAppointmentId(decodedUid))
            {
                var extractedUid = AppointmentItemUtils.ExtractUidFromGlobalId(decodedUid);

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

                if (AppointmentItemUtils.IsGlobalAppointmentId(decodedUid))
                {
                    s_logger.Debug($"Skipping deletion of server event - filename is GlobalAppointmentID format (likely recreated invite): {decodedUid}");
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
    }
}
