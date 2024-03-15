using System;
using System.Collections;
using System.IO;
using System.Linq;

namespace Y360OutlookConnector.Utilities
{
    public class RollingLogAppender : log4net.Appender.RollingFileAppender
    {
        private string _baseFileName;

        public int MaxDateRollBackups { get; set; } = 0;

        public RollingLogAppender()
        {
            PreserveLogFileNameExtension = true;
            StaticLogFileName = false;
        }

        public override void ActivateOptions()
        {
            _baseFileName = base.File;
            base.ActivateOptions();
        }

        public void ClearLogs()
        {
            var currentLogFileName = Path.GetFullPath(File);
            FileStream fs = null;
            try
            {
                fs = new FileStream(currentLogFileName, FileMode.Create);
            }
            finally
            {
                fs?.Close();
            }

            var files = GetLogFileList().ToList();
            files.RemoveAll(path => Path.GetFullPath(currentLogFileName).Equals(Path.GetFullPath(path), 
                StringComparison.OrdinalIgnoreCase));

            foreach (string fileName in files)
            {
                DeleteFile(fileName);
            }
        }

        protected override void AdjustFileBeforeAppend()
        {
            base.AdjustFileBeforeAppend();
            DeleteOutdatedFiles();
        }

        private string[] GetLogFileList()
        {
            var fileNameGlob = Path.GetFileName(CombinePath(_baseFileName, "*", PreserveLogFileNameExtension));
            return Directory.GetFiles(Path.GetDirectoryName(_baseFileName) ?? String.Empty, fileNameGlob);
        }

        protected void DeleteOutdatedFiles()
        {
            if (MaxDateRollBackups >= 0)
            {
                var fileNames = GetLogFileList();
                var fileDeleteList = GetFileDeleteList(fileNames, DateTimeStrategy.Now, _baseFileName, DatePattern,
                    MaxDateRollBackups, PreserveLogFileNameExtension);
                foreach (string fileName in fileDeleteList)
                {
                    DeleteFile(fileName);
                }
            }
        }

        protected static string CombinePath(string path1, string path2, bool preserveLogFileNameExtension)
        {
            var extension = Path.GetExtension(path1);
            if (preserveLogFileNameExtension && extension.Length > 0)
            {
                return Path.Combine(Path.GetDirectoryName(path1) ?? String.Empty,
                    Path.GetFileNameWithoutExtension(path1) + path2 + extension);
            }
            else
            {
                return path1 + path2;
            }
        }

        protected static ArrayList GetFileDeleteList(string[] fileNames, DateTime now, string baseFile, 
            string datePattern, int maxDateRollBackups, bool preserveLogFileNameExtension)
        {
            var list = new ArrayList();
            if (maxDateRollBackups >= 0)
            {
                var positiveList = new string[maxDateRollBackups + 1];
                for (var i = 0; i <= maxDateRollBackups; i++)
                {
                    var periodStart = GetRollDateTimeRelative(now, -i);
                    var periodPattern = periodStart.ToString(datePattern, 
                        System.Globalization.DateTimeFormatInfo.InvariantInfo) + "*";
                    var periodPatternPath = CombinePath(baseFile, periodPattern, preserveLogFileNameExtension);
                    positiveList[i] = Path.GetFileName(periodPatternPath).Split('*')[0];
                }

                foreach (var fileName in fileNames)
                {
                    var fn = Path.GetFileName(fileName);
                    var keep = false;
                    foreach (var fileStart in positiveList)
                    {
                        if (fn.StartsWith(fileStart))
                        {
                            keep = true;
                            break;
                        }
                    }
                    if (!keep)
                    {
                        list.Add(fileName);
                    }
                }
            }
            return list;
        }

        protected static DateTime GetRollDateTimeRelative(DateTime dateTime, int relativePeriod)
        {
            var result = dateTime;
            result = result.AddMilliseconds(-result.Millisecond);
            result = result.AddSeconds(-result.Second);
            result = result.AddMinutes(-result.Minute);
            result = result.AddHours(-result.Hour);
            result = result.AddDays(relativePeriod);
            return result;
        }
    }
}
