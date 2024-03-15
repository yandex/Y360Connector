namespace Y360OutlookConnector.Ui
{
    partial class Y360ConnectorRibbon : Microsoft.Office.Tools.Ribbon.RibbonBase
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        public Y360ConnectorRibbon()
            : base(Globals.Factory.GetRibbonFactory())
        {
            InitializeComponent();
        }

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.tab1 = this.Factory.CreateRibbonTab();
            this.MainGroup = this.Factory.CreateRibbonGroup();
            this.LoginButton = this.Factory.CreateRibbonButton();
            this.SyncNowButton = this.Factory.CreateRibbonButton();
            this.ToolsAndLayersButton = this.Factory.CreateRibbonButton();
            this.SettingsButton = this.Factory.CreateRibbonButton();
            this.AboutButton = this.Factory.CreateRibbonButton();
            this.HelpButton = this.Factory.CreateRibbonButton();
            this.tab1.SuspendLayout();
            this.MainGroup.SuspendLayout();
            this.SuspendLayout();
            // 
            // tab1
            // 
            this.tab1.Groups.Add(this.MainGroup);
            this.tab1.KeyTip = "YC";
            this.tab1.Label = "Y360 Connector";
            this.tab1.Name = "tab1";
            // 
            // MainGroup
            // 
            this.MainGroup.Items.Add(this.LoginButton);
            this.MainGroup.Items.Add(this.SyncNowButton);
            this.MainGroup.Items.Add(this.ToolsAndLayersButton);
            this.MainGroup.Items.Add(this.SettingsButton);
            this.MainGroup.Items.Add(this.AboutButton);
            this.MainGroup.Items.Add(this.HelpButton);
            this.MainGroup.Label = "Y360 Connector";
            this.MainGroup.Name = "MainGroup";
            // 
            // LoginButton
            // 
            this.LoginButton.Image = global::Y360OutlookConnector.Properties.Resources.Login;
            this.LoginButton.KeyTip = "L";
            this.LoginButton.Label = "Log in";
            this.LoginButton.Name = "LoginButton";
            this.LoginButton.ShowImage = true;
            this.LoginButton.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.LoginButton_Click);
            // 
            // SyncNowButton
            // 
            this.SyncNowButton.Image = global::Y360OutlookConnector.Properties.Resources.SyncNow;
            this.SyncNowButton.KeyTip = "SN";
            this.SyncNowButton.Label = "Synchronize";
            this.SyncNowButton.Name = "SyncNowButton";
            this.SyncNowButton.ShowImage = true;
            this.SyncNowButton.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.SyncNowButton_Click);
            // 
            // ToolsAndLayersButton
            // 
            this.ToolsAndLayersButton.Image = global::Y360OutlookConnector.Properties.Resources.Profiles;
            this.ToolsAndLayersButton.KeyTip = "T";
            this.ToolsAndLayersButton.Label = "Tools and Layers";
            this.ToolsAndLayersButton.Name = "ToolsAndLayersButton";
            this.ToolsAndLayersButton.ShowImage = true;
            this.ToolsAndLayersButton.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.ToolsAndLayersButton_Click);
            // 
            // SettingsButton
            // 
            this.SettingsButton.Image = global::Y360OutlookConnector.Properties.Resources.Settings;
            this.SettingsButton.KeyTip = "C";
            this.SettingsButton.Label = "Settings";
            this.SettingsButton.Name = "SettingsButton";
            this.SettingsButton.ShowImage = true;
            this.SettingsButton.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.SettingsButton_Click);
            // 
            // AboutButton
            // 
            this.AboutButton.Image = global::Y360OutlookConnector.Properties.Resources.About;
            this.AboutButton.KeyTip = "AB";
            this.AboutButton.Label = "About";
            this.AboutButton.Name = "AboutButton";
            this.AboutButton.ShowImage = true;
            this.AboutButton.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.AboutButton_Click);
            // 
            // HelpButton
            // 
            this.HelpButton.Image = global::Y360OutlookConnector.Properties.Resources.Help;
            this.HelpButton.KeyTip = "HP";
            this.HelpButton.Label = "Help";
            this.HelpButton.Name = "HelpButton";
            this.HelpButton.ShowImage = true;
            this.HelpButton.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.HelpButton_Click);
            // 
            // Y360ConnectorRibbon
            // 
            this.Name = "Y360ConnectorRibbon";
            this.RibbonType = "Microsoft.Outlook.Explorer";
            this.Tabs.Add(this.tab1);
            this.Load += new Microsoft.Office.Tools.Ribbon.RibbonUIEventHandler(this.Y360ConnectorRibbon_Load);
            this.tab1.ResumeLayout(false);
            this.tab1.PerformLayout();
            this.MainGroup.ResumeLayout(false);
            this.MainGroup.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        internal Microsoft.Office.Tools.Ribbon.RibbonTab tab1;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup MainGroup;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton SyncNowButton;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton LoginButton;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton ToolsAndLayersButton;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton SettingsButton;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton AboutButton;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton HelpButton;
    }
}

namespace Y360OutlookConnector
{
    partial class ThisRibbonCollection
    {
        internal Ui.Y360ConnectorRibbon Y360ConnectorRibbon
        {
            get { return this.GetRibbon<Ui.Y360ConnectorRibbon>(); }
        }
    }
}
