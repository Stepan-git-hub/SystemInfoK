using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using MySql.Data.MySqlClient;

namespace SystemInfoService
{
    public class Program
    {
        private static ConfigSettings config;
        private static Timer scanTimer;
        private static bool isRunning = true;  // Используется для контроля работы
        private static DateTime nextScanTime;
        private static Timer countdownTimer;
        private static object consoleLock = new object();

        static void Main(string[] args)
        {
            // Проверка на режим шифрования пароля (уже не нужен, так как отдельный проект)
            // Но если хотите оставить возможность через аргумент --encrypt, то:
            if (args.Length > 0 && args[0].ToLower() == "--encrypt")
            {
                // Временно вызываем метод для шифрования (но лучше использовать отдельный проект)
                Console.WriteLine("Используйте отдельный проект PasswordEncoder для шифрования пароля");
                Console.WriteLine("Нажмите любую клавишу...");
                Console.ReadKey();
                return;
            }

            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "System Information Service";

            Console.Clear();

            ConsoleHelper.PrintHeader("СИСТЕМА МОНИТОРИНГА ИНФОРМАЦИИ О ПК");
            ConsoleHelper.PrintInfo("Загрузка конфигурации...");

            config = LoadConfig();
            ValidateConfig(config);

            ConsoleHelper.PrintSection("КОНФИГУРАЦИЯ");
            ConsoleHelper.PrintInfo($"Период сканирования: {config.ScanIntervalMinutes} минут");
            ConsoleHelper.PrintInfo($"Следующее сканирование: {DateTime.Now.AddMinutes(config.ScanIntervalMinutes):HH:mm:ss}");
            ConsoleHelper.PrintInfo($"Уровень логирования: {config.LogLevel}");
            ConsoleHelper.PrintInfo($"База данных: MySQL ({config.MySQLServer}/{config.MySQLDatabase})");
            ConsoleHelper.PrintInfo($"Время запуска: {DateTime.Now:HH:mm:ss}");

            ConsoleHelper.PrintSeparator();
            ConsoleHelper.PrintInfo("Для выхода нажмите Ctrl+C");
            ConsoleHelper.PrintSeparator();

            Logger.SetLogLevel(config.LogLevel);
            Logger.LogInfo("Служба запущена");

            try
            {
                ConsoleHelper.PrintSection("ПОДКЛЮЧЕНИЕ К БАЗЕ ДАННЫХ");
                ConsoleHelper.PrintProgress("Подключение к MySQL...");

                MySqlDatabaseManager.InitializeDatabase(
                    config.MySQLServer,
                    config.MySQLDatabase,
                    config.MySQLUserId,
                    config.MySQLPassword
                );

                ConsoleHelper.PrintSuccess("Подключение к MySQL успешно");

                if (config.ScanOnStartup)
                {
                    ConsoleHelper.PrintSection("ПЕРВОНАЧАЛЬНОЕ СКАНИРОВАНИЕ");
                    PerformSystemScan();
                }

                SetupScanTimer();

                ConsoleHelper.PrintSection("СЛУЖБА ЗАПУЩЕНА");
                ConsoleHelper.PrintSuccess("Служба мониторинга активна");
                ConsoleHelper.PrintInfo("Заголовок окна показывает обратный отсчет");
                ConsoleHelper.PrintInfo("Детальные логи записываются в system_monitoring.log");

                var exitEvent = new ManualResetEvent(false);
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    ConsoleHelper.PrintSection("ОСТАНОВКА СЛУЖБЫ");
                    ConsoleHelper.PrintInfo("Завершение работы...");
                    isRunning = false;
                    exitEvent.Set();
                };

                exitEvent.WaitOne();

                scanTimer?.Dispose();
                countdownTimer?.Dispose();
                ConsoleHelper.PrintSuccess("Служба остановлена");

            }
            catch (Exception ex)
            {
                ConsoleHelper.PrintSection("КРИТИЧЕСКАЯ ОШИБКА");
                ConsoleHelper.PrintError($"Ошибка: {ex.Message}");
                ConsoleHelper.PrintInfo("Проверьте настройки в config.ini и убедитесь, что MySQL сервер запущен.");

                ConsoleHelper.PrintInfo("\nНажмите любую клавишу для выхода...");
                Console.ReadKey();
                return;
            }
        }

