using System.IO.Compression;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Api.Controllers;

public sealed partial class SystemApiController
{
    private void LogSystemAudit(string eventType, string action, string summary, bool success, string severity = "info", int? relatedGrowId = null, string? relatedFileName = null)
    {
        try
        {
            _auditRepository.Add(new SystemAuditEvent
            {
                EventType = eventType,
                Action = action,
                Summary = summary,
                Severity = severity,
                Source = "system-api",
                RelatedGrowId = relatedGrowId,
                RelatedFileName = relatedFileName,
                Success = success
            });
        }
        catch
        {
            // Audit logging must never break core system endpoints.
        }
    }


    private string? ResolveBackupPath(string fileName)
    {
        var backupRoot = _paths.BackupsPath;
        var backupPath = Path.Combine(backupRoot, fileName);
        var fullRoot = Path.GetFullPath(backupRoot);
        var fullPath = Path.GetFullPath(backupPath);

        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }



    private static ApiEndpointDto Endpoint(string method, string path, string purpose, bool localAdminOnly, params string[] rules)
        => new(
            Method: method,
            Path: path,
            Purpose: purpose,
            LocalAdminOnly: localAdminOnly,
            Rules: rules);


    private SqliteConnection OpenReadConnection()
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath, Mode = SqliteOpenMode.ReadOnly };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }


    private static string? ReadAppSetting(SqliteConnection connection, string key)
    {
        if (!TableExists(connection, "AppSettings"))
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar()?.ToString();
    }


    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }


    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        if (!TableExists(connection, tableName))
        {
            return false;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }


    private static bool IsSafeBackupFileName(string fileName)
        => fileName.StartsWith("grow-os-backup-", StringComparison.OrdinalIgnoreCase)
           && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
           && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
           && !fileName.Contains('/')
           && !fileName.Contains('\\')
           && fileName.Length <= 120;




    private static string? ResolveRestoreEntryKind(string entryName)
    {
        if (entryName.Equals("App_Data/grow-diary.db", StringComparison.OrdinalIgnoreCase))
        {
            return "database";
        }
        if (entryName.Equals("App_Data/grow-diary.db-wal", StringComparison.OrdinalIgnoreCase))
        {
            return "database-wal";
        }
        if (entryName.Equals("App_Data/grow-diary.db-shm", StringComparison.OrdinalIgnoreCase))
        {
            return "database-shm";
        }
        if (entryName.StartsWith("App_Data/knowledge/", StringComparison.OrdinalIgnoreCase))
        {
            return "knowledge";
        }

        return null;
    }


    private bool WouldOverwriteRestoreTarget(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var targetPath = Path.GetFullPath(Path.Combine(_paths.ContentRootPath, normalized));
        var root = Path.GetFullPath(_paths.ContentRootPath);
        return targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               && System.IO.File.Exists(targetPath);
    }


    private static bool IsUnsafeZipEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return true;
        }

        var normalized = entryName.Replace('\\', '/');
        return normalized.StartsWith("/", StringComparison.Ordinal)
               || normalized.Contains("../", StringComparison.Ordinal)
               || normalized.Contains("/..", StringComparison.Ordinal)
               || normalized.Equals("..", StringComparison.Ordinal);
    }


    private static string? ReadSchemaVersionFromBackupDatabase(ZipArchive archive, List<string> warnings)
    {
        var entry = archive.GetEntry("App_Data/grow-diary.db");
        if (entry is null)
        {
            return null;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "GrowOSRestorePlan_" + Guid.NewGuid().ToString("N"));
        var tempDb = Path.Combine(tempRoot, "grow-diary.db");
        try
        {
            Directory.CreateDirectory(tempRoot);
            entry.ExtractToFile(tempDb, overwrite: true);
            archive.GetEntry("App_Data/grow-diary.db-wal")?.ExtractToFile(tempDb + "-wal", overwrite: true);
            archive.GetEntry("App_Data/grow-diary.db-shm")?.ExtractToFile(tempDb + "-shm", overwrite: true);
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = tempDb,
                Mode = SqliteOpenMode.ReadOnly
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            return ReadAppSetting(connection, DatabaseInitializer.CurrentSchemaAppSettingKey);
        }
        catch
        {
            warnings.Add("Backup-Datenbank konnte nicht fuer die Schema-Pruefung gelesen werden.");
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }



    private static string CreateUniqueBackupFileName(string backupRoot)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : "-" + attempt.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            var fileName = $"grow-os-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}{suffix}.zip";
            if (!System.IO.File.Exists(Path.Combine(backupRoot, fileName)))
            {
                return fileName;
            }
        }

        return "grow-os-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip";
    }


    private static string RunSqliteQuickCheck(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        };

        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        return command.ExecuteScalar()?.ToString() ?? "quick_check returned no result";
    }


    private static void RestoreFileWithRollback(string sourcePath, string targetPath, string rollbackRoot, string rollbackName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var rollbackPath = Path.Combine(rollbackRoot, rollbackName);
        if (System.IO.File.Exists(targetPath))
        {
            System.IO.File.Move(targetPath, rollbackPath, overwrite: true);
        }

        System.IO.File.Copy(sourcePath, targetPath, overwrite: true);
    }


    private static void RestoreOptionalFileWithRollback(string sourcePath, string targetPath, string rollbackRoot, string rollbackName)
    {
        var rollbackPath = Path.Combine(rollbackRoot, rollbackName);
        if (System.IO.File.Exists(targetPath))
        {
            System.IO.File.Move(targetPath, rollbackPath, overwrite: true);
        }

        if (System.IO.File.Exists(sourcePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            System.IO.File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }


    private static void RestoreDirectoryWithRollback(string sourceDirectory, string targetDirectory, string rollbackRoot, string rollbackName)
    {
        var rollbackDirectory = Path.Combine(rollbackRoot, rollbackName);
        if (Directory.Exists(targetDirectory))
        {
            Directory.Move(targetDirectory, rollbackDirectory);
        }

        CopyDirectory(sourceDirectory, targetDirectory);
    }


    /// <summary>Die Namen der Bilddateien, die in eine Sicherung mit Bildern dürfen.</summary>
    private static readonly string[] BackupMediaExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic"];

    private const string BackupUploadsPrefix = "App_Data/uploads/";
    private const string BackupSnapshotsPrefix = "App_Data/snapshots/";

    private static bool IsBackupMediaEntry(string entryName)
        => entryName.StartsWith(BackupUploadsPrefix, StringComparison.OrdinalIgnoreCase)
           || entryName.StartsWith(BackupSnapshotsPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedBackupMediaName(string entryName)
        => BackupMediaExtensions.Contains(Path.GetExtension(entryName), StringComparer.OrdinalIgnoreCase);

    /// <summary>Alle Fotos (Uploads) und Kamerabilder (Tagesbilder) dieser Installation mit ihrem Namen in der Sicherung.</summary>
    private IEnumerable<(string Path, string Entry, long Length)> BackupMediaFiles()
    {
        foreach (var (root, prefix) in new[] { (_paths.UploadRootPath, BackupUploadsPrefix), (_paths.SnapshotsPath, BackupSnapshotsPrefix) })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(root, file).Replace(System.IO.Path.DirectorySeparatorChar, '/');
                var entry = prefix + relative;
                if (IsAllowedBackupMediaName(entry))
                {
                    yield return (file, entry, new FileInfo(file).Length);
                }
            }
        }
    }

    /// <summary>
    /// Die Sicherung entpacken: Bilder in den Zwischenordner im Datenverzeichnis (gleiches Laufwerk wie das Ziel,
    /// damit sie verschoben statt noch einmal kopiert werden), alles andere in den Temp-Ordner.
    /// </summary>
    private static void ExtractBackup(string archivePath, string tempRoot, string stagingRoot)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var tempFull = System.IO.Path.GetFullPath(tempRoot) + System.IO.Path.DirectorySeparatorChar;
        var stagingFull = System.IO.Path.GetFullPath(stagingRoot) + System.IO.Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var name = entry.FullName.Replace('\\', '/');
            string destination;
            string root;
            if (IsBackupMediaEntry(name))
            {
                root = stagingFull;
                destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(stagingRoot, name["App_Data/".Length..]));
            }
            else
            {
                root = tempFull;
                destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(tempRoot, name));
            }

            if (!destination.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Backup enthaelt einen unsicheren Pfad: " + name);
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static void RestoreDirectoryByMove(string sourceDirectory, string targetDirectory, string rollbackRoot, string rollbackName)
    {
        var rollbackDirectory = System.IO.Path.Combine(rollbackRoot, rollbackName);
        if (Directory.Exists(targetDirectory))
        {
            Directory.Move(targetDirectory, rollbackDirectory);
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetDirectory)!);
        Directory.Move(sourceDirectory, targetDirectory);
    }


    private static void RestoreRollbackFiles(string rollbackRoot)
    {
        if (!Directory.Exists(rollbackRoot))
        {
            return;
        }

        var appDataRoot = Directory.GetParent(rollbackRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            return;
        }

        var databasePath = Path.Combine(appDataRoot, "grow-diary.db");
        var rollbackDb = Path.Combine(rollbackRoot, "grow-diary.db");
        var rollbackWal = Path.Combine(rollbackRoot, "grow-diary.db-wal");
        var rollbackShm = Path.Combine(rollbackRoot, "grow-diary.db-shm");
        var rollbackKnowledge = Path.Combine(rollbackRoot, "knowledge");
        var knowledgePath = Path.Combine(appDataRoot, "knowledge");

        RestoreRollbackFile(rollbackDb, databasePath);
        RestoreRollbackFile(rollbackWal, databasePath + "-wal");
        RestoreRollbackFile(rollbackShm, databasePath + "-shm");

        if (Directory.Exists(rollbackKnowledge))
        {
            DeleteDirectoryBestEffort(knowledgePath);
            Directory.Move(rollbackKnowledge, knowledgePath);
        }

        // Fotos und Kamerabilder: lagen sie vor dem Zurückspielen woanders, kommen sie wieder an ihren Platz.
        foreach (var name in new[] { "uploads", "snapshots" })
        {
            var rollbackMedia = Path.Combine(rollbackRoot, name);
            if (Directory.Exists(rollbackMedia))
            {
                var mediaPath = Path.Combine(appDataRoot, name);
                DeleteDirectoryBestEffort(mediaPath);
                Directory.Move(rollbackMedia, mediaPath);
            }
        }
    }

    private static void RestoreRollbackFile(string rollbackPath, string targetPath)
    {
        if (!System.IO.File.Exists(rollbackPath))
        {
            return;
        }

        if (System.IO.File.Exists(targetPath))
        {
            System.IO.File.Delete(targetPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        System.IO.File.Move(rollbackPath, targetPath);
    }


    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var target = Path.Combine(targetDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            System.IO.File.Copy(file, target, overwrite: true);
        }
    }


    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }



    private static void AddIfExists(ZipArchive archive, string sourcePath, string entryName)
    {
        if (!System.IO.File.Exists(sourcePath))
        {
            return;
        }

        var entry = archive.CreateEntry(entryName);
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var destination = entry.Open();
        source.CopyTo(destination);
    }
}
