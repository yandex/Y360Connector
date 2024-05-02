using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Ui.Extensions;
using Y360OutlookConnector.Ui.Models;

namespace Y360OutlookConnector.Ui
{
    public partial class TelemostWpfSettingsControl : UserControl, ITelemostSettingsControl
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private readonly TelemostSettingsModel _model;

        public TelemostWpfSettingsControl(ComponentContainer components, AppointmentItem appointment)
        {
            InitializeComponent();
            _model = new TelemostSettingsModel(components, appointment);
            telemostSettingsWindow1.DataContext = _model;
        }

        void ITelemostSettingsControl.OnUserLogOff()
        {
            _model.IsLoggedIn = false;
            _model.UserAvatar = null;
            _model.UserName = string.Empty;
            _model.UserEmail = string.Empty;
        }

        private void UpdateUserAvatar(Image userAvatarImage)
        {
            if (userAvatarImage != null)
            {
                try
                {
                    _model.UserAvatar = userAvatarImage.ToWpfBitmap();
                    return;
                }
                catch(System.Exception ex)
                {
                    s_logger.Error("Fail convert avatar image to wpf", ex);
                }
            }
            _model.UserAvatar = new BitmapImage(new Uri("pack://application:,,,/Y360OutlookConnector;component/Resources/DefaultAva.png"));
        }

        void ITelemostSettingsControl.OnUserLogon(UserInfo userInfo, Image userAvatarImage)
        {
            _model.UserName = userInfo.RealName;
            _model.UserEmail = userInfo.Email;
            _model.IsLoggedIn = true;

            UpdateUserAvatar(userAvatarImage);
        }

        void ITelemostSettingsControl.UpdateMeetingInfo(TelemostMeetingInfo meetingInfo)
        {
            _model.IsMeetingCreated = meetingInfo != null;
            if (meetingInfo != null)
            {
                _model.IsMeetingInternal = meetingInfo.IsInternal;
            }
        }
    }
}