        static void SetupScanTimer()
        {
            int intervalMs = (int)(config.ScanIntervalMinutes * 60 * 1000);
            nextScanTime = DateTime.Now.AddMilliseconds(intervalMs);

            ConsoleHelper.PrintInfo($"Таймер сканирования: каждые {config.ScanIntervalMinutes} мин.");

            scanTimer = new Timer(_ =>
            {
                try
                {
                    if (!isRunning) return;
                    Logger.LogInfo($"Запуск запланированного сканирования...");
                    PerformSystemScan();
                    nextScanTime = DateTime.Now.AddMilliseconds(intervalMs);
                    Logger.LogInfo($"Сканирование завершено. Следующее через {config.ScanIntervalMinutes} минут в {nextScanTime:HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex);
                }
            }, null, intervalMs, intervalMs);

            Logger.LogVerbose($"Таймер сканирования установлен на {intervalMs} мс (каждые {config.ScanIntervalMinutes} минут)");

            countdownTimer = new Timer(_ =>
            {
                UpdateCountdown();
            }, null, 1000, 1000);
        }

        static void UpdateCountdown()
        {
            try
            {
                if (!isRunning) return;

                TimeSpan timeLeft = nextScanTime - DateTime.Now;

                if (timeLeft.TotalMilliseconds < 0)
                {
                    int intervalMs = (int)(config.ScanIntervalMinutes * 60 * 1000);
                    nextScanTime = DateTime.Now.AddMilliseconds(intervalMs);
                    timeLeft = nextScanTime - DateTime.Now;
                }

                string timeLeftFormatted;
                if (timeLeft.TotalHours >= 1)
                {
                    timeLeftFormatted = $"{(int)timeLeft.TotalHours:00}:{timeLeft.Minutes:00}:{timeLeft.Seconds:00}";
                }
                else if (timeLeft.TotalMinutes >= 1)
                {
                    timeLeftFormatted = $"{timeLeft.Minutes:00}:{timeLeft.Seconds:00}";
                }
                else
                {
                    timeLeftFormatted = $"{timeLeft.Seconds:00} сек";
                }

                string nextScanStr = nextScanTime.ToString("HH:mm:ss");
                string title = $"System Info Monitor | Следующее сканирование: {nextScanStr} | Осталось: {timeLeftFormatted}";

                try
                {
                    if (Console.Title != title)
                    {
                        Console.Title = title;
                    }
                }
                catch { }

                if (timeLeft.Seconds == 0 || timeLeft.Seconds == 30)
                {
                    Logger.LogVerbose($"До следующего сканирования: {timeLeftFormatted}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"Ошибка обновления обратного отсчета: {ex.Message}");
            }
        }

        static void PerformSystemScan()
        {
            DateTime scanDateTime = DateTime.Now;

            ConsoleHelper.PrintSection("СКАНИРОВАНИЕ СИСТЕМЫ");
            ConsoleHelper.PrintProgress($"Начало сканирования: {scanDateTime:HH:mm:ss}");
            ConsoleHelper.PrintInfo($"Компьютер: {Environment.MachineName}");

            try
            {
                ConsoleHelper.PrintVerbose("Сохранение информации о компьютере...");
                long computerId = MySqlDataRepository.SaveComputerInfo(
                    Environment.MachineName,
                    scanDateTime
                );

                if (computerId <= 0)
                {
                    throw new Exception("Не удалось сохранить информацию о компьютере");
                }

                ConsoleHelper.PrintSuccess($"ComputerID: {computerId}");

                bool isNewConfiguration = MySqlDataRepository.IsNewConfiguration(computerId);

                if (!isNewConfiguration)
                {
                    ConsoleHelper.PrintInfo("Конфигурация не изменилась. Обновление динамических данных...");

                    // ВСЕГДА СОХРАНЯЕМ ДАННЫЕ, КОТОРЫЕ МОГУТ ИЗМЕНИТЬСЯ
                    ConsoleHelper.PrintVerbose("Сбор информации об установленных программах...");
                    MySqlDataRepository.SaveInstalledSoftware(computerId, scanDateTime);

                    ConsoleHelper.PrintVerbose("Сбор информации о безопасности...");
                    MySqlDataRepository.SaveSecurityInfo(computerId, scanDateTime);

                    // Получаем метрики для проверки предупреждений
                    double currentCpuUsage = GetCurrentCPUUsage();
                    double currentMemoryUsage = GetCurrentMemoryUsage();
                    double currentFreeSpacePercent = GetFreeDiskSpacePercent();
                    double currentTemperature = GetCurrentTemperature();

                    ConsoleHelper.PrintVerbose($"  • Загрузка CPU: {currentCpuUsage:F1}%");
                    ConsoleHelper.PrintVerbose($"  • Использование памяти: {currentMemoryUsage:F1}%");
                    ConsoleHelper.PrintVerbose($"  • Свободно на диске: {currentFreeSpacePercent:F1}%");
                    ConsoleHelper.PrintVerbose($"  • Температура: {(currentTemperature > 0 ? currentTemperature.ToString("F1") + "°C" : "недоступно")}");

                    ConsoleHelper.PrintVerbose("Проверка системных предупреждений...");
                    MySqlDataRepository.CheckAndCreateAlerts(computerId, currentCpuUsage, currentMemoryUsage,
                                                               currentFreeSpacePercent, currentTemperature, scanDateTime);

                    ConsoleHelper.PrintSection("ИТОГИ СКАНИРОВАНИЯ");
                    ConsoleHelper.PrintSuccess($"Сканирование завершено (конфигурация не изменилась)!");
                    ConsoleHelper.PrintInfo($"ComputerID: {computerId}");
                    ConsoleHelper.PrintInfo($"Обновлено время сканирования: {scanDateTime:HH:mm:ss}");
                    ConsoleHelper.PrintInfo($"Программы: сохранены, Безопасность: сохранена, Предупреждения: проверены");

                    TimeSpan timeUntilNext = nextScanTime - DateTime.Now;
                    if (timeUntilNext.TotalMilliseconds < 0)
                    {
                        int intervalMs = (int)(config.ScanIntervalMinutes * 60 * 1000);
                        nextScanTime = DateTime.Now.AddMilliseconds(intervalMs);
                        timeUntilNext = nextScanTime - DateTime.Now;
                    }
                    ConsoleHelper.PrintProgress($"Следующее сканирование: {nextScanTime:HH:mm:ss} (через {FormatTimeSpan(timeUntilNext)})");

                    Logger.LogInfo($"Сканирование завершено (конфигурация не изменилась). ComputerID: {computerId}");
                    return;
                }

                ConsoleHelper.PrintInfo("Сохранение компонентов новой конфигурации...");

                int totalComponents = 0;
                string processorName = "";
                int driveCount = 0;
                int memoryModuleCount = 0;
                int videoCardCount = 0;
                double totalMemoryGB = 0;

                // Общая информация
                ConsoleHelper.PrintVerbose("Сбор информации о компьютере...");
                string ipAddress = MySqlDataRepository.GetLocalIPAddress();

                MySqlDataRepository.SaveGeneralInfo(
                    computerId,
                    Environment.OSVersion.ToString(),
                    Environment.ProcessorCount,
                    Environment.UserName,
                    Environment.Is64BitOperatingSystem,
                    ipAddress,
                    scanDateTime
                );

                ConsoleHelper.PrintSuccess($"Общая информация сохранена (ОС: {Environment.OSVersion}, Процессоров: {Environment.ProcessorCount}, IP: {ipAddress})");
                totalComponents++;

                // Процессор
                ConsoleHelper.PrintVerbose("Сбор информации о процессоре...");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        int? currentClockSpeed = obj["CurrentClockSpeed"] != null ?
                            Convert.ToInt32(obj["CurrentClockSpeed"]) : (int?)null;

                        int? numberOfCores = obj["NumberOfCores"] != null ?
                            Convert.ToInt32(obj["NumberOfCores"]) : (int?)null;

                        processorName = obj["Name"]?.ToString() ?? "Неизвестно";

                        MySqlDataRepository.SaveProcessorInfo(
                            computerId,
                            currentClockSpeed,
                            processorName,
                            obj["Manufacturer"]?.ToString(),
                            numberOfCores,
                            obj["Status"]?.ToString(),
                            scanDateTime
                        );

                        ConsoleHelper.PrintSuccess($"Процессор: {processorName}");
                        ConsoleHelper.PrintVerbose($"  • Тактовая частота: {currentClockSpeed} MHz");
                        ConsoleHelper.PrintVerbose($"  • Ядра: {numberOfCores}");
                        ConsoleHelper.PrintVerbose($"  • Производитель: {obj["Manufacturer"]?.ToString()}");

                        totalComponents++;
                        break;
                    }
                }

                // Материнская плата
                ConsoleHelper.PrintVerbose("Сбор информации о материнской плате...");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string manufacturer = obj["Manufacturer"]?.ToString() ?? "Неизвестно";
                        string product = obj["Product"]?.ToString() ?? "Неизвестно";

                        MySqlDataRepository.SaveMotherboardInfo(
                            computerId,
                            manufacturer,
                            product,
                            obj["SerialNumber"]?.ToString(),
                            obj["Version"]?.ToString(),
                            obj["Caption"]?.ToString(),
                            scanDateTime
                        );

                        ConsoleHelper.PrintSuccess($"Материнская плата: {manufacturer} {product}");
                        ConsoleHelper.PrintVerbose($"  • Серийный номер: {obj["SerialNumber"]?.ToString()}");
                        ConsoleHelper.PrintVerbose($"  • Версия: {obj["Version"]?.ToString()}");

                        totalComponents++;
                        break;
                    }
                }

                // Жесткие диски
                ConsoleHelper.PrintVerbose("Сбор информации о жестких дисках...");
                driveCount = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_DiskDrive"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        ulong sizeBytes = obj["Size"] != null ? Convert.ToUInt64(obj["Size"]) : 0;
                        double sizeGB = sizeBytes / 1073741824.0;

                        int? bytesPerSector = obj["BytesPerSector"] != null ?
                            Convert.ToInt32(obj["BytesPerSector"]) : (int?)null;

                        long? totalSectors = obj["TotalSectors"] != null ?
                            Convert.ToInt64(obj["TotalSectors"]) : (long?)null;

                        string model = obj["Model"]?.ToString() ?? "Неизвестно";

                        MySqlDataRepository.SaveHardDriveInfo(
                            computerId,
                            model,
                            obj["InterfaceType"]?.ToString(),
                            obj["SerialNumber"]?.ToString(),
                            sizeGB,
                            bytesPerSector,
                            totalSectors,
                            scanDateTime
                        );

                        driveCount++;
                        ConsoleHelper.PrintSuccess($"Диск {driveCount}: {model} ({sizeGB:F2} GB)");
                        ConsoleHelper.PrintVerbose($"  • Интерфейс: {obj["InterfaceType"]?.ToString()}");
                        ConsoleHelper.PrintVerbose($"  • Серийный номер: {obj["SerialNumber"]?.ToString()}");

                        totalComponents++;
                    }
                }

                if (driveCount == 0)
                {
                    ConsoleHelper.PrintWarning("Жесткие диски не обнаружены");
                }

                // Оперативная память
                ConsoleHelper.PrintVerbose("Сбор информации об оперативной памяти...");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_ComputerSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (obj["TotalPhysicalMemory"] != null)
                        {
                            totalMemoryGB = Convert.ToUInt64(obj["TotalPhysicalMemory"]) / 1073741824.0;
                        }
                        break;
                    }
                }

                long memoryId = MySqlDataRepository.SaveMemoryInfo(computerId, totalMemoryGB, scanDateTime);
                if (memoryId > 0)
                {
                    ConsoleHelper.PrintSuccess($"Оперативная память: {totalMemoryGB:F2} GB");
                    totalComponents++;

                    ConsoleHelper.PrintVerbose("Сбор информации о модулях памяти...");
                    memoryModuleCount = 0;
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
                    {
                        foreach (ManagementObject module in searcher.Get())
                        {
                            double capacityGB = module["Capacity"] != null ?
                                Convert.ToUInt64(module["Capacity"]) / 1073741824.0 : 0;

                            int? speedMHz = module["Speed"] != null ?
                                Convert.ToInt32(module["Speed"]) : (int?)null;

                            MySqlDataRepository.SaveMemoryModuleInfo(
                                memoryId,
                                module["DeviceLocator"]?.ToString(),
                                module["Manufacturer"]?.ToString(),
                                module["SerialNumber"]?.ToString(),
                                capacityGB,
                                speedMHz,
                                memoryModuleCount
                            );

                            memoryModuleCount++;
                            ConsoleHelper.PrintSuccess($"Модуль памяти {memoryModuleCount}: {capacityGB:F2} GB");
                            ConsoleHelper.PrintVerbose($"  • Слот: {module["DeviceLocator"]?.ToString()}");
                            ConsoleHelper.PrintVerbose($"  • Частота: {speedMHz} MHz");
                            ConsoleHelper.PrintVerbose($"  • Производитель: {module["Manufacturer"]?.ToString()}");

                            totalComponents++;
                        }
                    }
                }
                else
                {
                    ConsoleHelper.PrintWarning("Не удалось сохранить информацию о памяти");
                }

                // Видеокарты
                ConsoleHelper.PrintVerbose("Сбор информации о видеокартах...");
                videoCardCount = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        ulong vramBytes = obj["AdapterRAM"] != null ? Convert.ToUInt64(obj["AdapterRAM"]) : 0;
                        double vramGB = vramBytes / 1073741824.0;

                        int? refreshRate = obj["CurrentRefreshRate"] != null ?
                            Convert.ToInt32(obj["CurrentRefreshRate"]) : (int?)null;

                        string videoCardName = obj["Name"]?.ToString() ?? "Неизвестно";

                        MySqlDataRepository.SaveVideoCardInfo(
                            computerId,
                            videoCardName,
                            obj["AdapterCompatibility"]?.ToString(),
                            vramGB,
                            refreshRate,
                            obj["DriverVersion"]?.ToString(),
                            scanDateTime
                        );

                        videoCardCount++;
                        ConsoleHelper.PrintSuccess($"Видеокарта {videoCardCount}: {videoCardName} ({vramGB:F2} GB)");
                        ConsoleHelper.PrintVerbose($"  • Производитель: {obj["AdapterCompatibility"]?.ToString()}");
                        ConsoleHelper.PrintVerbose($"  • Частота обновления: {refreshRate} Hz");
                        ConsoleHelper.PrintVerbose($"  • Версия драйвера: {obj["DriverVersion"]?.ToString()}");

                        totalComponents++;
                    }
                }

                if (videoCardCount == 0)
                {
                    ConsoleHelper.PrintWarning("Видеокарты не обнаружены");
                }

                // ============================================================
                // НОВЫЕ ФУНКЦИИ ДЛЯ АДМИНИСТРАТОРА (для новой конфигурации)
                // ============================================================

                ConsoleHelper.PrintVerbose("Сбор информации об установленных программах...");
                MySqlDataRepository.SaveInstalledSoftware(computerId, scanDateTime);
                ConsoleHelper.PrintSuccess("Список программ сохранен");

                ConsoleHelper.PrintVerbose("Сбор информации о безопасности...");
                MySqlDataRepository.SaveSecurityInfo(computerId, scanDateTime);
                ConsoleHelper.PrintSuccess("Информация о безопасности сохранена");

                double newCpuUsage = GetCurrentCPUUsage();
                double newMemoryUsage = GetCurrentMemoryUsage();
                double newFreeSpacePercent = GetFreeDiskSpacePercent();
                double newTemperature = GetCurrentTemperature();

                ConsoleHelper.PrintVerbose($"  • Загрузка CPU: {newCpuUsage:F1}%");
                ConsoleHelper.PrintVerbose($"  • Использование памяти: {newMemoryUsage:F1}%");
                ConsoleHelper.PrintVerbose($"  • Свободно на диске: {newFreeSpacePercent:F1}%");
                ConsoleHelper.PrintVerbose($"  • Температура: {(newTemperature > 0 ? newTemperature.ToString("F1") + "°C" : "недоступно")}");

                ConsoleHelper.PrintVerbose("Проверка системных предупреждений...");
                MySqlDataRepository.CheckAndCreateAlerts(computerId, newCpuUsage, newMemoryUsage,
                                                           newFreeSpacePercent, newTemperature, scanDateTime);
                ConsoleHelper.PrintSuccess("Проверка предупреждений завершена");

                // ============================================================

                ConsoleHelper.PrintSection("ИТОГИ СКАНИРОВАНИЯ");
                ConsoleHelper.PrintSuccess($"Сохранена НОВАЯ конфигурация!");
                ConsoleHelper.PrintInfo($"ComputerID: {computerId}");
                ConsoleHelper.PrintInfo($"Время сканирования: {scanDateTime:HH:mm:ss}");
                ConsoleHelper.PrintInfo($"Всего сохранено компонентов: {totalComponents}");
                ConsoleHelper.PrintInfo($"Установленных программ: информация сохранена");
                ConsoleHelper.PrintInfo($"Безопасность: данные сохранены");
                ConsoleHelper.PrintInfo($"Предупреждения: проверка выполнена");

                Logger.LogInfo($"Сохранена новая конфигурация. ComputerID: {computerId}");
            }
            catch (Exception ex)
            {
                ConsoleHelper.PrintSection("ОШИБКА СКАНИРОВАНИЯ");
                ConsoleHelper.PrintError($"Ошибка: {ex.Message}");

                if (ex.InnerException != null)
                {
                    ConsoleHelper.PrintError($"Внутренняя ошибка: {ex.InnerException.Message}");
                }

                Logger.LogError(ex);
                throw;
            }
        }

        static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours:00}ч {ts.Minutes:00}м {ts.Seconds:00}с";
            else if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes:00}м {ts.Seconds:00}с";
            else
                return $"{ts.Seconds:00}с";
        }

        static ConfigSettings LoadConfig()
        {
            List<string> possibleConfigPaths = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                            "SystemInfoService", "config.ini"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "SystemInfoService", "config.ini")
            };

            var defaultConfig = new ConfigSettings
            {
                ScanIntervalMinutes = 60,
                ScanOnStartup = true,
                LogLevel = "Normal",
                MySQLServer = "localhost",
                MySQLDatabase = "system_monitoring",
                MySQLUserId = "root",
                MySQLPassword = ""
            };

            string foundConfigPath = null;

            foreach (string configPath in possibleConfigPaths)
            {
                if (File.Exists(configPath))
                {
                    foundConfigPath = configPath;
                    break;
                }
            }

            if (foundConfigPath == null)
            {
                foreach (string configPath in possibleConfigPaths)
                {
                    try
                    {
                        CreateDefaultConfig(configPath, defaultConfig);
                        ConsoleHelper.PrintSuccess($"Создан файл конфигурации: {configPath}");
                        foundConfigPath = configPath;
                        break;
                    }
                    catch (Exception ex)
                    {
                        ConsoleHelper.PrintWarning($"Не удалось создать конфиг в {configPath}: {ex.Message}");
                    }
                }
            }

            if (foundConfigPath == null)
            {
                ConsoleHelper.PrintWarning("Не удалось создать файл конфигурации. Используются настройки по умолчанию.");
                return defaultConfig;
            }

            try
            {
                return LoadConfigFromFile(foundConfigPath, defaultConfig);
            }
            catch (Exception ex)
            {
                ConsoleHelper.PrintWarning($"Ошибка загрузки конфигурации: {ex.Message}. Используются настройки по умолчанию.");
                Logger.LogError(ex);
                return defaultConfig;
            }
        }

        static ConfigSettings LoadConfigFromFile(string configPath, ConfigSettings defaultConfig)
        {
            var config = new ConfigSettings();
            var lines = File.ReadAllLines(configPath, Encoding.UTF8);

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                    continue;

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex > 0)
                {
                    string key = line.Substring(0, equalsIndex).Trim();
                    string value = line.Substring(equalsIndex + 1).Trim();

                    switch (key.ToLower())
                    {
                        case "scanintervalminutes":
                            if (double.TryParse(value, out double minutes) && minutes > 0)
                                config.ScanIntervalMinutes = minutes;
                            else
                                config.ScanIntervalMinutes = defaultConfig.ScanIntervalMinutes;
                            break;

                        case "scanonstartup":
                            if (bool.TryParse(value, out bool scanOnStart))
                                config.ScanOnStartup = scanOnStart;
                            else
                                config.ScanOnStartup = defaultConfig.ScanOnStartup;
                            break;

                        case "loglevel":
                            if (value.Equals("Minimal", StringComparison.OrdinalIgnoreCase) ||
                                value.Equals("Normal", StringComparison.OrdinalIgnoreCase) ||
                                value.Equals("Verbose", StringComparison.OrdinalIgnoreCase))
                            {
                                config.LogLevel = value;
                            }
                            else
                            {
                                config.LogLevel = defaultConfig.LogLevel;
                            }
                            break;

                        case "mysqlserver":
                            config.MySQLServer = value;
                            break;

                        case "mysqldatabase":
                            config.MySQLDatabase = value;
                            break;

                        case "mysqluserid":
                            config.MySQLUserId = value;
                            break;

                        case "mysqlpassword":
                            config.MySQLPassword = value;
                            break;
                    }
                }
            }

            return config;
        }

        static void CreateDefaultConfig(string configPath, ConfigSettings config)
        {
            StringBuilder iniContent = new StringBuilder();
            iniContent.AppendLine("; Конфигурация службы сбора информации о системе");
            iniContent.AppendLine(";");
            iniContent.AppendLine("; ScanIntervalMinutes - период сканирования в минутах (например: 60, 180, 1440)");
            iniContent.AppendLine("; ScanOnStartup - выполнять сканирование при запуске (true/false)");
            iniContent.AppendLine("; LogLevel - уровень логирования (Minimal/Normal/Verbose)");
            iniContent.AppendLine(";");
            iniContent.AppendLine("; MySQL Configuration");
            iniContent.AppendLine("; MySQLServer - адрес сервера MySQL (например: localhost, 192.168.1.100)");
            iniContent.AppendLine("; MySQLDatabase - имя базы данных");
            iniContent.AppendLine("; MySQLUserId - имя пользователя MySQL");
            iniContent.AppendLine("; MySQLPassword - пароль пользователя MySQL");
            iniContent.AppendLine();
            iniContent.AppendLine($"ScanIntervalMinutes={config.ScanIntervalMinutes}");
            iniContent.AppendLine($"ScanOnStartup={config.ScanOnStartup}");
            iniContent.AppendLine($"LogLevel={config.LogLevel}");
            iniContent.AppendLine();
            iniContent.AppendLine($"MySQLServer={config.MySQLServer}");
            iniContent.AppendLine($"MySQLDatabase={config.MySQLDatabase}");
            iniContent.AppendLine($"MySQLUserId={config.MySQLUserId}");
            iniContent.AppendLine($"MySQLPassword={config.MySQLPassword}");

            string configDir = Path.GetDirectoryName(configPath);
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            File.WriteAllText(configPath, iniContent.ToString(), Encoding.UTF8);
        }

        static void ValidateConfig(ConfigSettings config)
        {
            if (config.ScanIntervalMinutes < 1)
            {
                ConsoleHelper.PrintWarning("Внимание: ScanIntervalMinutes слишком мал. Установлено минимальное значение: 1 минута");
                config.ScanIntervalMinutes = 1;
            }

            if (config.ScanIntervalMinutes > 43200)
            {
                ConsoleHelper.PrintWarning("Внимание: ScanIntervalMinutes слишком велик. Установлено максимальное значение: 43200 минут (30 дней)");
                config.ScanIntervalMinutes = 43200;
            }

            if (string.IsNullOrEmpty(config.MySQLServer))
            {
                ConsoleHelper.PrintWarning("Внимание: MySQLServer не указан. Используется localhost");
                config.MySQLServer = "localhost";
            }

            if (string.IsNullOrEmpty(config.MySQLDatabase))
            {
                ConsoleHelper.PrintWarning("Внимание: MySQLDatabase не указан. Используется system_monitoring");
                config.MySQLDatabase = "system_monitoring";
            }

            if (string.IsNullOrEmpty(config.MySQLUserId))
            {
                ConsoleHelper.PrintWarning("Внимание: MySQLUserId не указан. Используется root");
                config.MySQLUserId = "root";
            }
        }

        static bool CheckDatabaseTables()
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    string sql = @"
                SELECT COUNT(*) FROM information_schema.TABLES 
                WHERE TABLE_SCHEMA = DATABASE() 
                AND TABLE_NAME IN ('Computers', 'GeneralInfo', 'Processors', 
                                  'Motherboards', 'HardDrives', 'Memory', 
                                  'MemoryModules', 'VideoCards', 'InstalledSoftware', 
                                  'SystemAlerts', 'SecurityInfo');";

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        int tableCount = Convert.ToInt32(command.ExecuteScalar());
                        bool allTablesExist = (tableCount >= 11);

                        if (!allTablesExist)
                        {
                            Logger.LogVerbose($"Найдено таблиц: {tableCount} из 11 необходимых");
                        }

                        return allTablesExist;
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.PrintError($"Ошибка проверки таблиц: {ex.Message}");
                return false;
            }
        }

        // ============================================================
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ ПОЛУЧЕНИЯ МЕТРИК
        // ============================================================

        private static double GetCurrentCPUUsage()
        {
            try
            {
                using (PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
                {
                    cpuCounter.NextValue();
                    Thread.Sleep(100);
                    return Math.Round(cpuCounter.NextValue(), 2);
                }
            }
            catch
            {
                return 0;
            }
        }

        private static double GetCurrentMemoryUsage()
        {
            try
            {
                using (PerformanceCounter memCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use"))
                {
                    return Math.Round(memCounter.NextValue(), 2);
                }
            }
            catch
            {
                return 0;
            }
        }

        private static double GetFreeDiskSpacePercent()
        {
            try
            {
                DriveInfo drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name == "C:\\");
                if (drive != null)
                {
                    double totalSpace = drive.TotalSize;
                    double freeSpace = drive.TotalFreeSpace;
                    return Math.Round((freeSpace / totalSpace) * 100, 2);
                }
                return 100;
            }
            catch
            {
                return 100;
            }
        }

        private static double GetCurrentTemperature()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_TemperatureProbe"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (obj["CurrentReading"] != null)
                        {
                            return Convert.ToDouble(obj["CurrentReading"]);
                        }
                    }
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        // ============================================================
        // ПУБЛИЧНЫЕ МЕТОДЫ ДЛЯ ВЫЗОВА ИЗ СЛУЖБЫ
        // ============================================================

        public static ConfigSettings LoadConfigFromFileStatic(string configPath)
        {
            var defaultConfig = new ConfigSettings();

            if (!File.Exists(configPath))
            {
                Logger.LogWarning($"Файл конфигурации не найден: {configPath}");
                return defaultConfig;
            }

            try
            {
                var config = new ConfigSettings();
                var lines = File.ReadAllLines(configPath, Encoding.UTF8);

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex > 0)
                    {
                        string key = line.Substring(0, equalsIndex).Trim();
                        string value = line.Substring(equalsIndex + 1).Trim();

                        switch (key.ToLower())
                        {
                            case "scanintervalminutes":
                                if (double.TryParse(value, out double minutes) && minutes > 0)
                                    config.ScanIntervalMinutes = minutes;
                                break;

                            case "scanonstartup":
                                if (bool.TryParse(value, out bool scanOnStart))
                                    config.ScanOnStartup = scanOnStart;
                                break;

                            case "loglevel":
                                if (value.Equals("Minimal", StringComparison.OrdinalIgnoreCase) ||
                                    value.Equals("Normal", StringComparison.OrdinalIgnoreCase) ||
                                    value.Equals("Verbose", StringComparison.OrdinalIgnoreCase))
                                {
                                    config.LogLevel = value;
                                }
                                break;

                            case "mysqlserver":
                                config.MySQLServer = value;
                                break;

                            case "mysqldatabase":
                                config.MySQLDatabase = value;
                                break;

                            case "mysqluserid":
                                config.MySQLUserId = value;
                                break;

                            case "mysqlpassword":
                                config.MySQLPassword = value;
                                break;
                        }
                    }
                }

                return config;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка загрузки конфигурации: {ex.Message}");
                return defaultConfig;
            }
        }

        public static void PerformSystemScanStatic()
        {
            DateTime scanDateTime = DateTime.Now;

            try
            {
                Logger.LogInfo($"Начало сканирования: {scanDateTime:HH:mm:ss}");

                long computerId = MySqlDataRepository.SaveComputerInfo(
                    Environment.MachineName,
                    scanDateTime
                );

                if (computerId <= 0)
                {
                    throw new Exception("Не удалось сохранить информацию о компьютере");
                }

                Logger.LogInfo($"ComputerID: {computerId}");

                bool isNewConfiguration = MySqlDataRepository.IsNewConfiguration(computerId);

                if (!isNewConfiguration)
                {
                    Logger.LogInfo("Конфигурация не изменилась. Обновление динамических данных...");

                    // ВСЕГДА СОХРАНЯЕМ ДИНАМИЧЕСКИЕ ДАННЫЕ
                    MySqlDataRepository.SaveInstalledSoftware(computerId, scanDateTime);
                    MySqlDataRepository.SaveSecurityInfo(computerId, scanDateTime);

                    double currentCpuUsage = GetCurrentCPUUsage();
                    double currentMemoryUsage = GetCurrentMemoryUsage();
                    double currentFreeSpacePercent = GetFreeDiskSpacePercent();
                    double currentTemperature = GetCurrentTemperature();

                    Logger.LogInfo($"Метрики: CPU={currentCpuUsage:F1}%, RAM={currentMemoryUsage:F1}%, DiskFree={currentFreeSpacePercent:F1}%, Temp={(currentTemperature > 0 ? currentTemperature.ToString("F1") + "°C" : "N/A")}");

                    MySqlDataRepository.CheckAndCreateAlerts(computerId, currentCpuUsage, currentMemoryUsage,
                                                               currentFreeSpacePercent, currentTemperature, scanDateTime);

                    Logger.LogInfo($"Сканирование завершено (конфигурация не изменилась). ComputerID: {computerId}");
                    return;
                }

                Logger.LogInfo("Сохранение новой конфигурации...");

                string ipAddress = MySqlDataRepository.GetLocalIPAddress();
                MySqlDataRepository.SaveGeneralInfo(
                    computerId,
                    Environment.OSVersion.ToString(),
                    Environment.ProcessorCount,
                    Environment.UserName,
                    Environment.Is64BitOperatingSystem,
                    ipAddress,
                    scanDateTime
                );

                Logger.LogInfo($"Общая информация сохранена. IP: {ipAddress}");

                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        int? currentClockSpeed = obj["CurrentClockSpeed"] != null ?
                            Convert.ToInt32(obj["CurrentClockSpeed"]) : (int?)null;

                        int? numberOfCores = obj["NumberOfCores"] != null ?
                            Convert.ToInt32(obj["NumberOfCores"]) : (int?)null;

                        MySqlDataRepository.SaveProcessorInfo(
                            computerId,
                            currentClockSpeed,
                            obj["Name"]?.ToString(),
                            obj["Manufacturer"]?.ToString(),
                            numberOfCores,
                            obj["Status"]?.ToString(),
                            scanDateTime
                        );

                        Logger.LogInfo($"Процессор сохранен: {obj["Name"]?.ToString()}");
                        break;
                    }
                }

                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        MySqlDataRepository.SaveMotherboardInfo(
                            computerId,
                            obj["Manufacturer"]?.ToString(),
                            obj["Product"]?.ToString(),
                            obj["SerialNumber"]?.ToString(),
                            obj["Version"]?.ToString(),
                            obj["Caption"]?.ToString(),
                            scanDateTime
                        );

                        Logger.LogInfo($"Материнская плата сохранена: {obj["Manufacturer"]?.ToString()} {obj["Product"]?.ToString()}");
                        break;
                    }
                }

                int driveCount = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_DiskDrive"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        ulong sizeBytes = obj["Size"] != null ? Convert.ToUInt64(obj["Size"]) : 0;
                        double sizeGB = sizeBytes / 1073741824.0;

                        int? bytesPerSector = obj["BytesPerSector"] != null ?
                            Convert.ToInt32(obj["BytesPerSector"]) : (int?)null;

                        long? totalSectors = obj["TotalSectors"] != null ?
                            Convert.ToInt64(obj["TotalSectors"]) : (long?)null;

                        MySqlDataRepository.SaveHardDriveInfo(
                            computerId,
                            obj["Model"]?.ToString(),
                            obj["InterfaceType"]?.ToString(),
                            obj["SerialNumber"]?.ToString(),
                            sizeGB,
                            bytesPerSector,
                            totalSectors,
                            scanDateTime
                        );

                        driveCount++;
                        Logger.LogInfo($"Диск {driveCount}: {obj["Model"]?.ToString()} ({sizeGB:F2} GB)");
                    }
                }

                double totalMemoryGB = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_ComputerSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (obj["TotalPhysicalMemory"] != null)
                        {
                            totalMemoryGB = Convert.ToUInt64(obj["TotalPhysicalMemory"]) / 1073741824.0;
                        }
                        break;
                    }
                }

                long memoryId = MySqlDataRepository.SaveMemoryInfo(computerId, totalMemoryGB, scanDateTime);
                int moduleCount = 0;

                if (memoryId > 0)
                {
                    Logger.LogInfo($"Оперативная память: {totalMemoryGB:F2} GB");

                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
                    {
                        foreach (ManagementObject module in searcher.Get())
                        {
                            double capacityGB = module["Capacity"] != null ?
                                Convert.ToUInt64(module["Capacity"]) / 1073741824.0 : 0;

                            int? speedMHz = module["Speed"] != null ?
                                Convert.ToInt32(module["Speed"]) : (int?)null;

                            MySqlDataRepository.SaveMemoryModuleInfo(
                                memoryId,
                                module["DeviceLocator"]?.ToString(),
                                module["Manufacturer"]?.ToString(),
                                module["SerialNumber"]?.ToString(),
                                capacityGB,
                                speedMHz,
                                moduleCount
                            );

                            moduleCount++;
                            Logger.LogInfo($"Модуль памяти {moduleCount}: {capacityGB:F2} GB");
                        }
                    }
                }

                int videoCount = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        ulong vramBytes = obj["AdapterRAM"] != null ? Convert.ToUInt64(obj["AdapterRAM"]) : 0;
                        double vramGB = vramBytes / 1073741824.0;

                        int? refreshRate = obj["CurrentRefreshRate"] != null ?
                            Convert.ToInt32(obj["CurrentRefreshRate"]) : (int?)null;

                        MySqlDataRepository.SaveVideoCardInfo(
                            computerId,
                            obj["Name"]?.ToString(),
                            obj["AdapterCompatibility"]?.ToString(),
                            vramGB,
                            refreshRate,
                            obj["DriverVersion"]?.ToString(),
                            scanDateTime
                        );

                        videoCount++;
                        Logger.LogInfo($"Видеокарта {videoCount}: {obj["Name"]?.ToString()} ({vramGB:F2} GB)");
                    }
                }

                // Динамические данные для новой конфигурации
                Logger.LogInfo("Сбор информации об установленных программах...");
                MySqlDataRepository.SaveInstalledSoftware(computerId, scanDateTime);

                Logger.LogInfo("Сбор информации о безопасности...");
                MySqlDataRepository.SaveSecurityInfo(computerId, scanDateTime);

                double newCpuUsage = GetCurrentCPUUsage();
                double newMemoryUsage = GetCurrentMemoryUsage();
                double newFreeSpacePercent = GetFreeDiskSpacePercent();
                double newTemperature = GetCurrentTemperature();

                Logger.LogInfo($"Метрики: CPU={newCpuUsage:F1}%, RAM={newMemoryUsage:F1}%, DiskFree={newFreeSpacePercent:F1}%, Temp={(newTemperature > 0 ? newTemperature.ToString("F1") + "°C" : "N/A")}");

                Logger.LogInfo("Проверка системных предупреждений...");
                MySqlDataRepository.CheckAndCreateAlerts(computerId, newCpuUsage, newMemoryUsage,
                                                           newFreeSpacePercent, newTemperature, scanDateTime);

                Logger.LogInfo($"Сканирование завершено. Сохранена новая конфигурация");
                Logger.LogInfo($"Сохранено: Процессор:1, Диски:{driveCount}, Память:1, Модули:{moduleCount}, Видеокарты:{videoCount}");
                Logger.LogInfo($"Программы: список сохранен, Безопасность: данные сохранены, Предупреждения: проверка выполнена");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка сканирования: {ex.Message}");
                Logger.LogError(ex);
                throw;
            }
        }
    }

    public class ConfigSettings
    {
        public double ScanIntervalMinutes { get; set; } = 60;
        public bool ScanOnStartup { get; set; } = true;
        public string LogLevel { get; set; } = "Normal";

        public string MySQLServer { get; set; } = "localhost";
        public string MySQLDatabase { get; set; } = "system_monitoring";
        public string MySQLUserId { get; set; } = "root";
        public string MySQLPassword { get; set; } = "";
    }
}