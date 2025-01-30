using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Y360OutlookConnector.Ui.Extensions;

namespace Y360OutlookConnectorUnitTests
{
    [TestClass]
    public class AppointmentItemExtensionsTests
    {
        [TestMethod]
        [DataRow("textAfter")]
        [DataRow("  textAfter")]
        [DataRow("\t\ttextAfter")]
        public void TestInsertTextIfBeforeEmptyText(string textAfter)
        {
            var textToAdd = "my text";
            var textBefore = string.Empty;
          
            var newText = textToAdd.InsertText(textBefore, textAfter);
            Assert.AreEqual(new StringBuilder(textToAdd).AppendLine().Append(textAfter).ToString(), newText);
        }

        [TestMethod]
        [DataRow("textBefore")]
        [DataRow("textBefore   ")]
        [DataRow("textBefore\t\t")]
        [DataRow("textBefore\t ")]
        public void TestInsertTextIfAfterEmptyText(string textBefore)
        {
            var textToAdd = "my text";
            var textAfter = string.Empty;

            var newText = textToAdd.InsertText(textBefore, textAfter);
            Assert.AreEqual(new StringBuilder(textBefore).AppendLine().Append(textToAdd).ToString(), newText);
        }

        [TestMethod]
        [DataRow("\r\ntextAfter")]
        [DataRow(" \r\n textAfter")]
        [DataRow("\t\r\n textAfter")]
        public void TestInsertTextIfBeforeEmptyAndAfterStartsWithNewLineText(string textAfter)
        {
            var textToAdd = "my text";
            var textBefore = string.Empty;
            
            var newText = textToAdd.InsertText(textBefore, textAfter);
            Assert.AreEqual(new StringBuilder(textToAdd).Append(textAfter).ToString(), newText);
        }

        [TestMethod]
        [DataRow("textBefore\r\n")]
        [DataRow("textBefore \r\n")]
        [DataRow("textBefore\t\r\n")]
        public void TestInsertTextIfAfterEmptyAndBeforeEndsWithNewLineText(string textBefore)
        {
            var textToAdd = "my text";
            var textAfter = string.Empty;

            var newText = textToAdd.InsertText(textBefore, textAfter);
            Assert.AreEqual(new StringBuilder(textBefore).Append(textToAdd).ToString(), newText);
        }
    }
}
