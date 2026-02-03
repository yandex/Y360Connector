using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CalDavSynchronizer.ChangeWatching;
using log4net;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization.Synchronizer.FolderMonitor
{
    public class GenericFolderMonitor : FolderMonitorBase
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public GenericFolderMonitor(Outlook.Folder folder) 
            : base(folder)
        {
        }

        protected override void HandleItem(object item, ItemAction action)
        {
            try
            {
                bool wasDeleted = action == ItemAction.Remove;

                IOutlookId entryId = null;
                switch (item)
                {
                    case Outlook.TaskItem task:
                        s_logger.Debug($"'{nameof(ItemAction)}.{action}': Task '{task.Subject}' '{task.EntryID}' ");
                        entryId = new GenericId(task.EntryID, task.LastModificationTime.ToUniversalTime(), wasDeleted);
                        break;
                    case Outlook.ContactItem contact:
                        s_logger.Debug($"'{nameof(ItemAction)}.{action}': Contact '{contact.LastNameAndFirstName}' '{contact.EntryID}' ");
                        s_logger.Debug($"Contact details: First='{contact.FirstName}', Middle='{contact.MiddleName}', Last='{contact.LastName}', Title='{contact.Title}', FileAs='{contact.FileAs}'");
                        entryId = new GenericId(contact.EntryID, contact.LastModificationTime.ToUniversalTime(), wasDeleted);
                        break;
                }

                if (entryId != null)
                {
                    OnItemChanged(entryId);
                }
            }
            catch
            {
                // no-op
            }
        }
    }
}
