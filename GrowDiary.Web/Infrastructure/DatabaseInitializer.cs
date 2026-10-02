using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GrowDiary.Web.Infrastructure;

public sealed partial class DatabaseInitializer
{
    public const string CurrentSchemaVersion = "backend-core.v0.18-candidate";
    public const string CurrentSchemaAppSettingKey = "backend:schemaVersion";
    public const string LastMigrationUtcAppSettingKey = "backend:lastMigrationUtc";

    public static readonly IReadOnlyList<SchemaMigrationDescriptor> RequiredMigrations = new[]
    {
        new SchemaMigrationDescriptor("0001-core-schema", "Core schema baseline", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0002-zero-tent-startup", "Zero-tent startup and explicit test data", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0003-tent-aggregate", "Tent aggregate details and archive/delete rules", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0004-hydro-setup-aggregate", "DWC/RDWC HydroSetup aggregate", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0005-grow-hydro-setup-link", "New grows require HydroSetup", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0006-hardware-hydro-setup-link", "Hardware linked to HydroSetups", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0007-addback-volume-logs", "HydroSetup volume, Addback and Changeout logs", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0008-export-backup-hardening", "Grow export, backup validation and release readiness", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0009-security-guardrails", "Local-only admin and remote guardrails", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0010-import-readiness", "Export integrity and import validation preflight", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0011-upgrade-preflight", "Migration status and upgrade preflight", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0012-restore-plan", "Backup restore dry-run and restore readiness", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0013-grow-import-plan", "Grow export import planning dry-run", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0014-system-audit-events", "System audit events for critical backend operations", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0015-api-error-format", "Uniform API error contract for backend endpoints", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0016-legacy-mvc-containment", "Legacy MVC endpoint containment for backup/export/camera routes", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0017-product-api-remote-guard", "Product API remote access guardrails", CurrentSchemaVersion),
        new SchemaMigrationDescriptor("0018-migration-engine-foundation", "Idempotent migration engine foundation and destructive migration guardrails", CurrentSchemaVersion, RequiresBackup: true),
        new SchemaMigrationDescriptor("0019-grow-run-snapshots", "Immutable grow tent and HydroSetup snapshots for comparison-safe exports", CurrentSchemaVersion)
    };

    private readonly AppPaths _paths;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(AppPaths paths, ILogger<DatabaseInitializer> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.DatabasePath)!);
        Directory.CreateDirectory(_paths.UploadRootPath);
        // VOR EnsureSchema lesen: danach hat jede Datenbank die Spalte.
        var growsVorDerZeltSpalte = GrowsHabenNochKeineZeltSpalte();
        DropLegacyTentSchemaIfNeeded();
        EnsureSchema();
        if (growsVorDerZeltSpalte)
        {
            AutoAssignExistingGrowsToTents();
        }
    }

}

public sealed record SchemaMigrationDescriptor(
    string Id,
    string Name,
    string RequiredForSchemaVersion,
    bool IsDestructive = false,
    bool RequiresBackup = false,
    string? Checksum = null);
