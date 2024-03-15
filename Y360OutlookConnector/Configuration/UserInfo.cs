using CalDavSynchronizer.Utilities;
using log4net;
using System;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace Y360OutlookConnector.Configuration
{
    public class UserInfo
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodInfo.GetCurrentMethod().DeclaringType);

        private static Random s_random = new Random();
        private const int c_saltLength = 17;

        public string UserName { get; set; }
        public string Email { get; set; }
        public string RealName { get; set; }
        public string DefaultAvatarId { get; set; }
        public string ProtectedAccessToken { get; set; }
        public string Salt { get; set; }

        [XmlIgnore]
        public SecureString AccessToken
        {
            get
            {
                if (string.IsNullOrEmpty(ProtectedAccessToken))
                    return new SecureString();

                var salt = Convert.FromBase64String(Salt);
                var data = Convert.FromBase64String(ProtectedAccessToken);
                try
                {
                    var transformedData = ProtectedData.Unprotect(data, salt, DataProtectionScope.CurrentUser);
                    return SecureStringUtility.ToSecureString(Encoding.Unicode.GetString(transformedData));
                }
                catch (CryptographicException x)
                {
                    s_logger.Error("Error while decrypting password. Using empty password", x);
                    return new SecureString();
                }
            }
            set
            {
                byte[] salt = new byte[c_saltLength];
                s_random.NextBytes(salt);
                Salt = Convert.ToBase64String(salt);

                byte[] data = Encoding.Unicode.GetBytes(SecureStringUtility.ToUnsecureString(value));
                byte[] transformedData = ProtectedData.Protect(data, salt, DataProtectionScope.CurrentUser);
                ProtectedAccessToken = Convert.ToBase64String(transformedData);
            }
        }
    }
}
