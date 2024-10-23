using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using CalDavSynchronizer.Implementation.Common;
using CalDavSynchronizer.Implementation.ComWrappers;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Outlook = Microsoft.Office.Interop.Outlook;


namespace Y360OutlookConnector.Utilities
{
    public static class AppointmentItemUtils
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        // https://learn.microsoft.com/en-us/openspecs/exchange_server_protocols/ms-oxocal/1d3aac05-a7b9-45cc-a213-47f0a0a2c5c1

        private static readonly byte[] GlobalObjectIdHeader = { 0x04, 0x00, 0x00, 0x00, 0x82, 0x00, 0xE0, 0x00, 
                                                                0x74, 0xC5, 0xB7, 0x10, 0x1A, 0x82, 0xE0, 0x08};

        private const string PidLidAppointmentReplyTime =
            "http://schemas.microsoft.com/mapi/id/{00062002-0000-0000-C000-000000000046}/82200040";
        private const string LID_OWNER_CRITICAL_CHANGE =
            "http://schemas.microsoft.com/mapi/id/{6ED8DA90-450B-101B-98DA-00AA003F1305}/001A0040";

        public static byte[] ConvertHexStringToByteArray(string hexString)
        {
            if (String.IsNullOrEmpty(hexString) || hexString.Length % 2 != 0)
                return Array.Empty<byte>();

            var formatProvider = CultureInfo.InvariantCulture;
            byte[] data = new byte[hexString.Length / 2];
            for (int index = 0; index < data.Length; index++)
            {
                string substring = hexString.Substring(index * 2, 2);
                if (!Byte.TryParse(substring, NumberStyles.HexNumber, formatProvider, out var byteValue))
                    return Array.Empty<byte>();

                data[index] = byteValue;
            }

            return data;
        }

        public static string ExtractUidFromGlobalId(string globalId)
        {
            var bytes = ConvertHexStringToByteArray(globalId);
            if (bytes.Length > 40)
            {
                byte[] data = new byte[bytes.Length - 40];
                Array.Copy(bytes, 40, data, 0, data.Length);
                var text = System.Text.Encoding.UTF8.GetString(data, 0, data.Length);
                if (text.StartsWith("vCal-Uid", StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Replace("vCal-Uid", String.Empty);
                    text = text.Replace("\u0001", String.Empty);
                    text = text.Replace("\0", String.Empty);
                    return text;
                }
            }
            return globalId ?? String.Empty;
        }

        public static bool IsGlobalAppointmentId(string hexString)
        {
            var bytes = ConvertHexStringToByteArray(hexString);
            if (bytes.Length < GlobalObjectIdHeader.Length)
                return false;

            var header = bytes.Take(GlobalObjectIdHeader.Length);
            if (!header.SequenceEqual(GlobalObjectIdHeader))
                return false;

            return true;
        }

        public static byte[] CreateGlobalExceptionIdFromGlobalAppointmentId(string globalAppointmentId, 
            DateTime originalStart)
        {
            var bytes = ConvertHexStringToByteArray(globalAppointmentId);
            if (bytes.Length < 20)
                return OutlookUtility.MapUidToGlobalExceptionId(globalAppointmentId, originalStart);

            byte[] yearsBytes = BitConverter.GetBytes(originalStart.Year);
            bytes[16] = yearsBytes[1];
            bytes[17] = yearsBytes[0];
            bytes[18] = (byte) originalStart.Month;
            bytes[19] = (byte) originalStart.Day;

            return bytes;
        }

        public static DateTime GetLastChangeTime(Outlook.AppointmentItem appointment)
        {
            var lastChangeTime = appointment.LastModificationTime.ToUniversalTime();
            using (var wrapper = GenericComObjectWrapper.Create(appointment.PropertyAccessor))
            {
                try
                {
                    var ownerCriticalChangeTime = (DateTime)wrapper.Inner.GetProperty(LID_OWNER_CRITICAL_CHANGE);
                    if (ownerCriticalChangeTime > lastChangeTime)
                        lastChangeTime = ownerCriticalChangeTime;

                }
                catch
                {
                    // no-op
                }

                try
                {
                    var appointmentReplyTime = (DateTime)wrapper.Inner.GetProperty(PidLidAppointmentReplyTime);
                    if (appointmentReplyTime > lastChangeTime)
                        lastChangeTime = appointmentReplyTime;
                }
                catch
                {
                    // no-op
                }
            }

            return lastChangeTime;
        }

        public static string CreateCalendarUrl(this AppointmentItem appointment, Uri eventUrl, string userId, string layerId, bool isEventSequence)
        {
            var urlBuilder = new UriBuilder(eventUrl);

            var extraParameters = new Dictionary<string, string>
            {
                ["layerId"] = layerId
            };
           
            if (!string.IsNullOrEmpty(userId))
            {
                extraParameters["uid"] = userId;
            }

            var startDate = appointment.Start.Date;
            var lastMonday = startDate.AddDays(-(startDate.DayOfWeek - DayOfWeek.Monday));

            extraParameters["show_date"] = lastMonday.ToString("yyyy-MM-dd");
            extraParameters["event_date"] = appointment.StartUTC.ToString("yyyy-MM-ddTHH:mm:00");
            extraParameters["applyToFuture"] = isEventSequence ? "1" : "0";

            var extraQueryString = string.Join("&", extraParameters.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));

            if (string.IsNullOrEmpty(urlBuilder.Query))
            {
                urlBuilder.Query = extraQueryString;
            }
            else
            {
                urlBuilder.Query = $"{urlBuilder.Query.Substring(1)}&{extraQueryString}";
            }

            return urlBuilder.ToString();
        }

        public static Folder GetFolder(this AppointmentItem appointment)
        {
            var isRecurring = appointment.IsRecurring;

            if (!isRecurring)
            {
                return appointment.Parent as Folder;
            }

            var recurrenceState = appointment.GetRecurrenceState();
            switch (recurrenceState)
            {
                case OlRecurrenceState.olApptMaster:
                    // Серия целиком
                    return appointment.Parent as Folder;
                case OlRecurrenceState.olApptException:
                case OlRecurrenceState.olApptOccurrence:
                    // Событие в серии
                    if (!(appointment.Parent is AppointmentItem parentEvent))
                    {
                        return null;
                    }
                    return parentEvent.Parent as Folder;
            }

            return null;
        }

        public static OlRecurrenceState GetRecurrenceState(this AppointmentItem appointment) => appointment.RecurrenceState;        
    }
}
