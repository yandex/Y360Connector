using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using log4net;

namespace Y360OutlookConnector.Configuration
{
    public static class XmlFile
    {
        private static readonly ILog s_logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        public static void Save<TObject>(string fileName, TObject obj)
        {
            try
            {
                if (String.IsNullOrEmpty(fileName))
                    throw new ArgumentException("fileName is empty");

                var folderPath = Path.GetDirectoryName(fileName);
                if (!String.IsNullOrEmpty(folderPath) && !Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                using (var writer = XmlWriter.Create(fileName, 
                           new XmlWriterSettings{ Indent = true, Encoding = Encoding.UTF8 }))
                {
                    var serializer = new XmlSerializer(obj.GetType());
                    serializer.Serialize(writer, obj);
                }
            }
            catch (Exception exc)
            {
                s_logger.Error($"XmlFile.Save<{typeof(TObject).FullName}> error:", exc);
            }
        }

        public static TObject Load<TObject>(string fileName) where TObject: new()
        {
            return Load(fileName, () => new TObject());
        }

        public static TObject Load<TObject>(string fileName, Func<TObject> defaultValueFactory)
        {
            TObject result = default(TObject);
            try
            {
                if (String.IsNullOrEmpty(fileName))
                    throw new ArgumentException("fileName is empty");

                if (File.Exists(fileName))
                {
                    using (var reader = XmlReader.Create(fileName))
                    {
                        var serializer = new XmlSerializer(typeof(TObject));
                        result = (TObject) serializer.Deserialize(reader);
                    }
                }
            }
            catch (Exception exc)
            {
                s_logger.Error($"XmlFile.Load<{typeof(TObject).FullName}>: error", exc);
            }

            if (result == null)
                result = defaultValueFactory();

            return result;
        }

    }
}
