using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Y360OutlookConnector.Configuration
{
    public class UserEmailsResponse
    {
        [JsonProperty("users")]
        public List<UserEmailData> Users { get; set; } = new List<UserEmailData>();
    }

    public class UserEmailData
    {
        [JsonProperty("addresses")]
        public List<EmailAddressData> Addresses { get; set; } = new List<EmailAddressData>();
    }

    public class EmailAddressData
    {
        [JsonProperty("address")]
        public string Address { get; set; }

        [JsonProperty("native")]
        public bool IsNative { get; set; }

        [JsonProperty("validated")]
        public bool IsValidated { get; set; }
    }
}
