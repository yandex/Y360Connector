using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CalDavSynchronizer.Contracts;
using CalDavSynchronizer.Implementation.Common;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Contacts;
using GenSync.EntityMapping;
using GenSync.Logging;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Thought.vCards;

namespace Y360OutlookConnector.Synchronization.EntityMappers
{
    public class ContactEntityMapper : IEntityMapper<IContactItemWrapper, vCard, ICardDavRepositoryLogger>
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private const string PR_EMAIL1ADDRESS = "http://schemas.microsoft.com/mapi/id/{00062004-0000-0000-C000-000000000046}/8084001F";
        private const string PR_EMAIL2ADDRESS = "http://schemas.microsoft.com/mapi/id/{00062004-0000-0000-C000-000000000046}/8094001F";
        private const string PR_EMAIL3ADDRESS = "http://schemas.microsoft.com/mapi/id/{00062004-0000-0000-C000-000000000046}/80a4001F";

        private readonly ContactMappingConfiguration _configuration;

        public ContactEntityMapper(ContactMappingConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public Task<vCard> Map1To2(IContactItemWrapper source, vCard target, IEntitySynchronizationLogger logger, ICardDavRepositoryLogger context)
        {
            target.RevisionDate = source.Inner.LastModificationTime.ToUniversalTime();

            target.GivenName = source.Inner.FirstName;
            target.FamilyName = source.Inner.LastName;
            target.NamePrefix = source.Inner.Title;
            target.NameSuffix = source.Inner.Suffix;
            target.AdditionalNames = source.Inner.MiddleName;
            target.Gender = MapGender2To1(source.Inner.Gender);

            target.Assistant = source.Inner.AssistantName;
            target.Spouse = source.Inner.Spouse;
            target.Manager = source.Inner.ManagerName;

            MapEmailAddresses1To2(source.Inner, target, logger);

            if (!String.IsNullOrEmpty(source.Inner.FileAs))
            {
                target.FormattedName = source.Inner.FileAs;
            }
            else if (!String.IsNullOrEmpty(source.Inner.CompanyAndFullName))
            {
                target.FormattedName = source.Inner.CompanyAndFullName;
            }
            else if (target.EmailAddresses.Count >= 1)
            {
                target.FormattedName = target.EmailAddresses[0].Address;
            }
            else
            {
                target.FormattedName = "<Empty>";
            }

            target.Nicknames.Clear();
            if (!String.IsNullOrEmpty(source.Inner.NickName))
            {
                Array.ForEach(
                    source.Inner.NickName.Split(new[] {CultureInfo.CurrentCulture.TextInfo.ListSeparator}, StringSplitOptions.RemoveEmptyEntries),
                    c => target.Nicknames.Add(c)
                );
            }

            target.AccessClassification = CommonEntityMapper.MapPrivacy1To2(source.Inner.Sensitivity);

            target.Categories.Clear();
            if (!String.IsNullOrEmpty(source.Inner.Categories))
            {
                Array.ForEach(
                    source.Inner.Categories.Split(new[] {CultureInfo.CurrentCulture.TextInfo.ListSeparator}, StringSplitOptions.RemoveEmptyEntries),
                    c => target.Categories.Add(c.Trim())
                );
            }

            target.IMs.Clear();
            if (!String.IsNullOrEmpty(source.Inner.IMAddress))
            {
                //IMAddress are expected to be in form of ([Protocol]: [Address]; [Protocol]: [Address])
                var imsRaw = source.Inner.IMAddress.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries);
                foreach (var imRaw in imsRaw)
                {
                    var imDetails = imRaw.Trim().Split(new[] {':'}, StringSplitOptions.RemoveEmptyEntries);
                    var im = new vCardIMPP();
                    if (imDetails.Length == 1)
                    {
                        im.Handle = imDetails[0].Trim();
                        // Set default ServiceType to the configured DefaultImServiceType (defaults to AIM)
                        im.ServiceType = _configuration.DefaultImServicType;
                    }
                    else if (imDetails.Length > 1)
                    {
                        var serviceType = IMTypeUtils.GetIMServiceType(imDetails[0].Trim());
                        if (serviceType == null)
                        {
                            im.ServiceType = _configuration.DefaultImServicType;
                            s_logger.Warn($"Unknown IM ServiceType '{imDetails[0]}' not implemented, defaulting to '{_configuration.DefaultImServicType}'");
                            logger.LogWarning($"Unknown IM ServiceType '{imDetails[0]}' not implemented, defaulting to '{_configuration.DefaultImServicType}'");
                        }
                        else
                        {
                            im.ServiceType = serviceType.Value;
                        }

                        im.Handle = imRaw.Substring(imRaw.IndexOf(":", StringComparison.Ordinal) + 1).Trim();
                    }

                    //Only add the im Address if not empty
                    if (!String.IsNullOrEmpty(im.Handle))
                    {
                        im.IsPreferred = target.IMs.Count == 0;
                        im.ItemType = ItemType.HOME;
                        target.IMs.Add(im);
                    }
                }
            }

            target.DeliveryAddresses.Clear();
            if (!String.IsNullOrEmpty(source.Inner.HomeAddress))
            {
                vCardDeliveryAddress homeAddress = new vCardDeliveryAddress();
                homeAddress.AddressType.Add(vCardDeliveryAddressTypes.Home);
                homeAddress.City = source.Inner.HomeAddressCity;
                homeAddress.Country = source.Inner.HomeAddressCountry;
                homeAddress.PostalCode = source.Inner.HomeAddressPostalCode;
                homeAddress.Region = source.Inner.HomeAddressState;
                homeAddress.Street = source.Inner.HomeAddressStreet;
                homeAddress.PoBox = source.Inner.HomeAddressPostOfficeBox;
                if (source.Inner.SelectedMailingAddress == OlMailingAddress.olHome)
                {
                    homeAddress.AddressType.Add(vCardDeliveryAddressTypes.Preferred);
                }

                target.DeliveryAddresses.Add(homeAddress);
            }

            if (!String.IsNullOrEmpty(source.Inner.BusinessAddress) || !String.IsNullOrEmpty(source.Inner.OfficeLocation))
            {
                vCardDeliveryAddress businessAddress = new vCardDeliveryAddress();
                businessAddress.AddressType.Add(vCardDeliveryAddressTypes.Work);
                businessAddress.City = source.Inner.BusinessAddressCity;
                businessAddress.Country = source.Inner.BusinessAddressCountry;
                businessAddress.PostalCode = source.Inner.BusinessAddressPostalCode;
                businessAddress.Region = source.Inner.BusinessAddressState;
                businessAddress.Street = source.Inner.BusinessAddressStreet;
                businessAddress.PoBox = source.Inner.BusinessAddressPostOfficeBox;
                if (!String.IsNullOrEmpty(source.Inner.OfficeLocation))
                {
                    businessAddress.ExtendedAddress = source.Inner.OfficeLocation;
                }

                if (source.Inner.SelectedMailingAddress == OlMailingAddress.olBusiness)
                {
                    businessAddress.AddressType.Add(vCardDeliveryAddressTypes.Preferred);
                }

                target.DeliveryAddresses.Add(businessAddress);
            }

            if (!String.IsNullOrEmpty(source.Inner.OtherAddress))
            {
                vCardDeliveryAddress otherAddress = new vCardDeliveryAddress
                {
                    City = source.Inner.OtherAddressCity,
                    Country = source.Inner.OtherAddressCountry,
                    PostalCode = source.Inner.OtherAddressPostalCode,
                    Region = source.Inner.OtherAddressState,
                    Street = source.Inner.OtherAddressStreet,
                    PoBox = source.Inner.OtherAddressPostOfficeBox
                };
                if (source.Inner.SelectedMailingAddress == OlMailingAddress.olOther)
                {
                    otherAddress.AddressType.Add(vCardDeliveryAddressTypes.Preferred);
                }

                target.DeliveryAddresses.Add(otherAddress);
            }

            MapPhoneNumbers1To2(source.Inner, target);

            if (_configuration.MapAnniversary)
            {
                target.Anniversary = source.Inner.Anniversary.Equals(OutlookUtility.OUTLOOK_DATE_NONE) ? default(DateTime?) : source.Inner.Anniversary.Date;
            }

            if (_configuration.MapBirthday)
            {
                target.BirthDate = source.Inner.Birthday.Equals(OutlookUtility.OUTLOOK_DATE_NONE) ? default(DateTime?) : source.Inner.Birthday.Date;
            }

            target.Organization = source.Inner.CompanyName;
            target.Department = source.Inner.Department;

            target.Title = source.Inner.JobTitle;
            target.Role = source.Inner.Profession;

            target.Websites.Clear();
            if (!String.IsNullOrEmpty(source.Inner.PersonalHomePage))
            {
                target.Websites.Add(new vCardWebsite(source.Inner.PersonalHomePage, vCardWebsiteTypes.Personal));
            }

            if (!String.IsNullOrEmpty(source.Inner.BusinessHomePage))
            {
                target.Websites.Add(new vCardWebsite(source.Inner.BusinessHomePage, vCardWebsiteTypes.Work));
            }

            target.Notes.Clear();
            if (!String.IsNullOrEmpty(source.Inner.Body))
            {
                target.Notes.Add(new vCardNote(source.Inner.Body));
            }

            return Task.FromResult(target);
        }

