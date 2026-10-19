using System.IO.Compression;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
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




    /// <summary>Was jeder Restore-Plan sagt — und ein echtes Zurückspielen nicht.</summary>
    private static readonly string[] PlanHinweise =
    [
        "Restore-Plan ist ein Dry-Run. Es wurden keine Dateien geaendert.",
        "Echter Restore ist nur ueber den Restore-Endpunkt mit Safety-Backup, Schema-Pruefung und Integritaetscheck erlaubt.",
    ];

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
        var warnings = new List<string>(PlanHinweise);

        string? backupSchemaVersion = null;
        bool containsDatabase;
        bool containsWal;
        bool containsShm;
        bool containsKnowledge;
        bool containsSecrets;
        bool containsKeys;
        bool containsUploads;
        bool hasUnsafeEntries;
        var mediaCount = 0;
        long mediaBytes = 0;
        var mediaNotImages = false;

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
            // Bilder (Fotos und Kamerabilder) werden eigens gezählt und geprüft; ihre Dateinamen sagen nichts über Geheimnisse.
            containsSecrets = normalizedEntries.Where(entry => !IsBackupMediaEntry(entry)).Any(entry =>
                entry.Equals("App_Data/ha-config.json", StringComparison.OrdinalIgnoreCase)
                || entry.Contains("token", StringComparison.OrdinalIgnoreCase)
                || entry.Contains("secret", StringComparison.OrdinalIgnoreCase));
            containsKeys = normalizedEntries.Any(entry => entry.StartsWith("App_Data/DataProtectionKeys/", StringComparison.OrdinalIgnoreCase));
            foreach (var entry in entries)
            {
                var mediaName = entry.FullName.Replace('\\', '/');
                if (!IsBackupMediaEntry(mediaName))
                {
                    continue;
                }

                mediaCount++;
                mediaBytes += entry.Length;
                if (!IsAllowedBackupMediaName(mediaName))
                {
                    mediaNotImages = true;
                }
            }

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

        var backupValid = containsDatabase && !containsSecrets && !containsKeys && !containsUploads && !hasUnsafeEntries && !mediaNotImages;
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
        if (mediaNotImages)
        {
            blockers.Add("Backup enthaelt bei den Fotos und Kamerabildern Dateien, die keine Bilder sind.");
        }
        if (mediaCount > 0)
        {
            warnings.Add("Die Fotos und Kamerabilder in dieser Installation werden durch die aus der Sicherung ersetzt.");
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
            Warnings: warnings,
            MediaIncluded: mediaCount > 0,
            MediaFileCount: mediaCount,
            MediaBytes: mediaBytes);

        LogSystemAudit("backup", "restore-plan-created", blockers.Count == 0 ? "Restore-Plan erfolgreich erstellt." : "Restore-Plan mit Blockern erstellt.", blockers.Count == 0, relatedFileName: fileName, severity: blockers.Count == 0 ? "info" : "warning");
        return Ok(dto);
    }


    [HttpPost("backup")]
    [ProducesResponseType(typeof(BackupManifestDto), StatusCodes.Status201Created)]
    public ActionResult<BackupManifestDto> CreateBackup([FromQuery] bool bilder = false)
    {
        var backupRoot = _paths.BackupsPath;
        Directory.CreateDirectory(backupRoot);

        var fileName = CreateUniqueBackupFileName(backupRoot);
        var backupPath = Path.Combine(backupRoot, fileName);
        if (System.IO.File.Exists(backupPath))
        {
            System.IO.File.Delete(backupPath);
        }

        var mediaCount = 0;
        long mediaBytes = 0;
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

            // Fotos und Kamerabilder nur auf Wunsch (Haken „mitsichern“): sie machen die Datei groß. JPEG und Co.
            // sind schon gepackt, deshalb ohne weitere Komprimierung, das spart Zeit.
            if (bilder)
            {
                foreach (var (path, entry, length) in BackupMediaFiles())
                {
                    archive.CreateEntryFromFile(path, entry, CompressionLevel.NoCompression);
                    mediaCount++;
                    mediaBytes += length;
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
            ExcludesUploads: !bilder,
            RestoreSupported: true,
            DownloadUrl: downloadUrl,
            IncludesMedia: bilder,
            MediaFileCount: mediaCount,
            MediaBytes: mediaBytes);

        LogSystemAudit("backup", "backup-created", bilder ? $"Backup {fileName} mit {mediaCount} Bildern erstellt." : $"Backup {fileName} erstellt.", true, relatedFileName: fileName);
        return Created(downloadUrl, manifest);
    }



    /// <summary>Wie viele Fotos und Kamerabilder es gibt und wie groß sie zusammen sind — die Zahl hinter dem Haken „mitsichern“.</summary>
    [HttpGet("backup/media-size")]
    [ProducesResponseType(typeof(BackupMediaSizeDto), StatusCodes.Status200OK)]
    public ActionResult<BackupMediaSizeDto> BackupMediaSize()
    {
        var files = BackupMediaFiles().ToList();
        return Ok(new BackupMediaSizeDto(files.Count, files.Sum(file => file.Length)));
    }

    /// <summary>
    /// Größte erlaubte Sicherung, gepackt. Mit Bildern wird sie groß; Bilder sind schon gepackt, die Grenze gilt also
    /// praktisch für Fotos und Kamerabilder zusammen. Davor kann Home Assistant selbst eine kleinere Grenze setzen.
    /// </summary>
    private const long MaxBackupUploadBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>Größte erlaubte Gesamtgröße nach dem Entpacken — Schutz vor Zip-Bomben.</summary>
    private const long MaxBackupUnpackedBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// Eine Sicherung hochladen, die auf einer anderen Installation entstanden ist (Umzug,
    /// neues Add-on). Sie landet unter neuem Namen im Sicherungsordner; geprüft und
    /// zurückgespielt wird danach wie bei jeder anderen Sicherung (<c>restore-plan</c>,
    /// <c>restore</c>). Hier wird nur abgewiesen, was keine Sicherung dieser App sein kann.
    /// </summary>
    [HttpPost("backup/upload")]
    [KeinKiZugriff("Eine hochgeladene Sicherung ersetzt beim Zurückspielen die ganze Datenbank, "
        + "auch die Schlüssel und ihre Stufen. Ein Assistent darf keine fremde Datenbank einschleusen; "
        + "hochladen geht deshalb nur von Hand.")]
    [RequestSizeLimit(MaxBackupUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBackupUploadBytes)]
    [ProducesResponseType(typeof(BackupManifestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BackupManifestDto>> UploadBackup(
        [FromForm] BackupUploadRequest request,
        CancellationToken cancellationToken)
    {
        var file = request.File;
        if (file is null || file.Length == 0)
        {
            return BadRequestError("backup_upload_empty", "Bitte wähle eine Sicherungsdatei (.zip) aus.");
        }

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequestError("backup_upload_not_zip", "Eine Sicherung ist eine .zip-Datei.");
        }

        if (file.Length > MaxBackupUploadBytes)
        {
            return BadRequestError("backup_upload_too_large", "Die Sicherung ist größer als 4 GB. Sichere ohne Fotos und Kamerabilder.");
        }

        var backupRoot = _paths.BackupsPath;
        Directory.CreateDirectory(backupRoot);
        var fileName = CreateUniqueBackupFileName(backupRoot);
        var backupPath = Path.Combine(backupRoot, fileName);
        var tempPath = backupPath + ".upload";

        try
        {
            await using (var target = System.IO.File.Create(tempPath))
            {
                await file.CopyToAsync(target, cancellationToken);
            }

            bool containsDatabase;
            bool containsWal;
            bool containsKnowledge;
            try
            {
                using var archive = ZipFile.OpenRead(tempPath);
                var names = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
                containsDatabase = names.Contains("App_Data/grow-diary.db", StringComparer.OrdinalIgnoreCase);
                containsWal = names.Contains("App_Data/grow-diary.db-wal", StringComparer.OrdinalIgnoreCase);
                containsKnowledge = names.Any(name => name.StartsWith("App_Data/knowledge/", StringComparison.OrdinalIgnoreCase));
                if (archive.Entries.Sum(entry => entry.Length) > MaxBackupUnpackedBytes)
                {
                    System.IO.File.Delete(tempPath);
                    return BadRequestError("backup_upload_too_large", "Die Sicherung ist entpackt größer als 8 GB.");
                }
            }
            catch (InvalidDataException)
            {
                System.IO.File.Delete(tempPath);
                LogSystemAudit("backup", "backup-upload-rejected", "Hochgeladene Datei ist kein gültiges Zip.", false, severity: "warning");
                return BadRequestError("backup_upload_invalid", "Die Datei ist keine gültige Sicherung (kein lesbares Zip).");
            }

            if (!containsDatabase)
            {
                System.IO.File.Delete(tempPath);
                LogSystemAudit("backup", "backup-upload-rejected", "Hochgeladene Sicherung enthält keine Hauptdatenbank.", false, severity: "warning");
                return BadRequestError("backup_upload_no_database", "Die Datei enthält keine Datenbank der App und ist keine Sicherung.");
            }

            System.IO.File.Move(tempPath, backupPath);

            var info = new FileInfo(backupPath);
            var downloadUrl = $"/api/system/backup/{Uri.EscapeDataString(fileName)}";
            var manifest = new BackupManifestDto(
                BackupSchema: "grow-os.backup.v1",
                CreatedAtUtc: DateTime.UtcNow,
                FileName: fileName,
                SizeBytes: info.Length,
                IncludesDatabase: containsDatabase,
                IncludesWal: containsWal,
                IncludesKnowledgeRuntimeCopy: containsKnowledge,
                ExcludesSecrets: true,
                ExcludesHomeAssistantConfig: true,
                ExcludesDataProtectionKeys: true,
                ExcludesUploads: true,
                RestoreSupported: true,
                DownloadUrl: downloadUrl);

            LogSystemAudit("backup", "backup-uploaded", $"Sicherung {fileName} hochgeladen.", true, relatedFileName: fileName);
            return Created(downloadUrl, manifest);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                System.IO.File.Delete(tempPath);
            }
        }
    }

    [HttpPost("backup/{fileName}/restore")]
    [KeinKiZugriff("Eine Sicherung enthält die Schlüssel und ihre Stufen. Über einen Schlüssel "
        + "zurückgespielt, holte sich ein heruntergestufter Schlüssel seine alten Stufen zurück "
        + "(Prüfer A-003, 03.10.2026). Zurückspielen ist deshalb nur von Hand möglich.")]
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
        // Ersetzt das Zurückspielen auch die Bilder, gehört der jetzige Bestand mit in die Rückfall-Sicherung.
        var safetyBackupResult = CreateBackup(bilder: plan.MediaIncluded);
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
        var stagingRoot = Path.Combine(_paths.DataRootPath, "restore-staging-" + restoreId);
        var restoredKnowledgeFiles = new List<string>();
        // Die Hinweise des Plans gelten dem Trockenlauf — im Ergebnis eines echten
        // Zurückspielens stand sonst „Es wurden keine Dateien geaendert" (Prüfer, 03.10.2026).
        var warnings = plan.Warnings.Except(PlanHinweise).ToList();

        try
        {
            // Fork AI (A-003, Prüfer 03.10.2026): Der Zugriff für KI-Assistenten
            // reist nicht mit der Sicherung zurück — sonst wird ein gelöschter,
            // vielleicht verratener Schlüssel wieder gültig.
            var kiZustand = KiZustandBeimZurueckspielen.Lesen(_paths.DatabasePath);

            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(rollbackRoot);
            Directory.CreateDirectory(stagingRoot);
            ExtractBackup(backupPath, tempRoot, stagingRoot);

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

            // Fotos und Kamerabilder: nur, wenn die Sicherung welche enthält. Sie werden verschoben statt kopiert
            // (der Zwischenordner liegt im Datenverzeichnis); die vorigen kommen in die Rückfall-Kopie.
            var mediaRestored = false;
            foreach (var (name, target) in new[] { ("uploads", _paths.UploadRootPath), ("snapshots", _paths.SnapshotsPath) })
            {
                var staged = Path.Combine(stagingRoot, name);
                if (Directory.Exists(staged))
                {
                    RestoreDirectoryByMove(staged, target, rollbackRoot, name);
                    mediaRestored = true;
                }
            }

            if (mediaRestored)
            {
                warnings.Add($"{plan.MediaFileCount} Fotos und Kamerabilder zurückgespielt.");
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
            // Vor dem Wegräumen der Rollback-Kopie: scheitert es, fängt der catch
            // und spielt die vorige Datei zurück.
            KiZustandBeimZurueckspielen.Schreiben(_paths.DatabasePath, kiZustand);
            SqliteConnection.ClearAllPools();

            DeleteDirectoryBestEffort(rollbackRoot);

            /* Fork AI (03.10.2026, offene Punkte B8): Neustart. Rund acht Stellen
               halten den Stand der vorigen Datenbank im Speicher (Grow-Plan,
               Wissensbasis, Wochenwerte, Schema-Merker, Start-Übernahmen) — nur
               ein Neustart erfasst alle, auch die, die später dazukommen. */
            var neustartGeplant = _neustart?.Planen($"Sicherung {fileName} zurückgespielt") ?? false;
            warnings.Add(neustartGeplant
                ? "Cultivate Control startet in wenigen Sekunden neu, damit alles im Speicher zur zurückgespielten Sicherung passt."
                : "Cultivate Control bitte jetzt neu starten: bis dahin halten Grow-Plan, Wissensbasis und Wochenwerte im Speicher den Stand vor dem Zurückspielen.");

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
                Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                NeustartGeplant: neustartGeplant);

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
            DeleteDirectoryBestEffort(stagingRoot);
            DeleteDirectoryBestEffort(rollbackRoot);
        }
    }



    [HttpGet("backup/{fileName}")]
    [KeinKiZugriff("In der Sicherungsdatei kann das Home-Assistant-Token im Klartext stehen "
        + "(ausserhalb des Add-on-Betriebs). Ein Schlüssel mit Verwaltung las es so heraus, obwohl "
        + "die HA-Verbindung selbst gesperrt ist (Prüfer A-003, 03.10.2026).")]
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
