namespace GrowDiary.Web.Api.Contracts;

public sealed record GrowExportDto(
    string SchemaVersion,
    string ExportId,
    DateTime ExportedAtUtc,
    bool Anonymized,
    string IntegrityHash,
    GrowExportSectionCountsDto SectionCounts,
    GrowDetailDto Grow,
    TentDto? TentSnapshot,
    HydroSetupDto? HydroSetupSnapshot,
    IReadOnlyList<MeasurementDto> Measurements,
    IReadOnlyList<JournalEntryDto> JournalEntries,
    IReadOnlyList<GrowTaskDto> Tasks,
    IReadOnlyList<HardwareItemDto> HardwareItems,
    HarvestDto? Harvest,
    IReadOnlyList<AddbackLogDto> AddbackLogs,
    IReadOnlyList<ChangeoutDto> Changeouts,
    IReadOnlyList<PhotoAssetDto> Photos,
    IReadOnlyList<string> Warnings);

public sealed record GrowExportSectionCountsDto(
    int Measurements,
    int JournalEntries,
    int Tasks,
    int HardwareItems,
    int AddbackLogs,
    int Changeouts,
    int Photos);

public sealed record GrowExportValidationDto(
    string ValidationSchema,
    DateTime CheckedAtUtc,
    string? ExportSchemaVersion,
    string? ExportId,
    bool IsValid,
    bool IntegrityHashValid,
    bool SectionCountsValid,
    bool ContainsPotentialSecrets,
    GrowExportSectionCountsDto? DeclaredSectionCounts,
    GrowExportSectionCountsDto? ActualSectionCounts,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);


