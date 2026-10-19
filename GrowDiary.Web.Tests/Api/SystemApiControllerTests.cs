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
    public async Task BackendHealth_NamesTheInstalledAddonVersion()
    {
        // Einstellungen → System: „Version“ zeigt die Nummer des installierten Add-ons, die der Supervisor nennt.
        var controller = new SystemApiController(_paths, _repository, _auditRepository, versionAuskunft: () => Task.FromResult<string?>("2.0.0-forkai.246"));

        var ok = Assert.IsType<OkObjectResult>((await controller.BackendHealth()).Result);

        Assert.Equal("2.0.0-forkai.246", Assert.IsType<BackendHealthDto>(ok.Value).AppVersion);
    }

    [Fact]
    public async Task BackendHealth_WithoutSupervisorHasNoVersion()
    {
        // Außerhalb von Home Assistant (Docker, Entwicklung) kennt der Fork seine Version nicht — dann null statt einer erfundenen Zahl.
        var ok = Assert.IsType<OkObjectResult>((await _controller.BackendHealth()).Result);

        Assert.Null(Assert.IsType<BackendHealthDto>(ok.Value).AppVersion);
    }

    [Fact]
    public async Task BackendHealth_ListsCurrentBackendCapabilities()
    {
        var result = await _controller.BackendHealth();

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

    private static Microsoft.AspNetCore.Http.IFormFile Zip(byte[] inhalt, string name = "grow-os-backup-x.zip")
        => new Microsoft.AspNetCore.Http.FormFile(new MemoryStream(inhalt), 0, inhalt.Length, "file", name);

    private static byte[] ZipMit(params (string Name, string Inhalt)[] eintraege)
    {
        using var speicher = new MemoryStream();
        using (var archiv = new ZipArchive(speicher, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, inhalt) in eintraege)
            {
                var eintrag = archiv.CreateEntry(name);
                using var schreiber = new StreamWriter(eintrag.Open());
                schreiber.Write(inhalt);
            }
        }

        return speicher.ToArray();
    }

    [Fact]
    public async Task UploadBackup_NimmtEineSicherungAnUndSieLaesstSichPruefenUndZurueckspielen()
    {
        // Rundweg des Umzugs: auf der alten Installation sichern, herunterladen, auf der neuen hochladen.
        var erzeugt = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);
        var bytes = await System.IO.File.ReadAllBytesAsync(Path.Combine(_paths.BackupsPath, erzeugt.FileName));

        var antwort = await _controller.UploadBackup(new BackupUploadRequest { File = Zip(bytes, "irgendein-name.zip") }, CancellationToken.None);

        var manifest = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(antwort.Result).Value);
        Assert.NotEqual(erzeugt.FileName, manifest.FileName);
        Assert.StartsWith("grow-os-backup-", manifest.FileName);
        Assert.True(manifest.IncludesDatabase);
        Assert.True(System.IO.File.Exists(Path.Combine(_paths.BackupsPath, manifest.FileName)));
        Assert.Empty(Directory.GetFiles(_paths.BackupsPath, "*.upload"));

        var plan = Assert.IsType<BackupRestorePlanDto>(Assert.IsType<OkObjectResult>(_controller.RestorePlan(manifest.FileName).Result).Value);
        Assert.True(plan.RestoreSupported, string.Join(" | ", plan.Blockers));

        var zurueck = _controller.RestoreBackup(manifest.FileName);
        Assert.IsType<OkObjectResult>(zurueck.Result);
    }

    [Fact]
    public async Task UploadBackup_WeistLeereDateiUndFalscheEndungAb()
    {
        var leer = await _controller.UploadBackup(new BackupUploadRequest { File = null }, CancellationToken.None);
        Assert.Equal("backup_upload_empty", Assert.IsType<ApiError>(Assert.IsType<BadRequestObjectResult>(leer.Result).Value).Code);

        var endung = await _controller.UploadBackup(new BackupUploadRequest { File = Zip([1, 2, 3], "sicherung.json") }, CancellationToken.None);
        Assert.Equal("backup_upload_not_zip", Assert.IsType<ApiError>(Assert.IsType<BadRequestObjectResult>(endung.Result).Value).Code);
    }

    [Fact]
    public async Task UploadBackup_WeistKaputtesZipAbUndHinterlaesstNichts()
    {
        var antwort = await _controller.UploadBackup(new BackupUploadRequest { File = Zip([1, 2, 3, 4, 5]) }, CancellationToken.None);

        Assert.Equal("backup_upload_invalid", Assert.IsType<ApiError>(Assert.IsType<BadRequestObjectResult>(antwort.Result).Value).Code);
        Assert.False(Directory.Exists(_paths.BackupsPath) && Directory.GetFiles(_paths.BackupsPath).Length > 0);
    }

    [Fact]
    public async Task UploadBackup_WeistEinZipOhneDatenbankAb()
    {
        var ohneDatenbank = ZipMit(("App_Data/knowledge/etwas.json", "{}"));

        var antwort = await _controller.UploadBackup(new BackupUploadRequest { File = Zip(ohneDatenbank) }, CancellationToken.None);

        Assert.Equal("backup_upload_no_database", Assert.IsType<ApiError>(Assert.IsType<BadRequestObjectResult>(antwort.Result).Value).Code);
        Assert.False(Directory.Exists(_paths.BackupsPath) && Directory.GetFiles(_paths.BackupsPath).Length > 0);
    }

    [Fact]
    public void UploadBackup_IstFuerKiSchluesselGesperrt()
    {
        // Wie das Zurückspielen: Ein Assistent darf keine fremde Datenbank einschleusen.
        var methode = typeof(SystemApiController).GetMethod(nameof(SystemApiController.UploadBackup))!;

        Assert.NotNull(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<GrowDiary.Web.Infrastructure.KiZugriff.KeinKiZugriffAttribute>(methode, inherit: true));
    }

    private string BildDatei(string wurzel, string relativ, byte[] inhalt)
    {
        var pfad = Path.Combine(wurzel, relativ.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
        File.WriteAllBytes(pfad, inhalt);
        return pfad;
    }

    private HashSet<string> ZipEintraege(string dateiname)
    {
        using var archiv = ZipFile.OpenRead(Path.Combine(_paths.BackupsPath, dateiname));
        return archiv.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateBackup_OhneHakenEnthaeltKeineBilder()
    {
        BildDatei(_paths.UploadRootPath, "1/foto.jpg", [1, 2, 3]);

        var manifest = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);

        Assert.False(manifest.IncludesMedia);
        Assert.True(manifest.ExcludesUploads);
        Assert.DoesNotContain(ZipEintraege(manifest.FileName), e => e.StartsWith("App_Data/uploads/") || e.StartsWith("App_Data/snapshots/"));
    }

    [Fact]
    public void CreateBackup_MitHakenEnthaeltFotosUndKamerabilderNurAlsBilder()
    {
        BildDatei(_paths.UploadRootPath, "1/foto.jpg", [1, 2, 3]);
        BildDatei(_paths.SnapshotsPath, "1/2026-07-25.jpg", [4, 5]);
        BildDatei(_paths.UploadRootPath, "1/notiz.txt", [9]);

        var manifest = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup(bilder: true).Result).Value);

        Assert.True(manifest.IncludesMedia);
        Assert.False(manifest.ExcludesUploads);
        Assert.Equal(2, manifest.MediaFileCount);
        Assert.Equal(5, manifest.MediaBytes);
        var eintraege = ZipEintraege(manifest.FileName);
        Assert.Contains("App_Data/uploads/1/foto.jpg", eintraege);
        Assert.Contains("App_Data/snapshots/1/2026-07-25.jpg", eintraege);
        Assert.DoesNotContain("App_Data/uploads/1/notiz.txt", eintraege);
    }

    [Fact]
    public void BackupMediaSize_ZaehltDieBilderDieMitkaemen()
    {
        BildDatei(_paths.UploadRootPath, "1/a.jpg", new byte[100]);
        BildDatei(_paths.SnapshotsPath, "1/b.png", new byte[20]);
        BildDatei(_paths.UploadRootPath, "1/c.txt", new byte[999]);

        var dto = Assert.IsType<BackupMediaSizeDto>(Assert.IsType<OkObjectResult>(_controller.BackupMediaSize().Result).Value);

        Assert.Equal(2, dto.FileCount);
        Assert.Equal(120, dto.Bytes);
    }

    [Fact]
    public void RestorePlan_ZeigtDieBilderUndBlockiertNichtWeilEinNameToken_heisst()
    {
        BildDatei(_paths.UploadRootPath, "1/token.jpg", [1, 2, 3]);
        var manifest = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup(bilder: true).Result).Value);

        var plan = Assert.IsType<BackupRestorePlanDto>(Assert.IsType<OkObjectResult>(_controller.RestorePlan(manifest.FileName).Result).Value);

        Assert.True(plan.MediaIncluded);
        Assert.Equal(1, plan.MediaFileCount);
        Assert.True(plan.RestoreSupported, string.Join(" | ", plan.Blockers));
        Assert.Contains(plan.Warnings, w => w.Contains("Fotos und Kamerabilder"));
        Assert.DoesNotContain(plan.Files, f => f.EntryName.StartsWith("App_Data/uploads/"));
    }

    [Fact]
    public void RestorePlan_BlockiertEineDateiDieKeinBildIstUndEinenUnsicherenPfad()
    {
        var manifest = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup(bilder: true).Result).Value);
        var pfad = Path.Combine(_paths.BackupsPath, manifest.FileName);
        using (var archiv = ZipFile.Open(pfad, ZipArchiveMode.Update))
        {
            using var w = new StreamWriter(archiv.CreateEntry("App_Data/uploads/1/programm.exe").Open());
            w.Write("MZ");
        }

        var nichtBild = Assert.IsType<BackupRestorePlanDto>(Assert.IsType<OkObjectResult>(_controller.RestorePlan(manifest.FileName).Result).Value);
        Assert.False(nichtBild.RestoreSupported);
        Assert.Contains(nichtBild.Blockers, b => b.Contains("keine Bilder"));

        using (var archiv = ZipFile.Open(pfad, ZipArchiveMode.Update))
        {
            archiv.GetEntry("App_Data/uploads/1/programm.exe")!.Delete();
            using var w = new StreamWriter(archiv.CreateEntry("App_Data/uploads/../ausbruch.jpg").Open());
            w.Write("x");
        }

        var unsicher = Assert.IsType<BackupRestorePlanDto>(Assert.IsType<OkObjectResult>(_controller.RestorePlan(manifest.FileName).Result).Value);
        Assert.False(unsicher.RestoreSupported);
        Assert.Contains(unsicher.Blockers, b => b.Contains("unsichere Pfade"));
    }

    [Fact]
    public void RestoreBackup_SpieltBilderZurueckErsetztDenBestandUndSichertDenAltenStandMit()
    {
        var foto = BildDatei(_paths.UploadRootPath, "1/foto.jpg", [1, 2, 3]);
        var tagesbild = BildDatei(_paths.SnapshotsPath, "1/2026-07-25.jpg", [4, 5]);
        var manifest = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup(bilder: true).Result).Value);

        // Danach: ein Bild ist weg, ein anderes ist neu, ein drittes verändert.
        File.Delete(tagesbild);
        File.WriteAllBytes(foto, [9, 9, 9, 9]);
        var neu = BildDatei(_paths.UploadRootPath, "1/neu.jpg", [7]);

        var antwort = _controller.RestoreBackup(manifest.FileName);

        var ergebnis = Assert.IsType<BackupRestoreResultDto>(Assert.IsType<OkObjectResult>(antwort.Result).Value);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(foto));
        Assert.Equal([4, 5], File.ReadAllBytes(tagesbild));
        Assert.False(File.Exists(neu));
        Assert.Contains(ergebnis.Warnings, w => w.Contains("2 Fotos und Kamerabilder zurückgespielt"));
        // Die Rückfall-Sicherung enthält die Bilder von vor dem Zurückspielen, auch das neue.
        var rueckfall = ZipEintraege(ergebnis.SafetyBackupFileName);
        Assert.Contains("App_Data/uploads/1/neu.jpg", rueckfall);
        Assert.Empty(Directory.GetDirectories(_paths.DataRootPath, "restore-staging-*"));
        Assert.Empty(Directory.GetDirectories(_paths.DataRootPath, "restore-rollback-*"));
    }

    [Fact]
    public void RestoreBackup_OhneBilderLaesstDieVorhandenenBilderStehen()
    {
        var manifest = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(_controller.CreateBackup().Result).Value);
        var foto = BildDatei(_paths.UploadRootPath, "1/foto.jpg", [1, 2, 3]);

        Assert.IsType<OkObjectResult>(_controller.RestoreBackup(manifest.FileName).Result);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(foto));
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
}
