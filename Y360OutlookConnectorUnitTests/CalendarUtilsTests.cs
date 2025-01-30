using CalDavSynchronizer.DataAccess;
using DDay.iCal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnectorUnitTests
{
    [TestClass]
    public class CalendarUtilsTests
    {
        private IICalendar GetCalendar(string resourceName)
        {
            var entity = ResourceLoader.LoadEvent(resourceName);
            return CalendarUtils.DeserializeEntityData(entity, new WebResourceName { });
        }

        private IEvent GetSingleEvent(string resourceName)
        {
            var data = GetCalendar(resourceName);
            return data.Events[0];
        }

        [TestMethod]
        [DataRow("event1.ics", "mailto:test08@компанияикс.рф")]
        [DataRow("event2.ics", "mailto:test08@компанияикс.рф")]
        public void TestAttendeeAddressTwoLines1(string resourceName, string expectedAttendee)
        {
            var ev = GetSingleEvent(resourceName);

            var attendees = ev.Attendees.Select(x => x.Copy<Attendee>()).ToList();
            var a = attendees.First();

            Assert.IsTrue(EmailAddress.AreSame(a.Value, new Uri(expectedAttendee)));
        }

        [TestMethod]
        [DataRow("event7.ics", "mailto:info@calendar.yandex.ru")]
        public void TestOrganizer(string resourceName, string expectedOrganizer)
        {
            var ev = GetSingleEvent(resourceName);

            var organizer = ev.Organizer;
            Assert.IsTrue(EmailAddress.AreSame(organizer.Value, new Uri(expectedOrganizer)));
        }

        [TestMethod]
        [DataRow("event3.ics", true)]
        [DataRow("event4.ics", false)]
        [DataRow("event5.ics", false)]
        [DataRow("event6.ics", false)]
        public void TestAllowEditProperty(string resourceName, bool expectedResult)
        {
            var ev = GetCalendar(resourceName);

            Assert.AreEqual(expectedResult, ev.CanParticipantsEditEvent());
        }
    }
}
