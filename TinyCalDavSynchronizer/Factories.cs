using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.DataAccess.HttpClientBasedClient;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Contacts;
using CalDavSynchronizer.Implementation.Events;
using CalDavSynchronizer.Implementation.Tasks;
using CalDavSynchronizer.Implementation.TimeRangeFiltering;
using CalDavSynchronizer.Scheduling;
using DDay.iCal;
using GenSync.InitialEntityMatching;
using GenSync.Synchronization;
using GenSync.Synchronization.StateCreationStrategies.ConflictStrategies;
using Thought.vCards;

namespace TinyCalDavSynchronizer
{
    public static class Factories
    {
        public static ConflictInitialSyncStateCreationStrategyAutomatic<string, DateTime, IContactItemWrapper,
                WebResourceName, string, vCard, ICardDavRepositoryLogger>
            CreateContactConflictInitialSyncStateCreationStrategyAutomatic(EntitySyncStateEnvironment<string, DateTime, 
                IContactItemWrapper,WebResourceName, string, vCard, ICardDavRepositoryLogger> environment)
        {
            return new ContactConflictInitialSyncStateCreationStrategyAutomatic(environment);
        }

        public static ConflictInitialSyncStateCreationStrategyAutomatic<AppointmentId, DateTime, 
                IAppointmentItemWrapper, WebResourceName, string, IICalendar, IEventSynchronizationContext>
            CreateEventConflictInitialSyncStateCreationStrategyAutomatic(EntitySyncStateEnvironment<AppointmentId, 
                DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar,
                IEventSynchronizationContext> environment)
        {
            return new EventConflictInitialSyncStateCreationStrategyAutomatic(environment);
        }

        public static ConflictInitialSyncStateCreationStrategyAutomatic<string, DateTime, ITaskItemWrapper, 
                WebResourceName, string, IICalendar, int>
            CreateTaskConflictInitialSyncStateCreationStrategyAutomatic(EntitySyncStateEnvironment<string, 
                DateTime, ITaskItemWrapper, WebResourceName, string, IICalendar, int> environment)
        {
            return new TaskConflictInitialSyncStateCreationStrategyAutomatic(environment);
        }

        public static IMatchDataFactory<ITaskItemWrapper, TaskEntityMatchData> CreateTaskEntityMatchDataFactory()
        {
            return new TaskEntityMatchDataFactory();
        }
        

        public static IMatchDataFactory<IContactItemWrapper, ContactMatchData> CreateContactMatchDataFactory()
        {
            return new ContactMatchDataFactory();
        }

        public static IMatchDataFactory<IAppointmentItemWrapper, EventEntityMatchData>
            CreateEventEntityMatchDataFactory()
        {
            return new EventEntityMatchDataFactory();
        }

        public static IMatchDataFactory<IICalendar, EventServerEntityMatchData> 
            CreateEventServerEntityMatchDataFactory()
        {
            return new EventServerEntityMatchDataFactory();
        }

        public static IInitialEntityMatcher<string, DateTime, ContactMatchData, WebResourceName, string, vCard>
            CreateInitialContactEntityMatcher(IEqualityComparer<WebResourceName> equalityComparer)
        {
            return new InitialContactEntityMatcher(equalityComparer);
        }

        public static IInitialEntityMatcher<AppointmentId, DateTime, EventEntityMatchData, WebResourceName, 
                string, EventServerEntityMatchData>
            CreateInitialEventEntityMatcher(IEqualityComparer<WebResourceName> equalityComparer)
        {
            return new InitialEventEntityMatcher(equalityComparer);
        }

        public static IInitialEntityMatcher<string, DateTime, TaskEntityMatchData, WebResourceName, string, IICalendar>
            CreateInitialTaskEntityMatcher(IEqualityComparer<WebResourceName> equalityComparer)
        {
            return new InitialTaskEntityMatcher(equalityComparer);
        }

        public static IDateTimeRangeProvider CreateDateTimeRangeProvider(int daysInThePast, int daysInTheFuture)
        {
            return new DateTimeRangeProvider(daysInThePast, daysInTheFuture);
        }

        public static IEqualityComparer<DateTime> CreateDateTimeEqualityComparer()
        {
            return new DateTimeEqualityComparer();
        }

        public static ICalendarResourceResolver CreateCalendarResourceResolver(ICalDavDataAccess calDavDataAccess)
        {
            return new CalendarResourceResolver(calDavDataAccess);
        }

        public static IHttpHeaders CreateHttpResponseHeadersAdapter(HttpResponseHeaders headersFromFirstCall,
            HttpResponseHeaders headersFromLastCall)
        {
            return new HttpResponseHeadersAdapter(headersFromFirstCall, headersFromLastCall);
        }
    }
}
