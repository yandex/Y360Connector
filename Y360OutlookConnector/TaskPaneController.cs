using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Microsoft.Office.Tools;
using Y360OutlookConnector.Ui;

namespace Y360OutlookConnector
{
    public class TaskPaneController : IDisposable
    {
        private readonly Dictionary<Inspector, InspectorWrapper> _inspectorWrappers = new Dictionary<Inspector, InspectorWrapper>();

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private Image _userAvatar;

        private class InspectorWrapper
        {
            private Inspector _inspector;
            private CustomTaskPane _taskPane;

            public CustomTaskPane TaskPane => _taskPane;
            public Inspector Inspector => _inspector;

            public event EventHandler InspectorClosed;

            public InspectorWrapper(Inspector inspector, AppointmentItem appointment)
            {
                _inspector = inspector;
                ((InspectorEvents_Event)inspector).Close +=
                    new InspectorEvents_CloseEventHandler(InspectorWrapper_Close);

                var settingsControl = new TelemostWpfSettingsControl(ThisAddIn.Components, appointment);

                _taskPane = Globals.ThisAddIn.CustomTaskPanes.Add(settingsControl, Localization.Strings.Telemost_SettingsWindow_SettingsWindowTitle, inspector);
                _taskPane.DockPosition = Microsoft.Office.Core.MsoCTPDockPosition.msoCTPDockPositionRight;
                _taskPane.DockPositionRestrict = Microsoft.Office.Core.MsoCTPDockPositionRestrict.msoCTPDockPositionRestrictNoChange;

                _taskPane.Width = 500;
            }

            private void InspectorWrapper_Close()
            {
                if (_taskPane != null)
                {
                    Globals.ThisAddIn.CustomTaskPanes.Remove(_taskPane);
                }

                InspectorClosed?.Invoke(this, EventArgs.Empty);
                ((InspectorEvents_Event)_inspector).Close -=
                    new InspectorEvents_CloseEventHandler(InspectorWrapper_Close);
                _taskPane = null;
                _inspector = null;
            }
        }
        

        public TaskPaneController()
        {

        }

        public CustomTaskPane GetSettingsPane(Inspector inspector)
        {
            if (!_inspectorWrappers.TryGetValue(inspector, out var inspectorWrapper))
            {
                return null;
            }

            return inspectorWrapper.TaskPane;
        }

        private async Task<Image> DownloadAvatarAsync(string userAvatarId)
        {
            try
            {
                var imageUrl = await LoginController.DownloadAvatar(userAvatarId);

                return new Bitmap(imageUrl);
            }
            catch (System.Exception ex)
            {
                //Telemetry.Signal(Telemetry.SettingsWindowEvents, "avatar_load_failure");
                s_logger.Error("Failed to load user avatar", ex);
            }

            return null;
        }

        private async Task EnsureUserAvatarAsync()
        {
            // Аватар подгружается по событию логина пользователя. Однако при загрузке outlook если пользователь уже 
            // залогинен, то такое событие не возникает и аватар не подгружен. Проверяем что есть идентификатор аватара
            // у залогиненого пользователя, а самого аватара нет и подгружаем в этом случае
            var userAvatarId = ThisAddIn.Components.LoginController.UserInfo?.DefaultAvatarId;

            if (string.IsNullOrEmpty(userAvatarId))
            {
                return;
            }

            if (_userAvatar != null)
            {
                return;
            }

            _userAvatar = await DownloadAvatarAsync(userAvatarId);
        }

        public async Task<CustomTaskPane> GetOrCreateSettingsPaneAsync(Inspector inspector)
        {
            if (!(inspector.CurrentItem is AppointmentItem appointment))
            {
                return null;
            }

            if (!_inspectorWrappers.TryGetValue(inspector, out var inspectorWrapper))
            {
                inspectorWrapper = new InspectorWrapper(inspector, appointment);

                inspectorWrapper.InspectorClosed += InspectorWrapper_InspectorClosed;
                _inspectorWrappers[inspector] = inspectorWrapper;


                if (ThisAddIn.Components.LoginController.IsUserLoggedIn)
                {
                    await EnsureUserAvatarAsync();
                    var settingsControl = inspectorWrapper.TaskPane.Control as ITelemostSettingsControl;

                    settingsControl?.OnUserLogon(ThisAddIn.Components.LoginController.UserInfo, _userAvatar);
                }

            }

            return inspectorWrapper.TaskPane;     
        }

        private void ClearUserAvatar()
        {
            _userAvatar?.Dispose();
            _userAvatar = null;
        }

        public async Task OnLoginStateChangedAsync(bool isUserLoggedIn)
        {
            ClearUserAvatar();

            var userAvatarId = ThisAddIn.Components.LoginController.UserInfo?.DefaultAvatarId;

            if (!string.IsNullOrEmpty(userAvatarId))
            {
                _userAvatar = await DownloadAvatarAsync(userAvatarId);               
            }            

            foreach (var wrapper in _inspectorWrappers.Values)
            {
                if (!(wrapper.TaskPane.Control is ITelemostSettingsControl settingsControl))
                {
                    continue;
                }

                if (isUserLoggedIn)
                {
                    settingsControl.OnUserLogon(ThisAddIn.Components.LoginController.UserInfo, _userAvatar);
                }
                else
                {
                    settingsControl.OnUserLogOff();
                }
                
            }
        }

        private void InspectorWrapper_InspectorClosed(object sender, EventArgs e)
        {
            var inspectorWrapper = sender as InspectorWrapper;

            if (inspectorWrapper == null)
            {
                return;
            }

            inspectorWrapper.InspectorClosed -= InspectorWrapper_InspectorClosed;
            _inspectorWrappers.Remove(inspectorWrapper.Inspector);
        }

        public void Dispose()
        {
            ClearUserAvatar();
        }
    }
}
