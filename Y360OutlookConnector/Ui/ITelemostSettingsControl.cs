using System.Drawing;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Ui.Extensions;

namespace Y360OutlookConnector.Ui
{
    public interface ITelemostSettingsControl
    {
        void OnUserLogOff();

        void OnUserLogon(UserInfo userInfo, Image userAvatarImage);

        void UpdateMeetingInfo(TelemostMeetingInfo meetingInfo);
    }
}
