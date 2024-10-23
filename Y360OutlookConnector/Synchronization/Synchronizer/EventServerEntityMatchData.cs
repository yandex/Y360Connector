using System;
using DDay.iCal;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    public class EventServerEntityMatchData
    {
        public string Summary { get; }
        public bool IsAllDay { get; }
        
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime StartUtc { get; }
        public DateTime EndUtc { get; }

        public EventServerEntityMatchData(IICalendar calendar)
        {
            var evt = calendar.Events[0];
            Summary = evt.Summary;
            IsAllDay = evt.IsAllDay;
            // Если встреча занимает несколько дней (или один целый день), то используются StartDate и
            // EndDate. Эти свойства содержат дату начала и окончания (не включая)
            StartDate = evt.Start.Value;
            EndDate = evt.End.Value;
            // Для событий, которые не занимают целиком день или несколько дней используем для сравнения время начала и окончания в UTC 
            // UTC используется так как время события задается по времени организатора, таймзона которого может отличаться от таймзоны участника
            StartUtc = evt.Start.UTC;
            EndUtc = evt.End.UTC;            
        }
    }
}
