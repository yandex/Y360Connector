using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using DDay.iCal;
using GenSync;
using GenSync.EntityRelationManagement;
using GenSync.EntityRepositories;
using GenSync.Logging;
using GenSync.Synchronization;
using GenSync.Synchronization.States;
using log4net;

namespace Y360OutlookConnector.Synchronization.Synchronizer.States
{
    public class CreateInBWith404Fallback :
        StateBase<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
            IEventSynchronizationContext>
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public AppointmentId AId { get; }
        public DateTime AVersion { get; }

        private IAppointmentItemWrapper _aEntity;
        private IEventSynchronizationContext _context;

        private readonly OutlookEventRepositoryWrapper _outlookRepository;
        private readonly CreateInB<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
            IICalendar, IEventSynchronizationContext> _inner;

        public CreateInBWith404Fallback(
            OutlookEventRepositoryWrapper outlookEventRepository,
            EntitySyncStateEnvironment<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
                IICalendar, IEventSynchronizationContext> environment,
            AppointmentId aId, DateTime aVersion)
            : base(environment)
        {
            _outlookRepository = outlookEventRepository;

            _inner = new CreateInB<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName,
                string, IICalendar, IEventSynchronizationContext>(environment, aId, aVersion);

            AId = aId;
            AVersion = aVersion;
        }

        public override IEntitySyncState<AppointmentId, DateTime, IAppointmentItemWrapper,
            WebResourceName, string, IICalendar, IEventSynchronizationContext> Abort()
        {
            return _inner.Abort();
        }

        public override void Accept(IEntitySyncStateContext<AppointmentId, DateTime, IAppointmentItemWrapper,
                WebResourceName, string, IICalendar, IEventSynchronizationContext> syncStateContext,
            ISynchronizationStateVisitor<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName,
                string, IICalendar, IEventSynchronizationContext> visitor)
        {
            visitor.Visit(syncStateContext, _inner);
        }

        public override void AddNewRelationNoThrow(Action<IEntityRelationData<AppointmentId, DateTime,
            WebResourceName, string>> addAction)
        {
            _inner.AddNewRelationNoThrow(addAction);
        }

        public override void AddRequiredEntitiesToLoad(Func<AppointmentId, bool> a, Func<WebResourceName, bool> b)
        {
            _inner.AddRequiredEntitiesToLoad(a, b);
        }

        public override void AddSyncronizationJob(
            IEntitySyncStateContext<AppointmentId, DateTime, IAppointmentItemWrapper,
                WebResourceName, string, IICalendar, IEventSynchronizationContext> stateContext,
            IJobList<AppointmentId, DateTime, IAppointmentItemWrapper> aJobs,
            IJobList<WebResourceName, string, IICalendar> bJobs,
            IEntitySynchronizationLoggerFactory<AppointmentId, IAppointmentItemWrapper, WebResourceName, IICalendar>
                loggerFactory,
            IEventSynchronizationContext context)
        {
            _context = context;

            var logger = loggerFactory.CreateEntitySynchronizationLogger(SynchronizationOperation.CreateInB);
            logger.SetAId(AId);
            logger.LogA(_aEntity);
            bJobs.AddCreateJob(new JobWrapper(stateContext, this, logger, context));
        }

        public override IEntitySyncState<AppointmentId, DateTime, IAppointmentItemWrapper,
            WebResourceName, string, IICalendar, IEventSynchronizationContext> FetchRequiredEntities(
            IReadOnlyDictionary<AppointmentId, IAppointmentItemWrapper> aEntities,
            IReadOnlyDictionary<WebResourceName, IICalendar> bEntities)
        {
            if (!aEntities.TryGetValue(AId, out _aEntity))
            {
                s_logger.InfoFormat($"Could not fetch entity '{AId}'. Discarding operation.");
                return Discard();
            }

            _inner.FetchRequiredEntities(aEntities, bEntities);
            return this;
        }

        public override IEntitySyncState<
            AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName,
            string, IICalendar, IEventSynchronizationContext> NotifyJobExecuted()
        {
            return _inner.NotifyJobExecuted();
        }

