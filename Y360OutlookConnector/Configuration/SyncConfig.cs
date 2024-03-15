using System;
using System.Collections.Generic;

namespace Y360OutlookConnector.Configuration
{
    public enum SyncTargetType
    {
        Calendar,
        Tasks,
        Contacts
    }

    public class SyncTargetConfig
    {
        public Guid Id { get; set; }
        public bool Active { get; set; }
        public string Url { get; set; }
        public string OutlookFolderEntryId { get; set; }
        public string OutlookFolderStoreId { get; set; }

        public SyncTargetConfig Clone()
        {
            return (SyncTargetConfig) MemberwiseClone();
        }
    }

    public class SyncConfig
    {
        public class UserConfig
        {
            public string User;
            public List<SyncTargetConfig> SyncTargets = new List<SyncTargetConfig>();
        }

        public List<UserConfig> Configs = new List<UserConfig>();
    }
}
