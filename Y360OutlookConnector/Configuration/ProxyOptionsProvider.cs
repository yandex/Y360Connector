using CalDavSynchronizer.Contracts;
using log4net;
using System;
using System.IO;
using System.Reflection;

namespace Y360OutlookConnector.Configuration
{
    public class ProxyOptionsProvider
    {
        private const string ProxyOptionsFileName = "proxy_options.xml";

        private readonly string _dataFolderPath;

        private ProxyOptions _proxyOptions;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public event EventHandler ProxyOptionsChanged;

        public ProxyOptionsProvider(string dataFolderPath)
        {
            _proxyOptions = CreateDefaultProxy();
            _dataFolderPath = dataFolderPath;
            _proxyOptions = LoadProxyOptions();
        }

        public ProxyOptions GetProxyOptions()
        {
            return _proxyOptions;
        }

        public void SetProxyOptions(ProxyOptions proxyOptions)
        {
            if (proxyOptions == null)
                proxyOptions = CreateDefaultProxy();

            s_logger.Info("New proxy options applied");

            _proxyOptions = proxyOptions;
            XmlFile.Save(Path.Combine(_dataFolderPath, ProxyOptionsFileName), _proxyOptions);

            ProxyOptionsChanged?.Invoke(null, EventArgs.Empty);
        }

        private ProxyOptions LoadProxyOptions()
        {
            var fileName = Path.Combine(_dataFolderPath, ProxyOptionsFileName);
            return XmlFile.Load(fileName, CreateDefaultProxy);
        }

        private static ProxyOptions CreateDefaultProxy()
        {
            return new ProxyOptions { ProxyUseDefault = true };
        }
    }
}
