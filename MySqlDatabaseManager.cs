using System;
using MySql.Data.MySqlClient;

namespace SystemInfoService
{
    public class MySqlDatabaseManager
    {
        private static string connectionString;

        public static void InitializeDatabase(string server, string database,
                                    string userId, string password)
        {
            // Расшифровываем пароль AES-256
            string decryptedPassword = AesEncryption.Decrypt(password);

            connectionString = $"Server={server};Database={database};User ID={userId};Password={decryptedPassword};CharSet=utf8mb4;";

            // Проверяем подключение
            using (var connection = new MySqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    ConsoleHelper.PrintSuccess("Успешное подключение к MySQL");
                    Logger.LogInfo("Подключение к MySQL установлено");

                    // Создаем таблицы если их нет
                    CreateTablesIfNotExist();
                }
                catch (Exception ex)
                {
                    ConsoleHelper.PrintError($"Ошибка подключения к MySQL: {ex.Message}");
                    Logger.LogError(ex);
                    throw;
                }
            }
        }

        public static MySqlConnection GetConnection()
        {
            return new MySqlConnection(connectionString);
        }

        public static void CreateTablesIfNotExist()
        {
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    string[] createTableQueries = {
                // Computers - добавляем ConfigurationHash
                @"CREATE TABLE IF NOT EXISTS Computers (
                    ComputerID INT PRIMARY KEY AUTO_INCREMENT,
                    ComputerName VARCHAR(100) NOT NULL,
                    ConfigurationHash VARCHAR(500) NOT NULL,
                    ScanDateTime DATETIME NOT NULL,
                    CreatedDate TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE KEY unique_computer_config (ComputerName, ConfigurationHash),
                    INDEX idx_computer_name (ComputerName),
                    INDEX idx_config_hash (ConfigurationHash)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                                // GeneralInfo - добавляем IPAddress
                @"CREATE TABLE IF NOT EXISTS GeneralInfo (
                    GeneralInfoID INT PRIMARY KEY AUTO_INCREMENT,
                    ComputerID INT NOT NULL,
                    OSVersion VARCHAR(255),
                    ProcessorCount INT,
                    UserName VARCHAR(100),
                    Is64BitOS BOOLEAN,
                    IPAddress VARCHAR(45),
                    ScanDateTime DATETIME NOT NULL,
                    FOREIGN KEY (ComputerID) REFERENCES Computers(ComputerID) ON DELETE CASCADE,
                    UNIQUE KEY unique_computer_general (ComputerID, ScanDateTime)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                // Processors
                @"CREATE TABLE IF NOT EXISTS Processors (
                    ProcessorID INT PRIMARY KEY AUTO_INCREMENT,
                    ComputerID INT NOT NULL,
                    CurrentClockSpeed INT,
                    Name VARCHAR(500),
                    Manufacturer VARCHAR(100),
                    NumberOfCores INT,
                    Status VARCHAR(50),
                    ScanDateTime DATETIME NOT NULL,
                    FOREIGN KEY (ComputerID) REFERENCES Computers(ComputerID) ON DELETE CASCADE,
                    UNIQUE KEY unique_computer_processor_scan (ComputerID, ScanDateTime),
                    INDEX idx_computer (ComputerID),
                    INDEX idx_scan (ScanDateTime)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                // Motherboards
                @"CREATE TABLE IF NOT EXISTS Motherboards (
                    MotherboardID INT PRIMARY KEY AUTO_INCREMENT,
                    ComputerID INT NOT NULL,
                    Manufacturer VARCHAR(100),
                    Product VARCHAR(100),
                    SerialNumber VARCHAR(100),
                    Version VARCHAR(50),
                    Description VARCHAR(255),
                    ScanDateTime DATETIME NOT NULL,
                    FOREIGN KEY (ComputerID) REFERENCES Computers(ComputerID) ON DELETE CASCADE,
                    UNIQUE KEY unique_computer_motherboard_scan (ComputerID, ScanDateTime),
                    INDEX idx_computer (ComputerID),
                    INDEX idx_scan (ScanDateTime)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                // HardDrives
                @"CREATE TABLE IF NOT EXISTS HardDrives (
                    HardDriveID INT PRIMARY KEY AUTO_INCREMENT,
                    ComputerID INT NOT NULL,
                    Model VARCHAR(200),
                    InterfaceType VARCHAR(50),
                    SerialNumber VARCHAR(100),
                    SizeGB DECIMAL(10,2),
                    BytesPerSector INT,
                    TotalSectors BIGINT,
                    ScanDateTime DATETIME NOT NULL,
                    FOREIGN KEY (ComputerID) REFERENCES Computers(ComputerID) ON DELETE CASCADE,
                    UNIQUE KEY unique_drive_computer_scan (ComputerID, SerialNumber, ScanDateTime),
                    INDEX idx_computer (ComputerID),
                    INDEX idx_scan (ScanDateTime)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                // Memory
                @"CREATE TABLE IF NOT EXISTS Memory (
                    MemoryID INT PRIMARY KEY AUTO_INCREMENT,
                    ComputerID INT NOT NULL,
                    TotalGB DECIMAL(10,2),
                    ScanDateTime DATETIME NOT NULL,
                    FOREIGN KEY (ComputerID) REFERENCES Computers(ComputerID) ON DELETE CASCADE,
                    UNIQUE KEY unique_computer_memory_scan (ComputerID, ScanDateTime),
                    INDEX idx_computer (ComputerID),
                    INDEX idx_scan (ScanDateTime)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                // MemoryModules
                @"CREATE TABLE IF NOT EXISTS MemoryModules (
                    MemoryModuleID INT PRIMARY KEY AUTO_INCREMENT,
                    MemoryID INT NOT NULL,
                    SlotLocation VARCHAR(100),
                    Manufacturer VARCHAR(100),
                    SerialNumber VARCHAR(100),
                    CapacityGB DECIMAL(10,2),
                    SpeedMHz INT,
                    ModuleIndex INT,
                    FOREIGN KEY (MemoryID) REFERENCES Memory(MemoryID) ON DELETE CASCADE,
                    UNIQUE KEY unique_memory_slot (MemoryID, SlotLocation)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                // VideoCards
                @"CREATE TABLE IF NOT EXISTS VideoCards (
                    VideoCardID INT PRIMARY KEY AUTO_INCREMENT,
                    ComputerID INT NOT NULL,
                    Name VARCHAR(200),
                    Manufacturer VARCHAR(100),
                    VideoMemoryGB DECIMAL(10,2),
                    CurrentRefreshRate INT,
                    DriverVersion VARCHAR(100),
                    ScanDateTime DATETIME NOT NULL,
                    FOREIGN KEY (ComputerID) REFERENCES Computers(ComputerID) ON DELETE CASCADE,
                    UNIQUE KEY unique_computer_video_scan (ComputerID, ScanDateTime),
                    INDEX idx_computer (ComputerID),
                    INDEX idx_scan (ScanDateTime)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                // InstalledSoftware
                @"CREATE TABLE IF NOT EXISTS InstalledSoftware (
                    SoftwareID INT PRIMARY KEY AUTO_INCREMENT,
                    ComputerID INT NOT NULL,
                    SoftwareName VARCHAR(200),
                    SoftwareVersion VARCHAR(100),
                    Publisher VARCHAR(200),
                    InstallDate DATE,
                    SizeMB DECIMAL(10,2),
                    ScanDateTime DATETIME NOT NULL,
                    FOREIGN KEY (ComputerID) REFERENCES Computers(ComputerID) ON DELETE CASCADE,
                    INDEX idx_computer (ComputerID),
                    INDEX idx_scan (ScanDateTime)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                // SystemAlerts
                @"CREATE TABLE IF NOT EXISTS SystemAlerts (
                    AlertID INT PRIMARY KEY AUTO_INCREMENT,
                    ComputerID INT NOT NULL,
                    AlertType VARCHAR(50),
                    AlertSource VARCHAR(100),
                    AlertMessage TEXT,
                    AlertValue VARCHAR(100),
                    IsResolved BOOLEAN DEFAULT FALSE,
                    ScanDateTime DATETIME NOT NULL,
                    FOREIGN KEY (ComputerID) REFERENCES Computers(ComputerID) ON DELETE CASCADE,
                    INDEX idx_computer (ComputerID),
                    INDEX idx_resolved (IsResolved),
                    INDEX idx_scan (ScanDateTime)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

                // SecurityInfo
                @"CREATE TABLE IF NOT EXISTS SecurityInfo (
                    SecurityID INT PRIMARY KEY AUTO_INCREMENT,
                    ComputerID INT NOT NULL,
                    AntivirusName VARCHAR(100),
                    AntivirusStatus VARCHAR(50),
                    WindowsUpdateStatus VARCHAR(50),
                    LastWindowsUpdate DATE,
                    FirewallEnabled BOOLEAN,
                    LastLoginUser VARCHAR(100),
                    ScanDateTime DATETIME NOT NULL,
                    FOREIGN KEY (ComputerID) REFERENCES Computers(ComputerID) ON DELETE CASCADE,
                    UNIQUE KEY unique_computer_security_scan (ComputerID, ScanDateTime),
                    INDEX idx_computer (ComputerID),
                    INDEX idx_scan (ScanDateTime)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;"
            };

                    ConsoleHelper.PrintProgress("Проверка и создание таблиц...");

                    foreach (string query in createTableQueries)
                    {
                        using (var command = new MySqlCommand(query, connection))
                        {
                            command.ExecuteNonQuery();
                            ConsoleHelper.PrintVerbose($"Таблица создана/проверена: {GetTableName(query)}");
                        }
                    }

                    ConsoleHelper.PrintSuccess("Все таблицы проверены/созданы");
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.PrintError($"Ошибка создания таблиц: {ex.Message}");
                Logger.LogError(ex);
                throw;
            }
        }

        private static string GetTableName(string query)
        {
            // Извлекаем имя таблицы из SQL запроса
            var match = System.Text.RegularExpressions.Regex.Match(
                query,
                @"CREATE TABLE IF NOT EXISTS (\w+)"
            );
            return match.Success ? match.Groups[1].Value : "Unknown";
        }
    }
}