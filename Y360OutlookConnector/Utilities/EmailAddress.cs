using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Y360OutlookConnector.Synchronization;

namespace Y360OutlookConnector.Utilities
{
    public class EmailAddress
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public string NameId { get; set; } = String.Empty;

        public string Domain { get; set; } = String.Empty;

        private static IUserEmailService _userEmailService;

        public static void SetUserEmailService(IUserEmailService userEmailService)
        {
            _userEmailService = userEmailService;
        }

        public override string ToString()
        {
            if (String.IsNullOrEmpty(NameId) && String.IsNullOrEmpty(Domain))
                return String.Empty;
            return $"{NameId}@{Domain}";
        }

        public EmailAddress Normalize()
        {
            return new EmailAddress
            {
                NameId = NameId.Trim().Replace('.', '-'),
                Domain = Domain.Trim()
            };
        }

        public static EmailAddress Parse(string text)
        {
            var result = new EmailAddress();
            if (String.IsNullOrEmpty(text)) return result;

            var parts = text.Split('@');
            if (parts.Length > 0)
                result.NameId = parts[0];
            if (parts.Length > 1)
                result.Domain = parts[1];
            return result;
        }

        public static bool AreSame(EmailAddress email1, EmailAddress email2)
        {
            if (email1 == null || email2 == null)
            {
                return false;
            }

            if (_userEmailService != null)
            {
                try
                {
                    return _userEmailService.AreEmailsSame(email1.ToString(), email2.ToString());
                }
                catch (Exception ex)
                {
                    s_logger.Error("Fail to compare emails via UserEmailService", ex);
                }
            }

            return String.Equals(email1.NameId, email2.NameId, StringComparison.OrdinalIgnoreCase)
                   && String.Equals(email1.Domain, email2.Domain, StringComparison.OrdinalIgnoreCase);
        }

        public static bool AreSame(string str1, string str2, IEnumerable<IEnumerable<string>> domainAliases = null)
        {
            if (String.IsNullOrEmpty(str1) || String.IsNullOrEmpty(str2))
            {
                return false;
            }

            //Для обратной совместимости игнорируем параметр алиасов и используем новый сервис
            return AreSame(Parse(str1), Parse(str2));
        }

        public static bool AreSame(Uri uri1, Uri uri2, IEnumerable<IEnumerable<string>> domainAliases = null)
        {
            try
            {
                if (uri1 == null || uri2 == null)
                    return false;

                if (uri1.Scheme != Uri.UriSchemeMailto || uri2.Scheme != Uri.UriSchemeMailto)
                    return false;

                int prefixLength = Uri.UriSchemeMailto.Length + 1; // "mailto" + ":"
                return AreSame(uri1.ToString().Substring(prefixLength),
                    uri2.ToString().Substring(prefixLength));
            }
            catch(Exception ex)
            {
                s_logger.Error($"Fail to compare {uri1.OriginalString} and {uri2.OriginalString}", ex);
                return false;
            }
        }
    }
}
