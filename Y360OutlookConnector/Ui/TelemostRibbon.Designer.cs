
using System;

namespace Y360OutlookConnector.Ui
{
    partial class TelemostRibbon : Microsoft.Office.Tools.Ribbon.RibbonBase
    {
        /// <summary>
        /// Обязательная переменная конструктора.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        public TelemostRibbon()
            : base(Globals.Factory.GetRibbonFactory())
        {
            InitializeComponent();
        }

        /// <summary> 
        /// Освободить все используемые ресурсы.
        /// </summary>
        /// <param name="disposing">истинно, если управляемый ресурс должен быть удален; иначе ложно.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Код, автоматически созданный конструктором компонентов

        /// <summary>
        /// Требуемый метод для поддержки конструктора — не изменяйте 
        /// содержимое этого метода с помощью редактора кода.
        /// </summary>
        private void InitializeComponent()
        {
            this.tab1 = this.Factory.CreateRibbonTab();
            this.group1 = this.Factory.CreateRibbonGroup();
            this.TelemostRibbonMenu = this.Factory.CreateRibbonMenu();
            this.TelemostInternalMeeting = this.Factory.CreateRibbonButton();
            this.TelemostExternalMeeting = this.Factory.CreateRibbonButton();
            this.TelemostSettings = this.Factory.CreateRibbonButton();
            this.tab1.SuspendLayout();
            this.group1.SuspendLayout();
            this.SuspendLayout();
            // 
            // tab1
            // 
            this.tab1.ControlId.ControlIdType = Microsoft.Office.Tools.Ribbon.RibbonControlIdType.Office;
            this.tab1.ControlId.OfficeId = "TabAppointment";
            this.tab1.Groups.Add(this.group1);
            this.tab1.Label = "TabAppointment";
            this.tab1.Name = "tab1";
            // 
            // group1
            // 
            this.group1.Items.Add(this.TelemostRibbonMenu);
            this.group1.Name = "group1";
            // 
            // TelemostRibbonMenu
            // 
            this.TelemostRibbonMenu.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.TelemostRibbonMenu.Image = global::Y360OutlookConnector.Properties.Resources.Yandex_telemost_2022;
            this.TelemostRibbonMenu.Items.Add(this.TelemostInternalMeeting);
            this.TelemostRibbonMenu.Items.Add(this.TelemostExternalMeeting);
            this.TelemostRibbonMenu.Items.Add(this.TelemostSettings);
            this.TelemostRibbonMenu.Label = "Телемост";
            this.TelemostRibbonMenu.Name = "TelemostRibbonMenu";
            this.TelemostRibbonMenu.ShowImage = true;
            // 
            // TelemostInternalMeeting
            // 
            this.TelemostInternalMeeting.Image = global::Y360OutlookConnector.Properties.Resources.TelemostInternalMeeting;
            this.TelemostInternalMeeting.Label = "Внутренняя встреча";
            this.TelemostInternalMeeting.Name = "TelemostInternalMeeting";
            this.TelemostInternalMeeting.ShowImage = true;
            this.TelemostInternalMeeting.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.TelemostInternalMeeting_Click);
            // 
            // TelemostExternalMeeting
            // 
            this.TelemostExternalMeeting.Image = global::Y360OutlookConnector.Properties.Resources.TelemostExternalMeeting;
            this.TelemostExternalMeeting.Label = "Внешняя встреча";
            this.TelemostExternalMeeting.Name = "TelemostExternalMeeting";
            this.TelemostExternalMeeting.ShowImage = true;
            this.TelemostExternalMeeting.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.TelemostExternalMeeting_Click);
            // 
            // TelemostSettings
            // 
            this.TelemostSettings.Image = global::Y360OutlookConnector.Properties.Resources.TelemostSettings;
            this.TelemostSettings.Label = "Настройки";
            this.TelemostSettings.Name = "TelemostSettings";
            this.TelemostSettings.ShowImage = true;
            this.TelemostSettings.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.TelemostSettings_Click);
            // 
            // TelemostRibbon
            // 
            this.Name = "TelemostRibbon";
            this.RibbonType = "Microsoft.Outlook.Appointment";
            this.Tabs.Add(this.tab1);
            this.Load += new Microsoft.Office.Tools.Ribbon.RibbonUIEventHandler(this.TelemostRibbon_Load);
            this.tab1.ResumeLayout(false);
            this.tab1.PerformLayout();
            this.group1.ResumeLayout(false);
            this.group1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        internal Microsoft.Office.Tools.Ribbon.RibbonTab tab1;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup group1;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton TelemostInternalMeeting;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton TelemostExternalMeeting;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton TelemostSettings;
        internal Microsoft.Office.Tools.Ribbon.RibbonMenu TelemostRibbonMenu;
    }
}

namespace Y360OutlookConnector
{
    partial class ThisRibbonCollection
    {
        internal Ui.TelemostRibbon TelemostRibbon
        {
            get { return this.GetRibbon<Ui.TelemostRibbon>(); }
        }
    }
}

