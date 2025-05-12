using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CalDavSynchronizer;
using CalDavSynchronizer.Contracts;
using CalDavSynchronizer.Implementation;
using CalDavSynchronizer.Implementation.Common;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using CalDavSynchronizer.Implementation.TimeRangeFiltering;
using GenSync;
using GenSync.EntityRepositories;
using GenSync.Logging;
using log4net;
using Y360OutlookConnector.Utilities;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    public class OutlookEventRepositoryWrapper : IEntityRepository<AppointmentId, DateTime, IAppointmentItemWrapper, IEventSynchronizationContext>
    {
        private static readonly ILog s_logger = LogManager.GetLogger(System.Reflection.MethodInfo.GetCurrentMethod().DeclaringType);
        private readonly EventMappingConfiguration _configuration;
        private readonly IOutlookSession _session;
        private readonly string _folderId;
        private readonly string _folderStoreId;

        public readonly OutlookEventRepository Inner;

        public OutlookEventRepositoryWrapper(IOutlookSession session,
            string folderId,
            string folderStoreId,
            IDateTimeRangeProvider dateTimeRangeProvider,
            EventMappingConfiguration configuration,
            IDaslFilterProvider daslFilterProvider,
            IQueryOutlookAppointmentItemFolderStrategy queryFolderStrategy,
            IComWrapperFactory comWrapperFactory,
            bool useDefaultFolderItemType)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _folderId = folderId;
            _folderStoreId = folderStoreId;

            Inner = new OutlookEventRepository(
                session,
                folderId,
                folderStoreId,
                dateTimeRangeProvider,
                configuration,
                daslFilterProvider,
                queryFolderStrategy,
                comWrapperFactory,
                useDefaultFolderItemType);
        }

        public void Cleanup(IAppointmentItemWrapper entity)
        {
            Inner.Cleanup(entity);
        }

        public void Cleanup(IEnumerable<IAppointmentItemWrapper> entities)
        {
            Inner.Cleanup(entities);
        }

        public Task<EntityVersion<AppointmentId, DateTime>> Create(Func<IAppointmentItemWrapper, Task<IAppointmentItemWrapper>> entityInitializer, IEventSynchronizationContext context)
        {
            return Inner.Create(entityInitializer, context);
        }

        public Task<IEnumerable<EntityWithId<AppointmentId, IAppointmentItemWrapper>>> Get(ICollection<AppointmentId> ids, ILoadEntityLogger logger, IEventSynchronizationContext context)
        {
            return Inner.Get(ids, logger, context);
        }

        public async Task<IEnumerable<EntityVersion<AppointmentId, DateTime>>> GetAllVersions(IEnumerable<AppointmentId> idsOfknownEntities, IEventSynchronizationContext context, IGetVersionsLogger logger)
        {
            var events = await Inner.GetAllVersions(idsOfknownEntities, context, logger);
            var eventsList = events.ToList();

            var missingEntities = idsOfknownEntities
                .Where(known => !eventsList.Any(e => e.Id.EntryId == known.EntryId))
                .ToList();

            if (missingEntities.Any())
            {
                var foundedMissingEntities = new List<EntityVersion<AppointmentId, DateTime>>();
                s_logger.Info($"Got items from Outlook: {eventsList.Count()} and KnownEntities contains: {idsOfknownEntities.Count()}");
                foreach (var id in missingEntities)
                {
                    var item = _session.GetAppointmentItemOrNull(id.EntryId, _folderId, _folderStoreId);
                    if (item != null)
                    {
                        s_logger.Info($"Successfully received aType Id - {id.EntryId} | {item?.Subject}");
                        Telemetry.Signal(Telemetry.ConfirmedBugEvent, "error_found_missed_meeting_outlook");
                        foundedMissingEntities.Add(AppointmentSlim.FromAppointmentItem(item).Version);
                    }
                }

                if (foundedMissingEntities != null)
                    eventsList.AddRange(foundedMissingEntities);
            }
            return eventsList;
        }

        public Task<IEnumerable<EntityVersion<AppointmentId, DateTime>>> GetVersions(IEnumerable<IdWithAwarenessLevel<AppointmentId>> idsOfEntitiesToQuery, IEventSynchronizationContext context, IGetVersionsLogger logger)
        {
            var result = new List<EntityVersion<AppointmentId, DateTime>>();

            foreach (var id in idsOfEntitiesToQuery)
            {
                var appointment = _session.GetAppointmentItemOrNull(id.Id.EntryId, _folderId, _folderStoreId);
                if (appointment != null)
                {
                    try
                    {
                        if (_configuration.IsCategoryFilterSticky && id.IsKnown || DoesMatchCategoryCriterion(appointment))
                        {
                            var lastChangeTime = AppointmentItemUtils.GetLastChangeTime(appointment);
                            result.Add(EntityVersion.Create(id.Id, lastChangeTime));
                            context.DuplicateEventCleaner.AnnounceAppointment(AppointmentSlim.FromAppointmentItem(appointment));
                        }
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject(appointment);
                    }
                }
            }

            return Task.FromResult<IEnumerable<EntityVersion<AppointmentId, DateTime>>>(result);
        }

        public Task<bool> TryDelete(AppointmentId entityId, DateTime version, IEventSynchronizationContext context, IEntitySynchronizationLogger logger)
        {
            return Inner.TryDelete(entityId, version, context, logger);
        }

        public Task<EntityVersion<AppointmentId, DateTime>> TryUpdate(AppointmentId entityId, DateTime version, IAppointmentItemWrapper entityToUpdate, Func<IAppointmentItemWrapper, Task<IAppointmentItemWrapper>> entityModifier, IEventSynchronizationContext context, IEntitySynchronizationLogger logger)
        {
            return Inner.TryUpdate(entityId, version, entityToUpdate, entityModifier, context, logger);
        }

        public Task VerifyUnknownEntities(Dictionary<AppointmentId, DateTime> unknownEntities, IEventSynchronizationContext context)
        {
            return Inner.VerifyUnknownEntities(unknownEntities, context);
        }

        private bool DoesMatchCategoryCriterion(Outlook.AppointmentItem item)
        {
            if (!_configuration.UseEventCategoryAsFilter)
                return true;

            var categoryCsv = item.Categories;

            if (string.IsNullOrEmpty(categoryCsv))
                return _configuration.InvertEventCategoryFilter || _configuration.IncludeEmptyEventCategoryFilter;

            var found = item.Categories
                .Split(new[] { CultureInfo.CurrentCulture.TextInfo.ListSeparator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Any(c => c == _configuration.EventCategory);
            return _configuration.InvertEventCategoryFilter ? !found : found;
        }
    }
}
