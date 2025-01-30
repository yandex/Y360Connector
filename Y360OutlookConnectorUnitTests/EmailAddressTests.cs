using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnectorUnitTests
{
    [TestClass]
    public class EmailAddressTests
    {
        [TestMethod]
        [DataRow("file:///test1@mail.ru", "file:///test1@mail.ru", false)]
        [DataRow("mailto:test1@mail.ru", "file:///test1@mail.ru", false)]
        [DataRow("mailto:test1@mail.ru", "mailto:test1@mail.ru", true)]
        [DataRow("mailto:test1@mail.ru", "mailto:test2@mail.ru", false)]
        [DataRow("mailto:test1@mail.ru", "mailto:test2", false)]
        public void TestSameEmailAddress(string address1, string address2, bool expectedResult)
        {
            Assert.AreEqual(expectedResult, EmailAddress.AreSame(new Uri(address1), new Uri(address2)));               
        }

        [TestMethod]
        [DataRow("mailto:test09@компанияикс.рф<info@calendar.yandex.ru>", false)] 
        [DataRow("mailto:test09@компанияикс.рф", true)]
        public void TestWellFormedUri(string uri, bool expectedResult)
        {
            Assert.AreEqual(expectedResult, Uri.IsWellFormedUriString(uri, UriKind.Absolute));
        }
    }
}
