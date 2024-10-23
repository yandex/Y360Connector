using System;
using System.IO;
using System.Reflection;
using log4net;

namespace Y360OutlookConnector.Configuration
{
    public class GeneralOptionsProvider
    {
        private const string GeneralOptionsFileName = "general_options.xml";

        private readonly string _dataFolderPath;

        private GeneralOptions _options;

        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public event EventHandler OptionsChanged;

        public GeneralOptionsProvider(string dataFolderPath)
        {
            _options = CreateDefaultOptions();
            _dataFolderPath = dataFolderPath;
            _options = LoadOptions();
        }

        public GeneralOptions Options
        {
            get => _options;
            set
            {
                if (value == null)
                    value = CreateDefaultOptions();

                s_logger.Info("New general options applied");

                _options = value;
                XmlFile.Save(Path.Combine(_dataFolderPath, GeneralOptionsFileName), _options);

                OptionsChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        private GeneralOptions LoadOptions()
        {
            var fileName = Path.Combine(_dataFolderPath, GeneralOptionsFileName);
            return XmlFile.Load(fileName, CreateDefaultOptions);
        }

        private static GeneralOptions CreateDefaultOptions()
        {
            return new GeneralOptions();
        }
    }
}
