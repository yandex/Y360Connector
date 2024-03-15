using System;
using System.Collections.Generic;
using System.IO;

namespace Y360OutlookConnector.Configuration
{
    class SyncConfigController
    {
        private const string SyncConfigFileName = "sync_config.xml";

        private readonly string _dataFolderPath;

        private Dictionary<string, List<SyncTargetConfig>> _configs;
        private string _currentUser;

        private string FilePath { get => Path.Combine(_dataFolderPath, SyncConfigFileName); }

        public SyncConfigController(string dataFolderPath)
        {
            if (String.IsNullOrEmpty(dataFolderPath)) throw new ArgumentException("data folder path is empty");
            _dataFolderPath = dataFolderPath;

            Load();
        }

        public void SelectUser(string userName)
        {
            if (String.IsNullOrEmpty(userName)) throw new ArgumentException("username is empty");
            _currentUser = userName;
        }

        public void SetConfig(List<SyncTargetConfig> syncTargets)
        {
            if (String.IsNullOrEmpty(_currentUser)) throw new ArgumentException("user not selected");

            _configs[_currentUser] = syncTargets.ConvertAll(x => x.Clone());

            Save();
        }

        public SyncTargetConfig GetSyncTargetById(Guid id)
        {
            if (String.IsNullOrEmpty(_currentUser)) throw new ArgumentException("user not selected");

            if (!_configs.TryGetValue(_currentUser, out var targetsList))
                return null;

            var syncTarget = targetsList.Find(x => x.Id == id);
            return syncTarget?.Clone();
        }

        public SyncTargetConfig GetSyncTargetByUrl(Uri url)
        {
            if (String.IsNullOrEmpty(_currentUser)) throw new ArgumentException("user not selected");

            if (!_configs.TryGetValue(_currentUser, out var targetsList))
                return null;

            var syncTarget = targetsList.Find(x => String.Equals(x.Url, url.ToString(), StringComparison.OrdinalIgnoreCase));
            return syncTarget?.Clone();
        }

        public bool IsFolderInUseByOtherUsers(string entryId, string storeId)
        {
            if (String.IsNullOrEmpty(_currentUser)) throw new ArgumentException("user not selected");

            foreach (var item in _configs)
            {
                if (String.Equals(item.Key, _currentUser, StringComparison.OrdinalIgnoreCase)) continue;

                var targetList = item.Value;
                if (targetList == null) continue;

                var target = targetList.Find(x =>
                    x.OutlookFolderEntryId == entryId && x.OutlookFolderStoreId == storeId);
                if (target != null)
                    return true;
            }

            return false;
        }

        public void Save()
        {
            var data = new SyncConfig { Configs = new List<SyncConfig.UserConfig>() };
            foreach (var item in _configs)
            {
                if (!String.IsNullOrEmpty(item.Key) && item.Value != null)
                    data.Configs.Add(new SyncConfig.UserConfig { User = item.Key, SyncTargets = item.Value });
            }

            XmlFile.Save(FilePath, data);
        }

        private void Load()
        {
            _configs = new Dictionary<string, List<SyncTargetConfig>>(StringComparer.OrdinalIgnoreCase);

            var data = XmlFile.Load<SyncConfig>(FilePath);
            foreach (var item in data.Configs)
            {
                _configs[item.User] = item.SyncTargets;
            }
        }
    }
}
