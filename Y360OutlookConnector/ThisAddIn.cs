using System;
using log4net;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Y360OutlookConnector.Configuration;
using Outlook = Microsoft.Office.Interop.Outlook;
using Office = Microsoft.Office.Core;
using Y360OutlookConnector.Synchronization;
using Y360OutlookConnector.Ui;
using System.Threading.Tasks;

namespace Y360OutlookConnector
{
    public partial class ThisAddIn
    {
        private static readonly int s_uiThreadId = Environment.CurrentManagedThreadId;

        public static SynchronizationContext UiContext { get; private set; }

        public static ComponentContainer Components { get; private set; }
        public static EventHandler ComponentsCreated;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private System.Windows.Forms.Timer _startupTimer;

        private IncomingInvitesMonitor _invitesMonitor;
        private InvitesInfoStorage _invitesInfo;

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            // Необходимо включить отслеживание получения приглашений при старте плагина, так как в противном
            // можем пропустить некоторые извещения
            var profileDataFolderPath = DataFolder.GetPathForProfile(Application.Session.CurrentProfileName);

             _invitesInfo = new InvitesInfoStorage(profileDataFolderPath);
            _invitesMonitor = new IncomingInvitesMonitor(Application, _invitesInfo);
            _invitesMonitor.Start();

            try
            {
                InitUiThreadContext();

                var applicationEvents = Application as Outlook.ApplicationEvents_11_Event;
                applicationEvents.Quit += ThisAddIn_ApplicationQuit;

                _startupTimer = new System.Windows.Forms.Timer();
                _startupTimer.Tick += StartupTimer_Tick;
                _startupTimer.Interval = 2000;
                _startupTimer.Enabled = true;
            }
            catch (Exception exc)
            {
                MessageBox.Show(exc.Message, Localization.Strings.Messages_ProductName, 
                    MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
            _invitesMonitor?.Dispose();
            _invitesMonitor = null;
            Components?.Dispose();
            Components = null;
        }

        private void StartupTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                InitLogging(Application);
                InitTelemetry(Application);
                InitLanguage(Application);

                _startupTimer.Enabled = false;
                _startupTimer.Dispose();
                _startupTimer = null;

                RestoreUiContext();
                Components = new ComponentContainer(Application, _invitesInfo);
                ComponentsCreated?.Invoke(this, EventArgs.Empty);
                _ = TryApplyLastFirstFromDeploymentAsync();
            }
            catch (Exception exc)
            {
                ExceptionHandler.Instance.Unexpected(exc);
            }
        }

        private void ThisAddIn_ApplicationQuit()
        {
            Components?.Dispose();
            Components = null;
        }

        public static void RestoreUiContext()
        {
            if (Environment.CurrentManagedThreadId == s_uiThreadId)
            {
                if (SynchronizationContext.Current == null)
                {
                    SynchronizationContext.SetSynchronizationContext(UiContext);
                }
            }
            else
            {
                s_logger.Warn("RestoreUiContext() should be called from the ui thread");
            }
        }

        private static void InitLogging(Outlook.Application application)
        {
            log4net.Config.XmlConfigurator.Configure();
            EnableLogToDebug();

            s_logger.Info($"Starting ({Assembly.GetExecutingAssembly().GetName().Version})...");
            s_logger.Info($"Outlook version {application.Version}, " +
                          $"Windows version {Environment.OSVersion.Version}, " +
                          $".NET CLR version {Environment.Version}");
        }

        [Conditional("DEBUG")]
        private static void EnableLogToDebug()
        {
            var debugAppender = new log4net.Appender.DebugAppender
            {
                Layout = new log4net.Layout.PatternLayout("[%thread]: %message%newline%exception")
            };
            debugAppender.ActivateOptions();

            var hierarchy = (log4net.Repository.Hierarchy.Hierarchy) LogManager.GetRepository();
            hierarchy.Root.AddAppender(debugAppender);
            hierarchy.Root.Level = log4net.Core.Level.All;
            log4net.Config.BasicConfigurator.Configure(hierarchy);
        }

