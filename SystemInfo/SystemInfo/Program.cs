using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;
using MySql.Data.MySqlClient;

namespace SystemInfoService
{
    class Program
    {
        private static ConfigSettings config;
        private static Timer scanTimer;
        private static bool isRunning = false;
        private static DateTime nextScanTime;
        private static Timer countdownTimer;
        private static object consoleLock = new object();
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "System Information Service";

            // Очищаем консоль
            Console.Clear();

            ConsoleHelper.PrintHeader("СИСТЕМА МОНИТОРИНГА ИНФОРМАЦИИ О ПК");
            ConsoleHelper.PrintInfo("Загрузка конфигурации...");

            // Загрузка конфигурации
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

            // Инициализация логгера
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

                // Первое сканирование сразу при запуске
                if (config.ScanOnStartup)
                {
                    ConsoleHelper.PrintSection("ПЕРВОНАЧАЛЬНОЕ СКАНИРОВАНИЕ");
                    PerformSystemScan();
                }

                // Настройка таймера для периодического сканирования
                SetupScanTimer();

                ConsoleHelper.PrintSection("СЛУЖБА ЗАПУЩЕНА");
                ConsoleHelper.PrintSuccess("Служба мониторинга активна");
                ConsoleHelper.PrintInfo("Заголовок окна показывает обратный отсчет");
                ConsoleHelper.PrintInfo("Детальные логи записываются в system_monitoring.log");

                // Ожидание нажатия Ctrl+C
                var exitEvent = new ManualResetEvent(false);
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true; // Предотвратить немедленное завершение
                    ConsoleHelper.PrintSection("ОСТАНОВКА СЛУЖБЫ");
                    ConsoleHelper.PrintInfo("Завершение работы...");
                    exitEvent.Set();
                };

                // Ожидание события выхода
                exitEvent.WaitOne();

                // Остановка таймеров перед выходом
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
            // Конвертируем минуты в миллисекунды
            int intervalMs = (int)(config.ScanIntervalMinutes * 60 * 1000);

            // Устанавливаем время следующего сканирования
            nextScanTime = DateTime.Now.AddMilliseconds(intervalMs);

            ConsoleHelper.PrintInfo($"Таймер сканирования: каждые {config.ScanIntervalMinutes} мин.");
            ConsoleHelper.PrintInfo($"Следующее сканирование: {nextScanTime:HH:mm:ss}");

