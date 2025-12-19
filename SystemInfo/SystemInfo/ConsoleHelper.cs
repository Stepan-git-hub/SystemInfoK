using System;

namespace SystemInfoService
{
    public static class ConsoleHelper
    {
        private static object lockObject = new object();

        public static void PrintHeader(string text)
        {
            lock (lockObject)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("╔════════════════════════════════════════════════════╗");
                Console.WriteLine($"║    {text,-40} ║");
                Console.WriteLine("╚════════════════════════════════════════════════════╝");
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        public static void PrintSection(string text)
        {
            lock (lockObject)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] 📊 {text}");
                Console.ResetColor();
            }
        }

        public static void PrintSeparator()
        {
            lock (lockObject)
            {
                Console.WriteLine(new string('═', 60));
            }
        }

        public static void PrintInfo(string message)
        {
            lock (lockObject)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"{message}");
                Console.ResetColor();
            }
        }

        public static void PrintSuccess(string message)
        {
            lock (lockObject)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ {message}");
                Console.ResetColor();
            }
        }

        public static void PrintProgress(string message)
        {
            lock (lockObject)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🔄 {message}");
                Console.ResetColor();
            }
        }

        public static void PrintVerbose(string message)
        {
            lock (lockObject)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 📝 {message}");
                Console.ResetColor();
            }
        }

        public static void PrintWarning(string message)
        {
            lock (lockObject)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠️  {message}");
                Console.ResetColor();
            }
        }

        public static void PrintError(string message)
        {
            lock (lockObject)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ {message}");
                Console.ResetColor();
            }
        }
    }
}