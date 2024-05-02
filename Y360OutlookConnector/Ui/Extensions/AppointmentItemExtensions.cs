using System;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using log4net;
using Microsoft.Office.Interop.Outlook;
using Newtonsoft.Json;
using Y360OutlookConnector.Clients;
using Y360OutlookConnector.Clients.Telemost.Model;

namespace Y360OutlookConnector.Ui.Extensions
{
    internal static class AppointmentItemExtensions
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private static readonly Regex EmptyText = new Regex(@"^\s*$", RegexOptions.Compiled);

        private const string TelemostMeetingSettingsPropertyName = "TelemostMeetingSettings";

        private const string ForbiddenErrorCode = "ForbiddenError";

        private static void SetBody(this AppointmentItem appointment, string body)
        {
            if (appointment == null)
            {
                return;
            }
            appointment.Body = body;
        }

        /// <summary>
        /// Обновление темы встречи, если пользователь уже что то установил, то не меняем.
        /// В противном случае устанавливается значение по умолчанию 
        /// </summary>
        /// <param name="appointment"></param>
        /// <param name="defaultSubject">Значение темы встречи по умолчанию</param>
        private static void UpdateSubject(this AppointmentItem appointment, string defaultSubject)
        {
            if (appointment == null)
            {
                return;
            }

            var currentSubject = appointment.Subject ?? string.Empty;

            if (EmptyText.IsMatch(currentSubject))
            {
                appointment.Subject = defaultSubject;
            }
        }

        /// <summary>
        /// Обновление места встречи, если пользователь уже что то установил, то не меняем.
        /// В противном случае устанавливается значение по умолчанию
        /// </summary>
        /// <param name="appointment"></param>
        /// <param name="defaultLocation">Значение места встречи по умолчанию</param>
        private static void UpdateLocation(this AppointmentItem appointment, string defaultLocation)
        {
            if (appointment == null)
            {
                return;
            }

            var currentLocation = appointment.Location ?? string.Empty;

            if (EmptyText.IsMatch(currentLocation))
            {
                appointment.Location = defaultLocation;
            }
        }

        private static void SetMeetingInfo(this AppointmentItem appointment, ConferenceShort data, bool isInternal)
        {
            if (appointment == null || data == null)
            {
                return;
            }

            var info = new TelemostMeetingInfo { Id = data.Id, JoinUrl = data.JoinUrl, IsInternal = isInternal };
            var newValue = JsonConvert.SerializeObject(info);

            var property = appointment.UserProperties.Find(TelemostMeetingSettingsPropertyName);

            if (property == null)
            {
                property = appointment.UserProperties.Add(TelemostMeetingSettingsPropertyName, OlUserPropertyType.olText);
            }

            property.Value = newValue;
        }

