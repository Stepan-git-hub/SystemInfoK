using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;  
using System.Management;           
using System.Net;
using System.Text;

namespace SystemInfoService
{
    public class MySqlDataRepository
    {
        public static long SaveComputerInfo(string computerName, DateTime scanDateTime)
        {
            try
            {
                // 1. Получаем хэш текущей конфигурации
                string currentConfigHash = GetCurrentConfigurationHash();
                Logger.LogVerbose($"Получен ConfigurationHash: {currentConfigHash}");

                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    // 2. Ищем существующую запись с таким же ComputerName И ConfigurationHash
                    string findSql = @"
                SELECT ComputerID, ScanDateTime 
                FROM Computers 
                WHERE ComputerName = @ComputerName 
                AND ConfigurationHash = @ConfigurationHash
                ORDER BY ScanDateTime DESC 
                LIMIT 1;
            ";

                    long? existingComputerId = null;
                    DateTime? lastScanDateTime = null;

                    using (var findCmd = new MySqlCommand(findSql, connection))
                    {
                        findCmd.Parameters.AddWithValue("@ComputerName", computerName);
                        findCmd.Parameters.AddWithValue("@ConfigurationHash", currentConfigHash);

                        using (var reader = findCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                existingComputerId = reader.GetInt64("ComputerID");
                                lastScanDateTime = reader.GetDateTime("ScanDateTime");
                                Logger.LogVerbose($"Найдена существующая конфигурация. ComputerID: {existingComputerId}");
                            }
                        }
                    }

                    if (existingComputerId.HasValue)
                    {
                        // 3. Конфигурация НЕ изменилась - обновляем время сканирования
                        string updateSql = @"
                    UPDATE Computers 
                    SET ScanDateTime = @ScanDateTime 
                    WHERE ComputerID = @ComputerID;
                ";

                        using (var updateCmd = new MySqlCommand(updateSql, connection))
                        {
                            updateCmd.Parameters.AddWithValue("@ComputerID", existingComputerId.Value);
                            updateCmd.Parameters.AddWithValue("@ScanDateTime", scanDateTime);
                            updateCmd.ExecuteNonQuery();
                        }

                        Logger.LogVerbose($"Конфигурация не изменилась. Обновлено время сканирования.");
                        return existingComputerId.Value;
                    }

                    // 4. Конфигурация ИЗМЕНИЛАСЬ - создаем новую запись
                    string insertSql = @"
                INSERT INTO Computers (ComputerName, ConfigurationHash, ScanDateTime)
                VALUES (@ComputerName, @ConfigurationHash, @ScanDateTime);
                SELECT LAST_INSERT_ID();
            ";

                    using (var command = new MySqlCommand(insertSql, connection))
                    {
                        command.Parameters.AddWithValue("@ComputerName", computerName);
                        command.Parameters.AddWithValue("@ConfigurationHash", currentConfigHash);
                        command.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                        var result = command.ExecuteScalar();
                        long computerId = Convert.ToInt64(result);
                        Logger.LogVerbose($"Создана новая конфигурация. ComputerID: {computerId}");
                        return computerId;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(new Exception($"Ошибка сохранения компьютера '{computerName}': {ex.Message}", ex));
                throw;
            }
        }

        public static void SaveGeneralInfo(long computerId,
                                  string osVersion,
                                  int processorCount,
                                  string userName,
                                  bool is64BitOS,
                                  string ipAddress, // Добавляем параметр
                                  DateTime scanDateTime)
        {
            using (var connection = MySqlDatabaseManager.GetConnection())
            {
                connection.Open();

                string sql = @"
            INSERT INTO GeneralInfo 
            (ComputerID, OSVersion, ProcessorCount, UserName, Is64BitOS, IPAddress, ScanDateTime)
            VALUES 
            (@ComputerID, @OSVersion, @ProcessorCount, @UserName, @Is64BitOS, @IPAddress, @ScanDateTime)
            ON DUPLICATE KEY UPDATE
                OSVersion = @OSVersion,
                ProcessorCount = @ProcessorCount,
                UserName = @UserName,
                Is64BitOS = @Is64BitOS,
                IPAddress = @IPAddress;
        ";

                using (var command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ComputerID", computerId);
                    command.Parameters.AddWithValue("@OSVersion", osVersion);
                    command.Parameters.AddWithValue("@ProcessorCount", processorCount);
                    command.Parameters.AddWithValue("@UserName", userName);
                    command.Parameters.AddWithValue("@Is64BitOS", is64BitOS);
                    command.Parameters.AddWithValue("@IPAddress", ipAddress ?? ""); // Новый параметр
                    command.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                    command.ExecuteNonQuery();
                }
            }
        }

