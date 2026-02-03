using System;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Implementation.ComWrappers;
using CalDavSynchronizer.Implementation.Events;
using DDay.iCal;
using GenSync.Synchronization.States;

namespace Y360OutlookConnector.Synchronization.Synchronizer.States
{
    public sealed class UpdateByUidCandidate 
        : Discard<AppointmentId, DateTime, IAppointmentItemWrapper, WebResourceName, string, IICalendar, IEventSynchronizationContext>
    {
        public AppointmentId AId { get; }
        public DateTime AVersion { get; }
        public string Uid { get; }

        public UpdateByUidCandidate(AppointmentId aId, DateTime aVerson, string uid)
        {
            AId = aId ?? throw new ArgumentNullException(nameof(aId));
            AVersion = aVerson;
            Uid = uid ?? throw new ArgumentNullException(nameof(uid));
        }
    }
}
