using System.IO.Compression;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

public sealed class SystemApiControllerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly AppPaths _paths;
    private readonly GrowRepository _repository;
    private readonly SystemApiController _controller;
    private readonly SystemAuditRepository _auditRepository;

    public SystemApiControllerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GrowSystemApiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _dbPath = Path.Combine(_tempRoot, "App_Data", "grow-diary.db");
        Environment.SetEnvironmentVariable("GROWDIARY_DB_PATH", _dbPath);
        _paths = new AppPaths(_tempRoot);
        TestDatabase.Initialize(_paths);
        _repository = new GrowRepository(_paths);
        _auditRepository = new SystemAuditRepository(_paths);
        _controller = new SystemApiController(_paths, _repository, _auditRepository);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GROWDIARY_DB_PATH", null);
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void ReleaseReadiness_ReturnsBackendV19CandidateAndRemainingV1Items()
    {
        var result = _controller.ReleaseReadiness();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BackendReleaseReadinessDto>(ok.Value);
        Assert.Equal("backend.v0.20-ready-not-v1.0", dto.Status);
        Assert.Contains(dto.CompletedFoundations, value => value == "zero-tent-startup");
        Assert.Contains(dto.CompletedFoundations, value => value == "grow-export-v1");
        Assert.Contains(dto.CompletedFoundations, value => value == "api-contract-manifest");
        Assert.Contains(dto.CompletedFoundations, value => value == "grow-export-integrity");
        Assert.Contains(dto.CompletedFoundations, value => value == "grow-export-validation");
        Assert.Contains(dto.CompletedFoundations, value => value == "security-status");
        Assert.Contains(dto.CompletedFoundations, value => value == "ingress-authenticated-access");
        Assert.Contains(dto.CompletedFoundations, value => value == "schema-migration-status");
        Assert.Contains(dto.CompletedFoundations, value => value == "upgrade-preflight-backup");
        Assert.Contains(dto.CompletedFoundations, value => value == "backup-restore-plan");
        Assert.Contains(dto.CompletedFoundations, value => value == "grow-import-plan");
        Assert.Contains(dto.CompletedFoundations, value => value == "grow-import-execute");
        Assert.Contains(dto.CompletedFoundations, value => value == "system-audit-events");
        Assert.Contains(dto.CompletedFoundations, value => value == "uniform-api-error-format");
        Assert.Contains(dto.CompletedFoundations, value => value == "legacy-mvc-endpoint-containment");
        Assert.Contains(dto.CompletedFoundations, value => value == "remote-product-api-guard");
        Assert.Contains(dto.CompletedFoundations, value => value == "schema-migration-plan");
        Assert.Contains(dto.CompletedFoundations, value => value == "safe-migration-engine-foundation");
        Assert.Contains(dto.RemainingBeforeV1, value => value == "destructive-migration-rollback");
        Assert.Contains(dto.Checks, check => check.Key == "security_guardrails" && check.Status == "pass");
        Assert.Contains(dto.Checks, check => check.Key == "restore_plan" && check.Status == "pass");
        Assert.Contains(dto.Checks, check => check.Key == "grow_import_plan" && check.Status == "pass");
        Assert.Contains(dto.Checks, check => check.Key == "grow_import_execute" && check.Status == "pass");
        Assert.Contains(dto.Checks, check => check.Key == "system_audit_events" && check.Status == "pass");
        Assert.Contains(dto.Checks, check => check.Key == "api_error_format" && check.Status == "pass");
        Assert.Contains(dto.Checks, check => check.Key == "remote_product_api_guard" && check.Status == "pass");
        Assert.Contains(dto.Checks, check => check.Key == "migration_engine_foundation" && check.Status == "pass");
        Assert.Contains(dto.Checks, check => check.Key == "restore_api" && check.Status == "pass");
    }

    [Fact]
    public void BackendHealth_ListsCurrentBackendCapabilities()
    {
        var result = _controller.BackendHealth();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BackendHealthDto>(ok.Value);
        Assert.True(dto.ZeroTentStartupSupported);
        Assert.Contains(dto.Capabilities, capability => capability == "grow-requires-hydro-setup");
        Assert.Contains(dto.Capabilities, capability => capability == "local-backup-without-secrets");
        Assert.Contains(dto.Capabilities, capability => capability == "api-contract-manifest");
        Assert.Contains(dto.Capabilities, capability => capability == "grow-export-integrity");
        Assert.Contains(dto.Capabilities, capability => capability == "grow-export-validation");
        Assert.Contains(dto.Capabilities, capability => capability == "security-status");
        Assert.Contains(dto.Capabilities, capability => capability == "local-only-admin-default");
        Assert.Contains(dto.Capabilities, capability => capability == "schema-migration-status");
        Assert.Contains(dto.Capabilities, capability => capability == "upgrade-preflight-backup");
        Assert.Contains(dto.Capabilities, capability => capability == "backup-restore-plan");
        Assert.Contains(dto.Capabilities, capability => capability == "backup-restore-execute");
        Assert.Contains(dto.Capabilities, capability => capability == "grow-import-plan");
        Assert.Contains(dto.Capabilities, capability => capability == "grow-import-execute");
        Assert.Contains(dto.Capabilities, capability => capability == "system-audit-events");
        Assert.Contains(dto.Capabilities, capability => capability == "uniform-api-error-format");
        Assert.Contains(dto.Capabilities, capability => capability == "legacy-mvc-endpoint-containment");
        Assert.Contains(dto.Capabilities, capability => capability == "remote-product-api-guard");
        Assert.Contains(dto.Capabilities, capability => capability == "schema-migration-plan");
        Assert.Contains(dto.Capabilities, capability => capability == "safe-migration-engine-foundation");
    }


    [Fact]
    public void AuditEvents_ReturnsCriticalSystemEvents()
    {
        _auditRepository.Add(new GrowDiary.Web.Models.SystemAuditEvent
        {
            EventType = "backup",
            Action = "backup-created",
            Summary = "Testbackup erstellt.",
            Severity = "info",
            Source = "test",
            RelatedFileName = "grow-os-backup-test.zip",
            Success = true
        });

        var result = _controller.AuditEvents(limit: 10, eventType: "backup");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var events = Assert.IsAssignableFrom<IReadOnlyList<SystemAuditEventDto>>(ok.Value);
        Assert.Contains(events, entry => entry.EventType == "backup" && entry.Action == "backup-created");
        Assert.DoesNotContain(events, entry => entry.Action == "audit-events-read");
    }

    [Fact]
    public void ApiManifest_ListsCoreAreasEndpointsAndRules()
    {
        var result = _controller.ApiManifest();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ApiManifestDto>(ok.Value);
        Assert.Equal("grow-os.api-manifest.v1", dto.SchemaVersion);
        Assert.Equal("backend-core.v0.18-candidate", dto.BackendSchema);
        Assert.Contains(dto.GlobalRules, rule => rule.Contains("HydroSetup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dto.GlobalRules, rule => rule.Contains("Ingress", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dto.Areas, area => area.Key == "tents");
        Assert.Contains(dto.Areas, area => area.Key == "hydro-setups");
        Assert.Contains(dto.Areas, area => area.Key == "grows");
        Assert.Contains(dto.Areas, area => area.Key == "operations");
        Assert.Contains(dto.Areas, area => area.Key == "export-backup-system");

        var growArea = Assert.Single(dto.Areas, area => area.Key == "grows");
        Assert.Contains(growArea.Endpoints, endpoint => endpoint.Method == "POST" && endpoint.Path == "/api/grows" && endpoint.LocalAdminOnly);
        Assert.Contains(growArea.Endpoints.Single(endpoint => endpoint.Method == "POST" && endpoint.Path == "/api/grows").Rules,
            rule => rule.Contains("SystemId", StringComparison.OrdinalIgnoreCase));

        var systemArea = Assert.Single(dto.Areas, area => area.Key == "export-backup-system");
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/system/api-manifest" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/system/security-status" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/system/audit-events" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/system/error-contract" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/system/migration-status" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/system/migration-plan" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/system/upgrade-preflight" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/system/backup/{fileName}/restore-plan" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/system/backup/{fileName}/restore" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/exports/grows/validate" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/exports/grows/import-plan" && endpoint.LocalAdminOnly);
        Assert.Contains(systemArea.Endpoints, endpoint => endpoint.Path == "/api/exports/grows/import" && endpoint.LocalAdminOnly);
    }

    [Fact]
    public void ErrorContract_ReturnsUniformApiErrorContract()
    {
        var result = _controller.ErrorContract();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ApiErrorContractDto>(ok.Value);
        Assert.Equal(ApiErrorFactory.SchemaVersion, dto.SchemaVersion);
        Assert.Contains("code", dto.RequiredFields);
        Assert.Contains("message", dto.RequiredFields);
        Assert.Contains("schemaVersion", dto.RequiredFields);
        Assert.Contains("fieldErrors", dto.OptionalFields);
        Assert.Contains("validation_failed", dto.StandardCodes);
        Assert.Contains("admin_access_required", dto.StandardCodes);
    }

    [Fact]
    public void MigrationStatus_ReturnsAppliedSchemaMigrations()
    {
        var result = _controller.MigrationStatus();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<SchemaMigrationStatusDto>(ok.Value);
        Assert.Equal("grow-os.schema-migrations.v1", dto.MigrationSchema);
        Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, dto.CurrentSchemaVersion);
        Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, dto.StoredSchemaVersion);
        Assert.True(dto.MigrationTableExists);
        Assert.True(dto.IsCurrent);
        Assert.Empty(dto.PendingMigrations);
        Assert.Contains(dto.AppliedMigrations, migration => migration.Id == "0011-upgrade-preflight");
        Assert.Contains(dto.AppliedMigrations, migration => migration.Id == "0012-restore-plan");
        Assert.Contains(dto.AppliedMigrations, migration => migration.Id == "0013-grow-import-plan");
        Assert.Contains(dto.AppliedMigrations, migration => migration.Id == "0014-system-audit-events");
        Assert.Contains(dto.AppliedMigrations, migration => migration.Id == "0015-api-error-format");
        Assert.Contains(dto.AppliedMigrations, migration => migration.Id == "0016-legacy-mvc-containment");
        Assert.Contains(dto.AppliedMigrations, migration => migration.Id == "0017-product-api-remote-guard");
        Assert.Contains(dto.AppliedMigrations, migration => migration.Id == "0018-migration-engine-foundation");
        Assert.Contains(dto.AppliedMigrations, migration => migration.Id == "0019-grow-run-snapshots");
    }

    [Fact]
    public void MigrationPlan_ReturnsDryRunPlanWithBackupGuardrails()
    {
        var result = _controller.MigrationPlan();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<SchemaMigrationPlanDto>(ok.Value);
        Assert.Equal("grow-os.schema-migration-plan.v1", dto.PlanSchema);
        Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, dto.CurrentSchemaVersion);
        Assert.False(dto.ApplySupported);
        Assert.False(dto.WouldModifyDatabase);
        Assert.Contains(dto.Items, item => item.Id == "0018-migration-engine-foundation" && item.RequiresBackup);
        Assert.Contains(dto.Items, item => item.Id == "0019-grow-run-snapshots" && !item.RequiresBackup);
        Assert.Contains(dto.Warnings, warning => warning.Contains("Dry-Run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpgradePreflight_CreatesAndValidatesBackupWhenDatabaseIsCurrent()
    {
        var result = _controller.UpgradePreflight();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UpgradePreflightDto>(ok.Value);
        Assert.Equal("grow-os.upgrade-preflight.v1", dto.PreflightSchema);
        Assert.True(dto.IsSafeToUpgrade);
        Assert.True(dto.DatabaseCurrent);
        Assert.True(dto.BackupCreated);
        Assert.True(dto.BackupValid);
        Assert.NotNull(dto.BackupFileName);
        Assert.NotNull(dto.BackupValidation);
        Assert.True(dto.MigrationStatus.IsCurrent);
        Assert.Empty(dto.Blockers);
    }


    [Fact]
    public void SecurityStatus_ReturnsLocalAndIngressGuardrailStateWithoutSecrets()
    {
        var result = _controller.SecurityStatus();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BackendSecurityStatusDto>(ok.Value);
        Assert.Equal("grow-os.security.v1", dto.SecuritySchema);
        Assert.Equal("local-and-ingress", dto.AdminAccessMode);
        Assert.True(dto.LocalOnlyAdminDefault);
        Assert.True(dto.IngressTrusted);
        Assert.Contains(dto.ProtectedRoutePrefixes, prefix => prefix == "/api/exports");
        Assert.Contains(dto.ProtectedRoutePrefixes, prefix => prefix == "/api/grows");
        Assert.Contains(dto.ProtectedRoutePrefixes, prefix => prefix == "/api/hydro-setups");
        Assert.Contains(dto.Notes, note => note.Contains("Ingress", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dto.SecretHandling, item => item.Contains("Home-Assistant-Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(dto.SecretHandling, item => item.Contains("Admin-Key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateBackup_ExcludesSecretsRuntimeKeysUploadsAndLogs()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "App_Data"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "App_Data", "DataProtectionKeys"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "wwwroot", "uploads"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "App_Data", "knowledge", "custom"));

        File.WriteAllText(Path.Combine(_tempRoot, "App_Data", "ha-config.json"), "secret-token");
        File.WriteAllText(Path.Combine(_tempRoot, "App_Data", "DataProtectionKeys", "key.xml"), "key-material");
        File.WriteAllText(Path.Combine(_tempRoot, "wwwroot", "uploads", "plant.jpg"), "image");
        File.WriteAllText(Path.Combine(_tempRoot, "runtime.log"), "log");
        File.WriteAllText(Path.Combine(_tempRoot, "App_Data", "knowledge", "custom", "note.json"), "{}");

        var result = _controller.CreateBackup();

        var created = Assert.IsType<CreatedResult>(result.Result);
        var manifest = Assert.IsType<BackupManifestDto>(created.Value);
        Assert.True(manifest.ExcludesSecrets);
        Assert.True(manifest.ExcludesHomeAssistantConfig);
        Assert.True(manifest.ExcludesDataProtectionKeys);
        Assert.True(manifest.ExcludesUploads);
        Assert.True(manifest.RestoreSupported);
        Assert.StartsWith("/api/system/backup/", manifest.DownloadUrl);

        var backupPath = Path.Combine(_tempRoot, "App_Data", "backups", manifest.FileName);
        using var archive = ZipFile.OpenRead(backupPath);
        var entries = archive.Entries.Select(entry => entry.FullName).ToList();

        Assert.Contains("App_Data/grow-diary.db", entries);
        Assert.Contains("App_Data/knowledge/custom/note.json", entries);
        Assert.DoesNotContain("App_Data/ha-config.json", entries);
        Assert.DoesNotContain("App_Data/DataProtectionKeys/key.xml", entries);
        Assert.DoesNotContain("wwwroot/uploads/plant.jpg", entries);
        Assert.DoesNotContain("runtime.log", entries);
    }


    [Fact]
    public void DatabaseStatus_ReturnsCurrentSchemaAndRequiredTables()
    {
        var result = _controller.DatabaseStatus();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<DatabaseStatusDto>(ok.Value);
        Assert.True(dto.DatabaseExists);
        Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, dto.ExpectedSchemaVersion);
        Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, dto.StoredSchemaVersion);
        Assert.True(dto.IsCurrent);
        Assert.Contains("Tents", dto.RequiredTablesPresent);
        Assert.Contains("GrowSystems", dto.RequiredTablesPresent);
        Assert.Contains("Grows.SystemId", dto.RequiredColumnsPresent);
        Assert.Contains("Grows.TentSnapshotJson", dto.RequiredColumnsPresent);
        Assert.Contains("Grows.HydroSetupSnapshotJson", dto.RequiredColumnsPresent);
        Assert.Contains("Grows.SnapshotsCapturedAtUtc", dto.RequiredColumnsPresent);
        Assert.Contains("HardwareItems.HydroSetupId", dto.RequiredColumnsPresent);
        Assert.Empty(dto.MissingRequiredTables);
        Assert.Empty(dto.MissingRequiredColumns);
    }

    [Fact]
    public void ValidateBackup_ReturnsValidResultForCreatedBackup()
    {
        var created = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);

        var result = _controller.ValidateBackup(created.FileName);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BackupValidationDto>(ok.Value);
        Assert.True(dto.Exists);
        Assert.True(dto.IsValid);
        Assert.True(dto.ContainsDatabase);
        Assert.False(dto.ContainsSecrets);
        Assert.False(dto.ContainsDataProtectionKeys);
        Assert.False(dto.ContainsUploads);
        Assert.True(dto.EntryCount >= 1);
    }

    [Fact]
    public void ValidateBackup_RejectsUnsafeFileNames()
    {
        var result = _controller.ValidateBackup("../ha-config.json");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("invalid_backup_file", error.Code);
    }

    [Fact]
    public void DownloadBackup_RejectsUnsafeFileNames()
    {
        var result = _controller.DownloadBackup("../ha-config.json");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("invalid_backup_file", error.Code);
    }



    [Fact]
    public void RestorePlan_ReturnsDryRunForValidBackupWithoutChangingFiles()
    {
        var created = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);

        var result = _controller.RestorePlan(created.FileName);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BackupRestorePlanDto>(ok.Value);
        Assert.Equal("grow-os.restore-plan.v1", dto.RestorePlanSchema);
        Assert.Equal(created.FileName, dto.FileName);
        Assert.True(dto.BackupValid);
        Assert.True(dto.DatabaseIncluded);
        Assert.True(dto.SchemaCompatible);
        Assert.True(dto.RestoreSupported);
        Assert.False(dto.RequiresManualStop);
        Assert.True(dto.WouldOverwriteExistingDatabase);
        Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, dto.BackupSchemaVersion);
        Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, dto.CurrentSchemaVersion);
        Assert.Empty(dto.Blockers);
        Assert.Contains(dto.Warnings, warning => warning.Contains("Dry-Run", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dto.Files, file => file.EntryName == "App_Data/grow-diary.db" && file.Kind == "database");
    }

    [Fact]
    public void RestorePlan_RejectsUnsafeFileNames()
    {
        var result = _controller.RestorePlan("../grow-os-backup-20260101-120000.zip");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("invalid_backup_file", error.Code);
    }

    [Fact]
    public void RestorePlan_DetectsSchemaMismatchWithoutRestoring()
    {
        var created = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);
        var backupPath = Path.Combine(_tempRoot, "App_Data", "backups", created.FileName);
        RewriteBackupSchemaVersion(backupPath, "backend-core.v0.0-old");

        var result = _controller.RestorePlan(created.FileName);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BackupRestorePlanDto>(ok.Value);
        Assert.False(dto.SchemaCompatible);
        Assert.Equal("backend-core.v0.0-old", dto.BackupSchemaVersion);
        Assert.Contains(dto.Blockers, blocker => blocker.Contains("Schema", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RestoreBackup_RestoresDatabaseAndCreatesSafetyBackup()
    {
        var created = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);
        _repository.CreateTent("Tent after backup");
        Assert.NotEmpty(_repository.GetTents(includeArchived: true));

        var result = _controller.RestoreBackup(created.FileName);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BackupRestoreResultDto>(ok.Value);
        Assert.Equal("grow-os.backup-restore.v1", dto.RestoreSchema);
        Assert.True(dto.Success);
        Assert.Equal(created.FileName, dto.FileName);
        Assert.NotEqual(created.FileName, dto.SafetyBackupFileName);
        Assert.True(dto.DatabaseRestored);
        Assert.True(File.Exists(Path.Combine(_tempRoot, "App_Data", "backups", dto.SafetyBackupFileName)));
        Assert.Empty(_repository.GetTents(includeArchived: true));

        var events = _auditRepository.GetRecent(limit: 20, eventType: "backup");
        Assert.Contains(events, entry => entry.Action == "backup-restored" && entry.Success);
    }

    [Fact]
    public void RestoreBackup_BlocksSchemaMismatchAndDoesNotChangeDatabase()
    {
        var created = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);
        var backupPath = Path.Combine(_tempRoot, "App_Data", "backups", created.FileName);
        RewriteBackupSchemaVersion(backupPath, "backend-core.v0.0-old");
        _repository.CreateTent("Tent that must survive blocked restore");

        var result = _controller.RestoreBackup(created.FileName);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("restore_blocked", error.Code);
        Assert.Contains(_repository.GetTents(includeArchived: true), tent => tent.Name == "Tent that must survive blocked restore");
    }

    [Fact]
    public void RestoreBackup_RejectsUnsafeFileNames()
    {
        var result = _controller.RestoreBackup("../grow-os-backup-20260101-120000.zip");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("invalid_backup_file", error.Code);
    }


    private static void RewriteBackupSchemaVersion(string backupPath, string schemaVersion)
    {
        var workRoot = Path.Combine(Path.GetTempPath(), "GrowBackupRewrite_" + Guid.NewGuid().ToString("N"));
        var extractRoot = Path.Combine(workRoot, "extract");
        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(backupPath, extractRoot);

        var dbPath = Path.Combine(extractRoot, "App_Data", "grow-diary.db");
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath };
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString()))
        {
            connection.Open();
            using (var checkpoint = connection.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpoint.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE AppSettings SET Value = $value WHERE Key = $key;";
                command.Parameters.AddWithValue("$value", schemaVersion);
                command.Parameters.AddWithValue("$key", DatabaseInitializer.CurrentSchemaAppSettingKey);
                command.ExecuteNonQuery();
            }

            using (var checkpoint = connection.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpoint.ExecuteNonQuery();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        File.Delete(backupPath);
        CreateZipFromDirectoryAllowingOpenFiles(extractRoot, backupPath);
        DeleteDirectoryWithRetries(workRoot);
    }

    private static void CreateZipFromDirectoryAllowingOpenFiles(string sourceDirectory, string destinationArchiveFileName)
    {
        using var archive = ZipFile.Open(destinationArchiveFileName, ZipArchiveMode.Create);
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace(Path.DirectorySeparatorChar, '/');
            var entry = archive.CreateEntry(relativePath);
            using var entryStream = entry.Open();
            using var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            sourceStream.CopyTo(entryStream);
        }
    }

    private static void DeleteDirectoryWithRetries(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                System.Threading.Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                System.Threading.Thread.Sleep(50);
            }
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void DownloadBackup_ReturnsPhysicalFileForExistingBackup()
    {
        var created = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);

        var result = _controller.DownloadBackup(created.FileName);

        var file = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal("application/zip", file.ContentType);
        Assert.Equal(created.FileName, file.FileDownloadName);
        Assert.True(File.Exists(file.FileName));
    }

    // ── Aufbewahrung und Liste (Fehlerregister F-012, 16.09.2026) ──

    private string BackupOrdner => Path.Combine(_tempRoot, "App_Data", "backups");

    /// <summary>Legt eine unechte Sicherung mit festem Alter an.</summary>
    private string AlteSicherung(string datei, int tageAlt)
    {
        Directory.CreateDirectory(BackupOrdner);
        var pfad = Path.Combine(BackupOrdner, datei);
        File.WriteAllText(pfad, "alt");
        File.SetLastWriteTimeUtc(pfad, DateTime.UtcNow.AddDays(-tageAlt));
        return datei;
    }

    [Fact]
    public void CreateBackup_BehaeltJeArtNurDieNeuestenFuenf()
    {
        for (var i = 1; i <= 7; i++) AlteSicherung($"grow-os-backup-2026010{i}-120000-0000000.zip", tageAlt: 20 - i);
        for (var i = 1; i <= 6; i++) AlteSicherung($"grow-os-backup-import-safety-2026010{i}-120000-0000000.zip", tageAlt: 20 - i);
        var fremd = AlteSicherung("eigene-notiz.zip", tageAlt: 99);
        Assert.Equal(14, Directory.GetFiles(BackupOrdner).Length); // Mengenwächter

        var created = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);

        var normale = BackupAufbewahrung.Auflisten(BackupOrdner).Where(e => e.Art == "sicherung").Select(e => e.Datei).ToList();
        Assert.Equal(BackupAufbewahrung.JeArt, normale.Count);
        Assert.Contains(created.FileName, normale);
        // Die neue plus die vier jüngsten alten (7, 6, 5, 4) bleiben.
        Assert.Contains("grow-os-backup-20260104-120000-0000000.zip", normale);
        Assert.DoesNotContain("grow-os-backup-20260103-120000-0000000.zip", normale);
        // Drei normale (1, 2, 3) und eine Import-Sicherung (die älteste) gehen.
        Assert.Equal(4, created.RemovedOldBackups!.Count);
        // Import-Sicherungen zählen getrennt: sechs vorhanden, eine davon ist zu viel.
        Assert.Equal(5, BackupAufbewahrung.Auflisten(BackupOrdner).Count(e => e.Art == "import"));
        // Was nicht zum Schema gehört, bleibt unberührt.
        Assert.True(File.Exists(Path.Combine(BackupOrdner, fremd)));

        var events = _auditRepository.GetRecent(limit: 20, eventType: "backup");
        Assert.Contains(events, e => e.Action == "backup-retention" && e.Success);
    }

    [Fact]
    public void RestoreBackup_DasEinzuspielendeBackupUeberstehtDasAufraeumen()
    {
        var created = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);
        // Das einzuspielende Backup ist das älteste — genau das würde das Aufräumen als Erstes löschen.
        File.SetLastWriteTimeUtc(Path.Combine(BackupOrdner, created.FileName), DateTime.UtcNow.AddDays(-30));
        for (var i = 1; i <= 5; i++) AlteSicherung($"grow-os-backup-2026020{i}-120000-0000000.zip", tageAlt: 10 - i);

        var result = _controller.RestoreBackup(created.FileName);

        var dto = Assert.IsType<BackupRestoreResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(dto.Success);
        Assert.True(File.Exists(Path.Combine(BackupOrdner, created.FileName)));
        Assert.True(File.Exists(Path.Combine(BackupOrdner, dto.SafetyBackupFileName)));
    }

    [Fact]
    public void ListBackups_ZeigtNeuesteZuerstMitGroesseUndArt()
    {
        AlteSicherung("grow-os-backup-20260101-120000-0000000.zip", tageAlt: 5);
        AlteSicherung("grow-os-backup-import-safety-20260102-120000-0000000.zip", tageAlt: 3);
        AlteSicherung("fremd.zip", tageAlt: 1);

        var dto = Assert.IsType<BackupListDto>(Assert.IsType<OkObjectResult>(_controller.ListBackups().Result).Value);

        Assert.Equal(2, dto.Backups.Count);
        Assert.Equal("import", dto.Backups[0].Kind);
        Assert.Equal("sicherung", dto.Backups[1].Kind);
        Assert.Equal(BackupAufbewahrung.JeArt, dto.KeptPerKind);
        Assert.Equal(6, dto.TotalBytes);
        Assert.StartsWith("/api/system/backup/", dto.Backups[0].DownloadUrl);
    }
}
