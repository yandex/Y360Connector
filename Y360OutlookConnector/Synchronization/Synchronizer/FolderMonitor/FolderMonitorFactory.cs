using System;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization.Synchronizer.FolderMonitor
{
    public class FolderMonitorFactory
    {
        private readonly Outlook.NameSpace _session;
        private readonly InvitesInfoStorage _invitesInfo;

        public FolderMonitorFactory(Outlook.NameSpace session, InvitesInfoStorage invitesInfo)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _invitesInfo = invitesInfo ?? throw new ArgumentNullException(nameof(invitesInfo));
        }

        public IFolderMonitor Create(string folderEntryId, string folderStoreId)
        {
            var folder = (Outlook.Folder) _session.GetFolderFromID(folderEntryId, folderStoreId);
            if (folder.DefaultItemType == Outlook.OlItemType.olAppointmentItem)
                return new AppointmentFolderMonitor(folder, _invitesInfo);
            return new GenericFolderMonitor(folder);
        }
    }
}
