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
    [HttpGet("migration-status")]
    [ProducesResponseType(typeof(SchemaMigrationStatusDto), StatusCodes.Status200OK)]
    public ActionResult<SchemaMigrationStatusDto> MigrationStatus()
    {
        var dto = BuildMigrationStatus();
        LogSystemAudit("system", "migration-status-read", "Migration-Status abgefragt.", dto.IsCurrent, severity: dto.IsCurrent ? "info" : "warning");
        return Ok(dto);
    }


    [HttpGet("migration-plan")]
    [ProducesResponseType(typeof(SchemaMigrationPlanDto), StatusCodes.Status200OK)]
    public ActionResult<SchemaMigrationPlanDto> MigrationPlan()
    {
        var dto = BuildMigrationPlan();
        LogSystemAudit("system", "migration-plan-read", "Migration-Plan abgefragt.", true);
        return Ok(dto);
    }


    [HttpPost("upgrade-preflight")]
    [ProducesResponseType(typeof(UpgradePreflightDto), StatusCodes.Status200OK)]
    public ActionResult<UpgradePreflightDto> UpgradePreflight()
    {
        var databaseStatus = ExtractOk<DatabaseStatusDto>(DatabaseStatus());
        var migrationStatus = BuildMigrationStatus();
        var blockers = new List<string>();
        var warnings = new List<string>();
        BackupManifestDto? backupManifest = null;
        BackupValidationDto? backupValidation = null;

        if (!databaseStatus.DatabaseExists)
        {
            blockers.Add("Datenbankdatei existiert noch nicht.");
        }
        if (!databaseStatus.IsCurrent)
        {
            blockers.Add("Datenbankstatus ist nicht aktuell.");
        }
        if (!migrationStatus.IsCurrent)
        {
            blockers.Add("Schema-Migrationsstatus ist nicht aktuell.");
        }

        if (blockers.Count == 0)
        {
            var backupResult = CreateBackup();
            if (backupResult.Result is CreatedResult created && created.Value is BackupManifestDto manifest)
            {
                backupManifest = manifest;
                var validation = ValidateBackup(manifest.FileName);
                if (validation.Result is OkObjectResult ok && ok.Value is BackupValidationDto validationDto)
                {
                    backupValidation = validationDto;
                    if (!validationDto.IsValid)
                    {
                        blockers.Add("Preflight-Backup ist nicht valide.");
                    }
                }
                else
                {
                    blockers.Add("Preflight-Backup konnte nicht validiert werden.");
                }
            }
            else
            {
                blockers.Add("Preflight-Backup konnte nicht erstellt werden.");
            }
        }
        else
        {
            warnings.Add("Backup wurde nicht erstellt, weil der Preflight bereits Blocker gefunden hat.");
        }

        var isSafe = blockers.Count == 0 && backupManifest is not null && backupValidation?.IsValid == true;
        LogSystemAudit("system", "upgrade-preflight-run", isSafe ? "Upgrade-Preflight erfolgreich ausgeführt." : "Upgrade-Preflight mit Blockern ausgeführt.", isSafe, relatedFileName: backupManifest?.FileName, severity: isSafe ? "info" : "warning");
        return Ok(new UpgradePreflightDto(
            PreflightSchema: "grow-os.upgrade-preflight.v1",
            CheckedAtUtc: DateTime.UtcNow,
            IsSafeToUpgrade: isSafe,
            DatabaseCurrent: databaseStatus.IsCurrent,
            BackupCreated: backupManifest is not null,
            BackupValid: backupValidation?.IsValid == true,
            BackupFileName: backupManifest?.FileName,
            BackupDownloadUrl: backupManifest?.DownloadUrl,
            Blockers: blockers,
            Warnings: warnings,
            DatabaseStatus: databaseStatus,
            MigrationStatus: migrationStatus,
            BackupValidation: backupValidation));
    }


    private SchemaMigrationPlanDto BuildMigrationPlan()
    {
        var status = BuildMigrationStatus();
        var appliedIds = status.AppliedMigrations.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = DatabaseInitializer.RequiredMigrations
            .Select(m => new SchemaMigrationPlanItemDto(
                Id: m.Id,
                Name: m.Name,
                RequiredForSchemaVersion: m.RequiredForSchemaVersion,
                Status: appliedIds.Contains(m.Id) ? "applied" : "pending",
                IsDestructive: m.IsDestructive,
                RequiresBackup: m.RequiresBackup || m.IsDestructive,
                ExecutionMode: m.IsDestructive ? "manual-blocked" : "idempotent",
                Checksum: m.Checksum))
            .ToArray();

        var pending = items.Where(item => string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase)).ToArray();
        var blockers = new List<string>();
        var warnings = new List<string>
        {
            "Migration-Plan ist ein Dry-Run. Dieser Endpoint führt keine Migration aus.",
            "Echte destructive Migrationen bleiben blockiert, bis Backup, Restore und manueller Rollback-Prozess vollständig implementiert sind."
        };

        if (pending.Any(item => item.IsDestructive))
        {
            blockers.Add("Mindestens eine destructive Migration ist pending und darf nicht automatisch ausgeführt werden.");
        }

        if (!status.IsCurrent)
        {
            warnings.Add("Migration-Status ist nicht vollständig aktuell; Upgrade-Preflight sollte vor jeder neuen Version ausgeführt werden.");
        }

        return new SchemaMigrationPlanDto(
            PlanSchema: "grow-os.schema-migration-plan.v1",
            CurrentSchemaVersion: DatabaseInitializer.CurrentSchemaVersion,
            CheckedAtUtc: DateTime.UtcNow,
            WouldModifyDatabase: pending.Length > 0,
            RequiresBackupBeforeApply: pending.Any(item => item.RequiresBackup),
            HasDestructiveSteps: items.Any(item => item.IsDestructive),
            ApplySupported: false,
            Items: items,
            Blockers: blockers,
            Warnings: warnings);
    }


    private SchemaMigrationStatusDto BuildMigrationStatus()
    {
        var required = DatabaseInitializer.RequiredMigrations;
        var warnings = new List<string>();
        string? storedSchemaVersion = null;
        var tableExists = false;
        var applied = new List<AppliedSchemaMigrationDto>();

        if (!System.IO.File.Exists(_paths.DatabasePath))
        {
            return new SchemaMigrationStatusDto(
                MigrationSchema: "grow-os.schema-migrations.v1",
                CurrentSchemaVersion: DatabaseInitializer.CurrentSchemaVersion,
                StoredSchemaVersion: null,
                CheckedAtUtc: DateTime.UtcNow,
                MigrationTableExists: false,
                IsCurrent: false,
                AppliedMigrations: Array.Empty<AppliedSchemaMigrationDto>(),
                PendingMigrations: required.Select(m => new PendingSchemaMigrationDto(m.Id, m.Name, m.RequiredForSchemaVersion)).ToArray(),
                Warnings: new[] { "Datenbankdatei existiert noch nicht." });
        }

        using var connection = OpenReadConnection();
        storedSchemaVersion = ReadAppSetting(connection, DatabaseInitializer.CurrentSchemaAppSettingKey);
        tableExists = TableExists(connection, "AppliedSchemaMigrations");
        if (tableExists)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, RequiredForSchemaVersion, AppliedAtUtc FROM AppliedSchemaMigrations ORDER BY Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var rawAppliedAt = reader["AppliedAtUtc"]?.ToString();
                DateTime? appliedAt = DateTime.TryParse(rawAppliedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                    ? parsed
                    : null;
                applied.Add(new AppliedSchemaMigrationDto(
                    Id: reader["Id"]?.ToString() ?? string.Empty,
                    Name: reader["Name"]?.ToString() ?? string.Empty,
                    RequiredForSchemaVersion: reader["RequiredForSchemaVersion"]?.ToString() ?? string.Empty,
                    AppliedAtUtc: appliedAt));
            }
        }
        else
        {
            warnings.Add("AppliedSchemaMigrations-Tabelle fehlt.");
        }

        var appliedIds = applied.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = required
            .Where(m => !appliedIds.Contains(m.Id))
            .Select(m => new PendingSchemaMigrationDto(m.Id, m.Name, m.RequiredForSchemaVersion))
            .ToArray();

        if (!string.Equals(storedSchemaVersion, DatabaseInitializer.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            warnings.Add("Gespeicherte Schema-Version weicht von der erwarteten Backend-Version ab.");
        }
        if (pending.Length > 0)
        {
            warnings.Add("Es gibt offene Schema-Migrationen.");
        }

        return new SchemaMigrationStatusDto(
            MigrationSchema: "grow-os.schema-migrations.v1",
            CurrentSchemaVersion: DatabaseInitializer.CurrentSchemaVersion,
            StoredSchemaVersion: storedSchemaVersion,
            CheckedAtUtc: DateTime.UtcNow,
            MigrationTableExists: tableExists,
            IsCurrent: tableExists && pending.Length == 0 && string.Equals(storedSchemaVersion, DatabaseInitializer.CurrentSchemaVersion, StringComparison.Ordinal),
            AppliedMigrations: applied,
            PendingMigrations: pending,
            Warnings: warnings);
    }


    private static T ExtractOk<T>(ActionResult<T> actionResult)
    {
        if (actionResult.Result is OkObjectResult ok && ok.Value is T value)
        {
            return value;
        }

        throw new InvalidOperationException($"Expected OkObjectResult<{typeof(T).Name}>.");
    }

}
