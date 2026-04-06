using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Management;
using System.Net;
using System.Linq;
using Microsoft.Win32;
using System.Text;
using System.IO;

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
                                  string ipAddress,
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
                    command.Parameters.AddWithValue("@IPAddress", ipAddress ?? "");
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
                        return tableCount >= 8;
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

                    string sql = @"
                SELECT 
                    COALESCE((SELECT Name FROM Processors 
                              WHERE ComputerID = @ComputerID 
                              ORDER BY ScanDateTime DESC LIMIT 1), '') as ProcessorName,
                    
                    COALESCE((SELECT CONCAT(Manufacturer, '|', Product) FROM Motherboards 
                              WHERE ComputerID = @ComputerID 
                              ORDER BY ScanDateTime DESC LIMIT 1), '') as MotherboardInfo,
                    
                    COALESCE((SELECT CAST(SUM(SizeGB) as CHAR) FROM HardDrives 
                              WHERE ComputerID = @ComputerID 
                              AND ScanDateTime = (
                                  SELECT MAX(ScanDateTime) FROM HardDrives 
                                  WHERE ComputerID = @ComputerID
                              )), '0') as TotalDriveSize,
                    
                    COALESCE((SELECT CAST(TotalGB as CHAR) FROM Memory 
                              WHERE ComputerID = @ComputerID 
                              ORDER BY ScanDateTime DESC LIMIT 1), '0') as TotalMemory,
                    
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
                return true;
            }
        }

        public static bool IsNewConfiguration(long computerId)
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

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

                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(hash.ToString());
                    byte[] hashBytes = md5.ComputeHash(inputBytes);

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

        public static string GetLocalIPAddress()
        {
            try
            {
                string ipAddress = "127.0.0.1";

                string hostName = Dns.GetHostName();
                IPAddress[] addresses = Dns.GetHostAddresses(hostName);

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

        // ============================================================
        // МЕТОДЫ ДЛЯ РАБОТЫ С УСТАНОВЛЕННЫМИ ПРОГРАММАМИ
        // ============================================================

        private class SoftwareInfoItem
        {
            public string Name { get; set; } = "";
            public string Version { get; set; } = "";
            public string Publisher { get; set; } = "";
            public DateTime? InstallDate { get; set; }
        }

        public static void SaveInstalledSoftware(long computerId, DateTime scanDateTime)
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    string deleteSql = "DELETE FROM InstalledSoftware WHERE ComputerID = @ComputerID AND ScanDateTime = @ScanDateTime";
                    using (var deleteCmd = new MySqlCommand(deleteSql, connection))
                    {
                        deleteCmd.Parameters.AddWithValue("@ComputerID", computerId);
                        deleteCmd.Parameters.AddWithValue("@ScanDateTime", scanDateTime);
                        deleteCmd.ExecuteNonQuery();
                    }

                    var allPrograms = new List<SoftwareInfoItem>();
                    var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Читаем из реестра
                    string registryPath64 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
                    AddProgramsFromRegistry(allPrograms, addedKeys, Microsoft.Win32.Registry.LocalMachine, registryPath64);

                    string registryPath32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
                    AddProgramsFromRegistry(allPrograms, addedKeys, Microsoft.Win32.Registry.LocalMachine, registryPath32);

                    string userRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
                    AddProgramsFromRegistry(allPrograms, addedKeys, Microsoft.Win32.Registry.CurrentUser, userRegistryPath);

                    // Добавляем антивирусы
                    AddSecuritySoftware(allPrograms, addedKeys);

                    // Сортируем по имени
                    var sortedPrograms = allPrograms.OrderBy(p => p.Name).ToList();

                    int count = 0;
                    foreach (var program in sortedPrograms)
                    {
                        if (string.IsNullOrEmpty(program.Name)) continue;
                        if (IsSystemComponent(program.Name)) continue;

                        string insertSql = @"
                            INSERT INTO InstalledSoftware 
                            (ComputerID, SoftwareName, SoftwareVersion, Publisher, InstallDate, SizeMB, ScanDateTime)
                            VALUES 
                            (@ComputerID, @Name, @Version, @Publisher, @InstallDate, @SizeMB, @ScanDateTime)";

                        using (var insertCmd = new MySqlCommand(insertSql, connection))
                        {
                            insertCmd.Parameters.AddWithValue("@ComputerID", computerId);
                            insertCmd.Parameters.AddWithValue("@Name", TruncateString(program.Name, 200));
                            insertCmd.Parameters.AddWithValue("@Version", TruncateString(program.Version ?? "", 100));
                            insertCmd.Parameters.AddWithValue("@Publisher", TruncateString(program.Publisher ?? "", 200));
                            insertCmd.Parameters.AddWithValue("@InstallDate", program.InstallDate.HasValue ? (object)program.InstallDate.Value : DBNull.Value);
                            insertCmd.Parameters.AddWithValue("@SizeMB", DBNull.Value);
                            insertCmd.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                            insertCmd.ExecuteNonQuery();
                            count++;
                        }
                    }

                    Logger.LogInfo($"Сохранено программ: {count}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка сохранения списка программ: {ex.Message}");
            }
        }

        private static void AddProgramsFromRegistry(
            List<SoftwareInfoItem> programs,
            HashSet<string> addedKeys,
            Microsoft.Win32.RegistryKey rootKey,
            string registryPath)
        {
            try
            {
                using (var key = rootKey.OpenSubKey(registryPath))
                {
                    if (key == null) return;

                    foreach (string subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey == null) continue;

                                string displayName = subKey.GetValue("DisplayName") as string;
                                if (string.IsNullOrEmpty(displayName)) continue;

                                string version = subKey.GetValue("DisplayVersion") as string ?? "";
                                string uniqueKey = $"{displayName}|{version}".ToLowerInvariant();

                                if (addedKeys.Contains(uniqueKey)) continue;
                                addedKeys.Add(uniqueKey);

                                string publisher = subKey.GetValue("Publisher") as string ?? "";

                                DateTime? installDate = null;
                                string installDateStr = subKey.GetValue("InstallDate") as string;
                                if (!string.IsNullOrEmpty(installDateStr) && installDateStr.Length == 8)
                                {
                                    if (DateTime.TryParseExact(installDateStr, "yyyyMMdd",
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                                    {
                                        installDate = parsedDate;
                                    }
                                }

                                programs.Add(new SoftwareInfoItem
                                {
                                    Name = displayName.Trim(),
                                    Version = version.Trim(),
                                    Publisher = publisher.Trim(),
                                    InstallDate = installDate
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogVerbose($"Ошибка чтения программы из реестра: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка чтения реестра {registryPath}: {ex.Message}");
            }
        }

        private static void AddSecuritySoftware(List<SoftwareInfoItem> programs, HashSet<string> addedKeys)
        {
            // WMI SecurityCenter2
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "root\\SecurityCenter2",
                    "SELECT * FROM AntiVirusProduct"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string displayName = obj["displayName"]?.ToString();
                        if (string.IsNullOrEmpty(displayName)) continue;

                        string version = obj["productVersion"]?.ToString() ?? "";
                        string uniqueKey = $"{displayName}|{version}".ToLowerInvariant();

                        if (addedKeys.Contains(uniqueKey)) continue;
                        addedKeys.Add(uniqueKey);

                        programs.Add(new SoftwareInfoItem
                        {
                            Name = displayName.Trim(),
                            Version = version,
                            Publisher = "Антивирус",
                            InstallDate = null
                        });
                        Logger.LogVerbose($"Найден антивирус через WMI: {displayName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"Ошибка получения антивирусов через WMI: {ex.Message}");
            }

            // Поиск в реестре
            string[] antivirusRegistryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows Defender",
                @"SOFTWARE\KasperskyLab",
                @"SOFTWARE\ESET",
                @"SOFTWARE\Avast",
                @"SOFTWARE\AVG",
                @"SOFTWARE\Bitdefender",
                @"SOFTWARE\McAfee",
                @"SOFTWARE\Symantec",
                @"SOFTWARE\Norton",
                @"SOFTWARE\DrWeb",
                @"SOFTWARE\Panda Software",
                @"SOFTWARE\TrendMicro",
                @"SOFTWARE\COMODO",
                @"SOFTWARE\F-Secure"
            };

            foreach (string path in antivirusRegistryPaths)
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path))
                    {
                        if (key != null)
                        {
                            string displayName = key.GetValue("DisplayName") as string;
                            if (string.IsNullOrEmpty(displayName))
                            {
                                displayName = path.Split('\\').Last();
                            }

                            string uniqueKey = $"{displayName}|".ToLowerInvariant();
                            if (!addedKeys.Contains(uniqueKey))
                            {
                                addedKeys.Add(uniqueKey);
                                string version = key.GetValue("DisplayVersion") as string ??
                                                key.GetValue("Version") as string ?? "";
                                programs.Add(new SoftwareInfoItem
                                {
                                    Name = displayName,
                                    Version = version,
                                    Publisher = "Антивирус",
                                    InstallDate = null
                                });
                                Logger.LogVerbose($"Найден антивирус в реестре: {displayName}");
                            }
                        }
                    }
                }
                catch { }
            }

            // Dr.Web через папку Program Files
            try
            {
                string drwebPath = @"C:\Program Files\DrWeb";
                if (Directory.Exists(drwebPath))
                {
                    string uniqueKey = "dr.web anti-virus|".ToLowerInvariant();
                    if (!addedKeys.Contains(uniqueKey))
                    {
                        addedKeys.Add(uniqueKey);
                        programs.Add(new SoftwareInfoItem
                        {
                            Name = "Dr.Web Anti-virus",
                            Version = "",
                            Publisher = "Doctor Web",
                            InstallDate = null
                        });
                        Logger.LogInfo($"Найден Dr.Web в Program Files");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"Ошибка поиска Dr.Web: {ex.Message}");
            }
        }

        private static bool IsSystemComponent(string programName)
        {
            string[] systemKeywords = new[]
            {
                "Microsoft .NET", "Microsoft Visual Studio", "Microsoft ASP.NET",
                "Microsoft Office", "MSBuild", "Windows SDK", "SQL Server",
                "IntelliTrace", "icecap", "ClickOnce", "DiagnosticsHub",
                "Entity Framework", "IIS", "Google Update Helper",
                "Targeting Pack", "Shared Framework", "AppHost Pack",
                "Runtime", "SDK", "Templates", "Toolset", "Host",
                "Package", "IntelliSense", "sptools", "vs_", "vcpp_",
                "windows_tools", "Microsoft OneDrive", "Microsoft Edge",
                "Microsoft Windows Communication", "Microsoft Workflow",
                "Update for", "Security Update", "Накопительный пакет",
                "Пакет нацеливания", "vs_BlendMsi", "vs_clickonce",
                "vs_community", "vs_CoreEditor", "vs_devenv", "vs_filehandler",
                "vs_FileTracker", "vs_github", "vs_minshell", "vs_tips",
                "vs_vsweb", "windows_tools", "vcpp_crt"
            };

            string lowerName = programName.ToLowerInvariant();

            foreach (var keyword in systemKeywords)
            {
                if (lowerName.Contains(keyword.ToLowerInvariant()))
                    return true;
            }

            return false;
        }

        private static string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        // ============================================================
        // МЕТОДЫ ДЛЯ РАБОТЫ С СИСТЕМНЫМИ ПРЕДУПРЕЖДЕНИЯМИ
        // ============================================================

        public static void CheckAndCreateAlerts(long computerId, double cpuUsage, double memoryUsage,
                                                double freeSpacePercent, double temperatureCelsius,
                                                DateTime scanDateTime)
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    if (cpuUsage > 90)
                    {
                        CreateAlert(connection, computerId, "Warning", "CPU",
                            $"Высокая загрузка процессора: {cpuUsage:F1}%",
                            cpuUsage.ToString("F1"), scanDateTime);
                    }

                    if (memoryUsage > 90)
                    {
                        CreateAlert(connection, computerId, "Warning", "Memory",
                            $"Высокое использование памяти: {memoryUsage:F1}%",
                            memoryUsage.ToString("F1"), scanDateTime);
                    }

                    if (freeSpacePercent < 10)
                    {
                        CreateAlert(connection, computerId, "Critical", "Disk",
                            $"Критически мало свободного места на диске: {freeSpacePercent:F1}%",
                            freeSpacePercent.ToString("F1"), scanDateTime);
                    }
                    else if (freeSpacePercent < 20)
                    {
                        CreateAlert(connection, computerId, "Warning", "Disk",
                            $"Мало свободного места на диске: {freeSpacePercent:F1}%",
                            freeSpacePercent.ToString("F1"), scanDateTime);
                    }

                    if (temperatureCelsius > 85)
                    {
                        CreateAlert(connection, computerId, "Critical", "Temperature",
                            $"Критическая температура: {temperatureCelsius:F1}°C",
                            temperatureCelsius.ToString("F1"), scanDateTime);
                    }
                    else if (temperatureCelsius > 75)
                    {
                        CreateAlert(connection, computerId, "Warning", "Temperature",
                            $"Высокая температура: {temperatureCelsius:F1}°C",
                            temperatureCelsius.ToString("F1"), scanDateTime);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка проверки предупреждений: {ex.Message}");
            }
        }

        private static void CreateAlert(MySqlConnection connection, long computerId, string alertType,
                                        string alertSource, string message, string value, DateTime scanDateTime)
        {
            try
            {
                string checkSql = @"
            SELECT AlertID FROM SystemAlerts 
            WHERE ComputerID = @ComputerID 
            AND AlertType = @AlertType 
            AND AlertSource = @AlertSource 
            AND IsResolved = FALSE";

                using (var checkCmd = new MySqlCommand(checkSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@ComputerID", computerId);
                    checkCmd.Parameters.AddWithValue("@AlertType", alertType);
                    checkCmd.Parameters.AddWithValue("@AlertSource", alertSource);

                    var existing = checkCmd.ExecuteScalar();
                    if (existing != null)
                    {
                        return;
                    }
                }

                string insertSql = @"
            INSERT INTO SystemAlerts 
            (ComputerID, AlertType, AlertSource, AlertMessage, AlertValue, IsResolved, ScanDateTime)
            VALUES 
            (@ComputerID, @AlertType, @AlertSource, @AlertMessage, @AlertValue, FALSE, @ScanDateTime)";

                using (var insertCmd = new MySqlCommand(insertSql, connection))
                {
                    insertCmd.Parameters.AddWithValue("@ComputerID", computerId);
                    insertCmd.Parameters.AddWithValue("@AlertType", alertType);
                    insertCmd.Parameters.AddWithValue("@AlertSource", alertSource);
                    insertCmd.Parameters.AddWithValue("@AlertMessage", message);
                    insertCmd.Parameters.AddWithValue("@AlertValue", value);
                    insertCmd.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                    insertCmd.ExecuteNonQuery();
                    Logger.LogWarning($"Создано предупреждение: {alertType} - {message}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка создания предупреждения: {ex.Message}");
            }
        }

        // ============================================================
        // МЕТОДЫ ДЛЯ РАБОТЫ С ИНФОРМАЦИЕЙ О БЕЗОПАСНОСТИ
        // ============================================================

        public static void SaveSecurityInfo(long computerId, DateTime scanDateTime)
        {
            try
            {
                using (var connection = MySqlDatabaseManager.GetConnection())
                {
                    connection.Open();

                    string antivirusName = "";
                    string antivirusStatus = "Unknown";

                    try
                    {
                        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                            "SELECT * FROM AntiVirusProduct",
                            "root\\SecurityCenter2"))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                antivirusName = obj["displayName"]?.ToString() ?? "";
                                int? productState = obj["productState"] != null ?
                                    Convert.ToInt32(obj["productState"]) : (int?)null;

                                if (productState.HasValue)
                                {
                                    if ((productState.Value & 0x10000) != 0)
                                        antivirusStatus = "Active";
                                    else
                                        antivirusStatus = "Disabled";
                                }
                                break;
                            }
                        }
                    }
                    catch
                    {
                        antivirusName = "Not available";
                    }

                    string windowsUpdateStatus = "Unknown";
                    DateTime? lastWindowsUpdate = null;

                    try
                    {
                        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                            "SELECT * FROM Win32_QuickFixEngineering"))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                string installedOn = obj["InstalledOn"]?.ToString();
                                if (!string.IsNullOrEmpty(installedOn) &&
                                    DateTime.TryParse(installedOn, out DateTime updateDate))
                                {
                                    if (!lastWindowsUpdate.HasValue || updateDate > lastWindowsUpdate.Value)
                                    {
                                        lastWindowsUpdate = updateDate;
                                    }
                                }
                            }
                        }

                        if (lastWindowsUpdate.HasValue)
                        {
                            TimeSpan daysSinceUpdate = DateTime.Now - lastWindowsUpdate.Value;
                            if (daysSinceUpdate.TotalDays > 30)
                                windowsUpdateStatus = "Outdated";
                            else if (daysSinceUpdate.TotalDays > 7)
                                windowsUpdateStatus = "Pending";
                            else
                                windowsUpdateStatus = "UpToDate";
                        }
                        else
                        {
                            windowsUpdateStatus = "NoUpdatesFound";
                        }
                    }
                    catch
                    {
                        windowsUpdateStatus = "Error";
                    }

                    bool firewallEnabled = false;
                    try
                    {
                        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                            "SELECT * FROM Win32_FirewallProduct",
                            "root\\SecurityCenter2"))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                int? productState = obj["productState"] != null ?
                                    Convert.ToInt32(obj["productState"]) : (int?)null;

                                if (productState.HasValue)
                                {
                                    firewallEnabled = (productState.Value & 0x10000) != 0;
                                }
                                break;
                            }
                        }
                    }
                    catch
                    {
                        try
                        {
                            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                                @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile"))
                            {
                                if (key != null)
                                {
                                    object enableFirewall = key.GetValue("EnableFirewall");
                                    if (enableFirewall != null)
                                    {
                                        firewallEnabled = Convert.ToInt32(enableFirewall) == 1;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    string lastLoginUser = Environment.UserName;
                    try
                    {
                        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                            "SELECT * FROM Win32_ComputerSystem"))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                lastLoginUser = obj["UserName"]?.ToString() ?? Environment.UserName;
                                break;
                            }
                        }
                    }
                    catch { }

                    string sql = @"
                INSERT INTO SecurityInfo 
                (ComputerID, AntivirusName, AntivirusStatus, WindowsUpdateStatus, 
                 LastWindowsUpdate, FirewallEnabled, LastLoginUser, ScanDateTime)
                VALUES 
                (@ComputerID, @AntivirusName, @AntivirusStatus, @WindowsUpdateStatus,
                 @LastWindowsUpdate, @FirewallEnabled, @LastLoginUser, @ScanDateTime)
                ON DUPLICATE KEY UPDATE
                    AntivirusName = @AntivirusName,
                    AntivirusStatus = @AntivirusStatus,
                    WindowsUpdateStatus = @WindowsUpdateStatus,
                    LastWindowsUpdate = @LastWindowsUpdate,
                    FirewallEnabled = @FirewallEnabled,
                    LastLoginUser = @LastLoginUser";

                    using (var command = new MySqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ComputerID", computerId);
                        command.Parameters.AddWithValue("@AntivirusName", antivirusName);
                        command.Parameters.AddWithValue("@AntivirusStatus", antivirusStatus);
                        command.Parameters.AddWithValue("@WindowsUpdateStatus", windowsUpdateStatus);
                        command.Parameters.AddWithValue("@LastWindowsUpdate", lastWindowsUpdate.HasValue ? (object)lastWindowsUpdate.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@FirewallEnabled", firewallEnabled);
                        command.Parameters.AddWithValue("@LastLoginUser", lastLoginUser);
                        command.Parameters.AddWithValue("@ScanDateTime", scanDateTime);

                        command.ExecuteNonQuery();
                    }

                    Logger.LogInfo($"Информация о безопасности сохранена. Антивирус: {antivirusName}, Статус: {antivirusStatus}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка сохранения информации о безопасности: {ex.Message}");
            }
        }
    }
}