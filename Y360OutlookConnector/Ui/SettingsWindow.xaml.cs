using CalDavSynchronizer.Contracts;
using CalDavSynchronizer.Utilities;
using log4net;
using log4net.Appender;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Synchronization;
using Y360OutlookConnector.Utilities;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Y360OutlookConnector.Ui
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow
    {
        private ProxyOptions _proxyOptions;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static SettingsWindow s_instance;

        private SettingsWindow()
        {
            InitializeComponent();

            IsVisibleChanged += SettingsWindow_IsVisibleChanged;

            var loginController = ThisAddIn.Components?.LoginController;
            if (loginController != null)
            {
                loginController.LoginStateChanged += LoginController_LoginStateChanged;
                SetUserInfo(loginController.UserInfo);
            }

            var proxyOptions = ThisAddIn.Components?.ProxyOptionsProvider.GetProxyOptions();
            var generalOptions = ThisAddIn.Components?.GeneralOptionsProvider.Options;

            SetProxyOption(proxyOptions);
            SetGeneralOption(generalOptions);

            if (ThisAddIn.Components != null)
                ThisAddIn.Components.SyncStatus.CriticalErrorChanged += SyncStatus_CriticalErrorChanged;
            UpdateProxyErrorPanel();
        }

        public static void ShowOrActivate()
        {
            if (s_instance == null)
            {
                s_instance = new SettingsWindow();
                s_instance.Closed += (o, e) => s_instance = null;
                s_instance.Show();
            }
            else
            {
                s_instance.Activate();
            }

            DumpAllOutlookFolders();
        }

        private void SettingsWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
                Telemetry.Signal(Telemetry.SettingsWindowEvents, "shown");
        }

        private void LoginController_LoginStateChanged(object sender, LoginStateEventArgs e)
        {
            var loginController = ThisAddIn.Components.LoginController;
            SetUserInfo(loginController.UserInfo);
        }

        private void SyncStatus_CriticalErrorChanged(object sender, Synchronization.CriticalErrorChangedEventArgs e)
        {
            UpdateProxyErrorPanel();
        }

        private void SetUserInfo(UserInfo userInfo)
        {
            bool userLoggedIn = !String.IsNullOrEmpty(userInfo.Email);
            
            LoggedOutPanel.Visibility = userLoggedIn ? Visibility.Collapsed : Visibility.Visible;
            LoggedInPanel.Visibility = userLoggedIn ? Visibility.Visible : Visibility.Collapsed;

            UserNameLabel.Text = userInfo.RealName;
            EmailLabel.Text = userInfo.Email;
            if (userLoggedIn)
                LoadAvatar(userInfo);
        }

        private void UpdateProxyErrorPanel()
        {
            var criticalError = ThisAddIn.Components.SyncStatus.CriticalError;
            switch (criticalError)
            {
                case CriticalError.ProxyConnectFailure:
                    ProxyErrorText.Text = Localization.Strings.SettingsWindow_ProxyErrorServer;
                    ProxyErrorPanel.Visibility = Visibility.Visible;
                    break;
                case CriticalError.ProxyAuthFailure:
                    ProxyErrorText.Text = Localization.Strings.SettingsWindow_ProxyErrorLogin;
                    ProxyErrorPanel.Visibility = Visibility.Visible;
                    break;
                default:
                    ProxyErrorPanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        #region General settings

        private bool _isInitializingGeneralOptions;

        private void SetGeneralOption(GeneralOptions options)
        {
            _isInitializingGeneralOptions = true;
            IncludeDebugLevelInfoCheckbox.IsChecked = options.UseDebugLevelLogging;
            UseExternalBrowserForLoginCheckbox.IsChecked = options.IsExternalBrowserUsedInLogin;
            FormatFileAsLastNameFirstCheckbox.IsChecked = options.FormatFileAsLastNameFirst;
            _isInitializingGeneralOptions = false;
        }

        private void IncludeDebugLevelInfoCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            var provider = ThisAddIn.Components?.GeneralOptionsProvider;

            var useDebugLevel = IncludeDebugLevelInfoCheckbox.IsChecked ?? false;

            LoggingUtils.ConfigureLogLevel(useDebugLevel);

            if (provider != null)
            {
                var options = provider.Options.Clone();
                options.UseDebugLevelLogging = useDebugLevel;
                provider.Options = options;
            }
        }

        private void UseExternalBrowserForLoginCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            var provider = ThisAddIn.Components?.GeneralOptionsProvider;

            var useExternalBrowserForLogin = UseExternalBrowserForLoginCheckbox.IsChecked ?? false;

            if (provider != null)
            {
                var options = provider.Options.Clone();
                options.IsExternalBrowserUsedInLogin = useExternalBrowserForLogin;
                provider.Options = options;
            }
        }

        private void FormatFileAsLastNameFirstCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingGeneralOptions)
            {
                s_logger.Info($"FormatFileAsLastNameFirstCheckbox_Changed: ignored because _isInitializingGeneralOptions is true");
                return;
            }

            var provider = ThisAddIn.Components?.GeneralOptionsProvider;
            var formatFileAsLastNameFirst = FormatFileAsLastNameFirstCheckbox.IsChecked ?? false;

            s_logger.Info($"FormatFileAsLastNameFirstCheckbox_Changed: handler entered, formatFileAsLastNameFirst={formatFileAsLastNameFirst}");

            if (provider != null)
            {
                var options = provider.Options.Clone();
                options.FormatFileAsLastNameFirst = formatFileAsLastNameFirst;
                provider.Options = options;

                if (formatFileAsLastNameFirst)
                {
                    s_logger.Info($"FormatFileAsLastNameFirstCheckbox_Changed: calling UpdateAllContactsFullName(true)");
                    UpdateAllContactsFullName(true);
                }
                else
                {
                    var syncManager = ThisAddIn.Components?.SyncManager;
                    if (syncManager != null)
                    {
                        s_logger.Info($"FormatFileAsLastNameFirstCheckbox_Changed: calling RestoreContactsFromServerAsync()");
                        _ = syncManager.RestoreContactsFromServerAsync();
                    }
                }
            }
        }

        internal static void UpdateAllContactsFullName(bool formatAsLastNameFirst)
        {
            try
            {
                s_logger.Info($"UpdateAllContactsFullName: entered, formatAsLastNameFirst={formatAsLastNameFirst}");
                var session = ThisAddIn.Components?.OutlookApplication?.Session;
                if (session == null)
                {
                    s_logger.Warn("UpdateAllContactsFullName: Outlook session is null, aborting");
                    return;
                }

                Outlook.Stores stores = null;
                try
                {
                    stores = session.Stores;
                    if (stores == null)
                    {
                        s_logger.Warn("UpdateAllContactsFullName: Outlook stores is null, aborting");
                        return;
                    }

                    for (int i = 1; i <= stores.Count; i++)
                    {
                        Outlook.Store store = null;
                        Outlook.Folder rootFolder = null;
                        try
                        {
                            var storeItem = stores[i];
                            store = storeItem as Outlook.Store;
                            if (store == null)
                            {
                                Marshal.ReleaseComObject(storeItem);
                                continue;
                            }

                            rootFolder = store.GetRootFolder() as Outlook.Folder;
                            if (rootFolder == null)
                            {
                                s_logger.Debug($"UpdateAllContactsFullName: store '{store.DisplayName}' has null root folder, skipping");
                                Marshal.ReleaseComObject(store);
                                store = null;
                                continue;
                            }

                            s_logger.Info(
                                $"UpdateAllContactsFullName: scanning store '{store.DisplayName}', root folder '{rootFolder.Name}', " +
                                $"EntryID='{rootFolder.EntryID}', StoreID='{rootFolder.StoreID}'");

                            UpdateContactsInSubfolders(rootFolder, formatAsLastNameFirst);

                            Marshal.ReleaseComObject(rootFolder);
                            rootFolder = null;
                        }
                        catch (Exception exStore)
                        {
                            s_logger.Error($"UpdateAllContactsFullName: error while processing store '{store?.DisplayName ?? "unknown"}'", exStore);
                        }
                        finally
                        {
                            if (rootFolder != null)
                            {
                                Marshal.ReleaseComObject(rootFolder);
                            }
                            if (store != null)
                            {
                                Marshal.ReleaseComObject(store);
                            }
                        }
                    }
                }
                finally
                {
                    if (stores != null)
                    {
                        Marshal.ReleaseComObject(stores);
                    }
                }
            }
            catch (Exception ex)
            {
                s_logger.Error("Failed to update contacts FullName", ex);
            }
        }

        internal static void UpdateContactsInFolder(Outlook.Folder folder, bool formatAsLastNameFirst)
        {
            if (folder == null)
            {
                return;
            }

            if (!formatAsLastNameFirst)
            {
                s_logger.Debug($"UpdateContactsInFolder: formatAsLastNameFirst is false, skipping folder '{folder.Name}'");
                return;
            }

            try
            {
                var items = folder.Items;
                if (items == null)
                {
                    return;
                }

                s_logger.Debug($"UpdateContactsInFolders: checking folder '{folder.Name}', EntryID='{folder.EntryID}', StoreID='{folder.StoreID}', ItemsCount='{items.Count}'");

                for (int i = 1; i <= items.Count; i++)
                {
                    Outlook.ContactItem contact = null;
                    try
                    {
                        var item = items[i];
                        contact = item as Outlook.ContactItem;
                        if (contact == null)
                        {
                            Marshal.ReleaseComObject(item);
                            continue;
                        }

                        var firstName = contact.FirstName ?? String.Empty;
                        var middleName = contact.MiddleName ?? String.Empty;
                        var lastName = contact.LastName ?? String.Empty;

                        var parts = new List<string>();
                        if (formatAsLastNameFirst)
                        {
                            if (!String.IsNullOrEmpty(lastName))
                            {
                                parts.Add(lastName);
                            }
                            if (!String.IsNullOrEmpty(firstName))
                            {
                                parts.Add(firstName);
                            }
                            if (!String.IsNullOrEmpty(middleName))
                            {
                                parts.Add(middleName);
                            }

                            if (parts.Count > 0)
                            {
                                var formattedName = String.Join(" ", parts);
                                bool changed = false;

                                if (!String.Equals(contact.FirstName ?? String.Empty, formattedName, StringComparison.Ordinal))
                                {
                                    contact.FirstName = formattedName;
                                    changed = true;
                                }
                                if (!String.IsNullOrEmpty(contact.MiddleName) || !String.IsNullOrEmpty(contact.LastName))
                                {
                                    contact.MiddleName = String.Empty;
                                    contact.LastName = String.Empty;
                                    changed = true;
                                }

                                if (changed)
                                {
                                    try 
                                    {
                                        contact.Save();
                                    }
                                    catch (Exception ex)
                                    {
                                        s_logger.Warn($"Failed to save contact in folder '{folder.Name}'", ex);
                                    }
                                }
                            }
                        }  
                    }
                    finally
                    {
                        if (contact != null)
                        {
                            Marshal.ReleaseComObject(contact);
                        }
                    }
                }

                Marshal.ReleaseComObject(items);
            }
            catch (Exception ex)
            {
                s_logger.Error("Failed to update contacts full names", ex);
            }
        }

        internal static void UpdateContactsInSubfolders(Outlook.Folder parentFolder, bool formatAsLastNameFirst)
        {
            if (parentFolder == null)
            {
                return;
            }

            try
            {
                var subfolders = parentFolder.Folders;
                if (subfolders == null)
                {
                    return;
                }

                for (int i = 1; i <= subfolders.Count; i++)
                {
                    Outlook.Folder subfolder = null;
                    try
                    {
                        var folderItem = subfolders[i];
                        subfolder = folderItem as Outlook.Folder;
                        if (subfolder == null)
                        {
                            Marshal.ReleaseComObject(folderItem);
                            continue;
                        }

                        s_logger.Debug($"UpdateContactsInSubfolders: checking folder '{subfolder.Name}', EntryID='{subfolder.EntryID}', StoreID='{subfolder.StoreID}', DefaultItemType='{subfolder.DefaultItemType}'");

                        bool isTargetedFolder = false;
                        var syncManager = ThisAddIn.Components?.SyncManager;
                        if (subfolder.DefaultItemType == Outlook.OlItemType.olContactItem)
                        {
                            if (syncManager != null)
                            {
                                isTargetedFolder = syncManager.IsSharedOrExternalContactsFolder(subfolder.EntryID, subfolder.StoreID);
                            }
                            else
                            {
                                s_logger.Debug("UpdateContactsInSubfolders: SyncManager is null, cannot determine if folder is targeted");
                            }
                        }

                        s_logger.Debug($"UpdateContactsInSubfolders: isTargetedFolder={isTargetedFolder} for '{subfolder.Name}'");

                        if (isTargetedFolder)
                        {
                            s_logger.Info($"UpdateContactsInSubfolders: Updating contacts full names in folder '{subfolder.Name}'");
                            UpdateContactsInFolder(subfolder, formatAsLastNameFirst);
                        }

                        UpdateContactsInSubfolders(subfolder, formatAsLastNameFirst);

                        Marshal.ReleaseComObject(subfolder);
                        subfolder = null;
                    }
                    catch (Exception ex)
                    {
                        s_logger.Error($"UpdateContactsInSubfolders: error while processing subfolder {subfolder?.Name ?? "unknown"}", ex);
                    }
                    finally
                    {
                        if (subfolder != null)
                        {
                            Marshal.ReleaseComObject(subfolder);
                        }
                    }
                }

                Marshal.ReleaseComObject(subfolders);
            }
            catch (Exception ex)
            {
                s_logger.Error($"Failed to update contacts full names in subfolders of {parentFolder.Name}", ex);
            }
        }

        #endregion
        private void SetProxyOption(ProxyOptions proxyOptions)
        {
            _proxyOptions = proxyOptions ?? new ProxyOptions { ProxyUseDefault = true };
            ProxyManualCheckbox.IsChecked = _proxyOptions.ProxyUseManual;
            ProxyUrlEdit.Text = _proxyOptions.ProxyUrl;
            ProxyUserNameEdit.Text = _proxyOptions.ProxyUserName;
            ProxyPasswordEdit.Password = SecureStringUtility.ToUnsecureString(_proxyOptions.ProxyPassword);
        }

        private static void ShowLogFileWithoutWarning()
        {
            var fileAppender = s_logger.Logger.Repository.GetAppenders()
                .FirstOrDefault(appender => appender is FileAppender) as FileAppender;

            try
            {
                var filePath = fileAppender?.File;
                if (!String.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    var argument = "/select, \"" + filePath + "\"";
                    Process.Start("explorer.exe", argument);
                }
            }
            catch (Exception x)
            {
                s_logger.Error("Show log failed:", x);
            }
        }

        private void ShowLogsLink_OnClick(object sender, RoutedEventArgs e)
        {
            Telemetry.Signal(Telemetry.SettingsWindowEvents, "show_logs_link");

            MessageBox.Show(
                Localization.Strings.SettingsWindow_LogShowWarning,
                Localization.Strings.Messages_ProductName, 
                MessageBoxButton.OK, 
                MessageBoxImage.Warning
            );
            ShowLogFileWithoutWarning();
        }

        private void ClearLogsLink_OnClick(object sender, RoutedEventArgs e)
        {
            Telemetry.Signal(Telemetry.SettingsWindowEvents, "clear_logs_link");

            var fileAppender = s_logger.Logger.Repository.GetAppenders()
                .FirstOrDefault(appender => appender is FileAppender) as FileAppender;

            if (fileAppender is RollingLogAppender rollingLogAppender)
            {
                rollingLogAppender.ClearLogs();
            }
            else if (fileAppender != null)
            {
                FileStream fs = null;
                try
                {
                    fs = new FileStream(fileAppender.File, FileMode.Create);
                }
                catch (Exception ex)
                {
                    s_logger.Error("Could not clear the log file!", ex);
                }
                finally
                {
                    fs?.Close();
                }
            }
        }

        private void LoginButton_OnClick(object sender, RoutedEventArgs e)
        {
            Telemetry.Signal(Telemetry.SettingsWindowEvents, "login_button");

            Close();
            ThisAddIn.Components?.StartLogin();
        }

        private void LogoutButton_OnClick(object sender, RoutedEventArgs e)
        {
            Telemetry.Signal(Telemetry.SettingsWindowEvents, "logout_button");

            var loginController = ThisAddIn.Components?.LoginController;
            loginController?.Logout();
        }

        private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
        {
            Telemetry.Signal(Telemetry.SettingsWindowEvents, "apply_button");

            if (ProxyManualCheckbox.IsChecked == true)
            {
                if (!ValidateProxyUrl())
                    return;

                Telemetry.Signal(Telemetry.SettingsWindowEvents, "manual_proxy_settings_on");
                if (!String.IsNullOrEmpty(ProxyUserNameEdit.Text))
                    Telemetry.Signal(Telemetry.SettingsWindowEvents, "manual_proxy_auth_used");
            }
            else
            {
                Telemetry.Signal(Telemetry.SettingsWindowEvents, "manual_proxy_settings_off");
            }

            var newProxyOptions = GetProxyOptionsFromUi();
            _proxyOptions = newProxyOptions;

            var proxyOptionsProvider = ThisAddIn.Components?.ProxyOptionsProvider;
            proxyOptionsProvider?.SetProxyOptions(_proxyOptions);
            ButtonsPanel.Visibility = Visibility.Collapsed;

            ProxyErrorPanel.Visibility = Visibility.Collapsed;
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            Telemetry.Signal(Telemetry.SettingsWindowEvents, "cancel_button");

            SetProxyOption(_proxyOptions);
            ButtonsPanel.Visibility = Visibility.Collapsed;
        }

        private void ProxyManualCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateButtonsPanelVisibility();
            ClearProxyUrlErrorMessage();
        }

        private void ProxyUrlEdit_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateButtonsPanelVisibility();
            ClearProxyUrlErrorMessage();
        }

        private void ProxyUserNameEdit_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateButtonsPanelVisibility();
        }

        private void ProxyPasswordEdit_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateButtonsPanelVisibility();
        }

        private ProxyOptions GetProxyOptionsFromUi()
        {
            return new ProxyOptions
            {
                ProxyUseManual = ProxyManualCheckbox.IsChecked == true,
                ProxyUseDefault = ProxyManualCheckbox.IsChecked == false,
                ProxyUrl = ProxyUrlEdit.Text,
                ProxyUserName = ProxyUserNameEdit.Text,
                ProxyPassword = SecureStringUtility.ToSecureString(ProxyPasswordEdit.Password)
            };
        }

        private bool AreProxyOptionsModified(ProxyOptions newProxyOptions)
        {
            if (!_proxyOptions.ProxyUseManual && !newProxyOptions.ProxyUseManual)
                return false;

            return _proxyOptions.ProxyUseManual != newProxyOptions.ProxyUseManual
                || _proxyOptions.ProxyUrl != newProxyOptions.ProxyUrl
                || _proxyOptions.ProxyUserName != newProxyOptions.ProxyUserName
                || SecureStringUtility.ToUnsecureString(_proxyOptions.ProxyPassword) != SecureStringUtility.ToUnsecureString(newProxyOptions.ProxyPassword);
        }

        private void UpdateButtonsPanelVisibility()
        {
            bool modified = AreProxyOptionsModified(GetProxyOptionsFromUi());
            ButtonsPanel.Visibility = modified ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool ValidateProxyUrl()
        {
            bool result = true;
            var errorMessage = String.Empty;
            var proxyUrlText = ProxyUrlEdit.Text;
            if (String.IsNullOrEmpty(proxyUrlText))
            {
                errorMessage = Localization.Strings.SettingsWindow_ProxyUrlErrorEmpty;
            }
            else
            {
                Uri proxyUrl;
                if (Uri.TryCreate(proxyUrlText, UriKind.Absolute, out proxyUrl)
                    || Uri.TryCreate("http://" + proxyUrlText, UriKind.Absolute, out proxyUrl))
                {
                    if (proxyUrl.Scheme != Uri.UriSchemeHttp)
                    {
                        errorMessage = Localization.Strings.SettingsWindow_ProxyUrlErrorNonHttp;
                    }
                    else
                    {
                        var fixedUrl = new Uri($"{Uri.UriSchemeHttp}://{proxyUrl.Authority}");
                        ProxyUrlEdit.Text = fixedUrl.ToString();
                    }
                }
                else
                {
                    errorMessage = Localization.Strings.SettingsWindow_ProxyUrlErrorInvalidValue;
                }
            }

            if (!String.IsNullOrEmpty(errorMessage))
            {
                SetProxyUrlErrorMessage(errorMessage);
                result = false;
            }
            else
            {
                ClearProxyUrlErrorMessage();
                result = true;
            }

            return result;
        }

        private void SetProxyUrlErrorMessage(string errorMessage)
        {
            ProxyUrlErrorLabel.Text = errorMessage;
            if (!String.IsNullOrEmpty(errorMessage))
            {
                ProxyUrlErrorLabel.Visibility = Visibility.Visible;
                ProxyUrlEdit.BorderBrush =
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 55, 55));
            }
            else
            {
                ProxyUrlErrorLabel.Visibility = Visibility.Collapsed;
                ProxyUrlEdit.ClearValue(System.Windows.Controls.Border.BorderBrushProperty);
            }
        }

        private void ClearProxyUrlErrorMessage()
        {
            SetProxyUrlErrorMessage(String.Empty);
        }

        private void LoadAvatar(UserInfo userInfo)
        {
            var result = LoginController.DownloadAvatar(userInfo.DefaultAvatarId);
            result.ContinueWith(task =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var bitmapImage = new BitmapImage(new Uri(task.Result));
                        UserAvatarImage.ImageSource = bitmapImage;
                    }
                    catch (Exception ex)
                    {
                        Telemetry.Signal(Telemetry.SettingsWindowEvents, "avatar_load_failure");
                        s_logger.Error("Failed to load user avatar", ex);
                    }
                });
            });
        }

        private static void DumpAllOutlookFolders()
        {
            try
            {
                var app = ThisAddIn.Components?.OutlookApplication;
                var session = app?.Session;
                if (session == null)
                    return;

                foreach (Outlook.Store store in session.Stores)
                {
                    try
                    {
                        var root = store.GetRootFolder() as Outlook.Folder;
                        if (root == null)
                            continue;

                        DumpFolderRecursive(root, store.DisplayName, "");
                        Marshal.ReleaseComObject(root);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(store);
                    }
                }
            }
            catch (Exception ex)
            {
                s_logger.Error("DumpAllOutlookFolders failed", ex);
            }
        }

        private static void DumpFolderRecursive(Outlook.Folder folder, string storeName, string indent)
        {
            try
            {
                var items = folder.Items;
                s_logger.Info(
                    $"{indent}Store='{storeName}', " +
                    $"Folder='{folder.Name}', " +
                    $"Path='{folder.FolderPath}', " +
                    $"DefaultItemType='{folder.DefaultItemType}', " +
                    $"EntryID='{folder.EntryID}', " +
                    $"StoreID='{folder.StoreID}', " +
                    $"Items='{items?.Count ?? 0}'");

                Marshal.ReleaseComObject(items);

                var subfolders = folder.Folders;
                foreach (Outlook.Folder sub in subfolders)
                {
                    DumpFolderRecursive(sub, storeName, indent + "  ");
                    Marshal.ReleaseComObject(sub);
                }
                Marshal.ReleaseComObject(subfolders);
            }
            catch (Exception ex)
            {
                s_logger.Error($"DumpFolderRecursive failed for '{folder.Name}'", ex);
            }
        }
    }
}
