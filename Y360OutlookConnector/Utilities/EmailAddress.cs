using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;

namespace Y360OutlookConnector.Utilities
{
    public class EmailAddress
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public string NameId { get; set; } = String.Empty;

        public string Domain { get; set; } = String.Empty;

        public static string[] KnownDomainsAliases =
        {
            "ya.ru",
            "yandex.by",
            "yandex.com",
            "yandex.kz",
            "yandex.ru"
        };

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
            return String.Equals(email1.NameId, email2.NameId, StringComparison.OrdinalIgnoreCase)
                   && String.Equals(email1.Domain, email2.Domain, StringComparison.OrdinalIgnoreCase);
        }

        public static bool AreSame(string str1, string str2, IEnumerable<string> domainAliases = null)
        {
            var email1 = Parse(str1).Normalize();
            var email2 = Parse(str2).Normalize();

            if (AreSame(email1, email2))
                return true;

            if (domainAliases != null)
            {
                var aliasesSet = new HashSet<string>(domainAliases, StringComparer.InvariantCultureIgnoreCase);
                if (aliasesSet.Contains(email1.Domain))
                {
                    foreach (var domain in aliasesSet)
                    {
                        var aliasedEmail = new EmailAddress { NameId = email2.NameId, Domain = domain};
                        if (AreSame(email1, aliasedEmail))
                            return true;
                    }
                }
            }

            return false;
        }

        public static bool AreSame(Uri uri1, Uri uri2, IEnumerable<string> domainAliases = null)
        {
            try
            {
                if (uri1 == null || uri2 == null)
                    return false;

                if (uri1.Scheme != Uri.UriSchemeMailto || uri2.Scheme != Uri.UriSchemeMailto)
                    return false;

                int prefixLength = Uri.UriSchemeMailto.Length + 1; // "mailto" + ":"
                return AreSame(uri1.ToString().Substring(prefixLength),
                    uri2.ToString().Substring(prefixLength), domainAliases);
            }
            catch(Exception ex)
            {
                s_logger.Error($"Fail to compare {uri1.OriginalString} and {uri2.OriginalString}", ex);
                return false;
            }
        }
    }
}
