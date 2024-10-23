using System;
using System.Collections.Generic;
using GenSync.InitialEntityMatching;
using CalDavSynchronizer.Implementation.Events;
using CalDavSynchronizer.DataAccess;

namespace Y360OutlookConnector.Synchronization.Synchronizer
{
    internal class InitialEventEntityMatcher : InitialEntityMatcherByPropertyGrouping<AppointmentId, DateTime, EventEntityMatchData, string, WebResourceName, string, EventServerEntityMatchData, string>
    {
        public InitialEventEntityMatcher(IEqualityComparer<WebResourceName> btypeIdEqualityComparer)
            : base(btypeIdEqualityComparer)
        {
        }

        protected override bool AreEqual(EventEntityMatchData atypeEntity, EventServerEntityMatchData evt)
        {
            if (evt.Summary == atypeEntity.Subject)
            {
                if (evt.IsAllDay)
                {
                    if (!atypeEntity.AllDayEvent)
                    {
                        return false;
                    }
                    return evt.StartDate == atypeEntity.Start && evt.EndDate == atypeEntity.End;
                }
                else
                {
                    if (atypeEntity.AllDayEvent)
                    {
                        return false;
                    }

                    if (evt.StartUtc != atypeEntity.StartUtc)
                    {
                        return false;
                    }

                    return evt.EndUtc == atypeEntity.EndUtc;
                }
            }

            return false;
        }

        protected override string GetAtypePropertyValue(EventEntityMatchData atypeEntity)
        {
            return atypeEntity.Subject?.ToLower() ?? string.Empty;
        }

        protected override string GetBtypePropertyValue(EventServerEntityMatchData btypeEntity)
        {
            return btypeEntity.Summary?.ToLower() ?? string.Empty;
        }

        protected override string MapAtypePropertyValue(string value)
        {
            return value;
        }
    }
}
