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
    [HttpGet("backup/{fileName}/validate")]
    [ProducesResponseType(typeof(BackupValidationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<BackupValidationDto> ValidateBackup(string fileName)
    {
        if (!IsSafeBackupFileName(fileName))
        {
            return BadRequestError("invalid_backup_file", "Backup-Dateiname ist ungueltig.");
        }

        var backupPath = ResolveBackupPath(fileName);
        if (backupPath is null || !System.IO.File.Exists(backupPath))
        {
            return NotFoundError("backup_not_found", "Backup wurde nicht gefunden.");
        }

        var warnings = new List<string>();
        using var archive = ZipFile.OpenRead(backupPath);
        var entries = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
        var containsDatabase = entries.Contains("App_Data/grow-diary.db", StringComparer.OrdinalIgnoreCase);
        var containsWal = entries.Contains("App_Data/grow-diary.db-wal", StringComparer.OrdinalIgnoreCase);
        var containsSecrets = entries.Any(entry =>
            entry.Equals("App_Data/ha-config.json", StringComparison.OrdinalIgnoreCase)
            || entry.Contains("token", StringComparison.OrdinalIgnoreCase)
            || entry.Contains("secret", StringComparison.OrdinalIgnoreCase));
        var containsKeys = entries.Any(entry => entry.StartsWith("App_Data/DataProtectionKeys/", StringComparison.OrdinalIgnoreCase));
        var containsUploads = entries.Any(entry => entry.StartsWith("wwwroot/uploads/", StringComparison.OrdinalIgnoreCase));

        if (!containsDatabase)
        {
            warnings.Add("Backup enthält keine Hauptdatenbank.");
        }
        if (containsSecrets)
        {
            warnings.Add("Backup enthält potenzielle Secrets.");
        }
        if (containsKeys)
        {
            warnings.Add("Backup enthält DataProtectionKeys.");
        }
        if (containsUploads)
        {
            warnings.Add("Backup enthält Upload-Dateien.");
        }

        var dto = new BackupValidationDto(
            BackupSchema: "grow-os.backup.v1",
            FileName: fileName,
            CheckedAtUtc: DateTime.UtcNow,
            Exists: true,
            IsValid: containsDatabase && !containsSecrets && !containsKeys && !containsUploads,
            ContainsDatabase: containsDatabase,
            ContainsWal: containsWal,
            ContainsSecrets: containsSecrets,
            ContainsDataProtectionKeys: containsKeys,
            ContainsUploads: containsUploads,
            EntryCount: entries.Count,
            Warnings: warnings);

        LogSystemAudit("backup", "backup-validated", dto.IsValid ? "Backup erfolgreich validiert." : "Backup-Validierung mit Warnungen/Blockern abgeschlossen.", dto.IsValid, relatedFileName: fileName, severity: dto.IsValid ? "info" : "warning");
        return Ok(dto);
    }




    [HttpPost("backup/{fileName}/restore-plan")]
    [ProducesResponseType(typeof(BackupRestorePlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<BackupRestorePlanDto> RestorePlan(string fileName)
    {
        if (!IsSafeBackupFileName(fileName))
        {
            return BadRequestError("invalid_backup_file", "Backup-Dateiname ist ungueltig.");
        }

        var backupPath = ResolveBackupPath(fileName);
        if (backupPath is null || !System.IO.File.Exists(backupPath))
        {
            return NotFoundError("backup_not_found", "Backup wurde nicht gefunden.");
        }

        var files = new List<BackupRestorePlanFileDto>();
        var blockers = new List<string>();
        var warnings = new List<string>
        {
            "Restore-Plan ist ein Dry-Run. Es wurden keine Dateien geaendert.",
            "Echter Restore ist nur ueber den Restore-Endpunkt mit Safety-Backup, Schema-Pruefung und Integritaetscheck erlaubt."
        };

        string? backupSchemaVersion = null;
        bool containsDatabase;
        bool containsWal;
        bool containsShm;
        bool containsKnowledge;
        bool containsSecrets;
        bool containsKeys;
        bool containsUploads;
        bool hasUnsafeEntries;

        using (var archive = ZipFile.OpenRead(backupPath))
        {
            var entries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToArray();
            var normalizedEntries = entries
                .Select(entry => entry.FullName.Replace('\\', '/'))
                .ToArray();

            containsDatabase = normalizedEntries.Contains("App_Data/grow-diary.db", StringComparer.OrdinalIgnoreCase);
            containsWal = normalizedEntries.Contains("App_Data/grow-diary.db-wal", StringComparer.OrdinalIgnoreCase);
            containsShm = normalizedEntries.Contains("App_Data/grow-diary.db-shm", StringComparer.OrdinalIgnoreCase);
            containsKnowledge = normalizedEntries.Any(entry => entry.StartsWith("App_Data/knowledge/", StringComparison.OrdinalIgnoreCase));
            containsSecrets = normalizedEntries.Any(entry =>
                entry.Equals("App_Data/ha-config.json", StringComparison.OrdinalIgnoreCase)
                || entry.Contains("token", StringComparison.OrdinalIgnoreCase)
                || entry.Contains("secret", StringComparison.OrdinalIgnoreCase));
            containsKeys = normalizedEntries.Any(entry => entry.StartsWith("App_Data/DataProtectionKeys/", StringComparison.OrdinalIgnoreCase));
            containsUploads = normalizedEntries.Any(entry => entry.StartsWith("wwwroot/uploads/", StringComparison.OrdinalIgnoreCase));
            hasUnsafeEntries = normalizedEntries.Any(IsUnsafeZipEntryName);

            foreach (var entry in entries)
            {
                var normalized = entry.FullName.Replace('\\', '/');
                var kind = ResolveRestoreEntryKind(normalized);
                if (kind is null)
                {
                    continue;
                }

                files.Add(new BackupRestorePlanFileDto(
                    EntryName: normalized,
                    RelativeTargetPath: normalized,
                    Kind: kind,
                    SizeBytes: entry.Length,
                    WouldOverwrite: WouldOverwriteRestoreTarget(normalized)));
            }

            if (containsDatabase)
            {
                backupSchemaVersion = ReadSchemaVersionFromBackupDatabase(archive, warnings);
            }
        }

        var backupValid = containsDatabase && !containsSecrets && !containsKeys && !containsUploads && !hasUnsafeEntries;
        var schemaCompatible = containsDatabase
            && !string.IsNullOrWhiteSpace(backupSchemaVersion)
            && string.Equals(backupSchemaVersion, DatabaseInitializer.CurrentSchemaVersion, StringComparison.Ordinal);

        if (!containsDatabase)
        {
            blockers.Add("Backup enthaelt keine Hauptdatenbank.");
        }
        if (containsSecrets)
        {
            blockers.Add("Backup enthaelt potenzielle Secrets.");
        }
        if (containsKeys)
        {
            blockers.Add("Backup enthaelt DataProtectionKeys.");
        }
        if (containsUploads)
        {
            blockers.Add("Backup enthaelt Upload-Dateien und ist nicht restore-ready.");
        }
        if (hasUnsafeEntries)
        {
            blockers.Add("Backup enthaelt unsichere Pfade.");
        }
        if (containsDatabase && !schemaCompatible)
        {
            blockers.Add("Backup-Schema ist nicht mit der aktuellen Backend-Version kompatibel.");
        }

        var dto = new BackupRestorePlanDto(
            RestorePlanSchema: "grow-os.restore-plan.v1",
            FileName: fileName,
            CheckedAtUtc: DateTime.UtcNow,
            BackupValid: backupValid,
            DatabaseIncluded: containsDatabase,
            WalIncluded: containsWal,
            ShmIncluded: containsShm,
            KnowledgeIncluded: containsKnowledge,
            SchemaCompatible: schemaCompatible,
            RestoreSupported: blockers.Count == 0 && backupValid && schemaCompatible,
            RequiresManualStop: false,
            WouldOverwriteExistingDatabase: System.IO.File.Exists(_paths.DatabasePath),
            BackupSchemaVersion: backupSchemaVersion,
            CurrentSchemaVersion: DatabaseInitializer.CurrentSchemaVersion,
            Files: files,
            Blockers: blockers,
            Warnings: warnings);

        LogSystemAudit("backup", "restore-plan-created", blockers.Count == 0 ? "Restore-Plan erfolgreich erstellt." : "Restore-Plan mit Blockern erstellt.", blockers.Count == 0, relatedFileName: fileName, severity: blockers.Count == 0 ? "info" : "warning");
        return Ok(dto);
    }


    [HttpPost("backup")]
    [ProducesResponseType(typeof(BackupManifestDto), StatusCodes.Status201Created)]
    public ActionResult<BackupManifestDto> CreateBackup()
    {
        var backupRoot = _paths.BackupsPath;
        Directory.CreateDirectory(backupRoot);

        var fileName = CreateUniqueBackupFileName(backupRoot);
        var backupPath = Path.Combine(backupRoot, fileName);
        if (System.IO.File.Exists(backupPath))
        {
            System.IO.File.Delete(backupPath);
        }

        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            AddIfExists(archive, _paths.DatabasePath, "App_Data/grow-diary.db");
            AddIfExists(archive, _paths.DatabasePath + "-wal", "App_Data/grow-diary.db-wal");
            AddIfExists(archive, _paths.DatabasePath + "-shm", "App_Data/grow-diary.db-shm");

            var knowledgeRoot = _paths.KnowledgeDataPath;
            if (Directory.Exists(knowledgeRoot))
            {
                foreach (var file in Directory.EnumerateFiles(knowledgeRoot, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(knowledgeRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                    archive.CreateEntryFromFile(file, $"App_Data/knowledge/{relative}");
                }
            }
        }

        var info = new FileInfo(backupPath);
        var downloadUrl = $"/api/system/backup/{Uri.EscapeDataString(fileName)}";
        var manifest = new BackupManifestDto(
            BackupSchema: "grow-os.backup.v1",
            CreatedAtUtc: DateTime.UtcNow,
            FileName: fileName,
            SizeBytes: info.Length,
            IncludesDatabase: System.IO.File.Exists(_paths.DatabasePath),
            IncludesWal: System.IO.File.Exists(_paths.DatabasePath + "-wal"),
            IncludesKnowledgeRuntimeCopy: Directory.Exists(_paths.KnowledgeDataPath),
            ExcludesSecrets: true,
            ExcludesHomeAssistantConfig: true,
            ExcludesDataProtectionKeys: true,
            ExcludesUploads: true,
            RestoreSupported: true,
            DownloadUrl: downloadUrl);

        LogSystemAudit("backup", "backup-created", $"Backup {fileName} erstellt.", true, relatedFileName: fileName);
        return Created(downloadUrl, manifest);
    }



    [HttpPost("backup/{fileName}/restore")]
    [ProducesResponseType(typeof(BackupRestoreResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status500InternalServerError)]
    public ActionResult<BackupRestoreResultDto> RestoreBackup(string fileName)
    {
        if (!IsSafeBackupFileName(fileName))
        {
            return BadRequestError("invalid_backup_file", "Backup-Dateiname ist ungueltig.");
        }

        var backupPath = ResolveBackupPath(fileName);
        if (backupPath is null || !System.IO.File.Exists(backupPath))
        {
            return NotFoundError("backup_not_found", "Backup wurde nicht gefunden.");
        }

        BackupRestorePlanDto plan;
        var planResult = RestorePlan(fileName);
        if (planResult.Result is OkObjectResult ok && ok.Value is BackupRestorePlanDto dto)
        {
            plan = dto;
        }
        else
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiErrorFactory.ServerError("restore_plan_failed", "Restore-Plan konnte vor dem Restore nicht erstellt werden.", HttpContext?.TraceIdentifier));
        }

        if (plan.Blockers.Count > 0 || !plan.BackupValid || !plan.SchemaCompatible || !plan.RestoreSupported)
        {
            LogSystemAudit("backup", "restore-blocked", "Restore wurde durch Preflight-Blocker verhindert.", false, relatedFileName: fileName, severity: "warning");
            return BadRequestError("restore_blocked", "Restore wurde blockiert: " + string.Join(" ", plan.Blockers.DefaultIfEmpty("Backup ist nicht restore-ready.")));
        }

        BackupManifestDto safetyBackup;
        var safetyBackupResult = CreateBackup();
        if (safetyBackupResult.Result is CreatedResult created && created.Value is BackupManifestDto manifest)
        {
            safetyBackup = manifest;
        }
        else
        {
            LogSystemAudit("backup", "restore-safety-backup-failed", "Restore wurde abgebrochen, weil kein Safety-Backup erstellt werden konnte.", false, relatedFileName: fileName, severity: "error");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiErrorFactory.ServerError("restore_safety_backup_failed", "Safety-Backup konnte vor dem Restore nicht erstellt werden.", HttpContext?.TraceIdentifier));
        }

        var restoreId = Guid.NewGuid().ToString("N");
        var tempRoot = Path.Combine(Path.GetTempPath(), "GrowOSRestore_" + restoreId);
        var rollbackRoot = Path.Combine(_paths.DataRootPath, "restore-rollback-" + restoreId);
        var restoredKnowledgeFiles = new List<string>();
        var warnings = new List<string>(plan.Warnings);

        try
        {
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(rollbackRoot);
            ZipFile.ExtractToDirectory(backupPath, tempRoot);

            var extractedDb = Path.Combine(tempRoot, "App_Data", "grow-diary.db");
            var extractedWal = extractedDb + "-wal";
            var extractedShm = extractedDb + "-shm";

            if (!System.IO.File.Exists(extractedDb))
            {
                throw new InvalidOperationException("Backup enthaelt keine extrahierbare Hauptdatenbank.");
            }

            var integrityResult = RunSqliteQuickCheck(extractedDb);
            if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Backup-Datenbank hat den SQLite-Integritaetscheck nicht bestanden: " + integrityResult);
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            Directory.CreateDirectory(Path.GetDirectoryName(_paths.DatabasePath)!);
            RestoreFileWithRollback(extractedDb, _paths.DatabasePath, rollbackRoot, "grow-diary.db");
            RestoreOptionalFileWithRollback(extractedWal, _paths.DatabasePath + "-wal", rollbackRoot, "grow-diary.db-wal");
            RestoreOptionalFileWithRollback(extractedShm, _paths.DatabasePath + "-shm", rollbackRoot, "grow-diary.db-shm");

            var extractedKnowledgeRoot = Path.Combine(tempRoot, "App_Data", "knowledge");
            if (Directory.Exists(extractedKnowledgeRoot))
            {
                var targetKnowledgeRoot = _paths.KnowledgeDataPath;
                RestoreDirectoryWithRollback(extractedKnowledgeRoot, targetKnowledgeRoot, rollbackRoot, "knowledge");
                restoredKnowledgeFiles.AddRange(
                    Directory.EnumerateFiles(extractedKnowledgeRoot, "*", SearchOption.AllDirectories)
                        .Select(file => "App_Data/knowledge/" + Path.GetRelativePath(extractedKnowledgeRoot, file).Replace(Path.DirectorySeparatorChar, '/'))
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            }

            /* Fork AI (02.10.2026): die Schema-Ergänzungen SOFORT, nicht erst beim
               Neustart. Stammt die Sicherung aus einer älteren Fassung, fehlen ihr
               Spalten und Tabellen, die der DatabaseInitializer beim Start
               nachrüstet — bis dahin scheiterte jede Abfrage, die sie braucht
               („no such column"). Derselbe Aufruf wie in Program.cs, keine eigene
               Liste. Noch VOR dem Wegräumen der Rollback-Kopie: scheitert er, fängt
               der catch unten und spielt die vorige Datei zurück, statt eine
               halb nachgerüstete Datenbank stehen zu lassen. */
            SqliteConnection.ClearAllPools();
            _schema.Initialize();

            DeleteDirectoryBestEffort(rollbackRoot);

            var result = new BackupRestoreResultDto(
                RestoreSchema: "grow-os.backup-restore.v1",
                FileName: fileName,
                RestoredAtUtc: DateTime.UtcNow,
                Success: true,
                SafetyBackupFileName: safetyBackup.FileName,
                SafetyBackupDownloadUrl: safetyBackup.DownloadUrl,
                DatabaseTargetPath: "App_Data/grow-diary.db",
                DatabaseRestored: true,
                WalRestored: System.IO.File.Exists(extractedWal),
                ShmRestored: System.IO.File.Exists(extractedShm),
                KnowledgeFileCount: restoredKnowledgeFiles.Count,
                RestoredKnowledgeFiles: restoredKnowledgeFiles,
                Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

            LogSystemAudit("backup", "backup-restored", $"Backup {fileName} wiederhergestellt. Safety-Backup: {safetyBackup.FileName}.", true, relatedFileName: fileName, severity: "warning");
            return Ok(result);
        }
        catch (Exception ex)
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                RestoreRollbackFiles(rollbackRoot);
            }
            catch
            {
                // Best-effort rollback only. Safety backup still exists.
            }

            LogSystemAudit("backup", "restore-failed", "Restore fehlgeschlagen: " + ex.Message, false, relatedFileName: fileName, severity: "error");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiErrorFactory.ServerError("restore_failed", "Restore ist fehlgeschlagen. Safety-Backup wurde erstellt: " + safetyBackup.FileName + ". Fehler: " + ex.Message, HttpContext?.TraceIdentifier));
        }
        finally
        {
            // Hinter dem Pfad liegt jetzt eine andere Datei — erfolgreich
            // zurueckgespielt oder per Rollback wiederhergestellt. Repositories,
            // die ihre Tabellen selbst anlegen (Kosten, Geraete), merken sich das
            // je Pfad; ohne dieses Vergessen fehlten ihre Tabellen bis zum
            // Neustart, wenn das Backup aus einer Fassung ohne sie stammt.
            EigenesSchema.Vergessen(_paths.DatabasePath);
            DeleteDirectoryBestEffort(tempRoot);
            DeleteDirectoryBestEffort(rollbackRoot);
        }
    }



    [HttpGet("backup/{fileName}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult DownloadBackup(string fileName)
    {
        if (!IsSafeBackupFileName(fileName))
        {
            return BadRequestError("invalid_backup_file", "Backup-Dateiname ist ungueltig.");
        }

        var fullPath = ResolveBackupPath(fileName);
        if (fullPath is null || !System.IO.File.Exists(fullPath))
        {
            return NotFoundError("backup_not_found", "Backup wurde nicht gefunden.");
        }

        LogSystemAudit("backup", "backup-downloaded", $"Backup {fileName} heruntergeladen.", true, relatedFileName: fileName);
        return PhysicalFile(fullPath, "application/zip", fileName);
    }



}
