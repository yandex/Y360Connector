
using System;

namespace Y360OutlookConnector.Ui
{
    partial class AppointmentRibbon : Microsoft.Office.Tools.Ribbon.RibbonBase
    {
        /// <summary>
        /// Обязательная переменная конструктора.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        public AppointmentRibbon()
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
            this.btnTelemostInternalMeeting = this.Factory.CreateRibbonButton();
            this.btnTelemostExternalMeeting = this.Factory.CreateRibbonButton();
            this.btnTelemostSettings = this.Factory.CreateRibbonButton();
            this.group2 = this.Factory.CreateRibbonGroup();
            this.YandexCalendarRibbonMenu = this.Factory.CreateRibbonMenu();
            this.btnEditEventInAppointment = this.Factory.CreateRibbonButton();
            this.btnNavigateToYandexCalendarInAppointment = this.Factory.CreateRibbonButton();
            this.tab2 = this.Factory.CreateRibbonTab();
            this.group3 = this.Factory.CreateRibbonGroup();
            this.SchedulingAssistantTabYandexCalendarMenu = this.Factory.CreateRibbonMenu();
            this.btnEditEventInSchedulingAssistant = this.Factory.CreateRibbonButton();
            this.btnNavigateToYandexCalendarInSchedulingAssistant = this.Factory.CreateRibbonButton();
            this.menu1 = this.Factory.CreateRibbonMenu();
            this.button2 = this.Factory.CreateRibbonButton();
            this.button3 = this.Factory.CreateRibbonButton();
            this.button4 = this.Factory.CreateRibbonButton();
            this.button5 = this.Factory.CreateRibbonButton();
            this.tab1.SuspendLayout();
            this.group1.SuspendLayout();
            this.group2.SuspendLayout();
            this.tab2.SuspendLayout();
            this.group3.SuspendLayout();
            this.SuspendLayout();
            // 
            // tab1
            // 
            this.tab1.ControlId.ControlIdType = Microsoft.Office.Tools.Ribbon.RibbonControlIdType.Office;
            this.tab1.ControlId.OfficeId = "TabAppointment";
            this.tab1.Groups.Add(this.group1);
            this.tab1.Groups.Add(this.group2);
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
            this.TelemostRibbonMenu.Items.Add(this.btnTelemostInternalMeeting);
            this.TelemostRibbonMenu.Items.Add(this.btnTelemostExternalMeeting);
            this.TelemostRibbonMenu.Items.Add(this.btnTelemostSettings);
            this.TelemostRibbonMenu.Label = "Телемост";
            this.TelemostRibbonMenu.Name = "TelemostRibbonMenu";
            this.TelemostRibbonMenu.ShowImage = true;
            // 
            // btnTelemostInternalMeeting
            // 
            this.btnTelemostInternalMeeting.Image = global::Y360OutlookConnector.Properties.Resources.TelemostInternalMeeting;
            this.btnTelemostInternalMeeting.Label = "Внутренняя встреча";
            this.btnTelemostInternalMeeting.Name = "btnTelemostInternalMeeting";
            this.btnTelemostInternalMeeting.ShowImage = true;
            this.btnTelemostInternalMeeting.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.TelemostInternalMeeting_Click);
            // 
            // btnTelemostExternalMeeting
            // 
            this.btnTelemostExternalMeeting.Image = global::Y360OutlookConnector.Properties.Resources.TelemostExternalMeeting;
            this.btnTelemostExternalMeeting.Label = "Внешняя встреча";
            this.btnTelemostExternalMeeting.Name = "btnTelemostExternalMeeting";
            this.btnTelemostExternalMeeting.ShowImage = true;
            this.btnTelemostExternalMeeting.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.TelemostExternalMeeting_Click);
            // 
            // btnTelemostSettings
            // 
            this.btnTelemostSettings.Image = global::Y360OutlookConnector.Properties.Resources.TelemostSettings;
            this.btnTelemostSettings.Label = "Настройки";
            this.btnTelemostSettings.Name = "btnTelemostSettings";
            this.btnTelemostSettings.ShowImage = true;
            this.btnTelemostSettings.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.TelemostSettings_Click);
            // 
            // group2
            // 
            this.group2.Items.Add(this.YandexCalendarRibbonMenu);
            this.group2.Name = "group2";
            // 
            // YandexCalendarRibbonMenu
            // 
            this.YandexCalendarRibbonMenu.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.YandexCalendarRibbonMenu.Image = global::Y360OutlookConnector.Properties.Resources.YandexCalendar;
            this.YandexCalendarRibbonMenu.Items.Add(this.btnEditEventInAppointment);
            this.YandexCalendarRibbonMenu.Items.Add(this.btnNavigateToYandexCalendarInAppointment);
            this.YandexCalendarRibbonMenu.Label = "Яндекс Календарь";
            this.YandexCalendarRibbonMenu.Name = "YandexCalendarRibbonMenu";
            this.YandexCalendarRibbonMenu.ShowImage = true;
            // 
            // btnEditEventInAppointment
            // 
            this.btnEditEventInAppointment.Image = global::Y360OutlookConnector.Properties.Resources.Edit;
            this.btnEditEventInAppointment.Label = "Редактировать событие";
            this.btnEditEventInAppointment.Name = "btnEditEventInAppointment";
            this.btnEditEventInAppointment.ShowImage = true;
            this.btnEditEventInAppointment.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.EditEvent_Click);
            // 
            // btnNavigateToYandexCalendarInAppointment
            // 
            this.btnNavigateToYandexCalendarInAppointment.Image = global::Y360OutlookConnector.Properties.Resources.Calendar;
            this.btnNavigateToYandexCalendarInAppointment.Label = "Перейти в Календарь";
            this.btnNavigateToYandexCalendarInAppointment.Name = "btnNavigateToYandexCalendarInAppointment";
            this.btnNavigateToYandexCalendarInAppointment.ShowImage = true;
            this.btnNavigateToYandexCalendarInAppointment.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.NavigateToYandexCalendar_Click);
            // 
            // tab2
            // 
            this.tab2.ControlId.ControlIdType = Microsoft.Office.Tools.Ribbon.RibbonControlIdType.Office;
            this.tab2.ControlId.OfficeId = "TabSchedulingAssistant";
            this.tab2.Groups.Add(this.group3);
            this.tab2.Label = "TabSchedulingAssistant";
            this.tab2.Name = "tab2";
            // 
            // group3
            // 
            this.group3.Items.Add(this.SchedulingAssistantTabYandexCalendarMenu);
            this.group3.Name = "group3";
            // 
            // SchedulingAssistantTabYandexCalendarMenu
            // 
            this.SchedulingAssistantTabYandexCalendarMenu.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.SchedulingAssistantTabYandexCalendarMenu.Image = global::Y360OutlookConnector.Properties.Resources.YandexCalendar;
            this.SchedulingAssistantTabYandexCalendarMenu.Items.Add(this.btnEditEventInSchedulingAssistant);
            this.SchedulingAssistantTabYandexCalendarMenu.Items.Add(this.btnNavigateToYandexCalendarInSchedulingAssistant);
            this.SchedulingAssistantTabYandexCalendarMenu.Label = "Яндекс Календарь";
            this.SchedulingAssistantTabYandexCalendarMenu.Name = "SchedulingAssistantTabYandexCalendarMenu";
            this.SchedulingAssistantTabYandexCalendarMenu.ShowImage = true;
            // 
            // btnEditEventInSchedulingAssistant
            // 
            this.btnEditEventInSchedulingAssistant.Image = global::Y360OutlookConnector.Properties.Resources.Edit;
            this.btnEditEventInSchedulingAssistant.Label = "Редактировать событие";
            this.btnEditEventInSchedulingAssistant.Name = "btnEditEventInSchedulingAssistant";
            this.btnEditEventInSchedulingAssistant.ShowImage = true;
            this.btnEditEventInSchedulingAssistant.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.EditEvent_Click);
            // 
            // btnNavigateToYandexCalendarInSchedulingAssistant
            // 
            this.btnNavigateToYandexCalendarInSchedulingAssistant.Image = global::Y360OutlookConnector.Properties.Resources.Calendar;
            this.btnNavigateToYandexCalendarInSchedulingAssistant.Label = "Перейти в Календарь";
            this.btnNavigateToYandexCalendarInSchedulingAssistant.Name = "btnNavigateToYandexCalendarInSchedulingAssistant";
            this.btnNavigateToYandexCalendarInSchedulingAssistant.ShowImage = true;
            this.btnNavigateToYandexCalendarInSchedulingAssistant.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.NavigateToYandexCalendar_Click);
            // 
            // menu1
            // 
            this.menu1.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.menu1.Image = global::Y360OutlookConnector.Properties.Resources.Yandex_telemost_2022;
            this.menu1.Items.Add(this.button2);
            this.menu1.Items.Add(this.button3);
            this.menu1.Items.Add(this.button4);
            this.menu1.Items.Add(this.button5);
            this.menu1.Label = "Телемост";
            this.menu1.Name = "menu1";
            this.menu1.ShowImage = true;
            // 
            // button2
            // 
            this.button2.Image = global::Y360OutlookConnector.Properties.Resources.TelemostInternalMeeting;
            this.button2.Label = "Внутренняя встреча";
            this.button2.Name = "button2";
            this.button2.ShowImage = true;
            // 
            // button3
            // 
            this.button3.Image = global::Y360OutlookConnector.Properties.Resources.TelemostExternalMeeting;
            this.button3.Label = "Внешняя встреча";
            this.button3.Name = "button3";
            this.button3.ShowImage = true;
            // 
            // button4
            // 
            this.button4.Image = global::Y360OutlookConnector.Properties.Resources.TelemostSettings;
            this.button4.Label = "Настройки";
            this.button4.Name = "button4";
            this.button4.ShowImage = true;
            // 
            // button5
            // 
            this.button5.Label = "Edit";
            this.button5.Name = "button5";
            this.button5.ShowImage = true;
            // 
            // AppointmentRibbon
            // 
            this.Name = "AppointmentRibbon";
            this.RibbonType = "Microsoft.Outlook.Appointment";
            this.Tabs.Add(this.tab1);
            this.Tabs.Add(this.tab2);
            this.Load += new Microsoft.Office.Tools.Ribbon.RibbonUIEventHandler(this.AppointmentRibbon_Load);
            this.tab1.ResumeLayout(false);
            this.tab1.PerformLayout();
            this.group1.ResumeLayout(false);
            this.group1.PerformLayout();
            this.group2.ResumeLayout(false);
            this.group2.PerformLayout();
            this.tab2.ResumeLayout(false);
            this.tab2.PerformLayout();
            this.group3.ResumeLayout(false);
            this.group3.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        internal Microsoft.Office.Tools.Ribbon.RibbonTab tab1;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup group1;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnTelemostInternalMeeting;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnTelemostExternalMeeting;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnTelemostSettings;
        internal Microsoft.Office.Tools.Ribbon.RibbonMenu TelemostRibbonMenu;
        internal Microsoft.Office.Tools.Ribbon.RibbonMenu menu1;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton button2;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton button3;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton button4;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton button5;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup group2;
        internal Microsoft.Office.Tools.Ribbon.RibbonMenu YandexCalendarRibbonMenu;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnEditEventInAppointment;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnNavigateToYandexCalendarInAppointment;
        private Microsoft.Office.Tools.Ribbon.RibbonTab tab2;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup group3;
        internal Microsoft.Office.Tools.Ribbon.RibbonMenu SchedulingAssistantTabYandexCalendarMenu;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnEditEventInSchedulingAssistant;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnNavigateToYandexCalendarInSchedulingAssistant;
    }
}

namespace Y360OutlookConnector
{
    partial class ThisRibbonCollection
    {
        internal Ui.AppointmentRibbon TelemostRibbon
        {
            get { return this.GetRibbon<Ui.AppointmentRibbon>(); }
        }
    }
}

