using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Y360OutlookConnector.Configuration;

namespace Y360OutlookConnector
{
    public class LoginStateEventArgs : EventArgs
    {
        public LoginStateEventArgs(bool isUserLoggedIn)
        {
            IsUserLoggedIn = isUserLoggedIn;
        }

        public bool IsUserLoggedIn { get; }
    }

    public class LoginController
    {
        private const string UserInfoFileName = "user_info.xml";

        public UserInfo UserInfo { get; private set; }
        public bool IsUserLoggedIn => !String.IsNullOrEmpty(UserInfo.UserName);

        public event EventHandler<LoginStateEventArgs> LoginStateChanged;

        private readonly string _dataFolderPath;

        public LoginController(string dataFolderPath)
        {
            if (String.IsNullOrEmpty(dataFolderPath))
                throw new ArgumentException("Invalid value of data folder path");

            _dataFolderPath = dataFolderPath;
            UserInfo = XmlFile.Load<UserInfo>(Path.Combine(_dataFolderPath, UserInfoFileName));
        }

        public void OnUserLogin(UserInfo userInfo)
        {
            UserInfo = userInfo;
            XmlFile.Save(Path.Combine(_dataFolderPath, UserInfoFileName), UserInfo);
            
            LoginStateChanged?.Invoke(this, new LoginStateEventArgs(IsUserLoggedIn));

            if (IsUserLoggedIn)
                Ui.ErrorWindow.HideError(Ui.ErrorWindow.ErrorType.Unauthorized);
        }

        public void Logout()
        {
            OnUserLogin(new UserInfo());
        }

        public static async Task<string> DownloadAvatar(string avatarId)
        {
            if (String.IsNullOrEmpty(avatarId))
                return "pack://application:,,,/Y360OutlookConnector;component/Resources/DefaultAva.png";

            var fileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            using (var client = new HttpClient())
            {
                var url = $"https://avatars.yandex.net/get-yapic/{avatarId}/islands-75";

                ThisAddIn.RestoreUiContext();
                using (var stream = await client.GetStreamAsync(url))
                {
                    using (var fileStream = new FileStream(fileName, FileMode.OpenOrCreate))
                    {
                        await stream.CopyToAsync(fileStream);
                        return fileName;
                    }
                }
            }
        }
    }
}
