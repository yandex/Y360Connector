using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CalDavSynchronizer;
using CalDavSynchronizer.Contracts;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation;
using CalDavSynchronizer.Implementation.Common;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Contacts;
using CalDavSynchronizer.Implementation.Events;
using CalDavSynchronizer.Implementation.Tasks;
using CalDavSynchronizer.Implementation.TimeRangeFiltering;
using CalDavSynchronizer.Implementation.TimeZones;
using CalDavSynchronizer.Synchronization;
using DDay.iCal;
using DDay.iCal.Serialization.iCalendar;
using GenSync.EntityMapping;
using GenSync.EntityRelationManagement;
using GenSync.EntityRepositories;
using GenSync.EntityRepositories.Decorators;
using GenSync.InitialEntityMatching;
using GenSync.Logging;
using GenSync.ProgressReport;
using GenSync.Synchronization;
using GenSync.Synchronization.StateCreationStrategies;
using GenSync.Synchronization.StateFactories;
using GenSync.Utilities;
using NodaTime.TimeZones;
using Thought.vCards;
using TinyCalDavSynchronizer;
using Y360OutlookConnector.Clients;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Synchronization.EntityMappers;
using Y360OutlookConnector.Synchronization.Synchronizer.SyncStrategy;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    public class SynchronizerFactory
    {
        private const int EffectiveChunkSize = 100;

        private readonly ITotalProgressFactory _totalProgressFactory;
        private readonly IOutlookSession _outlookSession;
        private readonly string _profileDataFolder;
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly GlobalTimeZoneCache _globalTimeZoneCache = new GlobalTimeZoneCache();
        private readonly IDaslFilterProvider _daslFilterProvider = new DaslFilterProvider(false);
        private readonly IComWrapperFactory _comWrapperFactory = new ComWrapperFactory();
        private readonly IExceptionHandlingStrategy _exceptionHandlingStrategy = new ExceptionHandlingStrategy();
        private readonly InvitesInfoStorage _invitesInfoStorage;

        private readonly IEqualityComparer<DateTime> _aTypeVersionComparer = Factories.CreateDateTimeEqualityComparer();

        public SynchronizerFactory(IOutlookSession outlookSession, IHttpClientFactory httpClientFactory,
            string profileDataFolder, ITotalProgressFactory totalProgressFactory,
            InvitesInfoStorage invitesInfoStorage)
        {
            _profileDataFolder = profileDataFolder;
            _outlookSession = outlookSession;
            _totalProgressFactory = totalProgressFactory;
            _httpClientFactory = httpClientFactory;
            _outlookSession = outlookSession;
            _invitesInfoStorage = invitesInfoStorage;
        }

        public CancellableSynchronizer CreateSynchronizer(SyncTargetInfo syncTarget, string userEmail,
            string serverUserCommonName)
        {
            var folder = _outlookSession.GetFolderFromId(syncTarget.Config.OutlookFolderEntryId,
                syncTarget.Config.OutlookFolderStoreId);

            if (folder == null)
                return null;

            var storageDataDir = Path.Combine(_profileDataFolder, syncTarget.Config.Id.ToString());
            switch (syncTarget.TargetType)
            {
                case SyncTargetType.Calendar:
                    return CreateEventSynchronizer(new Uri(syncTarget.Config.Url),
                        syncTarget.Config.OutlookFolderEntryId, syncTarget.Config.OutlookFolderStoreId,
                        GetAccountForFolder(folder), userEmail, serverUserCommonName, syncTarget.IsReadOnly,
                        storageDataDir);
                case SyncTargetType.Tasks:
                    return CreateTaskSynchronizer(new Uri(syncTarget.Config.Url),
                        syncTarget.Config.OutlookFolderEntryId, syncTarget.Config.OutlookFolderStoreId,
                        syncTarget.IsReadOnly, storageDataDir);
                case SyncTargetType.Contacts:
                    return CreateContactSynchronizer(new Uri(syncTarget.Config.Url),
                        syncTarget.Config.OutlookFolderEntryId, syncTarget.Config.OutlookFolderStoreId,
                        syncTarget.IsReadOnly, storageDataDir);
                default:
                    throw new NotSupportedException($"Unsupported sync target type {syncTarget.TargetType}");
            }
        }

        private CancellableSynchronizer CreateEventSynchronizer(Uri calendarUri,
            string outlookFolderEntryId, string outlookFolderStoreId,
            string outlookEmailAddress, string serverEmailAddress, string serverUserCommonName,
            bool isReadonly, string storageDataDirectory)
        {
            var mappingConfiguration = new EventMappingConfiguration
            {
                MapReminder = ReminderMapping.JustUpcoming,
                MapSensitivityPrivateToClassConfidential = false,
                MapClassConfidentialToSensitivityPrivate = false,
                MapClassPublicToSensitivityPrivate = false,
                MapSensitivityPublicToDefault = false,
                MapAttendees = true,
                ScheduleAgentClient = true,
                SendNoAppointmentNotifications = false,
                OrganizerAsDelegate = false,
                MapBody = true,
                MapRtfBodyToXAltDesc = false,
                MapXAltDescToRtfBody = false,
                CreateEventsInUTC = false,
                UseIanaTz = true,
                IncludeHistoricalData = false,
                UseGlobalAppointmentID = true,
                IncludeEmptyEventCategoryFilter = false,
                InvertEventCategoryFilter = false,
                CleanupDuplicateEvents = false,
                MapCustomProperties = false,
                EventTz = GetEventTimeZone()
            };

            var cancelTokenSource = new CancellationTokenSource();
            var synchronizer = CreateEventSynchronizer(calendarUri, outlookFolderEntryId,
                outlookFolderStoreId, outlookEmailAddress, serverEmailAddress, serverUserCommonName,
                isReadonly, storageDataDirectory, mappingConfiguration, cancelTokenSource,
                EntityMappers.EventEntityMapper.Create);

            return new CancellableSynchronizer(synchronizer, cancelTokenSource);
        }

        private CancellableSynchronizer CreateTaskSynchronizer(Uri calendarUri,
            string outlookFolderEntryId, string outlookFolderStoreId, bool isReadonly,
            string storageDataDirectory)
        {
            var mappingConfiguration = new TaskMappingConfiguration
            {
                MapReminder = ReminderMapping.JustUpcoming,
                MapReminderAsDateTime = false,
                MapPriority = true,
                MapBody = true,
                MapRecurringTasks = true,
                MapStartAndDueAsFloating = false,
                IncludeEmptyTaskCategoryFilter = false,
                InvertTaskCategoryFilter = false,
                MapCustomProperties = false
            };

            var cancelTokenSource = new CancellationTokenSource();
            var synchronizer = CreateTaskSynchronizer(calendarUri, outlookFolderEntryId, outlookFolderStoreId,
                isReadonly, storageDataDirectory, mappingConfiguration, cancelTokenSource);

            return new CancellableSynchronizer(synchronizer, cancelTokenSource);
        }

        private CancellableSynchronizer CreateContactSynchronizer(Uri serverUri,
            string outlookFolderEntryId, string outlookFolderStoreId, bool isReadonly,
            string storageDataDirectory)
        {
            var mappingConfiguration = new ContactMappingConfiguration
            {
                MapAnniversary = false,
                MapBirthday = false,
                MapContactPhoto = false,
                KeepOutlookFileAs = true,
                FixPhoneNumberFormat = false,
                MapOutlookEmail1ToWork = true
            };

            var entityMapper = new EntityMappers.ContactEntityMapper(mappingConfiguration);

            var cancelTokenSource = new CancellationTokenSource();
            var synchronizer = CreateContactSynchronizer(serverUri, outlookFolderEntryId, outlookFolderStoreId,
                isReadonly, storageDataDirectory, mappingConfiguration, cancelTokenSource, entityMapper);

            return new CancellableSynchronizer(synchronizer, cancelTokenSource);
        }

        private IOutlookSynchronizer CreateContactSynchronizer(Uri serverUrl, string outlookFolderEntryId,
            string outlookFolderStoreId, bool isReadOnly, string storageDataDirectory,
            ContactMappingConfiguration mappingParameters, CancellationTokenSource cancelTokenSource,
            IEntityMapper<IContactItemWrapper, vCard, ICardDavRepositoryLogger> entityMapper)
        {
            var aTypeRepository = new OutlookContactRepository<ICardDavRepositoryLogger>(
                _outlookSession,
                outlookFolderEntryId,
                outlookFolderStoreId,
                _daslFilterProvider,
                QueryOutlookFolderByGetTableStrategy.Instance,
                _comWrapperFactory,
                false);

            var webDavClient = _httpClientFactory.CreateWebDavClient(cancelTokenSource);
            var cardDavDataAccess = new CardDavDataAccess(
                serverUrl, webDavClient,
                "text/vcard", /* write vcards, but read anything except x-vlists, in case of any servers return wrong contenttype  */
                contentType => contentType != "text/x-vlist");

            var bTypeVersionComparer = EqualityComparer<string>.Default;

            var cardDavRepository = new CardDavRepository<int>(cardDavDataAccess, mappingParameters.WriteImAsImpp,
                bTypeVersionComparer);
            var bTypeRepository = new LoggingCardDavRepositoryDecorator(cardDavRepository);

            var entityRelationDataFactory = new OutlookContactRelationDataFactory();

            var syncStateFactory =
                new EntitySyncStateFactory<string, DateTime, IContactItemWrapper, WebResourceName, string, vCard,
                    ICardDavRepositoryLogger>(
                    entityMapper,
                    entityRelationDataFactory,
                    CalDavSynchronizer.Utilities.ExceptionHandler.Instance);

            var bTypeIdEqualityComparer = WebResourceName.Comparer;
            var aTypeIdEqualityComparer = EqualityComparer<string>.Default;

            var storageDataAccess =
                new EntityRelationDataAccess<string, DateTime, OutlookContactRelationData, WebResourceName, string>(
                    storageDataDirectory);

            var bTypeStateAwareEntityRepository =
                new VersionAwareToStateAwareEntityRepositoryAdapter<WebResourceName, string, ICardDavRepositoryLogger,
                    string>(bTypeRepository, bTypeIdEqualityComparer, bTypeVersionComparer);

            var stateTokenDataAccess = NullStateTokensDataAccess<int, string>.Instance;

            var synchronizationMode = isReadOnly
                ? SynchronizationMode.ReplicateServerIntoOutlook
                : SynchronizationMode.MergeInBothDirections;

            var synchronizer =
                new Synchronizer<string, DateTime, IContactItemWrapper, WebResourceName, string, vCard,
                    ICardDavRepositoryLogger, ContactMatchData, vCard, int, string>(
                    aTypeRepository,
                    RunInBackgroundDecoratorFactory.Create(bTypeRepository),
                    BatchEntityRepositoryAdapter.Create(aTypeRepository, _exceptionHandlingStrategy),
                    RunInBackgroundDecoratorFactory.Create(
                        BatchEntityRepositoryAdapter.Create(bTypeRepository, _exceptionHandlingStrategy)),
                    InitialSyncStateCreationStrategyFactory<string, DateTime, IContactItemWrapper, WebResourceName,
                        string, vCard, ICardDavRepositoryLogger>.Create(
                        syncStateFactory,
                        syncStateFactory.Environment,
                        synchronizationMode,
                        ConflictResolution.ServerWins,
                        Factories.CreateContactConflictInitialSyncStateCreationStrategyAutomatic),
                    storageDataAccess,
                    entityRelationDataFactory,
                    Factories.CreateInitialContactEntityMatcher(bTypeIdEqualityComparer),
                    aTypeIdEqualityComparer,
                    bTypeIdEqualityComparer,
                    _totalProgressFactory,
                    _aTypeVersionComparer,
                    bTypeVersionComparer,
                    syncStateFactory,
                    _exceptionHandlingStrategy,
                    Factories.CreateContactMatchDataFactory(),
                    IdentityMatchDataFactory<vCard>.Instance,
                    EffectiveChunkSize,
                    CreateChunkedExecutor(EffectiveChunkSize),
                    FullEntitySynchronizationLoggerFactory.Create<string, IContactItemWrapper, WebResourceName, vCard>(
                        EntityLogMessageFactory.Instance),
                    new VersionAwareToStateAwareEntityRepositoryAdapter<string, DateTime, ICardDavRepositoryLogger,
                        int>(aTypeRepository, aTypeIdEqualityComparer, _aTypeVersionComparer),
                    RunInBackgroundDecoratorFactory.Create(bTypeStateAwareEntityRepository),
                    stateTokenDataAccess);

            return new OutlookSynchronizer<WebResourceName, string>(
                new ContextCreatingSynchronizerDecorator<string, DateTime, IContactItemWrapper, WebResourceName, string,
                    vCard, ICardDavRepositoryLogger>(
                    synchronizer,
                    new SynchronizationContextFactory<ICardDavRepositoryLogger>(() =>
                        NullCardDavRepositoryLogger.Instance)));
        }

        private static IInitialEntityMatcher<AppointmentId, DateTime, EventEntityMatchData, WebResourceName,
                string, EventServerEntityMatchData>
            CreateInitialEventEntityMatcher(IEqualityComparer<WebResourceName> equalityComparer)
        {
            return new InitialEventEntityMatcher(equalityComparer);
        }

        public static IMatchDataFactory<IICalendar, EventServerEntityMatchData>
            CreateEventServerEntityMatchDataFactory()
        {
            return new EventServerEntityMatchDataFactory();
        }

        private IOutlookSynchronizer CreateEventSynchronizer(Uri calendarUri,
            string outlookFolderEntryId, string outlookFolderStoreId, string outlookEmailAddress,
            string serverEmailAddress, string serverUserCommonName, bool isReadonly, string storageDataDirectory,
            EventMappingConfiguration mappingParameters, CancellationTokenSource cancelTokenSource,
            Func<string, Uri, string, string, string, ITimeZoneCache, EventMappingConfiguration, ITimeZone,
                IOutlookTimeZones, ICalendarResourceResolver, IEntityMapper<IAppointmentItemWrapper, IICalendar,
                    IEventSynchronizationContext>> entityMapperFactory)
        {
            var entityRelationDataAccess =
                new EntityRelationDataAccess<AppointmentId, DateTime, OutlookEventRelationData, WebResourceName,
                    string>(storageDataDirectory);

            var dateTimeRangeProvider = Factories.CreateDateTimeRangeProvider(60, 365);

            var aTypeRepository = new OutlookEventRepositoryWrapper(
                _outlookSession,
                outlookFolderEntryId,
                outlookFolderStoreId,
                dateTimeRangeProvider,
                mappingParameters,
                _daslFilterProvider,
                new QueryAppointmentFolderStrategy(),
                _comWrapperFactory,
                false);

            var bTypeVersionComparer = EqualityComparer<string>.Default;

            var webDavClient = _httpClientFactory.CreateWebDavClient(cancelTokenSource);
            var calDavDataAccess = new CalDavDataAccess(calendarUri, webDavClient);

            var bTypeRepository = new CalDavRepository<IEventSynchronizationContext>(
                calDavDataAccess,
                new iCalendarSerializer(),
                CalDavRepository.EntityType.Event,
                dateTimeRangeProvider,
                false,
                bTypeVersionComparer);

            var timeZoneCache = new TimeZoneCache(_httpClientFactory.CreateHttpClient(),
                mappingParameters.IncludeHistoricalData, _globalTimeZoneCache);

            var entityMapper = entityMapperFactory(
                outlookEmailAddress,
                new Uri("mailto:" + serverEmailAddress), serverUserCommonName,
                _outlookSession.TimeZones.CurrentTimeZone.ID,
                _outlookSession.ApplicationVersion,
                timeZoneCache,
                mappingParameters,
                null,
                _outlookSession.TimeZones,
                Factories.CreateCalendarResourceResolver(calDavDataAccess)
            );

            var outlookEventRelationDataFactory = new OutlookEventRelationDataFactory();

            var syncStateFactory =
                new EntitySyncStateFactory<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
                    IICalendar, IEventSynchronizationContext>(
                    entityMapper,
                    outlookEventRelationDataFactory,
                    ExceptionHandler.Instance
                );

            var bTypeIdEqualityComparer = WebResourceName.Comparer;
            var aTypeIdEqualityComparer = AppointmentId.Comparer;

            var aTypeWriteRepository = BatchEntityRepositoryAdapter.Create(aTypeRepository, _exceptionHandlingStrategy);
            var bTypeWriteRepository = BatchEntityRepositoryAdapter.Create(bTypeRepository, _exceptionHandlingStrategy);

            var eventSyncStateCreationStrategy = CreateEventInitialSyncStateStrategy(isReadonly, syncStateFactory, aTypeRepository);

            var synchronizer =
                new Synchronizer<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                    IEventSynchronizationContext, EventEntityMatchData, EventServerEntityMatchData, int, string>(
                    aTypeRepository,
                    bTypeRepository,
                    aTypeWriteRepository,
                    bTypeWriteRepository,
                    eventSyncStateCreationStrategy,
                    entityRelationDataAccess,
                    outlookEventRelationDataFactory,
                    CreateInitialEventEntityMatcher(bTypeIdEqualityComparer),
                    aTypeIdEqualityComparer,
                    bTypeIdEqualityComparer,
                    _totalProgressFactory,
                    _aTypeVersionComparer,
                    bTypeVersionComparer,
                    syncStateFactory,
                    _exceptionHandlingStrategy,
                    Factories.CreateEventEntityMatchDataFactory(),
                    CreateEventServerEntityMatchDataFactory(),
                    EffectiveChunkSize,
                    CreateChunkedExecutor(EffectiveChunkSize),
                    FullEntitySynchronizationLoggerFactory
                        .Create<AppointmentId, IAppointmentItemWrapper, WebResourceName, IICalendar>(
                            EntityLogMessageFactory.Instance),
                    new VersionAwareToStateAwareEntityRepositoryAdapter<AppointmentId, DateTime,
                        IEventSynchronizationContext, int>(aTypeRepository, aTypeIdEqualityComparer,
                        _aTypeVersionComparer),
                    new VersionAwareToStateAwareEntityRepositoryAdapter<WebResourceName, string,
                        IEventSynchronizationContext, string>(bTypeRepository, bTypeIdEqualityComparer,
                        bTypeVersionComparer),
                    NullStateTokensDataAccess<int, string>.Instance,
                    new EventSyncInterceptorFactory(_invitesInfoStorage));

            return new OutlookEventSynchronizer<WebResourceName, string>(
                new ContextCreatingSynchronizerDecorator<AppointmentId, DateTime, IAppointmentItemWrapper,
                    WebResourceName, string, IICalendar, IEventSynchronizationContext>(
                    synchronizer,
                    new EventSynchronizationContextFactory(
                        aTypeRepository.Inner,
                        bTypeRepository,
                        entityRelationDataAccess,
                        mappingParameters.CleanupDuplicateEvents,
                        aTypeIdEqualityComparer,
                        _outlookSession,
                        NullColorCategoryMapperFactory.Instance)));
        }

        private IOutlookSynchronizer CreateTaskSynchronizer(Uri calendarUri, string outlookFolderEntryId,
            string outlookFolderStoreId, bool isReadOnly, string storageDataDirectory,
            TaskMappingConfiguration mappingParameters, CancellationTokenSource cancelTokenSource)
        {
            var aTypeRepository = new OutlookTaskRepository(
                _outlookSession,
                outlookFolderEntryId,
                outlookFolderStoreId,
                _daslFilterProvider,
                mappingParameters,
                QueryOutlookFolderByGetTableStrategy.Instance,
                _comWrapperFactory,
                false);

            var webDavClient = _httpClientFactory.CreateWebDavClient(cancelTokenSource);
            var calDavDataAccess = new CalDavDataAccess(calendarUri, webDavClient);

            var bTypeVersionComparer = EqualityComparer<string>.Default;

            var bTypeRepository = new CalDavRepository<int>(
                calDavDataAccess,
                new iCalendarSerializer(),
                CalDavRepository.EntityType.Todo,
                NullDateTimeRangeProvider.Instance,
                false,
                bTypeVersionComparer);

            var relationDataFactory = new TaskRelationDataFactory();
            var syncStateFactory =
                new EntitySyncStateFactory<string, DateTime, ITaskItemWrapper, WebResourceName, string, IICalendar,
                    int>(
                    new TaskEntityMapper(_outlookSession.TimeZones.CurrentTimeZone.ID, mappingParameters),
                    relationDataFactory,
                    ExceptionHandler.Instance);

            var bTypeIdEqualityComparer = WebResourceName.Comparer;
            var aTypeIdEqualityComparer = EqualityComparer<string>.Default;

            var aTypeWriteRepository = BatchEntityRepositoryAdapter.Create(aTypeRepository, _exceptionHandlingStrategy);
            var bTypeWriteRepository = BatchEntityRepositoryAdapter.Create(bTypeRepository, _exceptionHandlingStrategy);

            var entityRelationDataAccess =
                new EntityRelationDataAccess<string, DateTime, TaskRelationData, WebResourceName, string>(
                    storageDataDirectory);

            var synchronizationMode = isReadOnly
                ? SynchronizationMode.ReplicateServerIntoOutlook
                : SynchronizationMode.MergeInBothDirections;

            var synchronizer =
                new Synchronizer<string, DateTime, ITaskItemWrapper, WebResourceName, string, IICalendar, int,
                    TaskEntityMatchData, IICalendar, int, string>(
                    aTypeRepository,
                    bTypeRepository,
                    aTypeWriteRepository,
                    bTypeWriteRepository,
                    InitialSyncStateCreationStrategyFactory<string, DateTime, ITaskItemWrapper, WebResourceName, string,
                        IICalendar, int>.Create(
                        syncStateFactory,
                        syncStateFactory.Environment,
                        synchronizationMode,
                        ConflictResolution.ServerWins,
                        Factories.CreateTaskConflictInitialSyncStateCreationStrategyAutomatic),
                    entityRelationDataAccess,
                    relationDataFactory,
                    Factories.CreateInitialTaskEntityMatcher(bTypeIdEqualityComparer),
                    aTypeIdEqualityComparer,
                    bTypeIdEqualityComparer,
                    _totalProgressFactory,
                    _aTypeVersionComparer,
                    bTypeVersionComparer,
                    syncStateFactory,
                    _exceptionHandlingStrategy,
                    Factories.CreateTaskEntityMatchDataFactory(),
                    IdentityMatchDataFactory<IICalendar>.Instance,
                    EffectiveChunkSize,
                    CreateChunkedExecutor(EffectiveChunkSize),
                    FullEntitySynchronizationLoggerFactory
                        .Create<string, ITaskItemWrapper, WebResourceName,
                            IICalendar>(EntityLogMessageFactory.Instance),
                    new VersionAwareToStateAwareEntityRepositoryAdapter<string, DateTime, int, int>(aTypeRepository,
                        aTypeIdEqualityComparer, _aTypeVersionComparer),
                    new VersionAwareToStateAwareEntityRepositoryAdapter<WebResourceName, string, int, string>(
                        bTypeRepository, bTypeIdEqualityComparer, bTypeVersionComparer),
                    NullStateTokensDataAccess<int, string>.Instance);

            return new OutlookSynchronizer<WebResourceName, string>(
                new NullContextSynchronizerDecorator<string, DateTime, ITaskItemWrapper, WebResourceName, string,
                    IICalendar>(synchronizer));
        }

        private IChunkedExecutor CreateChunkedExecutor(int chunkSize)
        {
            return new ChunkedExecutor(chunkSize);
        }

        private static string GetEventTimeZone()
        {
            try
            {
                return NodaTime.DateTimeZoneProviders.Tzdb.GetSystemDefault().Id;
            }
            catch (DateTimeZoneNotFoundException)
            {
                // Default to GMT if Windows Zone can't be mapped to IANA zone.
                return "Etc/GMT";
            }
        }

        private static string GetAccountForFolder(Outlook.MAPIFolder folder)
        {
            var store = folder.Store;
            var application = folder.Application;

            foreach (Outlook.Account account in application.Session.Accounts)
            {
                if (account.DeliveryStore.StoreID == store.StoreID)
                    return account.SmtpAddress;
            }

            return String.Empty;
        }

        private IInitialSyncStateCreationStrategy<AppointmentId, DateTime, IAppointmentItemWrapper,
                WebResourceName, string, IICalendar, IEventSynchronizationContext>
            CreateEventInitialSyncStateStrategy(bool isReadonly,
                IEntitySyncStateFactory<AppointmentId, DateTime, IAppointmentItemWrapper,
                    WebResourceName, string, IICalendar, IEventSynchronizationContext> syncStateFactory,
                OutlookEventRepositoryWrapper outlookRepository)
        {
            if (isReadonly)
                return new EventSyncStrategyServerToOutlook(syncStateFactory);
            return new EventSyncStrategyBothWays(syncStateFactory, _invitesInfoStorage, _outlookSession, outlookRepository);
        }

        class EntityLogMessageFactory :
            IEntityLogMessageFactory<IAppointmentItemWrapper, IICalendar>,
            IEntityLogMessageFactory<ITaskItemWrapper, IICalendar>,
            IEntityLogMessageFactory<IContactItemWrapper, vCard>
        {
            public static readonly EntityLogMessageFactory Instance = new EntityLogMessageFactory();

            private EntityLogMessageFactory()
            {
            }

            public string GetADisplayNameOrNull(IAppointmentItemWrapper entity)
            {
                return entity.Inner.Subject;
            }

            public string GetADisplayNameOrNull(ITaskItemWrapper entity)
            {
                return entity.Inner.Subject;
            }

            public string GetBDisplayNameOrNull(IICalendar entity)
            {
                return entity.Calendar.Events.FirstOrDefault()?.Summary ??
                       entity.Calendar.Todos.FirstOrDefault()?.Summary;
            }

            public string GetADisplayNameOrNull(IContactItemWrapper entity)
            {
                return entity.Inner.FullName;
            }

            public string GetBDisplayNameOrNull(vCard entity)
            {
                return entity.FormattedName;
            }
        }
    }
}
