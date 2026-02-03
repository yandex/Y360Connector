using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using log4net;

namespace Y360OutlookConnector.Synchronization
{
    public class FailedEntityInfo
    {
        public string EntityId { get; set; }
        public string EntityType { get; set; }
        public DateTime FailedAt { get; set; }
        public string ErrorMessage { get; set; }
        public int RetryCount { get; set; }
        public DateTime? LastRetryAt { get; set; }
    }
    public class FailedEntityTracker
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        private readonly List<FailedEntityInfo> _failedEntities = new List<FailedEntityInfo>();
        private readonly object _lockObject = new object();
        private readonly int _maxRetryCount = 3;

        public FailedEntityTracker()
        {
        }

        private static string TruncateEntityId(string entityId, int maxLength = 20)
        {
            if (string.IsNullOrEmpty(entityId))
            {
                return entityId;
            }
            return entityId.Length > maxLength ? "..." + entityId.Substring(entityId.Length - maxLength) : entityId;
        }

        public void AddFailedEntity(string entityId, string entityType, Exception exception)
        {
            lock (_lockObject)
            {
                var existingEntity = _failedEntities.FirstOrDefault(e => e.EntityId == entityId && e.EntityType == entityType);
                if (existingEntity != null)
                {
                    existingEntity.RetryCount++;
                    existingEntity.LastRetryAt = DateTime.UtcNow;
                    existingEntity.ErrorMessage = exception.Message;
                }
                else
                {
                    _failedEntities.Add(new FailedEntityInfo
                    {
                        EntityId = entityId,
                        EntityType = entityType,
                        FailedAt = DateTime.UtcNow,
                        ErrorMessage = exception.Message,
                        RetryCount = 0,
                        LastRetryAt = null
                    });
                }

                var truncatedEntityId = TruncateEntityId(entityId);
                s_logger.Warn($"Added failed entity: {entityType} - {truncatedEntityId}, Retry count: {existingEntity?.RetryCount ?? 0}");
            }
        }

        public List<FailedEntityInfo> GetRetryableEntities()
        {
            lock (_lockObject)
            {

                return _failedEntities
                    .Where(e => e.RetryCount < _maxRetryCount)
                    .ToList();
            }
        }

        public void MarkEntityAsRetried(string entityId, string entityType)
        {
            lock (_lockObject)
            {
                var entity = _failedEntities.FirstOrDefault(e => e.EntityId == entityId && e.EntityType == entityType);
                if (entity != null)
                {
                    entity.LastRetryAt = DateTime.UtcNow;
                }
            }
        }

        public void RemoveEntity(string entityId, string entityType)
        {
            lock (_lockObject)
            {
                var entity = _failedEntities.FirstOrDefault(e => e.EntityId == entityId && e.EntityType == entityType);

                if (entity != null)
                {
                    _failedEntities.Remove(entity);
                    var truncatedEntityId = TruncateEntityId(entityId);
                    s_logger.Info($"Removed failed entity from tracking: {entityType} - {truncatedEntityId}");
                }
            }
        }

        public void CleanupOldEntities()
        {
            lock (_lockObject)
            {
                var now = DateTime.UtcNow;
                var cutoffDate = now.AddMinutes(-60);
                
                var entitiesToRemove = _failedEntities
                    .Where(e => e.RetryCount >= _maxRetryCount || e.FailedAt < cutoffDate)
                    .ToList();

                foreach (var entity in entitiesToRemove)
                {
                    _failedEntities.Remove(entity);
                    var truncatedEntityId = TruncateEntityId(entity.EntityId);
                    s_logger.Info($"Cleaned up old failed entity: {entity.EntityType} - {truncatedEntityId}");
                }
            }
        }
    }
}