public sealed record GrowImportPlanDto(
    string ImportPlanSchema,
    DateTime CheckedAtUtc,
    bool ExportValid,
    bool ImportSupported,
    bool WouldModifyDatabase,
    bool IsAnonymized,
    string? ExportId,
    string? ExportSchemaVersion,
    string? IntegrityHash,
    GrowImportPlanSourceDto? Source,
    GrowExportSectionCountsDto? SectionCounts,
    IReadOnlyList<GrowImportPlanItemDto> PlannedItems,
    IReadOnlyList<GrowImportPlanConflictDto> Conflicts,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

public sealed record GrowImportPlanSourceDto(
    int? OriginalGrowId,
    string? GrowName,
    string? TentName,
    string? HydroSetupName,
    DateTime? ExportedAtUtc);

public sealed record GrowImportPlanItemDto(
    string Kind,
    string Action,
    int Count,
    string? Notes);

public sealed record GrowImportPlanConflictDto(
    string Kind,
    string Severity,
    string Message);

public sealed record GrowImportResultDto(
    string ImportSchema,
    DateTime ImportedAtUtc,
    bool Success,
    string ExportId,
    int ImportedGrowId,
    string ImportedGrowName,
    string SafetyBackupFileName,
    string SafetyBackupDownloadUrl,
    int ImportedMeasurements,
    int ImportedJournalEntries,
    int ImportedTasks,
    int ImportedAddbackLogs,
    int ImportedChangeouts,
    int ImportedHarvestEntries,
    int SkippedHardwareItems,
    int SkippedPhotos,
    IReadOnlyList<string> Warnings);

/// <param name="BauKennung">
/// Die Kennung des Builds, aus dem die laufende Programmdatei stammt.
///
/// <para><b>Warum das hier steht.</b> Am 24.08.2026 habe ich eine Aenderung
/// gegen eine App gemessen, die noch aus einer frueheren Sitzung lief — der
/// Port war belegt, der Start meldete trotzdem Erfolg, und eine halbe Stunde
/// Messung galt dem falschen Stand. Die Ergebnis-Regel in CLAUDE.md sagt „nur
/// gegen den gebauten Stand"; ohne diese Zahl laesst sich das nicht
/// nachpruefen, sondern nur glauben.</para>
///
/// <para><b>Es ist ausdruecklich KEIN Datum.</b> Bei einem deterministischen
/// Build schreibt der Compiler dort einen Hash ueber die Eingaben, der als
/// Zeitstempel gelesen im Jahr 2050 landet. Ein Feld namens „gebaut am" mit
/// einem Datum aus der Zukunft waere schlimmer als gar keins — verglichen wird
/// auf <b>Gleichheit</b>, nie auf frueher/spaeter.</para>
/// </param>
public sealed record BackendHealthDto(
    string AppName,
    string BackendSchema,
    DateTime CheckedAtUtc,
    int TentCount,
    int HydroSetupCount,
    int GrowCount,
    bool ZeroTentStartupSupported,
    IReadOnlyList<string> Capabilities,
    string BauKennung);

public sealed record BackupManifestDto(
    string BackupSchema,
    DateTime CreatedAtUtc,
    string FileName,
    long SizeBytes,
    bool IncludesDatabase,
    bool IncludesWal,
    bool IncludesKnowledgeRuntimeCopy,
    bool ExcludesSecrets,
    bool ExcludesHomeAssistantConfig,
    bool ExcludesDataProtectionKeys,
    bool ExcludesUploads,
    bool RestoreSupported,
    string DownloadUrl,
    IReadOnlyList<string>? RemovedOldBackups = null);

/// <summary>Eine Sicherung im Backup-Ordner.</summary>
/// <param name="Kind"><c>sicherung</c> (von Hand oder vor einer Wiederherstellung) oder <c>import</c> (vor einem Grow-Import).</param>
public sealed record BackupListItemDto(
    string FileName,
    string Kind,
    long SizeBytes,
    DateTime ModifiedAtUtc,
    string DownloadUrl);

/// <summary>Inhalt des Backup-Ordners.</summary>
public sealed record BackupListDto(
    int KeptPerKind,
    long TotalBytes,
    IReadOnlyList<BackupListItemDto> Backups);

public sealed record BackendReleaseReadinessDto(
    string Status,
    string BackendSchema,
    DateTime CheckedAtUtc,
    IReadOnlyList<ReleaseReadinessCheckDto> Checks,
    IReadOnlyList<string> CompletedFoundations,
    IReadOnlyList<string> RemainingBeforeV1);

public sealed record ReleaseReadinessCheckDto(
    string Key,
    string Status,
    string Message);

public sealed record DatabaseStatusDto(
    string ExpectedSchemaVersion,
    string? StoredSchemaVersion,
    DateTime CheckedAtUtc,
    bool DatabaseExists,
    bool IsCurrent,
    IReadOnlyList<string> RequiredTablesPresent,
    IReadOnlyList<string> MissingRequiredTables,
    IReadOnlyList<string> RequiredColumnsPresent,
    IReadOnlyList<string> MissingRequiredColumns,
    IReadOnlyList<string> Warnings);

public sealed record BackupValidationDto(
    string BackupSchema,
    string FileName,
    DateTime CheckedAtUtc,
    bool Exists,
    bool IsValid,
    bool ContainsDatabase,
    bool ContainsWal,
    bool ContainsSecrets,
    bool ContainsDataProtectionKeys,
    bool ContainsUploads,
    int EntryCount,
    IReadOnlyList<string> Warnings);


public sealed record BackupRestorePlanDto(
    string RestorePlanSchema,
    string FileName,
    DateTime CheckedAtUtc,
    bool BackupValid,
    bool DatabaseIncluded,
    bool WalIncluded,
    bool ShmIncluded,
    bool KnowledgeIncluded,
    bool SchemaCompatible,
    bool RestoreSupported,
    bool RequiresManualStop,
    bool WouldOverwriteExistingDatabase,
    string? BackupSchemaVersion,
    string CurrentSchemaVersion,
    IReadOnlyList<BackupRestorePlanFileDto> Files,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

public sealed record BackupRestorePlanFileDto(
    string EntryName,
    string RelativeTargetPath,
    string Kind,
    long SizeBytes,
    bool WouldOverwrite);

public sealed record BackupRestoreResultDto(
    string RestoreSchema,
    string FileName,
    DateTime RestoredAtUtc,
    bool Success,
    string SafetyBackupFileName,
    string SafetyBackupDownloadUrl,
    string DatabaseTargetPath,
    bool DatabaseRestored,
    bool WalRestored,
    bool ShmRestored,
    int KnowledgeFileCount,
    IReadOnlyList<string> RestoredKnowledgeFiles,
    IReadOnlyList<string> Warnings);

public sealed record SchemaMigrationStatusDto(
    string MigrationSchema,
    string CurrentSchemaVersion,
    string? StoredSchemaVersion,
    DateTime CheckedAtUtc,
    bool MigrationTableExists,
    bool IsCurrent,
    IReadOnlyList<AppliedSchemaMigrationDto> AppliedMigrations,
    IReadOnlyList<PendingSchemaMigrationDto> PendingMigrations,
    IReadOnlyList<string> Warnings);

public sealed record AppliedSchemaMigrationDto(
    string Id,
    string Name,
    string RequiredForSchemaVersion,
    DateTime? AppliedAtUtc);

public sealed record PendingSchemaMigrationDto(
    string Id,
    string Name,
    string RequiredForSchemaVersion);

public sealed record UpgradePreflightDto(
    string PreflightSchema,
    DateTime CheckedAtUtc,
    bool IsSafeToUpgrade,
    bool DatabaseCurrent,
    bool BackupCreated,
    bool BackupValid,
    string? BackupFileName,
    string? BackupDownloadUrl,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    DatabaseStatusDto DatabaseStatus,
    SchemaMigrationStatusDto MigrationStatus,
    BackupValidationDto? BackupValidation);
