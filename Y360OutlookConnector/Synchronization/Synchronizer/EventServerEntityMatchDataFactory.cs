using DDay.iCal;
using GenSync.InitialEntityMatching;


namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    internal class EventServerEntityMatchDataFactory : IMatchDataFactory<IICalendar, EventServerEntityMatchData>
    {
        public EventServerEntityMatchData CreateMatchData(IICalendar entity)
        {
            return new EventServerEntityMatchData(entity);
        }
    }
}
