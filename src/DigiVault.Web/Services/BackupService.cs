using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DigiVault.Web.Services;

public interface IBackupService
{
    Task<string> CreateFullBackupAsync();
    Task<bool> RestoreFromBackupAsync(string backupId);
    Task<List<BackupInfo>> GetBackupHistoryAsync();
    Task<string?> GetBackupFilePathAsync(string backupId);
    Task<bool> DeleteBackupAsync(string backupId);
}

public class BackupService : IBackupService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackupService> _logger;
    private readonly string _backupPath;

    public BackupService(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<BackupService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _backupPath = Path.Combine(Directory.GetCurrentDirectory(), "backups");
        Directory.CreateDirectory(_backupPath);
    }

    public async Task<string> CreateFullBackupAsync()
    {
        var backupId = $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        var tempPath = Path.Combine(_backupPath, "temp", backupId);
        Directory.CreateDirectory(tempPath);

        try
        {
            _logger.LogInformation("Starting full backup: {BackupId}", backupId);

            await BackupDatabaseAsync(tempPath);
            await BackupImagesAsync(tempPath);

            var metadata = new BackupMetadata
            {
                BackupId = backupId,
                CreatedAt = DateTime.UtcNow,
                Version = "1.0",
                DatabaseVersion = await GetDatabaseVersionAsync(),
                BackupType = "Full"
            };

            await File.WriteAllTextAsync(
                Path.Combine(tempPath, "metadata.json"),
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true })
            );

            var archivePath = Path.Combine(_backupPath, $"{backupId}.zip");
            ZipFile.CreateFromDirectory(tempPath, archivePath, CompressionLevel.Optimal, false);
            Directory.Delete(tempPath, true);

            var fileInfo = new FileInfo(archivePath);
            _logger.LogInformation("Backup completed: {BackupId}, Size: {Size} MB",
                backupId, fileInfo.Length / 1024 / 1024);

            return backupId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed: {BackupId}", backupId);
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
            throw;
        }
    }

    private async Task BackupDatabaseAsync(string backupPath)
    {
        var dbBackupPath = Path.Combine(backupPath, "database");
        Directory.CreateDirectory(dbBackupPath);

        var connectionString = _configuration.GetConnectionString("DefaultConnection")!;
        var backupFile = Path.Combine(dbBackupPath, "digivault.sql");

        var pgDumpPath = FindPgDump();
        if (string.IsNullOrEmpty(pgDumpPath))
        {
            _logger.LogWarning("pg_dump not found, using alternative backup method");
            await CreateAlternativeBackup(backupFile, connectionString);
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = pgDumpPath,
            Arguments = $"-h {builder.Host} -p {builder.Port} -U {builder.Username} -d {builder.Database} -f \"{backupFile}\" --no-password",
            UseShellExecute = false,
            RedirectStandardError = true,
            Environment = { ["PGPASSWORD"] = builder.Password }
        };

        using var process = System.Diagnostics.Process.Start(processInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogWarning("pg_dump failed, falling back to alternative: {Error}", error);
                await CreateAlternativeBackup(backupFile, connectionString);
            }
        }
    }

    private static string? FindPgDump()
    {
        var possiblePaths = new[]
        {
            "pg_dump",
            "/usr/bin/pg_dump",
            "/usr/local/bin/pg_dump"
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit(1000);
                if (process.ExitCode == 0) return path;
            }
            catch { }
        }
        return null;
    }

    private async Task CreateAlternativeBackup(string backupFile, string connectionString)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- DigiVault Database Backup");
        sb.AppendLine($"-- Created at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var tables = new List<string>();
        using (var cmd = new NpgsqlCommand(@"
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            AND table_name NOT LIKE '__EF%'
            ORDER BY table_name", connection))
        {
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            sb.AppendLine($"-- Table: {table}");
            using var cmd = new NpgsqlCommand($"SELECT * FROM \"{table}\"", connection);
            using var reader = await cmd.ExecuteReaderAsync();

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add($"\"{reader.GetName(i)}\"");

            while (await reader.ReadAsync())
            {
                var values = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        values.Add("NULL");
                    }
                    else
                    {
                        var value = reader.GetValue(i);
                        var type = reader.GetFieldType(i);
                        if (type == typeof(string) || type == typeof(DateTime) || type == typeof(Guid))
                            values.Add($"'{value.ToString()!.Replace("'", "''")}'");
                        else if (type == typeof(bool))
                            values.Add(((bool)value) ? "true" : "false");
                        else
                            values.Add(value.ToString()!);
                    }
                }
                sb.AppendLine($"INSERT INTO \"{table}\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)});");
            }
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(backupFile, sb.ToString(), Encoding.UTF8);
    }

    private async Task BackupImagesAsync(string backupPath)
    {
        var imagesBackupPath = Path.Combine(backupPath, "images");
        Directory.CreateDirectory(imagesBackupPath);

        var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "uploads");
        if (Directory.Exists(sourcePath))
        {
            CopyDirectory(sourcePath, Path.Combine(imagesBackupPath, "uploads"));
        }
    }

    public async Task<bool> RestoreFromBackupAsync(string backupId)
    {
        _logger.LogInformation("Restore from backup: {BackupId}", backupId);
        return await Task.FromResult(false);
    }

    public async Task<List<BackupInfo>> GetBackupHistoryAsync()
    {
        var backups = new List<BackupInfo>();

        if (Directory.Exists(_backupPath))
        {
            foreach (var file in Directory.GetFiles(_backupPath, "*.zip"))
            {
                var fileInfo = new FileInfo(file);
                backups.Add(new BackupInfo
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    CreatedAt = fileInfo.CreationTimeUtc,
                    SizeInBytes = fileInfo.Length,
                    Type = "Local",
                    Status = "Available"
                });
            }
        }

        return await Task.FromResult(backups.OrderByDescending(b => b.CreatedAt).ToList());
    }

    public Task<string?> GetBackupFilePathAsync(string backupId)
    {
        var path = Path.Combine(_backupPath, $"{backupId}.zip");
        return Task.FromResult(File.Exists(path) ? path : null);
    }

    public Task<bool> DeleteBackupAsync(string backupId)
    {
        var path = Path.Combine(_backupPath, $"{backupId}.zip");
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Deleted backup: {BackupId}", backupId);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private async Task<string> GetDatabaseVersionAsync()
    {
        var migrations = await _context.Database.GetAppliedMigrationsAsync();
        return migrations.LastOrDefault() ?? "Unknown";
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(sourceDir))
            CopyDirectory(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
    }
}

public class BackupInfo
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long SizeInBytes { get; set; }
    public string Type { get; set; } = "Local";
    public string Status { get; set; } = "Available";
}

public class BackupMetadata
{
    public string BackupId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Version { get; set; } = "1.0";
    public string DatabaseVersion { get; set; } = string.Empty;
    public string BackupType { get; set; } = "Full";
}
