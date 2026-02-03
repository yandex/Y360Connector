using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnector.Synchronization
{
    public interface IUserEmailService
    {
        Task<List<EmailAddress>> GetUserEmailsAsync(string accessToken);
        bool AreEmailsSame(string email1, string email2);
        bool IsUserEmail(string email);
        void ClearCache();
    }
}
