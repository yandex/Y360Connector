using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CalDavSynchronizer.DataAccess;
using DDay.iCal;
using log4net;

namespace Y360OutlookConnector.Utilities
{
    public static class WebDavClientExtensions
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public static async Task<IICalendar> GetEntityAsync(this IWebDavClient webDavClient, string uId, string configUrl)
        {
            Uri serverUrl;
            try
            {
                serverUrl = new Uri(configUrl);

                var calDavDataAccess = new CalDavDataAccess(serverUrl, webDavClient);

                var webResourceName = new WebResourceName($"{serverUrl.AbsolutePath}{uId}.ics");
                var entities = await calDavDataAccess.GetEntities(new[] { webResourceName });

                var entity = entities.FirstOrDefault();

                if (entity == null)
                {
                    return null;
                }

                return CalendarUtils.DeserializeEntityData(entity.Entity, webResourceName);
            }
            catch (Exception ex)
            {
                s_logger.Error(ex.Message);
                return null;
            }
        }

    }
}
