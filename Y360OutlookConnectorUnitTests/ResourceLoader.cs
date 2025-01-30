using System.IO;
using System.Reflection;

namespace Y360OutlookConnectorUnitTests
{
    public static class ResourceLoader
    {
        public static string LoadEvent(string resourceName)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Y360OutlookConnectorUnitTests.Data.{resourceName}"))
            {
                using(var r = new StreamReader(s))
                {
                    return r.ReadToEnd();
                }
            }
        }
    }
}
