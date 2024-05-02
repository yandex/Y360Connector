using System;
using System.Collections.Generic;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnector.Synchronization
{
    public class InvitesInfo
    {
        public class Entry
        {
            private const int LifetimeHours = 6;

            public string Uid;
            public DateTime OriginTime = DateTime.MinValue;
            public bool MarkedForDeletion = false;
            public bool IsAppointmentItemOverriden = false;
            public DateTime CreationTime = DateTime.UtcNow;

            public bool IsOutdated(DateTime timePoint)
            {
                return (timePoint - CreationTime) > TimeSpan.FromHours(LifetimeHours);
            }
        }

        public readonly List<Entry> Invites = new List<Entry>();
    }

    public class InvitesInfoStorage
    {
        private readonly InvitesInfo _info;
        private readonly string _fileName;

        public InvitesInfoStorage(string profileDataFolderPath)
        {
            _fileName = System.IO.Path.Combine(profileDataFolderPath, "invites_info.xml");
            _info = XmlFile.Load<InvitesInfo>(_fileName);
        }

        public void AddIncomingInvite(string globalAppointmentId, DateTime lastModificationTime)
        {
            if (String.IsNullOrEmpty(globalAppointmentId)) return;

            lock (_info)
            {
                var uid = AppointmentItemUtils.ExtractUidFromGlobalId(globalAppointmentId);
                var entry = _info.Invites.Find(x => x.Uid == uid);
                if (entry != null)
                {
                    entry.OriginTime = lastModificationTime;
                }
                else
                {
                    _info.Invites.Add(new InvitesInfo.Entry{ Uid = uid, OriginTime = lastModificationTime });
                }
            }
        }

        public bool FindAndSetAppointmentItemOverriden(string globalAppointmentId, DateTime lastModificationTime)
        {
            if (String.IsNullOrEmpty(globalAppointmentId)) return false;

            lock (_info)
            {
                var uid = AppointmentItemUtils.ExtractUidFromGlobalId(globalAppointmentId);
                var entry = _info.Invites.Find(x => x.Uid == uid && x.OriginTime == lastModificationTime);
                if (entry == null || entry.IsAppointmentItemOverriden) return false;

                entry.IsAppointmentItemOverriden = true;
                return true;
            }
        }

        public void OnInviteDeleted(string uid)
        {
            lock (_info)
            {
                var index = _info.Invites.FindIndex(x => x.Uid == uid);
                if (index >= 0)
                {
                    if (_info.Invites[index].MarkedForDeletion)
                        _info.Invites.RemoveAt(index);
                    else
                        _info.Invites[index].MarkedForDeletion = true;
                }
                else
                {
                    _info.Invites.Add(new InvitesInfo.Entry { Uid = uid, MarkedForDeletion = true });
                }
            }
        }

        public bool FindMarkedForDeletion(string uid)
        {
            lock (_info)
            {
                return _info.Invites.Find(x => x.Uid == uid && x.MarkedForDeletion) != null;
            }
        }

        public void CleanUp()
        {
            lock (_info)
            {
                var now = DateTime.UtcNow;
                _info.Invites.RemoveAll(x => x.IsOutdated(now));
            }
        }

        public void Save()
        {
            lock (_info)
            {
                XmlFile.Save(_fileName, _info);
            }
        }
    }
}
