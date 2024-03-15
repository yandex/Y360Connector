using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;

namespace Y360OutlookConnector.Configuration
{
    public class DataFolder
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public static string GetPathForProfile(string profileName)
        {
            if (String.IsNullOrEmpty(profileName))
                s_logger.Error("profile name is empty");

            var dataFolderBase = GetRootPath();
            var filePath = Path.Combine(dataFolderBase, "profiles.xml");
            var profileList = XmlFile.Load<List<ProfileEntry>>(filePath);

            string folderPath;
            var profileEntry = profileList.Find(
                x => String.Equals(x.Profile, profileName, StringComparison.OrdinalIgnoreCase));
            if (profileEntry == null)
            {
                var folderName = Guid.NewGuid().ToString("N");
                folderPath = Path.Combine(dataFolderBase, folderName);
                Directory.CreateDirectory(folderPath);
                s_logger.Info($"Creating data folder for profile {profileName}: {folderPath}");

                profileList.Add(new ProfileEntry{ Folder = folderName, Profile = profileName });
                XmlFile.Save(filePath, profileList);
            }
            else
            {
                folderPath = Path.Combine(dataFolderBase, profileEntry.Folder);
                s_logger.Info($"Data folder path for profile {profileName}: {folderPath}");
            }

            return folderPath;
        }

        public static string GetRootPath(bool useRoamingFolder = false)
        {
            var appData = Environment.GetFolderPath(useRoamingFolder ? 
                Environment.SpecialFolder.ApplicationData : Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "Yandex", "Y360.OutlookConnector", "data");
        }
    }

    public class ProfileEntry
    {
        [XmlAttribute]
        public string Profile { get; set; }

        [XmlAttribute]
        public string Folder { get; set; }
    }
}