        public Task<IContactItemWrapper> Map2To1(vCard source, IContactItemWrapper target, IEntitySynchronizationLogger logger, ICardDavRepositoryLogger context)
        {
            target.Inner.FirstName = source.GivenName;
            target.Inner.LastName = source.FamilyName;
            target.Inner.Title = source.NamePrefix;
            target.Inner.Suffix = source.NameSuffix;
            target.Inner.MiddleName = source.AdditionalNames;
            target.Inner.Gender = MapGender1To2(source.Gender);

            target.Inner.AssistantName = source.Assistant;
            target.Inner.Spouse = source.Spouse;
            target.Inner.ManagerName = source.Manager;

            if (String.IsNullOrEmpty(target.Inner.FullName))
                target.Inner.FullName = source.FormattedName;
            if (!_configuration.KeepOutlookFileAs)
                target.Inner.FileAs = source.FormattedName;

            if (source.Nicknames.Count > 0)
            {
                string[] nickNames = new string[source.Nicknames.Count];
                source.Nicknames.CopyTo(nickNames, 0);
                target.Inner.NickName = String.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator, nickNames);
            }
            else
            {
                target.Inner.NickName = String.Empty;
            }

            target.Inner.Sensitivity = CommonEntityMapper.MapPrivacy2To1(source.AccessClassification);