        private static void InitLanguage(Outlook.Application application)
        {
            var cultureName = "en-US";

            var languageSettings = application?.LanguageSettings;
            if (languageSettings != null)
            {
                // https://learn.microsoft.com/en-us/office/vba/api/office.msolanguageid
                var langId = languageSettings.get_LanguageID(Office.MsoAppLanguageID.msoLanguageIDUI);
                if (langId == 1049)
                    cultureName = "ru-RU";
            }
            // Устанавливаем язык ресурсов приложения в один из поддерживаемых (или русский или английский)
            Localization.Strings.Culture = new System.Globalization.CultureInfo(cultureName);
        }

        private static void InitTelemetry(Outlook.Application application)
        {
            Telemetry.Initialize(DataFolder.GetRootPath());

            if (RegistrySettings.IsFirstTimeRun())
            {
                RegistrySettings.DeleteFirstTimeValue();
                Telemetry.Signal(Telemetry.AppStartEvents, "first_time");
                s_logger.Debug("First run after install");
            }

            Telemetry.Signal(Telemetry.AppStartEvents, "outlook_version", application?.Version);
            var langId = application?.LanguageSettings?.get_LanguageID(Office.MsoAppLanguageID.msoLanguageIDUI) ?? 0;
            Telemetry.Signal(Telemetry.AppStartEvents, "outlook_lang_id", langId.ToString());
        }

        private static void InitUiThreadContext()
        {
            if (SynchronizationContext.Current == null)
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            UiContext = SynchronizationContext.Current;
        }

        private async Task TryApplyLastFirstFromDeploymentAsync()
        {
            try
            {
                var generalOptionsProvider = Components?.GeneralOptionsProvider;
                var options = generalOptionsProvider?.Options;
                if (options == null)
                {
                    s_logger.Debug($"TryApplyLastFirstFromDeploymentAsync: GeneralOptionsProvider or Options is null, skipping auto-apply");
                    return;
                }

                if (options.AutoLastFirstApplied)
                {
                    s_logger.Debug("TryApplyLastFirstFromDeploymentAsync: AutoLastFirstApplied is true, skipping auto-apply");
                    return;
                }

                if (!RegistrySettings.ShouldEnableLastFirstAfterInstall())
                {
                    s_logger.Debug("TryApplyLastFirstFromDeploymentAsync: RegistrySettings.ShouldEnableLastFirstAfterInstall() is false, skipping auto-apply");
                    return;
                }

                s_logger.Info("TryApplyLastFirstFromDeploymentAsync: Applying LastFirst formatting from deployment flag");

                //Wait for the sync configuration to be loaded before applying changes to contacts
                var syncManager = Components?.SyncManager;
                if (syncManager != null)
                {
                    try
                    {
                        s_logger.Debug("TryApplyLastFirstFromDeploymentAsync: Waiting for SyncManager to load sync configuration");
                        await syncManager.GetSyncTargets();
                        s_logger.Debug("TryApplyLastFirstFromDeploymentAsync: SyncManager sync configuration loaded, proceeding with contact update");
                    }
                    catch (Exception ex)
                    {
                        s_logger.Warn("TryApplyLastFirstFromDeploymentAsync: Failed to load sync configuration from SyncManager", ex);
                    }
                }

                var updatedOptions = options.Clone();
                updatedOptions.FormatFileAsLastNameFirst = true;
                updatedOptions.AutoLastFirstApplied = true;
                generalOptionsProvider.Options = updatedOptions;

                SettingsWindow.UpdateAllContactsFullName(true);

                s_logger.Info("TryApplyLastFirstFromDeploymentAsync: successfully applied LastFirst formatting from deployment flag");
            }
            catch (Exception ex)
            {
                s_logger.Warn("Failed to apply LastFirst setting from deployment flag", ex);
            }
        }


        #region VSTO generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }
        
        #endregion
    }
}
