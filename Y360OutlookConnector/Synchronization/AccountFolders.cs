using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CalDavSynchronizer.Ui;
using log4net;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Utilities;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Synchronization
{
    public class AccountFolders
    {
        private readonly Outlook.NameSpace _session;
        private readonly Outlook.Account _account;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private enum AccountMatchKind
        {
            NoSessionAccounts,
            MatchedBySmtp,
            MatchedByLocalPart,
            FallbackToFirstAccount
        }

        public AccountFolders(string userEmail, Outlook.NameSpace session)
        {
            _session = session;
            AccountMatchKind matchKind;
            _account = FindBestMatchAccount(userEmail, out matchKind);

            switch (matchKind)
            {
                case AccountMatchKind.MatchedBySmtp:
                case AccountMatchKind.MatchedByLocalPart:
                    s_logger.Debug($"Matched Outlook account ({matchKind}): {_account.SmtpAddress}");
                    break;
                case AccountMatchKind.FallbackToFirstAccount:
                    s_logger.WarnFormat("No Outlook account matched Connector user email by SMTP or local part; " +
                        "using first account in Session.Accounts as fallback. " +
                        "Connector user email: '{0}', chosen SmtpAddress: '{1}', Session.Accounts.Count: {2}.",
                        userEmail ?? String.Empty,
                        _account != null ? (_account.SmtpAddress ?? String.Empty) : String.Empty,
                        _session.Accounts.Count);
                    s_logger.Debug($"Fallback Outlook account: {_account.SmtpAddress}");
                    break;
                case AccountMatchKind.NoSessionAccounts:
                    s_logger.WarnFormat(
                        "Session.Accounts is empty; cannot bind to a mailbox account. " +
                        "Connector user email: '{0}'. Default session folders will be used where applicable.",
                        userEmail ?? String.Empty);
                    break;
            }
        }

        public Outlook.MAPIFolder GetRootFolder()
        {
            try
            {
                return _account?.DeliveryStore?.GetRootFolder() ?? _session.DefaultStore.GetRootFolder();
            }
            catch (COMException ex)
            {
                s_logger.Error("Failed to get root folder via account DeliveryStore, falling back to DefaultStore", ex);
                return _session.DefaultStore.GetRootFolder();
            }
        }

        public string CreateNewFolderName(SyncTargetType targetType, string baseName)
        {
            var parentFolder = GetDefaultFolder(targetType) ?? GetRootFolder();

            for (int i = 1; i < 100; ++i)
            {
                string name = (i > 1) ? $"{baseName} ({i})" : baseName;
                if (!HasSubfolder(parentFolder, name))
                {
                    return name;
                }
            }

            return null;
        }

        public Outlook.MAPIFolder CreateNewFolder(SyncTargetType targetType, string baseName)
        {
            string newFolderName = String.Empty;
            try
            {
                var parentFolder = GetDefaultFolder(targetType) ?? GetRootFolder();

                newFolderName = CreateNewFolderName(targetType, baseName);
                if (String.IsNullOrEmpty(newFolderName))
                    return null;

                var newFolder = parentFolder.Folders.Add(newFolderName, ToOlDefaultFolders(targetType));
                if (targetType == SyncTargetType.Contacts)
                    newFolder.ShowAsOutlookAB = true;

                return newFolder;
            }
            catch (Exception exc)
            {
                s_logger.Error($"Failed to create folder {newFolderName}", exc);
                return null;
            }
        }

        public OutlookFolderDescriptor GetDefaultFolderDescriptor(SyncTargetType targetType)
        {
            var folder = GetDefaultFolder(targetType);
            return folder != null ? new OutlookFolderDescriptor(folder) : null;
        }

        private Outlook.MAPIFolder GetDefaultFolder(SyncTargetType targetType)
        {
            Outlook.MAPIFolder result = null;
            try
            {
                switch (targetType)
                {
                    case SyncTargetType.Calendar:
                        result = _account?.DeliveryStore?.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderCalendar) ??
                                 _session.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderCalendar);
                        break;
                    case SyncTargetType.Contacts:
                        result = _account?.DeliveryStore?.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderContacts) ??
                                 _session.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderContacts);
                        break;
                    case SyncTargetType.Tasks:
                        result = _account?.DeliveryStore?.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderTasks) ??
                                 _session.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderTasks);
                        break;
                }
            }
            catch (Exception exc)
            {
                s_logger.Error($"Failed to get default folder for {targetType}", exc);
            }
            return result;
        }

        private Outlook.Account FindBestMatchAccount(string userEmail, out AccountMatchKind matchKind)
        {
            // First pass - compare the whole email addresses
            foreach (Outlook.Account account in _session.Accounts)
            {
                try
                {
                    if (account.DeliveryStore == null) continue;
                }
                catch (Exception exc)
                {
                    s_logger.Error($"Failed to retrieve delivery store for account {account.UserName}", exc);
                }

                if (EmailAddress.AreSame(userEmail, account.SmtpAddress))
                {
                    Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "suitable_account_found");
                    matchKind = AccountMatchKind.MatchedBySmtp;
                    return account;
                }
            }

            // Second pass - compare only the left parts of email addresses
            foreach (Outlook.Account account in _session.Accounts)
            {
                try
                {
                    if (account.DeliveryStore == null)
                    {
                        continue;
                    }
                }
                catch (Exception exc)
                {
                    s_logger.Error($"Failed to retrieve delivery store for account {account.UserName}", exc);
                    continue;
                }

                var userNameId = EmailAddress.Parse(userEmail).Normalize().NameId;
                var accountNameId = EmailAddress.Parse(account.SmtpAddress).Normalize().NameId;
                if (String.Equals(userNameId, accountNameId, StringComparison.OrdinalIgnoreCase))
                {
                    Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "suitable_account_found");
                    matchKind = AccountMatchKind.MatchedByLocalPart;
                    return account;
                }
            }

            if (_session.Accounts.Count > 0)
            {
                Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "suitable_account_fallback_first");
                matchKind = AccountMatchKind.FallbackToFirstAccount;
                return _session.Accounts[1];
            }

            Telemetry.Signal(Telemetry.SyncConfigWindowEvents, "suitable_account_not_found");
            matchKind = AccountMatchKind.NoSessionAccounts;
            return null;
        }

        public static bool IsFolderTrashed(Outlook.MAPIFolder folder)
        {
            var store = folder?.Store;

            var trashFolder = store?.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderDeletedItems);
            if (trashFolder == null) return false;

            var parentFolder = folder.Parent as Outlook.MAPIFolder;
            while (parentFolder != null)
            {
                if (parentFolder.EntryID == trashFolder.EntryID && parentFolder.StoreID == trashFolder.StoreID)
                    return true;

                parentFolder = parentFolder.Parent as Outlook.MAPIFolder;
            }

            return false;
        }

        private static Outlook.OlDefaultFolders ToOlDefaultFolders(SyncTargetType targetType)
        {
            switch (targetType)
            {
                case SyncTargetType.Contacts:
                    return Outlook.OlDefaultFolders.olFolderContacts;
                case SyncTargetType.Tasks:
                    return Outlook.OlDefaultFolders.olFolderTasks;
                case SyncTargetType.Calendar:
                    return Outlook.OlDefaultFolders.olFolderCalendar;
                default:
                    return Outlook.OlDefaultFolders.olFolderCalendar;
            }
        }

        private static bool HasSubfolder(Outlook.MAPIFolder folder, string nameToCheck)
        {
            foreach (var entry in folder.Folders)
            {
                if (entry is Outlook.MAPIFolder subfolder)
                {
                    string subfolderName = subfolder.Name ?? "";

                    if (subfolderName == nameToCheck)
                        return true;

                    // "Folder name (This computer only)"
                    var regex = new Regex(Regex.Escape(nameToCheck) + @"\s\(\D+?\)$");
                    if (regex.IsMatch(subfolderName))
                        return true;
                }
            }
            return false;
        }
    }
}