        private static void PrependTextToBody(this AppointmentItem appointment, string text)
        {
            if (appointment == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var body = appointment.GetBody();

            if (string.IsNullOrEmpty(body))
            {
                appointment.Body = text;
            }
            else
            {
                var sb = new StringBuilder(text);

                // Добавляем перевод строки, только если текст в теле сообщения не начинается с перевода строки
                if (!body.StartsWith(Environment.NewLine))
                {
                    sb.AppendLine();
                }
                sb.Append(body);

                appointment.Body = sb.ToString();
            }
        }

        private static string GetBody(this AppointmentItem appointment)
        {
            if (appointment == null)
            {
                return null;
            }
            return appointment.Body;
        }

        private static void UpdateStatusLineWithError(this Inspector inspector, bool isCreateError, ApiCallResult<ConferenceShort> result)
        {
            var msgFormat = isCreateError ? Localization.Strings.Telemost_Messages_MeetingCreateErrorMessage : Localization.Strings.Telemost_Messages_MeetingUpdateErrorMessage;

            string message;

            if (result.Error != null)
            {
                var error = result.Error?._Error ?? string.Empty;

                if (error == ForbiddenErrorCode)
                {
                    msgFormat = isCreateError ? Localization.Strings.Telemost_Messages_MeetingCreateForbiddenErrorMessage : Localization.Strings.Telemost_Messages_MeetingUpdateForbiddenErrorMessage;
                }
                message = string.Format(msgFormat, error);
            }
            else
            {
                if (result.Exception is NoInternetException)
                {
                    message = Localization.Strings.Telemost_Messages_NoInternetErrorMessage;
                } 
                else 
                {
                    message = string.Format(msgFormat, "?");
                }
            }

            inspector.UpdateStatusLine(message);
        }

        private static async Task UpdateMeetingAsync(this AppointmentItem currentAppointment, TelemostMeetingInfo meetingInfo, bool isMeetingInternal)
        {
            var currentInspector = currentAppointment.GetInspector;

            currentInspector.UpdateStatusLine(Localization.Strings.Telemost_Messages_MeetingUpdatingMessage);

            Telemetry.Signal(Telemetry.TelemostApiCalls, isMeetingInternal ? "update_meeting_internal" : "update_meeting_external");

            var result = await ThisAddIn.Components?.TelemostClient.UpdateTelemostMeetingAsync(meetingInfo.Id, isMeetingInternal);

            // Проверка, не было ли закрыто окно встречи во время вызова API Телемоста, если было закрыто, то игнорируем результаты вызова
            if (!OutlookApplicationExtensions.GetApplication().IsAppointmentValid(currentAppointment))
            {
                s_logger.Info("Appointment window was closed.Ignore API call results");
                return;
            }

            var yandexRequestId = result.YandexRequestId ?? "?";

            if (result.Data == null)
            {
                Telemetry.Signal(Telemetry.TelemostApiCalls, isMeetingInternal ? "update_meeting_internal_error" : "update_meeting_external_error");

                currentInspector.UpdateStatusLineWithError(false, result);
                if (result.Error != null)
                {
                    s_logger.Error($"Telemost Api call PATCH telemost-api/conferences/{meetingInfo.Id} id: {result.RequestId} yandexRequestId: {yandexRequestId} error: {result.Error}");
                }
                else
                {
                    s_logger.Error($"Telemost Api call PATCH telemost-api/conferences/{meetingInfo.Id} id: {result.RequestId} yandexRequestId: {yandexRequestId} error");
                }
            }
            else
            {
                Telemetry.Signal(Telemetry.TelemostApiCalls, isMeetingInternal ? "update_meeting_internal_success" : "update_meeting_external_success");

                currentInspector.UpdateStatusLine(Localization.Strings.Telemost_Messages_MeetingUpdatedMessage);
                currentAppointment.SetMeetingInfo(result.Data, isMeetingInternal);

                s_logger.Info($"Telemost Api call PATCH telemost-api/conferences/{meetingInfo.Id} id: {result.RequestId} yandexRequestId: {yandexRequestId} result: {result.Data}");

                var joinUrl = result.Data.JoinUrl ?? string.Empty;
                currentAppointment.UpdateLocation(joinUrl);
                currentAppointment.UpdateSubject(Localization.Strings.Telemost_Messages_MeetingSubject);

                var linkText = string.Format(Localization.Strings.Telemost_Messages_MeetingLinkMessage, joinUrl);
                var body = new StringBuilder(linkText);

                var infoText = Localization.Strings.Telemost_Messages_MeetingInternalMessage;

                if (isMeetingInternal)
                {
                    body.AppendLine();
                    body.Append(infoText);
                }

                var newText = body.ToString();

                // Поиск в теле сообщения информации о митинге и замена его, если не найдем образец, то вставляем информацию о встрече 
                // в начале тела сообщения
                var currentBody = currentAppointment.GetBody();

                var pos = currentBody?.IndexOf(linkText) ?? -1;

                if (pos < 0)
                {
                    currentAppointment.PrependTextToBody(newText);
                }
                else
                {
                    var pos1 = currentBody.Substring(pos + linkText.Length).IndexOf(infoText);

                    var textBefore = string.Empty;
                    if (pos > 0)
                    {
                        textBefore = currentBody.Substring(0, pos - 1);
                    }

                    string textAfter;
                    if (pos1 < 0)
                    {
                        textAfter = currentBody.Substring(pos + linkText.Length);
                    }
                    else
                    {
                        var textBetweenParts = currentBody.Substring(pos + linkText.Length, pos1);

                        if (EmptyText.IsMatch(textBetweenParts))
                        {
                            textAfter = currentBody.Substring(pos + linkText.Length + pos1 + infoText.Length);
                        }
                        else
                        {
                            textAfter = currentBody.Substring(pos + linkText.Length);
                        }
                    }

                    var newBody = new StringBuilder(textBefore);

                    if (textBefore.Length > 0 && !textBefore.EndsWith(Environment.NewLine))
                    {
                        newBody.AppendLine();
                    }
                    newBody.Append(newText);

                    if (textAfter.Length > 0 && !textAfter.StartsWith(Environment.NewLine))
                    {
                        newBody.AppendLine();
                    }
                    newBody.Append(textAfter);

                    currentAppointment.SetBody(newBody.ToString());

                    // Применяем сохранение, только если встреча или собрание уже сохранены в outlook
                    if (!string.IsNullOrEmpty(currentAppointment.EntryID))
                    {
                        currentAppointment.Save();
                    }                    
                }
            }
        }

        private static async Task CreateMeetingAsync(this AppointmentItem currentAppointment, bool isMeetingInternal)
        {
            var currentInspector = currentAppointment.GetInspector;

            currentInspector.UpdateStatusLine(Localization.Strings.Telemost_Messages_MeetingCreatingMessage);

            Telemetry.Signal(Telemetry.TelemostApiCalls, isMeetingInternal ? "create_meeting_internal" : "create_meeting_external");
            var result = await ThisAddIn.Components?.TelemostClient.CreateTelemostMeetingAsync(isMeetingInternal);

            if (!OutlookApplicationExtensions.GetApplication().IsAppointmentValid(currentAppointment))
            {
                s_logger.Info("Appointment window was closed.Ignore API call results");
                return;
            }

            var yandexRequestId = result.YandexRequestId ?? "?";

            if (result.Data == null)
            {
                Telemetry.Signal(Telemetry.TelemostApiCalls, isMeetingInternal ? "create_meeting_internal_error" : "create_meeting_external_error");

                currentInspector.UpdateStatusLineWithError(true, result);

                if (result.Error != null)
                {
                    s_logger.Error($"Telemost Api call POST telemost-api/conferences id: {result.RequestId} yandexRequestId: {yandexRequestId} error: {result.Error}");
                }
                else
                {
                    s_logger.Error($"Telemost Api call POST telemost-api/conferences id: {result.RequestId} yandexRequestId: {yandexRequestId} error");
                }
            }
            else
            {
                Telemetry.Signal(Telemetry.TelemostApiCalls, isMeetingInternal ? "create_meeting_internal_success" : "create_meeting_external_success");

                currentInspector.UpdateStatusLine(Localization.Strings.Telemost_Messages_MeetingCreatedMessage);
                currentAppointment.SetMeetingInfo(result.Data, isMeetingInternal);

                s_logger.Info($"Telemost Api call POST telemost-api/conferences id: {result.RequestId} yandexRequestId: {yandexRequestId} result: {result.Data}");

                var joinUrl = result.Data.JoinUrl ?? string.Empty;
                currentAppointment.UpdateLocation(joinUrl);
                currentAppointment.UpdateSubject(Localization.Strings.Telemost_Messages_MeetingSubject);

                var body = new StringBuilder(string.Format(Localization.Strings.Telemost_Messages_MeetingLinkMessage, joinUrl));

                if (isMeetingInternal)
                {
                    body.AppendLine();
                    body.Append(Localization.Strings.Telemost_Messages_MeetingInternalMessage);
                }

                currentAppointment.PrependTextToBody(body.ToString());
            }
        }

        public static async Task CreateOrUpdateMeetingAsync(this AppointmentItem appointmentItem, bool isMeetingInternal)
        {

            if (appointmentItem == null)
            {
                return;
            }

            var inspector = appointmentItem.GetInspector;

            if (inspector == null)
            {
                return;
            }

            var meetingSetting = appointmentItem.GetMeetingInfo();
            if (meetingSetting != null)
            {
                // Обновляем уже добавленный митинг
                await appointmentItem.UpdateMeetingAsync(meetingSetting, isMeetingInternal);
            }
            else
            {
                // создаем новый митинг
                await appointmentItem.CreateMeetingAsync(isMeetingInternal);
            }

            var settingsPane = ThisAddIn.Components.PaneController.GetSettingsPane(inspector);

            var settingsControl = settingsPane?.Control as ITelemostSettingsControl;
            settingsControl?.UpdateMeetingInfo(appointmentItem.GetMeetingInfo());
        }

        public static TelemostMeetingInfo GetMeetingInfo(this AppointmentItem appointment)
        {
            if (appointment == null)
            {
                return null;
            }

            var property = appointment.UserProperties.Find(TelemostMeetingSettingsPropertyName);

            if (property == null)
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<TelemostMeetingInfo>(property.Value as string);
            }
            catch (System.Exception ex)
            {
                s_logger.Error("Fail to deserialize telemost meeting settings", ex);
                return null;
            }
        }
    }
}
