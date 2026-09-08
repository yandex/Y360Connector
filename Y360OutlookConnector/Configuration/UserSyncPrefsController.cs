using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using log4net;

namespace Y360OutlookConnector.Configuration
{
    public class UserSyncPrefs
    {
        public class UserPrefs
        {
            [XmlAttribute] public string User;
            public List<TargetPref> Targets = new List<TargetPref>();
        }

        public class TargetPref
        {
            [XmlAttribute] public string Url;
            [XmlAttribute] public bool Active;
        }

        public List<UserPrefs> Users = new List<UserPrefs>();
    }

    internal class UserSyncPrefsController
    {
        private const string PrefsFileName = "user_sync_prefs.xml";

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private readonly string _filePath;
        private UserSyncPrefs _data;

        internal UserSyncPrefsController(string dataFolderPath, SyncConfigController syncConfig)
        {
            _filePath = Path.Combine(dataFolderPath, PrefsFileName);
            _data = XmlFile.Load<UserSyncPrefs>(_filePath);

            if (!File.Exists(_filePath) || _data.Users.Count == 0)
            {
                MigrateFrom(syncConfig);
            }
        }

        // Возвращает null, если записи для данного URL нет (новый таргет — применяется дефолт)
        public bool? GetActive(string userEmail, string url)
        {
            var user = FindUser(userEmail);
            if (user == null)
            {
                return null;
            }

            var pref = user.Targets.Find(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
            if (pref == null)
            {
                return null;
            }

            return pref.Active;
        }

        public Dictionary<string, bool> GetActiveSnapshot(string userEmail)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var user = FindUser(userEmail);
            if (user == null)
            {
                return result;
            }

            foreach (var target in user.Targets)
            {
                if (!String.IsNullOrEmpty(target.Url))
                {
                    result[target.Url] = target.Active;
                }
            }

            return result;
        }

        public void SaveAll(string userEmail, List<SyncTargetConfig> configs)
        {
            var user = FindOrCreateUser(userEmail);
            user.Targets = configs.ConvertAll(c => new UserSyncPrefs.TargetPref
            {
                Url = c.Url,
                Active = c.Active
            });
            XmlFile.Save(_filePath, _data);
        }

        private void MigrateFrom(SyncConfigController syncConfig)
        {
            s_logger.Info("user_sync_prefs.xml not found, migrating active states from sync_config.xml");
            var allConfigs = syncConfig.GetAllConfigs();
            foreach (var entry in allConfigs)
            {
                if (string.IsNullOrEmpty(entry.Key) || entry.Value == null)
                {
                    continue;
                }

                var user = FindOrCreateUser(entry.Key);
                user.Targets = entry.Value.ConvertAll(c => new UserSyncPrefs.TargetPref
                {
                    Url = c.Url,
                    Active = c.Active
                });
            }
            XmlFile.Save(_filePath, _data);
        }

        private UserSyncPrefs.UserPrefs FindUser(string email)
        {
            return _data.Users.Find(u => string.Equals(u.User, email, StringComparison.OrdinalIgnoreCase));
        }

        private UserSyncPrefs.UserPrefs FindOrCreateUser(string email)
        {
            var user = FindUser(email);
            if (user == null)
            {
                user = new UserSyncPrefs.UserPrefs
                {
                    User = email
                };
                _data.Users.Add(user);
            }
            return user;
        }
    }
}