        public static void SaveProcessorInfo(long computerId,
                                           int? currentClockSpeed,
                                           string name,
                                           string manufacturer,
                                           int? numberOfCores,
                                           string status,
                                           DateTime scanDateTime)
        {
            using (var connection = MySqlDatabaseManager.GetConnection())
            {
                connection.Open();

                string sql = @"
                    INSERT INTO Processors 
                    (ComputerID, CurrentClockSpeed, Name, Manufacturer, NumberOfCores, Status, ScanDateTime)
                    VALUES 
                    (@ComputerID, @CurrentClockSpeed, @Name, @Manufacturer, @NumberOfCores, @Status, @ScanDateTime)
                    ON DUPLICATE KEY UPDATE
                        CurrentClockSpeed = @CurrentClockSpeed,
                        Name = @Name,
                        Manufacturer = @Manufacturer,
                        NumberOfCores = @NumberOfCores,
                        Status = @Status;
                ";

                using (var command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ComputerID", computerId);
                    command.Parameters.AddWithValue("@CurrentClockSpeed", (object)currentClockSpeed ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Name", name ?? "");
                    command.Parameters.AddWithValue("@Manufacturer", manufacturer ?? "");
                    command.Parameters.AddWithValue("@NumberOfCores", (object)numberOfCores ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Status", status ?? "");
                    command.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                    command.ExecuteNonQuery();
                }
            }
        }

        public static void SaveMotherboardInfo(long computerId,
                                             string manufacturer,
                                             string product,
                                             string serialNumber,
                                             string version,
                                             string description,
                                             DateTime scanDateTime)
        {
            using (var connection = MySqlDatabaseManager.GetConnection())
            {
                connection.Open();

                string sql = @"
                    INSERT INTO Motherboards 
                    (ComputerID, Manufacturer, Product, SerialNumber, Version, Description, ScanDateTime)
                    VALUES 
                    (@ComputerID, @Manufacturer, @Product, @SerialNumber, @Version, @Description, @ScanDateTime)
                    ON DUPLICATE KEY UPDATE
                        Manufacturer = @Manufacturer,
                        Product = @Product,
                        SerialNumber = @SerialNumber,
                        Version = @Version,
                        Description = @Description;
                ";

                using (var command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ComputerID", computerId);
                    command.Parameters.AddWithValue("@Manufacturer", manufacturer ?? "");
                    command.Parameters.AddWithValue("@Product", product ?? "");
                    command.Parameters.AddWithValue("@SerialNumber", serialNumber ?? "");
                    command.Parameters.AddWithValue("@Version", version ?? "");
                    command.Parameters.AddWithValue("@Description", description ?? "");
                    command.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                    command.ExecuteNonQuery();
                }
            }
        }

        public static void SaveHardDriveInfo(long computerId,
                                           string model,
                                           string interfaceType,
                                           string serialNumber,
                                           double sizeGB,
                                           int? bytesPerSector,
                                           long? totalSectors,
                                           DateTime scanDateTime)
        {
            using (var connection = MySqlDatabaseManager.GetConnection())
            {
                connection.Open();

                string sql = @"
                    INSERT INTO HardDrives 
                    (ComputerID, Model, InterfaceType, SerialNumber, SizeGB, BytesPerSector, TotalSectors, ScanDateTime)
                    VALUES 
                    (@ComputerID, @Model, @InterfaceType, @SerialNumber, @SizeGB, @BytesPerSector, @TotalSectors, @ScanDateTime)
                    ON DUPLICATE KEY UPDATE
                        Model = @Model,
                        InterfaceType = @InterfaceType,
                        SerialNumber = @SerialNumber,
                        SizeGB = @SizeGB,
                        BytesPerSector = @BytesPerSector,
                        TotalSectors = @TotalSectors;
                ";

                using (var command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ComputerID", computerId);
                    command.Parameters.AddWithValue("@Model", model ?? "");
                    command.Parameters.AddWithValue("@InterfaceType", interfaceType ?? "");
                    command.Parameters.AddWithValue("@SerialNumber", serialNumber ?? "");
                    command.Parameters.AddWithValue("@SizeGB", sizeGB);
                    command.Parameters.AddWithValue("@BytesPerSector", (object)bytesPerSector ?? DBNull.Value);
                    command.Parameters.AddWithValue("@TotalSectors", (object)totalSectors ?? DBNull.Value);
                    command.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                    command.ExecuteNonQuery();
                }
            }
        }

        public static long SaveMemoryInfo(long computerId, double totalGB, DateTime scanDateTime)
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    string sql = @"
                INSERT INTO Memory (ComputerID, TotalGB, ScanDateTime)
                VALUES (@ComputerID, @TotalGB, @ScanDateTime)
                ON DUPLICATE KEY UPDATE
                    TotalGB = @TotalGB;
                SELECT LAST_INSERT_ID();
            ";

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ComputerID", computerId);
                        command.Parameters.AddWithValue("@TotalGB", totalGB);
                        command.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                        var result = command.ExecuteScalar();
                        long memoryId = result != null ? Convert.ToInt64(result) : 0;

                        if (memoryId > 0)
                        {
                            Logger.LogVerbose($"Память сохранена. MemoryID: {memoryId}, Объем: {totalGB:F2} GB");
                        }

                        return memoryId;
                    }
                }
            }
            catch (MySqlException mysqlEx)
            {
                Logger.LogError($"MySQL ошибка #{mysqlEx.Number}: {mysqlEx.Message}");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка SaveMemoryInfo: {ex.Message}");
                return 0;
            }
        }

        private static long FindExistingMemoryId(long computerId, DateTime scanDateTime)
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    string sql = @"
                SELECT MemoryID FROM Memory 
                WHERE ComputerID = @ComputerID 
                AND ScanDateTime = @ScanDateTime 
                LIMIT 1;
            ";

                    using (var cmd = new MySqlCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@ComputerID", computerId);
                        cmd.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                        var result = cmd.ExecuteScalar();
                        return result != null ? Convert.ToInt64(result) : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка поиска MemoryID: {ex.Message}");
                return 0;
            }
        }

        public static DateTime? GetLastScanDateTime(long computerId)
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    string sql = @"
                SELECT MAX(ScanDateTime) as LastScanDateTime
                FROM Computers 
                WHERE ComputerID = @ComputerID;
            ";

                    using (var cmd = new MySqlCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@ComputerID", computerId);

                        var result = cmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                        {
                            return Convert.ToDateTime(result);
                        }
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка получения времени последнего сканирования: {ex.Message}");
                return null;
            }
        }

        public static void SaveMemoryModuleInfo(long memoryId,
                                  string slotLocation,
                                  string manufacturer,
                                  string serialNumber,
                                  double capacityGB,
                                  int? speedMHz,
                                  int moduleIndex)
        {
            try
            {
                if (memoryId <= 0)
                {
                    Logger.LogWarning($"Неверный MemoryID: {memoryId}. Модуль #{moduleIndex} не будет сохранен.");
                    return;
                }

                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    string sql = @"
                INSERT INTO MemoryModules 
                (MemoryID, SlotLocation, Manufacturer, SerialNumber, CapacityGB, SpeedMHz, ModuleIndex)
                VALUES 
                (@MemoryID, @SlotLocation, @Manufacturer, @SerialNumber, @CapacityGB, @SpeedMHz, @ModuleIndex)
            ";

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@MemoryID", memoryId);
                        command.Parameters.AddWithValue("@SlotLocation", slotLocation ?? "");
                        command.Parameters.AddWithValue("@Manufacturer", manufacturer ?? "");
                        command.Parameters.AddWithValue("@SerialNumber", serialNumber ?? "");
                        command.Parameters.AddWithValue("@CapacityGB", capacityGB);
                        command.Parameters.AddWithValue("@SpeedMHz", (object)speedMHz ?? DBNull.Value);
                        command.Parameters.AddWithValue("@ModuleIndex", moduleIndex);

                        command.ExecuteNonQuery();
                        Logger.LogVerbose($"Модуль памяти #{moduleIndex} сохранен. Слот: {slotLocation}, Объем: {capacityGB:F2} GB");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка сохранения модуля памяти #{moduleIndex}: {ex.Message}");
            }
        }

        public static void SaveVideoCardInfo(long computerId,
                                           string name,
                                           string manufacturer,
                                           double videoMemoryGB,
                                           int? currentRefreshRate,
                                           string driverVersion,
                                           DateTime scanDateTime)
        {
            using (var connection = MySqlDatabaseManager.GetConnection())
            {
                connection.Open();

                string sql = @"
                    INSERT INTO VideoCards 
                    (ComputerID, Name, Manufacturer, VideoMemoryGB, CurrentRefreshRate, DriverVersion, ScanDateTime)
                    VALUES 
                    (@ComputerID, @Name, @Manufacturer, @VideoMemoryGB, @CurrentRefreshRate, @DriverVersion, @ScanDateTime)
                    ON DUPLICATE KEY UPDATE
                        Name = @Name,
                        Manufacturer = @Manufacturer,
                        VideoMemoryGB = @VideoMemoryGB,
                        CurrentRefreshRate = @CurrentRefreshRate,
                        DriverVersion = @DriverVersion;
                ";

                using (var command = new MySqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ComputerID", computerId);
                    command.Parameters.AddWithValue("@Name", name ?? "");
                    command.Parameters.AddWithValue("@Manufacturer", manufacturer ?? "");
                    command.Parameters.AddWithValue("@VideoMemoryGB", videoMemoryGB);
                    command.Parameters.AddWithValue("@CurrentRefreshRate", (object)currentRefreshRate ?? DBNull.Value);
                    command.Parameters.AddWithValue("@DriverVersion", driverVersion ?? "");
                    command.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                    command.ExecuteNonQuery();
                }
            }
        }

        // Метод для проверки существования таблиц
        public static bool CheckTablesExist()
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
                        return tableCount >= 8; // Должно быть минимум 8 таблиц
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(new Exception($"Ошибка проверки таблиц: {ex.Message}"));
                return false;
            }
        }

        public static bool ShouldSaveComponents(long computerId, DateTime scanDateTime)
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    // Проверяем, есть ли уже компоненты с таким ScanDateTime
                    string sql = @"
                SELECT COUNT(*) as ComponentCount FROM (
                    SELECT 1 FROM Processors WHERE ComputerID = @ComputerID AND ScanDateTime = @ScanDateTime
                    UNION ALL
                    SELECT 1 FROM Motherboards WHERE ComputerID = @ComputerID AND ScanDateTime = @ScanDateTime
                    UNION ALL
                    SELECT 1 FROM Memory WHERE ComputerID = @ComputerID AND ScanDateTime = @ScanDateTime
                ) as Components;
            ";

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ComputerID", computerId);
                        command.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                        var result = command.ExecuteScalar();
                        int componentCount = result != null ? Convert.ToInt32(result) : 0;

                        // Если компоненты с этим ScanDateTime уже есть, не сохраняем снова
                        return componentCount == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка проверки компонентов: {ex.Message}");
                return true;
            }
        }
        public static bool HasConfigurationChanged(long computerId)
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    // Получаем ПОСЛЕДНЮЮ сохраненную конфигурацию из БД
                    string sql = @"
                SELECT 
                    -- Процессор (последний по дате)
                    COALESCE((SELECT Name FROM Processors 
                              WHERE ComputerID = @ComputerID 
                              ORDER BY ScanDateTime DESC LIMIT 1), '') as ProcessorName,
                    
                    -- Материнская плата (последняя по дате)
                    COALESCE((SELECT CONCAT(Manufacturer, '|', Product) FROM Motherboards 
                              WHERE ComputerID = @ComputerID 
                              ORDER BY ScanDateTime DESC LIMIT 1), '') as MotherboardInfo,
                    
                    -- Общий объем жестких дисков (последний по дате)
                    COALESCE((SELECT CAST(SUM(SizeGB) as CHAR) FROM HardDrives 
                              WHERE ComputerID = @ComputerID 
                              AND ScanDateTime = (
                                  SELECT MAX(ScanDateTime) FROM HardDrives 
                                  WHERE ComputerID = @ComputerID
                              )), '0') as TotalDriveSize,
                    
                    -- Объем оперативной памяти (последний по дате)
                    COALESCE((SELECT CAST(TotalGB as CHAR) FROM Memory 
                              WHERE ComputerID = @ComputerID 
                              ORDER BY ScanDateTime DESC LIMIT 1), '0') as TotalMemory,
                    
                    -- Видеокарты (последние по дате)
                    COALESCE((SELECT GROUP_CONCAT(Name ORDER BY Name SEPARATOR '|') FROM VideoCards 
                              WHERE ComputerID = @ComputerID 
                              AND ScanDateTime = (
                                  SELECT MAX(ScanDateTime) FROM VideoCards 
                                  WHERE ComputerID = @ComputerID
                              )), '') as VideoCardsList;
            ";

                    string lastConfigHash = "";

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ComputerID", computerId);

                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                lastConfigHash = $"{reader["ProcessorName"]}|{reader["MotherboardInfo"]}|" +
                                               $"{reader["TotalDriveSize"]}|{reader["TotalMemory"]}|" +
                                               $"{reader["VideoCardsList"]}";
                            }
                        }
                    }

                    // Получаем текущую конфигурацию (из WMI)
                    string currentConfigHash = GetCurrentConfigurationHash();

                    bool hasChanged = (lastConfigHash != currentConfigHash);

                    Logger.LogVerbose($"Проверка конфигурации:");
                    Logger.LogVerbose($"  Последняя в БД: {lastConfigHash}");
                    Logger.LogVerbose($"  Текущая из WMI: {currentConfigHash}");
                    Logger.LogVerbose($"  Изменилась: {hasChanged}");

                    return hasChanged;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка проверки изменений: {ex.Message}");
                return true; // При ошибке считаем, что есть изменения
            }
        }

        public static bool IsNewConfiguration(long computerId)
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    // Проверяем, есть ли уже компоненты для этого ComputerID
                    string sql = @"
                SELECT COUNT(*) as ComponentCount FROM (
                    SELECT 1 FROM Processors WHERE ComputerID = @ComputerID
                    UNION ALL
                    SELECT 1 FROM Motherboards WHERE ComputerID = @ComputerID
                    UNION ALL
                    SELECT 1 FROM Memory WHERE ComputerID = @ComputerID
                ) as Components;
            ";

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ComputerID", computerId);

                        var result = command.ExecuteScalar();
                        int componentCount = result != null ? Convert.ToInt32(result) : 0;

                        // Если компонентов нет, значит это новая конфигурация
                        return componentCount == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка проверки конфигурации: {ex.Message}");
                return true;
            }
        }

        public static string GetCurrentConfigurationHash()
        {
            try
            {
                StringBuilder hash = new StringBuilder();

                // 1. Процессор
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        hash.Append(obj["Name"]?.ToString() ?? "");
                        hash.Append("|");
                        hash.Append(obj["Manufacturer"]?.ToString() ?? "");
                        hash.Append("|");
                        hash.Append(obj["NumberOfCores"]?.ToString() ?? "");
                        break;
                    }
                }

                // 2. Материнская плата
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        hash.Append(obj["Manufacturer"]?.ToString() ?? "");
                        hash.Append("|");
                        hash.Append(obj["Product"]?.ToString() ?? "");
                        hash.Append("|");
                        hash.Append(obj["SerialNumber"]?.ToString() ?? "");
                        break;
                    }
                }

                // 3. Жесткие диски (суммарный объем)
                double totalDriveSizeGB = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_DiskDrive"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (obj["Size"] != null)
                        {
                            ulong sizeBytes = Convert.ToUInt64(obj["Size"]);
                            totalDriveSizeGB += sizeBytes / 1073741824.0;
                        }
                    }
                }
                hash.Append(totalDriveSizeGB.ToString("F2"));
                hash.Append("|");

                // 4. Оперативная память (общий объем)
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
                hash.Append(totalMemoryGB.ToString("F2"));
                hash.Append("|");

                // 5. Видеокарты
                List<string> videoCards = new List<string>();
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        videoCards.Add((obj["Name"]?.ToString() ?? "") + "|" +
                                      (obj["AdapterCompatibility"]?.ToString() ?? "") + "|" +
                                      (obj["AdapterRAM"]?.ToString() ?? ""));
                    }
                }
                videoCards.Sort();
                hash.Append(string.Join("||", videoCards));

                Logger.LogVerbose($"Сырой хэш для MD5: {hash}");

                // Создаем MD5 хэш (исправленная версия)
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(hash.ToString());
                    byte[] hashBytes = md5.ComputeHash(inputBytes);

                    // Исправление: BitConverter.ToString вместо ToHexString
                    string md5Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

                    Logger.LogVerbose($"MD5 хэш конфигурации: {md5Hash}");
                    return md5Hash;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка получения хэша конфигурации: {ex.Message}");
                return "error_" + Guid.NewGuid().ToString();
            }
        }
        // Метод для получения локального IP-адреса компьютера
        public static string GetLocalIPAddress()
        {
            try
            {
                string ipAddress = "127.0.0.1"; // Значение по умолчанию

                // Получаем имя хоста
                string hostName = Dns.GetHostName();

                // Получаем все IP-адреса для этого хоста
                IPAddress[] addresses = Dns.GetHostAddresses(hostName);

                // Ищем первый IPv4 адрес (не loopback)
                foreach (IPAddress address in addresses)
                {
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address))
                    {
                        ipAddress = address.ToString();
                        break;
                    }
                }

                Logger.LogVerbose($"Получен IP-адрес: {ipAddress}");
                return ipAddress;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка получения IP-адреса: {ex.Message}");
                return "127.0.0.1";
            }
        }
    }
}