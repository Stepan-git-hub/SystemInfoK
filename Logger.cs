using System;
using System.IO;

namespace SystemInfoService
{
    public static class Logger
    {
        private static string logLevel = "Normal";
        private static string logFile = "system_monitoring.log";
        private static object lockObject = new object();

        public static void SetLogLevel(string level)
        {
            logLevel = level;
        }

        public static void LogInfo(string message)
        {
            WriteLog("INFO", message);
            // НЕ выводим в консоль
        }

        public static void LogVerbose(string message)
        {
            if (logLevel == "Verbose")
            {
                WriteLog("VERBOSE", message);
                // НЕ выводим в консоль
            }
        }

        public static void LogError(Exception ex)
        {
            WriteLog("ERROR", $"{ex.GetType().Name}: {ex.Message}");
            WriteLog("ERROR", $"StackTrace: {ex.StackTrace}");

            // Ошибки показываем в консоли через ConsoleHelper
            ConsoleHelper.PrintError($"{ex.GetType().Name}: {ex.Message}");
        }

        public static void LogError(string message)
        {
            WriteLog("ERROR", message);
            ConsoleHelper.PrintError(message);
        }

        public static void LogWarning(string message)
        {
            WriteLog("WARNING", message);
            ConsoleHelper.PrintWarning(message);
        }

        private static void WriteLog(string level, string message)
        {
            lock (lockObject)
            {
                try
                {
                    string logEntry = $"[{DateTime.Now:dd.MM.yyyy HH:mm:ss}] {level}: {message}";
                    File.AppendAllText(logFile, logEntry + Environment.NewLine);
                }
                catch
                {
                    // Игнорируем ошибки записи в лог
                }
            }
        }
    }
}