            if (source.Categories.Count > 0)
            {
                string[] categories = new string[source.Categories.Count];
                source.Categories.CopyTo(categories, 0);
                target.Inner.Categories = String.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator, categories);
            }
            else
            {
                target.Inner.Categories = String.Empty;
            }

            MapIMs2To1(source, target.Inner);

            target.Inner.Email1Address = String.Empty;
            target.Inner.Email1DisplayName = String.Empty;
            target.Inner.Email2Address = String.Empty;
            target.Inner.Email2DisplayName = String.Empty;
            target.Inner.Email3Address = String.Empty;
            target.Inner.Email3DisplayName = String.Empty;
            if (source.EmailAddresses.Count >= 1)
            {
                bool FirstPredicate(vCardEmailAddress e) => _configuration.MapOutlookEmail1ToWork ? e.ItemType == ItemType.WORK : e.ItemType == ItemType.HOME;

                var first = source.EmailAddresses.FirstOrDefault(FirstPredicate) ?? source.EmailAddresses.First();
                target.Inner.Email1Address = first.Address;

                var second = source.EmailAddresses.FirstOrDefault(e => _configuration.MapOutlookEmail1ToWork ? e.ItemType == ItemType.HOME : e.ItemType == ItemType.WORK && e != first) ??
                             source.EmailAddresses.FirstOrDefault(e => e != first);

                if (second != null)
                {
                    target.Inner.Email2Address = second.Address;

                    var other = source.EmailAddresses.FirstOrDefault(e => e != first && e != second);
                    if (other != null)
                    {
                        target.Inner.Email3Address = other.Address;
                    }
                }
            }

            MapPostalAddresses2To1(source, target.Inner);

            MapTelephoneNumber2To1(source, target.Inner);

            if (_configuration.MapAnniversary)
            {
                if (source.Anniversary.HasValue)
                {
                    if (!source.Anniversary.Value.Date.Equals(target.Inner.Anniversary))
                    {
                        try
                        {
                            target.Inner.Anniversary = source.Anniversary.Value;
                        }
                        catch (COMException ex)
                        {
                            s_logger.Warn("Could not update contact anniversary.", ex);
                            logger.LogWarning("Could not update contact anniversary.", ex);
                        }
                        catch (OverflowException ex)
                        {
                            s_logger.Warn("Contact anniversary has invalid value.", ex);
                            logger.LogWarning("Contact anniversary has invalid value.", ex);
                        }
                    }
                }
                else
                {
                    target.Inner.Anniversary = OutlookUtility.OUTLOOK_DATE_NONE;
                }
            }

            if (_configuration.MapBirthday)
            {
                if (source.BirthDate.HasValue)
                {
                    if (!source.BirthDate.Value.Date.Equals(target.Inner.Birthday))
                    {
                        try
                        {
                            target.Inner.Birthday = source.BirthDate.Value;
                        }
                        catch (COMException ex)
                        {
                            s_logger.Warn("Could not update contact birthday.", ex);
                            logger.LogWarning("Could not update contact birthday.", ex);
                        }
                        catch (OverflowException ex)
                        {
                            s_logger.Warn("Contact birthday has invalid value.", ex);
                            logger.LogWarning("Contact birthday has invalid value.", ex);
                        }
                    }
                }
                else
                {
                    target.Inner.Birthday = OutlookUtility.OUTLOOK_DATE_NONE;
                }
            }

            target.Inner.CompanyName = source.Organization;
            target.Inner.Department = GetVCardFixedDepartment(source);

            target.Inner.JobTitle = source.Title;
            target.Inner.Profession = source.Role;

            target.Inner.Body = source.Notes.Count > 0 ? source.Notes[0].Text : String.Empty;

            // Correcting the 'Display As' field for email 1.
            // We want the name to be represented as "Surname Firstname Middlename"
            var email1Address = target.Inner.Email1Address;
            if (!String.IsNullOrEmpty(email1Address))
            {
                target.SaveAndReload();

                var parts = new List<string>
                {
                    source.FamilyName,
                    source.GivenName,
                    source.AdditionalNames
                };

                var fullName = String.Join(" ", parts.Where(x => !String.IsNullOrEmpty(x)));
                target.Inner.Email1DisplayName = $"{fullName} ({email1Address})";
            }

            return Task.FromResult(target);
        }

        private static OlGender MapGender1To2(vCardGender sourceGender)
        {
            switch (sourceGender)
            {
                case vCardGender.Female:
                    return OlGender.olFemale;
                case vCardGender.Male:
                    return OlGender.olMale;
                case vCardGender.Unknown:
                    return OlGender.olUnspecified;
            }

            throw new NotImplementedException($"Mapping for value '{sourceGender}' not implemented.");
        }

        private static vCardGender MapGender2To1(OlGender sourceGender)
        {
            switch (sourceGender)
            {
                case OlGender.olFemale:
                    return vCardGender.Female;
                case OlGender.olMale:
                    return vCardGender.Male;
                case OlGender.olUnspecified:
                    return vCardGender.Unknown;
            }

            throw new NotImplementedException($"Mapping for value '{sourceGender}' not implemented.");
        }

        private void MapEmailAddresses1To2(ContactItem source, vCard target, IEntitySynchronizationLogger logger)
        {
            target.EmailAddresses.Clear();
            if (!String.IsNullOrEmpty(source.Email1Address))
            {
                string email1Address = String.Empty;

                if (source.Email1AddressType == "EX")
                {
                    try
                    {
                        email1Address = GetPropertySafe(source.PropertyAccessor, PR_EMAIL1ADDRESS);
                    }
                    catch (COMException ex)
                    {
                        s_logger.Warn("Could not get property PR_EMAIL1ADDRESS for Email1Address", ex);
                        logger.LogWarning("Could not get property PR_EMAIL1ADDRESS for Email1Address", ex);
                    }
                }
                else
                {
                    email1Address = source.Email1Address;
                }

                if (!String.IsNullOrEmpty(email1Address))
                    target.EmailAddresses.Add(new vCardEmailAddress(email1Address, vCardEmailAddressType.Internet, _configuration.MapOutlookEmail1ToWork ? ItemType.WORK : ItemType.HOME));
            }

            if (!String.IsNullOrEmpty(source.Email2Address))
            {
                string email2Address = String.Empty;

                if (source.Email2AddressType == "EX")
                {
                    try
                    {
                        email2Address = GetPropertySafe(source.PropertyAccessor, PR_EMAIL2ADDRESS);
                    }
                    catch (COMException ex)
                    {
                        s_logger.Warn("Could not get property PR_EMAIL2ADDRESS for Email2Address", ex);
                        logger.LogWarning("Could not get property PR_EMAIL2ADDRESS for Email2Address", ex);
                    }
                }
                else
                {
                    email2Address = source.Email2Address;
                }

                if (!String.IsNullOrEmpty(email2Address))
                    target.EmailAddresses.Add(new vCardEmailAddress(email2Address, vCardEmailAddressType.Internet, _configuration.MapOutlookEmail1ToWork ? ItemType.HOME : ItemType.WORK));
            }

            if (!String.IsNullOrEmpty(source.Email3Address))
            {
                string email3Address = String.Empty;

                if (source.Email3AddressType == "EX")
                {
                    try
                    {
                        email3Address =  GetPropertySafe(source.PropertyAccessor, PR_EMAIL3ADDRESS);
                    }
                    catch (COMException ex)
                    {
                        s_logger.Warn("Could not get property PR_EMAIL3ADDRESS for Email3Address", ex);
                        logger.LogWarning("Could not get property PR_EMAIL3ADDRESS for Email3Address", ex);
                    }
                }
                else
                {
                    email3Address = source.Email3Address;
                }

                if (!String.IsNullOrEmpty(email3Address))
                    target.EmailAddresses.Add(new vCardEmailAddress(email3Address));
            }
        }

        private static void MapPhoneNumbers1To2(ContactItem source, vCard target)
        {
            target.Phones.Clear();
            if (!String.IsNullOrEmpty(source.PrimaryTelephoneNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.PrimaryTelephoneNumber, vCardPhoneTypes.Main);
                phoneNumber.IsPreferred = true;
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.MobileTelephoneNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.MobileTelephoneNumber, vCardPhoneTypes.Cellular);
                phoneNumber.IsPreferred = (target.Phones.Count == 0);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.HomeTelephoneNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.HomeTelephoneNumber, vCardPhoneTypes.Home);
                phoneNumber.IsPreferred = (target.Phones.Count == 0);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.Home2TelephoneNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.Home2TelephoneNumber, vCardPhoneTypes.HomeVoice);
                phoneNumber.IsPreferred = (target.Phones.Count == 0);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.HomeFaxNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.HomeFaxNumber, vCardPhoneTypes.Fax | vCardPhoneTypes.Home);
                phoneNumber.IsPreferred = (target.Phones.Count == 0);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.BusinessTelephoneNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.BusinessTelephoneNumber, vCardPhoneTypes.Work);
                phoneNumber.IsPreferred = (target.Phones.Count == 0);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.Business2TelephoneNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.Business2TelephoneNumber, vCardPhoneTypes.WorkVoice);
                phoneNumber.IsPreferred = (target.Phones.Count == 0);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.BusinessFaxNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.BusinessFaxNumber, vCardPhoneTypes.WorkFax);
                phoneNumber.IsPreferred = (target.Phones.Count == 0);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.PagerNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.PagerNumber, vCardPhoneTypes.Pager);
                phoneNumber.IsPreferred = (target.Phones.Count == 0);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.CarTelephoneNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.CarTelephoneNumber, vCardPhoneTypes.Car);
                phoneNumber.IsPreferred = (target.Phones.Count == 0);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.ISDNNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.ISDNNumber, vCardPhoneTypes.ISDN);
                phoneNumber.IsPreferred = (target.Phones.Count == 0);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.OtherTelephoneNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.OtherTelephoneNumber, vCardPhoneTypes.Voice);
                target.Phones.Add(phoneNumber);
            }

            if (!String.IsNullOrEmpty(source.OtherFaxNumber))
            {
                vCardPhone phoneNumber = new vCardPhone(source.OtherFaxNumber, vCardPhoneTypes.Fax);
                target.Phones.Add(phoneNumber);
            }
        }

        private void MapTelephoneNumber2To1(vCard source, ContactItem target)
        {
            target.HomeTelephoneNumber = String.Empty;
            target.BusinessTelephoneNumber = String.Empty;
            target.BusinessFaxNumber = String.Empty;
            target.PrimaryTelephoneNumber = String.Empty;
            target.MobileTelephoneNumber = String.Empty;
            target.PagerNumber = String.Empty;
            target.OtherTelephoneNumber = String.Empty;
            target.HomeFaxNumber = String.Empty;
            target.OtherFaxNumber = String.Empty;
            target.Home2TelephoneNumber = String.Empty;
            target.Business2TelephoneNumber = String.Empty;
            target.CarTelephoneNumber = String.Empty;
            target.ISDNNumber = String.Empty;

            // if no PhoneTypes are set (e.g. Yandex drops the types) 
            // assume a default ordering of cell,work,home to avoid data loss of the first 3 numbers

            if (source.Phones.Count >= 1 && source.Phones.All(p => p.PhoneType == vCardPhoneTypes.Default))
            {
                var phoneNumber1 = source.Phones[0].FullNumber;
                target.MobileTelephoneNumber = _configuration.FixPhoneNumberFormat
                    ? FixPhoneNumberFormat(phoneNumber1)
                    : phoneNumber1;

                if (source.Phones.Count >= 2)
                {
                    var phoneNumber2 = source.Phones[1].FullNumber;
                    target.BusinessTelephoneNumber = _configuration.FixPhoneNumberFormat
                        ? FixPhoneNumberFormat(phoneNumber2)
                        : phoneNumber2;
                    if (source.Phones.Count >= 3)
                    {
                        var phoneNumber3 = source.Phones[2].FullNumber;
                        target.HomeTelephoneNumber = _configuration.FixPhoneNumberFormat
                            ? FixPhoneNumberFormat(phoneNumber3)
                            : phoneNumber3;
                    }
                }

                return;
            }

            foreach (var phoneNumber in source.Phones)
            {
                string sourceNumber = _configuration.FixPhoneNumberFormat ? FixPhoneNumberFormat(phoneNumber.FullNumber) : phoneNumber.FullNumber;
                if (phoneNumber.IsMain)
                {
                    target.PrimaryTelephoneNumber = sourceNumber;
                }
                else if (phoneNumber.IsCellular)
                {
                    target.MobileTelephoneNumber = sourceNumber;
                }
                else if (phoneNumber.IsiPhone && String.IsNullOrEmpty(target.MobileTelephoneNumber))
                {
                    target.MobileTelephoneNumber = sourceNumber;
                }
                else if (phoneNumber.IsHome && !phoneNumber.IsFax)
                {
                    if (String.IsNullOrEmpty(target.HomeTelephoneNumber))
                    {
                        target.HomeTelephoneNumber = sourceNumber;
                    }
                    else
                    {
                        target.Home2TelephoneNumber = sourceNumber;
                    }
                }
                else if (phoneNumber.IsWork && !phoneNumber.IsFax)
                {
                    if (String.IsNullOrEmpty(target.BusinessTelephoneNumber))
                    {
                        target.BusinessTelephoneNumber = sourceNumber;
                    }
                    else
                    {
                        target.Business2TelephoneNumber = sourceNumber;
                    }
                }
                else if (phoneNumber.IsFax)
                {
                    if (phoneNumber.IsHome)
                    {
                        target.HomeFaxNumber = sourceNumber;
                    }
                    else
                    {
                        if (String.IsNullOrEmpty(target.BusinessFaxNumber))
                        {
                            target.BusinessFaxNumber = sourceNumber;
                        }
                        else
                        {
                            target.OtherFaxNumber = sourceNumber;
                        }
                    }
                }
                else if (phoneNumber.IsPager)
                {
                    target.PagerNumber = sourceNumber;
                }
                else if (phoneNumber.IsCar)
                {
                    target.CarTelephoneNumber = sourceNumber;
                }
                else if (phoneNumber.IsISDN)
                {
                    target.ISDNNumber = sourceNumber;
                }
                else
                {
                    if (phoneNumber.IsPreferred && String.IsNullOrEmpty(target.PrimaryTelephoneNumber))
                    {
                        target.PrimaryTelephoneNumber = sourceNumber;
                    }
                    else if (phoneNumber.IsPreferred && String.IsNullOrEmpty(target.HomeTelephoneNumber))
                    {
                        target.HomeTelephoneNumber = sourceNumber;
                    }
                    else
                    {
                        target.OtherTelephoneNumber = sourceNumber;
                    }
                }
            }
        }

        private static string FixPhoneNumberFormat(string number)
        {
            // Reformat telephone numbers so that Outlook can split country/area code and extension
            var match = Regex.Match(number, @"(\+\d+) (\d+) (\d+)( \d+)?");
            if (match.Success)
            {
                string ext = String.IsNullOrEmpty(match.Groups[4].Value) ? String.Empty : " - " + match.Groups[4].Value;

                return match.Groups[1].Value + " ( " + match.Groups[2].Value + " ) " + match.Groups[3].Value + ext;
            }
            else
            {
                return number;
            }
        }

        private static void MapPostalAddresses2To1(vCard source, ContactItem target)
        {
            target.HomeAddress = String.Empty;
            target.HomeAddressStreet = String.Empty;
            target.HomeAddressCity = String.Empty;
            target.HomeAddressPostalCode = String.Empty;
            target.HomeAddressCountry = String.Empty;
            target.HomeAddressState = String.Empty;
            target.HomeAddressPostOfficeBox = String.Empty;

            target.BusinessAddress = String.Empty;
            target.BusinessAddressStreet = String.Empty;
            target.BusinessAddressCity = String.Empty;
            target.BusinessAddressPostalCode = String.Empty;
            target.BusinessAddressCountry = String.Empty;
            target.BusinessAddressState = String.Empty;
            target.BusinessAddressPostOfficeBox = String.Empty;
            target.OfficeLocation = String.Empty;

            target.OtherAddress = String.Empty;
            target.OtherAddressStreet = String.Empty;
            target.OtherAddressCity = String.Empty;
            target.OtherAddressPostalCode = String.Empty;
            target.OtherAddressCountry = String.Empty;
            target.OtherAddressState = String.Empty;
            target.OtherAddressPostOfficeBox = String.Empty;

            target.SelectedMailingAddress = OlMailingAddress.olNone;

            foreach (var sourceAddress in source.DeliveryAddresses)
            {
                if (sourceAddress.IsHome)
                {
                    target.HomeAddressCity = sourceAddress.City;
                    target.HomeAddressCountry = sourceAddress.Country;
                    target.HomeAddressPostalCode = sourceAddress.PostalCode;
                    target.HomeAddressState = sourceAddress.Region;
                    target.HomeAddressStreet = sourceAddress.Street;
                    if (!String.IsNullOrEmpty(sourceAddress.ExtendedAddress))
                        target.HomeAddressStreet += "\r\n" + sourceAddress.ExtendedAddress;
                    target.HomeAddressPostOfficeBox = sourceAddress.PoBox;
                    if (sourceAddress.IsPreferred)
                    {
                        target.SelectedMailingAddress = OlMailingAddress.olHome;
                    }
                }
                else if (sourceAddress.IsWork)
                {
                    target.BusinessAddressCity = sourceAddress.City;
                    target.BusinessAddressCountry = sourceAddress.Country;
                    target.BusinessAddressPostalCode = sourceAddress.PostalCode;
                    target.BusinessAddressState = sourceAddress.Region;
                    target.BusinessAddressStreet = sourceAddress.Street;
                    if (!String.IsNullOrEmpty(sourceAddress.ExtendedAddress))
                    {
                        if (String.IsNullOrEmpty(target.OfficeLocation))
                        {
                            target.OfficeLocation = sourceAddress.ExtendedAddress;
                        }
                        else
                        {
                            target.BusinessAddressStreet += "\r\n" + sourceAddress.ExtendedAddress;
                        }
                    }

                    target.BusinessAddressPostOfficeBox = sourceAddress.PoBox;
                    if (sourceAddress.IsPreferred)
                    {
                        target.SelectedMailingAddress = OlMailingAddress.olBusiness;
                    }
                }
                else
                {
                    target.OtherAddressCity = sourceAddress.City;
                    target.OtherAddressCountry = sourceAddress.Country;
                    target.OtherAddressPostalCode = sourceAddress.PostalCode;
                    target.OtherAddressState = sourceAddress.Region;
                    target.OtherAddressStreet = sourceAddress.Street;
                    if (!String.IsNullOrEmpty(sourceAddress.ExtendedAddress))
                        target.OtherAddressStreet += "\r\n" + sourceAddress.ExtendedAddress;
                    target.OtherAddressPostOfficeBox = sourceAddress.PoBox;
                    if (sourceAddress.IsPreferred)
                    {
                        target.SelectedMailingAddress = OlMailingAddress.olOther;
                    }
                }
            }
        }

        private void MapIMs2To1(vCard source, ContactItem target)
        {
            var alreadyContainedImAddresses = new HashSet<string>();

            target.IMAddress = String.Empty;
            foreach (var im in source.IMs)
            {
                string imString;

                if (im.ServiceType != IMServiceType.Unspecified && im.ServiceType != _configuration.DefaultImServicType)
                    imString = im.ServiceType + ": " + im.Handle;
                else
                    imString = im.Handle;

                if (!String.IsNullOrEmpty(target.IMAddress))
                {
                    // some servers like iCloud use IMPP and X-PROTOCOL together
                    // don't add IM address twice in such case
                    if (alreadyContainedImAddresses.Add(imString))
                    {
                        target.IMAddress += "; " + imString;
                    }
                }
                else
                {
                    target.IMAddress = imString;
                }
            }
        }

        private static string GetVCardFixedDepartment(vCard vcard)
        {
            var department = vcard.Department;
            var suffix = ";" + vcard.Title;
            if (department.EndsWith(suffix))
            {
                department = department.Substring(0, department.Length - suffix.Length);
            }

            var allDepartments = new List<string>(department.Split(';'));
            allDepartments.RemoveAll(x => String.IsNullOrWhiteSpace(x));
            return String.Join(", ", allDepartments);
        }

        private static dynamic GetPropertySafe(PropertyAccessor accessor, string propertyName)
        {
            using (var wrapper = GenericComObjectWrapper.Create(accessor))
            {
                return wrapper.Inner.GetProperty(propertyName);
            }
        }
    }
}