            // Таймер для периодического сканирования
            scanTimer = new Timer(_ =>
            {
                try
                {
                    Logger.LogInfo($"Запуск запланированного сканирования...");
                    PerformSystemScan();

                    // Обновляем время следующего сканирования
                    nextScanTime = DateTime.Now.AddMilliseconds(intervalMs);

                    Logger.LogInfo($"Сканирование завершено. Следующее через {config.ScanIntervalMinutes} минут");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex);
                }
            }, null, intervalMs, intervalMs);

            Logger.LogVerbose($"Таймер сканирования установлен на {intervalMs} мс (каждые {config.ScanIntervalMinutes} минут)");

            // Запускаем таймер обратного отсчета (обновление каждую секунду)
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

                // Проверяем, не пора ли сканировать
                if (DateTime.Now >= nextScanTime)
                {
                    // Если время пришло, обновляем для следующего цикла
                    int intervalMs = (int)(config.ScanIntervalMinutes * 60 * 1000);
                    nextScanTime = DateTime.Now.AddMilliseconds(intervalMs);
                }

                TimeSpan timeLeft = nextScanTime - DateTime.Now;

                // Форматируем время
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

                // Обновляем заголовок консоли
                string nextScanStr = nextScanTime.ToString("HH:mm:ss");
                string title = $"System Info Monitor | Следующее сканирование: {nextScanStr} | Осталось: {timeLeftFormatted}";

                // Безопасное обновление заголовка консоли
                try
                {
                    if (Console.Title != title)
                    {
                        Console.Title = title;
                    }
                }
                catch
                {
                    // Игнорируем ошибки при обновлении заголовка
                }

                // Выводим в консоль каждые 30 секунд для дебага
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
                // 1. Сохраняем информацию о компьютере (он сам определит - новая конфигурация или нет)
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

                // 2. Проверяем, это новый ComputerID или существующий
                bool isNewConfiguration = MySqlDataRepository.IsNewConfiguration(computerId);

                if (!isNewConfiguration)
                {
                    // Это существующая конфигурация - компоненты уже сохранены ранее
                    ConsoleHelper.PrintInfo("Конфигурация не изменилась. Компоненты уже сохранены.");

                    ConsoleHelper.PrintSection("ИТОГИ СКАНИРОВАНИЯ");
                    ConsoleHelper.PrintSuccess($"Сканирование завершено (конфигурация не изменилась)!");
                    ConsoleHelper.PrintInfo($"ComputerID: {computerId}");
                    ConsoleHelper.PrintInfo($"Обновлено время сканирования: {scanDateTime:HH:mm:ss}");

                    TimeSpan timeLeft = nextScanTime - DateTime.Now;
                    ConsoleHelper.PrintProgress($"Следующее сканирование: {nextScanTime:HH:mm:ss}");

                    Logger.LogInfo($"Сканирование завершено (конфигурация не изменилась). ComputerID: {computerId}");
                    return;
                }

                // 3. Это НОВАЯ конфигурация - сохраняем все компоненты
                ConsoleHelper.PrintInfo("Сохранение компонентов новой конфигурации...");

                int totalComponents = 0;
                string processorName = "";
                int driveCount = 0;
                int memoryModuleCount = 0;
                int videoCardCount = 0;
                double totalMemoryGB = 0;

                // 4.1. Общая информация
                ConsoleHelper.PrintVerbose("Сохранение общей информации...");
                MySqlDataRepository.SaveGeneralInfo(
                    computerId,
                    Environment.OSVersion.ToString(),
                    Environment.ProcessorCount,
                    Environment.UserName,
                    Environment.Is64BitOperatingSystem,
                    scanDateTime
                );

                ConsoleHelper.PrintSuccess($"Общая информация сохранена (ОС: {Environment.OSVersion}, Процессоров: {Environment.ProcessorCount})");
                totalComponents++;

                // 4.2. Информация о процессоре
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
                        break; // Берем только первый процессор
                    }
                }

                // 4.3. Информация о материнской плате
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

                // 4.4. Информация о жестких дисках
                ConsoleHelper.PrintVerbose("Сбор информации о жестких дисках...");
                driveCount = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_DiskDrive"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        ulong sizeBytes = obj["Size"] != null ?
                            Convert.ToUInt64(obj["Size"]) : 0;
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

                // 4.5. Информация об оперативной памяти
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

                    // 4.6. Информация о модулях памяти
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

                // 4.7. Информация о видеокартах
                ConsoleHelper.PrintVerbose("Сбор информации о видеокартах...");
                videoCardCount = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        ulong vramBytes = obj["AdapterRAM"] != null ?
                            Convert.ToUInt64(obj["AdapterRAM"]) : 0;
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

                ConsoleHelper.PrintSection("ИТОГИ СКАНИРОВАНИЯ");
                ConsoleHelper.PrintSuccess($"Сохранена НОВАЯ конфигурация!");
                ConsoleHelper.PrintInfo($"ComputerID: {computerId}");
                ConsoleHelper.PrintInfo($"Время сканирования: {scanDateTime:HH:mm:ss}");

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

        // Вспомогательный метод для форматирования времени (добавьте в класс Program)
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
            // Определяем несколько возможных местоположений конфига в порядке приоритета
            List<string> possibleConfigPaths = new List<string>
            {
                // 1. Текущая папка с исполняемым файлом (для запуска вручную)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini"),

                // 2. Папка CommonApplicationData (для службы)
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                            "SystemInfoService", "config.ini"),

                // 3. Папка ApplicationData текущего пользователя
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "SystemInfoService", "config.ini")
            };

            // Значения по умолчанию
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

            // Ищем существующий конфиг
            foreach (string configPath in possibleConfigPaths)
            {
                if (File.Exists(configPath))
                {
                    foundConfigPath = configPath;
                    break;
                }
            }

            // Если конфиг не найден, создаем его в первом доступном месте
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

            // Если так и не удалось создать конфиг, используем настройки по умолчанию
            if (foundConfigPath == null)
            {
                ConsoleHelper.PrintWarning("Не удалось создать файл конфигурации. Используются настройки по умолчанию.");
                return defaultConfig;
            }

            // Загружаем настройки из найденного конфига
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

            // Создаем папку если нужно
            string configDir = Path.GetDirectoryName(configPath);
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            File.WriteAllText(configPath, iniContent.ToString(), Encoding.UTF8);
        }

        static void ValidateConfig(ConfigSettings config)
        {
            if (config.ScanIntervalMinutes < 1) // Минимум 1 минута
            {
                ConsoleHelper.PrintWarning("Внимание: ScanIntervalMinutes слишком мал. Установлено минимальное значение: 1 минута");
                config.ScanIntervalMinutes = 1;
            }

            if (config.ScanIntervalMinutes > 43200) // Максимум 30 дней в минутах (30*24*60)
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
                                  'MemoryModules', 'VideoCards');
            ";

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        int tableCount = Convert.ToInt32(command.ExecuteScalar());
                        bool allTablesExist = (tableCount >= 8);

                        if (!allTablesExist)
                        {
                            Logger.LogVerbose($"Найдено таблиц: {tableCount} из 8 необходимых");

                            // Показываем какие таблицы есть
                            string checkSql = "SHOW TABLES;";
                            using (var checkCommand = new MySqlCommand(checkSql, connection))
                            using (var reader = checkCommand.ExecuteReader())
                            {
                                Logger.LogVerbose("Существующие таблицы:");
                                while (reader.Read())
                                {
                                    Logger.LogVerbose($"  - {reader[0]}");
                                }
                            }
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
    }
    public class ConfigSettings
    {
        public double ScanIntervalMinutes { get; set; } = 60;
        public bool ScanOnStartup { get; set; } = true;
        public string LogLevel { get; set; } = "Normal";

        // MySQL настройки
        public string MySQLServer { get; set; } = "localhost";
        public string MySQLDatabase { get; set; } = "system_monitoring";
        public string MySQLUserId { get; set; } = "root";
        public string MySQLPassword { get; set; } = "";
    }
}