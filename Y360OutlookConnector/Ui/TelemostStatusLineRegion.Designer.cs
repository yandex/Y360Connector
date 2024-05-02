
using System;

namespace Y360OutlookConnector.Ui
{
    [System.ComponentModel.ToolboxItemAttribute(false)]
    partial class TelemostStatusLine : Microsoft.Office.Tools.Outlook.FormRegionBase
    {
        public TelemostStatusLine(Microsoft.Office.Interop.Outlook.FormRegion formRegion)
            : base(Globals.Factory, formRegion)
        {
            this.InitializeComponent();          
        }

        /// <summary> 
        /// Обязательная переменная конструктора.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Освободить все используемые ресурсы.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Код, созданный конструктором областей формы

        /// <summary> 
        /// Требуемый метод для поддержки конструктора — не изменяйте 
        /// содержимое этого метода с помощью редактора кода.
        /// </summary>
        private static void InitializeManifest(Microsoft.Office.Tools.Outlook.FormRegionManifest manifest, Microsoft.Office.Tools.Outlook.Factory factory)
        {
            manifest.FormRegionName = "Телемост Сообщения";
            manifest.FormRegionType = Microsoft.Office.Tools.Outlook.FormRegionType.Adjoining;

        }

        #endregion

        #region Код, автоматически созданный конструктором компонентов

        /// <summary> 
        /// Обязательный метод для поддержки конструктора — не изменяйте 
        /// содержимое этого метода в редакторе кода.
        /// </summary>
        private void InitializeComponent()
        {
            this.labelMessage = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // labelMessage
            // 
            this.labelMessage.Dock = System.Windows.Forms.DockStyle.Fill;
            this.labelMessage.Location = new System.Drawing.Point(0, 0);
            this.labelMessage.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelMessage.Name = "labelMessage";
            this.labelMessage.Size = new System.Drawing.Size(150, 55);
            this.labelMessage.TabIndex = 0;
            this.labelMessage.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // TelemostStatusLine
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.labelMessage);
            this.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.Name = "TelemostStatusLine";
            this.Size = new System.Drawing.Size(150, 55);
            this.ResumeLayout(false);

        }

        #endregion

        private void SetTextLabelMethod(string text)
        {
            labelMessage.Text = text;
        }
        public void SetMessage(string text)
        {
            Invoke((Action<string>)(msg => SetTextLabelMethod(msg)), text);
        }

        private System.Windows.Forms.Label labelMessage;

        public partial class TelemostStatusLineRegionFactory : Microsoft.Office.Tools.Outlook.IFormRegionFactory
        {
            public event Microsoft.Office.Tools.Outlook.FormRegionInitializingEventHandler FormRegionInitializing;

            private Microsoft.Office.Tools.Outlook.FormRegionManifest _Manifest;

            [System.Diagnostics.DebuggerNonUserCodeAttribute()]
            public TelemostStatusLineRegionFactory()
            {
                this._Manifest = Globals.Factory.CreateFormRegionManifest();
                TelemostStatusLine.InitializeManifest(this._Manifest, Globals.Factory);
                _Manifest.FormRegionName = Localization.Strings.Telemost_Messages_InfoPanelTitle;
                this.FormRegionInitializing += new Microsoft.Office.Tools.Outlook.FormRegionInitializingEventHandler(this.TelemostStatusLineRegionFactory_FormRegionInitializing);
            }

            [System.Diagnostics.DebuggerNonUserCodeAttribute()]
            public Microsoft.Office.Tools.Outlook.FormRegionManifest Manifest
            {
                get
                {
                    return this._Manifest;
                }
            }

            [System.Diagnostics.DebuggerNonUserCodeAttribute()]
            Microsoft.Office.Tools.Outlook.IFormRegion Microsoft.Office.Tools.Outlook.IFormRegionFactory.CreateFormRegion(Microsoft.Office.Interop.Outlook.FormRegion formRegion)
            {
                TelemostStatusLine form = new TelemostStatusLine(formRegion);
                form.Factory = this;
                return form;
            }

            [System.Diagnostics.DebuggerNonUserCodeAttribute()]
            byte[] Microsoft.Office.Tools.Outlook.IFormRegionFactory.GetFormRegionStorage(object outlookItem, Microsoft.Office.Interop.Outlook.OlFormRegionMode formRegionMode, Microsoft.Office.Interop.Outlook.OlFormRegionSize formRegionSize)
            {
                throw new System.NotSupportedException();
            }

            [System.Diagnostics.DebuggerNonUserCodeAttribute()]
            bool Microsoft.Office.Tools.Outlook.IFormRegionFactory.IsDisplayedForItem(object outlookItem, Microsoft.Office.Interop.Outlook.OlFormRegionMode formRegionMode, Microsoft.Office.Interop.Outlook.OlFormRegionSize formRegionSize)
            {
                if (this.FormRegionInitializing != null)
                {
                    Microsoft.Office.Tools.Outlook.FormRegionInitializingEventArgs cancelArgs = Globals.Factory.CreateFormRegionInitializingEventArgs(outlookItem, formRegionMode, formRegionSize, false);
                    this.FormRegionInitializing(this, cancelArgs);
                    return !cancelArgs.Cancel;
                }
                else
                {
                    return true;
                }
            }

            [System.Diagnostics.DebuggerNonUserCodeAttribute()]
            Microsoft.Office.Tools.Outlook.FormRegionKindConstants Microsoft.Office.Tools.Outlook.IFormRegionFactory.Kind
            {
                get
                {
                    return Microsoft.Office.Tools.Outlook.FormRegionKindConstants.WindowsForms;
                }
            }
        }
    }    
}

namespace Y360OutlookConnector
{
    partial class WindowFormRegionCollection
    {
        internal Y360OutlookConnector.Ui.TelemostStatusLine TelemostStatusLineRegion
        {
            get
            {
                foreach (var item in this)
                {
                    if (item.GetType() == typeof(Y360OutlookConnector.Ui.TelemostStatusLine))
                        return (Y360OutlookConnector.Ui.TelemostStatusLine)item;
                }
                return null;
            }
        }
    }
}

