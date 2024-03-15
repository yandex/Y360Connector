using CalDavSynchronizer.Ui.ConnectionTests;
using System;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector.Synchronization
{
    public class SyncTargetInfo
    {
        public SyncTargetConfig Config { get; private set; }

        public Guid Id => Config.Id;
        public string Name { get; set; }
        public AccessPrivileges Privileges { get; set; }
        public SyncTargetType TargetType { get; set; }
        public bool IsReadOnly => !Privileges.HasFlag(AccessPrivileges.All);
        public bool IsPrimary { get; set; }

        public SyncTargetInfo(SyncTargetConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public SyncTargetInfo Clone()
        {
            var clone = (SyncTargetInfo)MemberwiseClone();
            clone.Config = Config.Clone();
            return clone;
        }
    }
}
