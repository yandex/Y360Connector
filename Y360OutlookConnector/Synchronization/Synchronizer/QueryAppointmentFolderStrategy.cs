using System;
using System.Reflection;
using System.Collections.Generic;
using CalDavSynchronizer;
using CalDavSynchronizer.Implementation.Common;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using GenSync;
using GenSync.Logging;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    public class QueryAppointmentFolderStrategy : IQueryOutlookAppointmentItemFolderStrategy
    {
        private static readonly ILog s_logger =
            LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private const string PR_GLOBAL_OBJECT_ID =
            "http://schemas.microsoft.com/mapi/id/{6ED8DA90-450B-101B-98DA-00AA003F1305}/00030102";

        private const string PR_LONG_TERM_ENTRYID_FROM_TABLE = "http://schemas.microsoft.com/mapi/proptag/0x66700102";
        private const string PR_ENTRYID = "http://schemas.microsoft.com/mapi/proptag/0x0FFF0102";
        private const string PidLidAppointmentReplyTime =
            "http://schemas.microsoft.com/mapi/id/{00062002-0000-0000-C000-000000000046}/82200040";
        private const string LID_OWNER_CRITICAL_CHANGE =
            "http://schemas.microsoft.com/mapi/id/{6ED8DA90-450B-101B-98DA-00AA003F1305}/001A0040";

        private const string LastModificationTimeColumnId = "LastModificationTime";
        private const string SubjectColumnId = "Subject";
        private const string StartColumnId = "Start";
        private const string EndColumnId = "End";
        private const string EntryIdColumnId = "EntryID";

        public List<AppointmentSlim> QueryAppointmentFolder(IOutlookSession session, Folder calendarFolder,
            string filter, IGetVersionsLogger logger)
        {
            try
            {
                return QueryAppointmentFolderByGetTable(session, calendarFolder, filter, logger);
            }
            catch (System.Exception ex)
            {
                s_logger.Warn($"Failed to query folder by get table strategy. Error: {ex.Message}");
            }

            return QueryAppointmentFolderByRequestingItem(session, calendarFolder, filter, logger);
        }

        private List<AppointmentSlim> QueryAppointmentFolderByGetTable(IOutlookSession session, 
            Folder calendarFolder, string filter, IGetVersionsLogger logger)
        {
            var events = new List<AppointmentSlim>();

            using (var tableWrapper = GenericComObjectWrapper.Create(calendarFolder.GetTable(filter)))
            {
                var table = tableWrapper.Inner;
                table.Columns.RemoveAll();
                table.Columns.Add(PR_GLOBAL_OBJECT_ID);
                table.Columns.Add(PR_LONG_TERM_ENTRYID_FROM_TABLE);
                table.Columns.Add(PR_ENTRYID);
                table.Columns.Add(LastModificationTimeColumnId);
                table.Columns.Add(SubjectColumnId);
                table.Columns.Add(StartColumnId);
                table.Columns.Add(EndColumnId);
                table.Columns.Add(LID_OWNER_CRITICAL_CHANGE);
                table.Columns.Add(PidLidAppointmentReplyTime);

                while (!table.EndOfTable)
                {
                    var row = table.GetNextRow();
                    var appointmentSlim = ParseRow(row, logger);
                    if (appointmentSlim != null)
                    {
                        events.Add(appointmentSlim);
                    }
                }
            }

            return events;
        }

        private List<AppointmentSlim> QueryAppointmentFolderByRequestingItem(IOutlookSession session, 
            Folder folder, string filter, IGetVersionsLogger logger)
        {
            var events = new List<AppointmentSlim>();

            using (var tableWrapper = GenericComObjectWrapper.Create(folder.GetTable(filter)))
            {
                var table = tableWrapper.Inner;
                table.Columns.RemoveAll();
                table.Columns.Add(EntryIdColumnId);

                var storeId = folder.StoreID;
                while (!table.EndOfTable)
                {
                    var row = table.GetNextRow();
                    var entryId = (string) row[EntryIdColumnId];
                    var appointmentSlim = CreateAppointmentSlim(entryId, storeId, session);
                    if (appointmentSlim != null)
                    {
                        events.Add(appointmentSlim);
                    }
                }
            }

            return events;
        }

        private static AppointmentSlim ParseRow(Row row, IGetVersionsLogger logger)
        {
            try
            {
                string entryId;
                if (row[PR_LONG_TERM_ENTRYID_FROM_TABLE] is byte[] entryIdArray && entryIdArray.Length > 0)
                {
                    entryId = row.BinaryToString(PR_LONG_TERM_ENTRYID_FROM_TABLE);
                }
                else
                {
                    entryId = row.BinaryToString(PR_ENTRYID);
                    s_logger.Warn($"Could not access long-term ENTRYID of appointment '{entryId}', " +
                                  $"use short-term ENTRYID as fallback.");
                }

                string globalAppointmentId = null;
                try
                {
                    if (row[PR_GLOBAL_OBJECT_ID] is byte[] globalIdArray && globalIdArray.Length > 0)
                    {
                        globalAppointmentId = row.BinaryToString(PR_GLOBAL_OBJECT_ID);
                    }
                }
                catch (System.Exception ex)
                {
                    s_logger.Warn($"Could not access GlobalAppointmentID of appointment '{entryId}'.", ex);
                }

                var subject = (string) row[SubjectColumnId];
                var appointmentId = new AppointmentId(entryId, globalAppointmentId);

                if (!GetDateTime(row, LastModificationTimeColumnId, true, out DateTime lastModificationTime))
                {
                    s_logger.Warn($"Column '{nameof(LastModificationTimeColumnId)}' of event '{entryId}' is NULL.");
                    logger.LogWarning(entryId, $"Column '{nameof(LastModificationTimeColumnId)}' is NULL.");
                    lastModificationTime = OutlookUtility.OUTLOOK_DATE_NONE;
                }

                if (GetDateTime(row, LID_OWNER_CRITICAL_CHANGE, false, out DateTime ownerCriticalChange)
                    && (lastModificationTime == OutlookUtility.OUTLOOK_DATE_NONE || ownerCriticalChange > lastModificationTime))
                {
                    lastModificationTime = ownerCriticalChange;
                }

                if (GetDateTime(row, LID_OWNER_CRITICAL_CHANGE, false, out DateTime appointmentReplyTime)
                    && (lastModificationTime == OutlookUtility.OUTLOOK_DATE_NONE || appointmentReplyTime > lastModificationTime))
                {
                    lastModificationTime = appointmentReplyTime;
                }

                var startObject = row[StartColumnId];
                DateTime? start;
                if (startObject != null)
                {
                    start = (DateTime)startObject;
                }
                else
                {
                    s_logger.Warn($"Column '{nameof(StartColumnId)}' of event '{entryId}' is NULL.");
                    logger.LogWarning(entryId, $"Column '{nameof(StartColumnId)}' is NULL.");
                    start = null;
                }

                var endObject = row[EndColumnId];
                DateTime? end;
                if (endObject != null)
                {
                    end = (DateTime)endObject;
                }
                else
                {
                    s_logger.Warn($"Column '{nameof(EndColumnId)}' of event '{entryId}' is NULL.");
                    logger.LogWarning(entryId, $"Column '{nameof(EndColumnId)}' is NULL.");
                    end = null;
                }

                var entityVersion = EntityVersion.Create(appointmentId, lastModificationTime);
                return new AppointmentSlim(entityVersion, start, end, subject);
            }
            catch (System.Exception ex)
            {
                s_logger.Warn("Parse folder table row error", ex);
                return null;
            }
        }

        private static bool GetDateTime(Row row, string propertyName, bool isLocalTime, out DateTime dateTime)
        {
            dateTime = OutlookUtility.OUTLOOK_DATE_NONE;
            bool result = false;
            try
            {
                var obj = row[propertyName];
                if (obj != null)
                {
                    dateTime = (DateTime)obj;
                    if (isLocalTime)
                        dateTime = dateTime.ToUniversalTime();
                    result = true;
                }
            }
            catch (System.Exception ex)
            {
                result = false;
                s_logger.Debug($"Failed to retrieve datetime from {propertyName}", ex);
            }
            return result;
        }

        private static AppointmentSlim CreateAppointmentSlim(string entryId, string storeId, IOutlookSession session)
        {
            try
            {
                using (var appointmentWrapper = GenericComObjectWrapper.Create(
                    session.GetAppointmentItem(entryId, storeId)))
                {
                    var appointment = appointmentWrapper.Inner;
                    var appointmentId = new AppointmentId
                    {
                        EntryId = appointment.EntryID, 
                        GlobalAppointmentId = appointment.GlobalAppointmentID
                    };

                    var changeTime = AppointmentItemUtils.GetLastChangeTime(appointment);
                    var entityVersion = new EntityVersion<AppointmentId, DateTime>(appointmentId, changeTime);
                    return new AppointmentSlim(entityVersion, 
                        appointment.Start, appointment.End, appointment.Subject);
                }
            }
            catch (System.Exception ex)
            {
                s_logger.Error($"Could not fetch AppointmentItem '{entryId}', skipping.", ex);
                return null;
            }
        }
    }
}
