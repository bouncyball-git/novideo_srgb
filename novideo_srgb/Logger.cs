using System;
using System.IO;
using System.Threading;

namespace novideo_srgb
{
    internal static class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string _logPath;
        private static readonly bool _enabled;

        static Logger()
        {
            try
            {
                _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");

                // Truncate at startup so each run is isolated and the file does not grow forever.
                File.WriteAllText(_logPath,
                    "===== novideo_srgb log opened " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====" + Environment.NewLine);
                _enabled = true;
            }
            catch
            {
                _enabled = false;
            }
        }

        public static string LogPath => _logPath;
        public static bool Enabled => _enabled;

        public static void Log(string message)
        {
            if (!_enabled) return;
            try
            {
                var line = DateTime.Now.ToString("HH:mm:ss.fff")
                           + " [" + Thread.CurrentThread.ManagedThreadId.ToString("D2") + "] "
                           + message
                           + Environment.NewLine;
                lock (_lock)
                {
                    File.AppendAllText(_logPath, line);
                }
            }
            catch
            {
                // never let logging crash the app
            }
        }

        public static void LogException(string context, Exception ex)
        {
            if (ex == null)
            {
                Log(context + ": <null exception>");
                return;
            }
            Log(context + ": " + ex.GetType().FullName + ": " + ex.Message);
            Log(ex.ToString());
            if (ex.InnerException != null)
            {
                LogException(context + " -> InnerException", ex.InnerException);
            }
        }
    }
}
