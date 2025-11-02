using System;
using System.IO;
using System.Text;

namespace BoltDownloader.Services
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath = string.Empty;

        public static void Init()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "BoltDownloader");
                Directory.CreateDirectory(dir);
                _logFilePath = Path.Combine(dir, "debug.log");
                Info("Logger initialized");
            }
            catch { }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message, Exception? ex = null)
        {
            var full = ex == null ? message : message + "\n" + ex.ToString();
            Write("ERROR", full);
        }

        private static void Write(string level, string message)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (string.IsNullOrEmpty(_logFilePath)) return;
                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
