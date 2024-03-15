using System;
using log4net;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Y360OutlookConnector.Configuration;
using Outlook = Microsoft.Office.Interop.Outlook;
using Office = Microsoft.Office.Core;

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

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
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
                Components = new ComponentContainer(Application);
                ComponentsCreated?.Invoke(this, EventArgs.Empty);
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
            var languageSettings = application?.LanguageSettings;
            if (languageSettings != null)
            {
                // https://learn.microsoft.com/en-us/office/vba/api/office.msolanguageid
                var langId = languageSettings.get_LanguageID(Office.MsoAppLanguageID.msoLanguageIDUI);
                string cultureName = "en-US";
                if (langId == 1049)
                    cultureName = "ru-RU";

                Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(cultureName);
            }
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
