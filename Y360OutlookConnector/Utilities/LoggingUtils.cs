using System;
using log4net;
using log4net.Core;
using log4net.Repository.Hierarchy;

namespace Y360OutlookConnector.Utilities
{
    public static class LoggingUtils
    {
        public static void ConfigureLogLevel(bool debugLogLevel)
        {
            if (debugLogLevel)
            {
                ((Hierarchy)LogManager.GetRepository()).Root.Level = Level.Debug;
            }
            else
            {
                ((Hierarchy)LogManager.GetRepository()).Root.Level = Level.Info;
            }

            ((Hierarchy)LogManager.GetRepository()).RaiseConfigurationChanged(EventArgs.Empty);
        }
    }
}