        public override IEntitySyncState<
            AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName,
            string, IICalendar, IEventSynchronizationContext> Resolve()
        {
            return this;
        }

        public override void Dispose()
        {
            _inner.Dispose();
        }

        private async Task<IICalendar> InitializeEntity(IICalendar entity, IEntitySynchronizationLogger logger,
            IEventSynchronizationContext context)
        {
            return await _environment.Mapper.Map1To2(_aEntity, entity, logger, context);
        }

        private void NotifyOperationSuceeded(
            IEntitySyncStateContext<AppointmentId, DateTime, IAppointmentItemWrapper,
                WebResourceName, string, IICalendar, IEventSynchronizationContext> stateContext,
            EntityVersion<WebResourceName, string> newVersion,
            IEntitySynchronizationLogger<AppointmentId, IAppointmentItemWrapper, WebResourceName, IICalendar> logger)
        {
            logger.SetBId(newVersion.Id);
            stateContext.SetState(CreateDoNothing(AId, AVersion, newVersion.Id, newVersion.Version));
        }

        private void NotifyOperationFailed(IEntitySyncStateContext<AppointmentId, DateTime, IAppointmentItemWrapper,
                WebResourceName, string, IICalendar, IEventSynchronizationContext> stateContext,
            Exception exception,
            IEntitySynchronizationLogger<AppointmentId, IAppointmentItemWrapper, WebResourceName, IICalendar> logger)
        {
            if (exception is WebDavClientException webDavClientException
                && webDavClientException.StatusCode == HttpStatusCode.NotFound)
            {
                s_logger.Info("Received 404 error when trying to create an event. The event will be deleted from Outlook");
                _outlookRepository.TryDelete(AId, AVersion, _context, logger);
                stateContext.SetState(Discard());
            }
            else
            {
                logger.LogAbortedDueToError(exception);
                LogException(exception);
                stateContext.SetState(Discard());
            }
        }

        private void NotifyOperationFailed(
            IEntitySyncStateContext<AppointmentId, DateTime, IAppointmentItemWrapper,
                WebResourceName, string, IICalendar, IEventSynchronizationContext> stateContext,
            string errorMessage,
            IEntitySynchronizationLogger<AppointmentId, IAppointmentItemWrapper, WebResourceName, IICalendar> logger)
        {
            logger.LogAbortedDueToError(errorMessage);
            stateContext.SetState(Discard());
        }

        private struct JobWrapper : ICreateJob<WebResourceName, string, IICalendar>
        {
            private readonly IEntitySyncStateContext<AppointmentId, DateTime, IAppointmentItemWrapper,
                WebResourceName, string, IICalendar, IEventSynchronizationContext> _stateContext;

            private readonly CreateInBWith404Fallback _state;

            readonly IEntitySynchronizationLogger<AppointmentId, IAppointmentItemWrapper, WebResourceName, IICalendar>
                _logger;

            private readonly IEventSynchronizationContext _context;

            public JobWrapper(
                IEntitySyncStateContext<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string,
                    IICalendar, IEventSynchronizationContext> stateContext,
                CreateInBWith404Fallback state,
                IEntitySynchronizationLogger<AppointmentId, IAppointmentItemWrapper, WebResourceName, IICalendar>
                    logger,
                IEventSynchronizationContext context)
            {
                _stateContext = stateContext;
                _state = state ?? throw new ArgumentNullException(nameof(state));
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
                _context = context;
            }

            public async Task<IICalendar> InitializeEntity(IICalendar entity)
            {
                return await _state.InitializeEntity(entity, _logger, _context);
            }

            public void NotifyOperationSuceeded(EntityVersion<WebResourceName, string> result)
            {
                _state.NotifyOperationSuceeded(_stateContext, result, _logger);
            }

            public void NotifyOperationFailed(Exception exception)
            {
                _state.NotifyOperationFailed(_stateContext, exception, _logger);
            }

            public void NotifyOperationFailed(string errorMessage)
            {
                _state.NotifyOperationFailed(_stateContext, errorMessage, _logger);
            }
        }
    }
}
