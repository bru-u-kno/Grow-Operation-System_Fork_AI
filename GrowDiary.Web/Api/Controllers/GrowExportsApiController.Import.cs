using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

public sealed partial class GrowExportsApiController
{
    [HttpPost("import")]
    [ProducesResponseType(typeof(GrowImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status500InternalServerError)]
    public ActionResult<GrowImportResultDto> ImportGrow([FromBody] GrowExportDto? export)
    {
        if (export is null)
        {
            return BadRequestError("invalid_export", "Export konnte nicht gelesen werden.");
        }

        var plan = BuildImportPlan(export, wouldModifyDatabase: true);
        if (plan.Blockers.Count > 0 || !plan.ExportValid || !plan.ImportSupported)
        {
            LogExportAudit(
                action: "grow-import-blocked",
                summary: "Grow-Import wurde durch Preflight-Blocker verhindert.",
                success: false,
                relatedGrowId: export.Grow?.Id,
                relatedFileName: export.ExportId,
                severity: "warning");
            return BadRequestError("import_blocked", "Import wurde blockiert: " + string.Join(" ", plan.Blockers.DefaultIfEmpty("Export ist nicht import-ready.")));
        }

        var safetyBackup = CreateImportSafetyBackup();
        if (safetyBackup is null)
        {
            LogExportAudit(
                action: "grow-import-safety-backup-failed",
                summary: "Grow-Import wurde abgebrochen, weil kein Safety-Backup erstellt werden konnte.",
                success: false,
                relatedGrowId: export.Grow?.Id,
                relatedFileName: export.ExportId,
                severity: "error");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiErrorFactory.ServerError("import_safety_backup_failed", "Safety-Backup konnte vor dem Import nicht erstellt werden.", HttpContext?.TraceIdentifier));
        }

        int? importedGrowId = null;
        try
        {
            var warnings = new List<string>(plan.Warnings);
            if ((export.HardwareItems?.Count ?? 0) > 0)
            {
                warnings.Add("Hardware wurde bewusst nicht als aktives Inventar importiert; die Daten bleiben im Export-Snapshot/Importkontext.");
            }
            if ((export.Photos?.Count ?? 0) > 0)
            {
                warnings.Add("Foto-Metadaten wurden bewusst nicht importiert, weil JSON-Exporte keine Bilddateien enthalten.");
            }

            var grow = ToImportedGrowRun(export);
            importedGrowId = _repository.CreateGrow(grow);

            var measurementIdMap = new Dictionary<int, int>();
            var importedMeasurements = 0;
            foreach (var measurement in export.Measurements ?? Array.Empty<MeasurementDto>())
            {
                var importedId = _repository.CreateMeasurement(ToImportedMeasurement(measurement, importedGrowId.Value));
                measurementIdMap[measurement.Id] = importedId;
                importedMeasurements++;
            }

            var importedJournalEntries = 0;
            foreach (var entry in export.JournalEntries ?? Array.Empty<JournalEntryDto>())
            {
                _journalRepository.Create(ToImportedJournalEntry(entry, importedGrowId.Value, measurementIdMap));
                importedJournalEntries++;
            }

            var importedTasks = 0;
            foreach (var task in export.Tasks ?? Array.Empty<GrowTaskDto>())
            {
                _taskRepository.Create(ToImportedHistoricalTask(task, importedGrowId.Value));
                importedTasks++;
            }

            var importedAddbackLogs = 0;
            foreach (var entry in export.AddbackLogs ?? Array.Empty<AddbackLogDto>())
            {
                _repository.CreateAddbackLog(ToImportedAddbackLog(entry, importedGrowId.Value));
                importedAddbackLogs++;
            }

            var importedChangeouts = 0;
            foreach (var entry in export.Changeouts ?? Array.Empty<ChangeoutDto>())
            {
                _repository.CreateChangeout(ToImportedChangeout(entry, importedGrowId.Value));
                importedChangeouts++;
            }

            var importedHarvestEntries = 0;
            if (export.Harvest is not null)
            {
                _harvestRepository.Create(ToImportedHarvest(export.Harvest, importedGrowId.Value));
                importedHarvestEntries = 1;
            }

            var importedGrow = _repository.GetGrow(importedGrowId.Value);
            var result = new GrowImportResultDto(
                ImportSchema: "grow-os.grow-import.v1",
                ImportedAtUtc: DateTime.UtcNow,
                Success: true,
                ExportId: export.ExportId,
                ImportedGrowId: importedGrowId.Value,
                ImportedGrowName: importedGrow?.Name ?? grow.Name,
                SafetyBackupFileName: safetyBackup.Value.FileName,
                SafetyBackupDownloadUrl: safetyBackup.Value.DownloadUrl,
                ImportedMeasurements: importedMeasurements,
                ImportedJournalEntries: importedJournalEntries,
                ImportedTasks: importedTasks,
                ImportedAddbackLogs: importedAddbackLogs,
                ImportedChangeouts: importedChangeouts,
                ImportedHarvestEntries: importedHarvestEntries,
                SkippedHardwareItems: export.HardwareItems?.Count ?? 0,
                SkippedPhotos: export.Photos?.Count ?? 0,
                Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

            LogExportAudit(
                action: "grow-import-executed",
                summary: $"Grow-Import erfolgreich ausgefuehrt. Neue lokale Grow-Id: {importedGrowId.Value}.",
                success: true,
                relatedGrowId: importedGrowId.Value,
                relatedFileName: export.ExportId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            if (importedGrowId.HasValue)
            {
                try { _repository.DeleteGrow(importedGrowId.Value); } catch { }
            }

            LogExportAudit(
                action: "grow-import-failed",
                summary: "Grow-Import fehlgeschlagen: " + ex.Message,
                success: false,
                relatedGrowId: importedGrowId,
                relatedFileName: export.ExportId,
                severity: "error");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiErrorFactory.ServerError("import_failed", "Import ist fehlgeschlagen. Safety-Backup wurde erstellt: " + safetyBackup.Value.FileName + ". Fehler: " + ex.Message, HttpContext?.TraceIdentifier));
        }
    }


    private GrowRun ToImportedGrowRun(GrowExportDto export)
    {
        var source = export.Grow ?? throw new InvalidOperationException("Export enthaelt keinen Grow-Datensatz.");
        var importedName = string.IsNullOrWhiteSpace(source.Name)
            ? $"Import {export.ExportId}"
            : source.Name.Trim() + " (Import)";

        return new GrowRun
        {
            TentId = null,
            SystemId = null,
            SetupId = null,
            Name = importedName,
            Strain = source.Strain,
            Breeder = source.Breeder,
            Status = source.Status,
            MediumType = source.MediumType,
            FeedingStyle = source.FeedingStyle,
            HydroStyle = source.HydroStyle,
            IrrigationType = source.IrrigationType,
            WaterSource = source.WaterSource,
            Environment = source.Environment,
            Light = source.Light,
            ContainerSize = source.ContainerSize,
            ReservoirSize = source.ReservoirSize,
            MediumDetail = source.MediumDetail,
            IrrigationStyle = source.IrrigationStyle,
            HasChiller = source.HasChiller,
            SeedType = source.SeedType,
            StartMaterial = source.StartMaterial,
            GerminationMethod = source.GerminationMethod,
            PropagationMedium = source.PropagationMedium,
            CloneSource = source.CloneSource,
            CloneIsRooted = source.CloneIsRooted,
            BreederFlowerWeeksMin = source.BreederFlowerWeeksMin,
            BreederFlowerWeeksMax = source.BreederFlowerWeeksMax,
            PlannedVegDays = source.PlannedVegDays,
            // StrainId absichtlich NICHT: die Ziel-Installation hat andere
            // Sorten-IDs — ein fremder Verweis zeigte auf irgendetwas.
            // Name und Breeder reisen als Text mit und reichen zum Zuordnen.
            PlantCount = source.PlantCount,
            PhenoNumber = source.PhenoNumber,
            EntryPoint = source.EntryPoint,
            DaysAlreadyInPhase = source.DaysAlreadyInPhase,
            AutoflowerDaysSinceGermination = source.AutoflowerDaysSinceGermination,
            StartDate = source.StartDate.Date,
            EndDate = source.EndDate?.Date,
            FlipDate = source.FlipDate?.Date,
            GerminatedAt = source.GerminatedAt,
            RootedAt = source.RootedAt,
            Nutrients = source.Nutrients,
            Notes = AppendImportNote(source.Notes, export),
            TentSnapshotJson = export.TentSnapshot is null ? null : JsonSerializer.Serialize(ToTentSnapshot(export.TentSnapshot), ExportJsonOptions),
            HydroSetupSnapshotJson = export.HydroSetupSnapshot is null ? null : JsonSerializer.Serialize(ToHydroSetupSnapshot(export.HydroSetupSnapshot), ExportJsonOptions),
            SnapshotsCapturedAtUtc = DateTime.UtcNow
        };
    }


    private static string? AppendImportNote(string? existingNotes, GrowExportDto export)
    {
        var marker = $"Importiert aus Grow-Export {export.ExportId} vom {export.ExportedAtUtc:O}.";
        return string.IsNullOrWhiteSpace(existingNotes) ? marker : existingNotes.Trim() + Environment.NewLine + marker;
    }


    private static Measurement ToImportedMeasurement(MeasurementDto dto, int growId)
        => new()
        {
            GrowId = growId,
            TakenAt = dto.TakenAt,
            Stage = dto.Stage,
            Source = ValueOrigin.Imported,
            Notes = dto.Notes,
            AirTemperatureC = dto.AirTemperatureC,
            HumidityPercent = dto.HumidityPercent,
            HeightCm = dto.HeightCm,
            WaterAmountMl = dto.WaterAmountMl,
            RunoffAmountMl = dto.RunoffAmountMl,
            IrrigationPh = dto.IrrigationPh,
            IrrigationEc = dto.IrrigationEc,
            DrainPh = dto.DrainPh,
            DrainEc = dto.DrainEc,
            ReservoirPh = dto.ReservoirPh,
            ReservoirEc = dto.ReservoirEc,
            ReservoirWaterTempC = dto.ReservoirWaterTempC,
            ReservoirLevelCm = dto.ReservoirLevelCm,
            ReservoirLevelLiters = dto.ReservoirLevelLiters,
            DissolvedOxygenMgL = dto.DissolvedOxygenMgL,
            OrpMv = dto.OrpMv,
            TopOffLiters = dto.TopOffLiters,
            AddbackEc = dto.AddbackEc,
            SolutionChange = dto.SolutionChange,
            PpfdMol = dto.PpfdMol,
            Co2Ppm = dto.Co2Ppm
        };


    private static JournalEntry ToImportedJournalEntry(JournalEntryDto dto, int growId, IReadOnlyDictionary<int, int> measurementIdMap)
        => new()
        {
            GrowId = growId,
            MeasurementId = dto.MeasurementId.HasValue && measurementIdMap.TryGetValue(dto.MeasurementId.Value, out var mappedId) ? mappedId : null,
            Title = dto.Title,
            Body = dto.Body,
            EntryType = dto.EntryType,
            Source = ValueOrigin.Imported,
            OccurredAtUtc = dto.OccurredAtUtc
        };


    private static GrowTask ToImportedHistoricalTask(GrowTaskDto dto, int growId)
        => new()
        {
            GrowId = growId,
            Title = dto.Title,
            Notes = string.IsNullOrWhiteSpace(dto.Notes)
                ? $"Import-Historie. Ursprünglicher Status: {dto.Status}."
                : dto.Notes.Trim() + Environment.NewLine + $"Import-Historie. Ursprünglicher Status: {dto.Status}.",
            DueAtUtc = dto.DueAtUtc,
            Priority = dto.Priority,
            Status = GrowTaskStatus.Done,
            CompletedAtUtc = dto.CompletedAtUtc ?? DateTime.UtcNow
        };


    private static AddbackLogEntry ToImportedAddbackLog(AddbackLogDto dto, int growId)
        => new()
        {
            GrowId = growId,
            HydroSetupId = null,
            Kind = dto.Kind,
            PerformedAtUtc = dto.PerformedAtUtc,
            ReservoirLiters = dto.ReservoirLiters,
            EcBefore = dto.EcBefore,
            EcTarget = dto.EcTarget,
            EcStock = dto.EcStock,
            EcAfter = dto.EcAfter,
            PhBefore = dto.PhBefore,
            PhAfter = dto.PhAfter,
            LitersAdded = dto.LitersAdded,
            NewReservoirVolumeLiters = dto.NewReservoirVolumeLiters,
            UsedHydroSetupVolume = dto.UsedHydroSetupVolume,
            Notes = dto.Notes
        };


    private static ChangeoutEntry ToImportedChangeout(ChangeoutDto dto, int growId)
        => new()
        {
            GrowId = growId,
            HydroSetupId = null,
            Kind = dto.Kind,
            PerformedAtUtc = dto.PerformedAtUtc,
            VolumeChangedLiters = dto.VolumeChangedLiters,
            PercentChanged = dto.PercentChanged,
            EcBefore = dto.EcBefore,
            EcAfter = dto.EcAfter,
            PhBefore = dto.PhBefore,
            PhAfter = dto.PhAfter,
            Notes = dto.Notes
        };


    private static HarvestEntry ToImportedHarvest(HarvestDto dto, int growId)
        => new()
        {
            GrowId = growId,
            HarvestedAt = DateTime.TryParse(dto.HarvestedAtLocal, out var harvestedAt) ? harvestedAt.Date : DateTime.Today,
            WetWeightG = dto.WetWeightG,
            DryWeightG = dto.DryWeightG,
            DryDays = dto.DryDays,
            YieldNotes = dto.YieldNotes,
            Rating = dto.Rating,
            FlavorNotes = dto.FlavorNotes,
            EffectNotes = dto.EffectNotes,
            NugStructure = dto.NugStructure,
            // Der Export schreibt das Feld hinaus, der Import warf es weg —
            // ein Ausfuhr/Einfuhr-Rundweg verlor die Einzelgewichte still.
            PlantWeightsJson = dto.PlantWeightsJson
        };


    private ImportSafetyBackup? CreateImportSafetyBackup()
    {
        try
        {
            var backupRoot = _paths.BackupsPath;
            Directory.CreateDirectory(backupRoot);
            var fileName = CreateUniqueImportSafetyBackupFileName(backupRoot);
            var backupPath = Path.Combine(backupRoot, fileName);

            using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
            {
                AddBackupEntryIfExists(archive, _paths.DatabasePath, "App_Data/grow-diary.db");
                AddBackupEntryIfExists(archive, _paths.DatabasePath + "-wal", "App_Data/grow-diary.db-wal");
                AddBackupEntryIfExists(archive, _paths.DatabasePath + "-shm", "App_Data/grow-diary.db-shm");
            }

            BackupAufbewahrung.Aufraeumen(backupRoot, new[] { fileName });
            return new ImportSafetyBackup(fileName, $"/api/system/backup/{Uri.EscapeDataString(fileName)}");
        }
        catch
        {
            return null;
        }
    }


    private static string CreateUniqueImportSafetyBackupFileName(string backupRoot)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : "-" + attempt.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            var fileName = $"grow-os-backup-import-safety-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}{suffix}.zip";
            if (!System.IO.File.Exists(Path.Combine(backupRoot, fileName)))
            {
                return fileName;
            }
        }

        return "grow-os-backup-import-safety-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip";
    }


    private static void AddBackupEntryIfExists(ZipArchive archive, string sourcePath, string entryName)
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


    private readonly record struct ImportSafetyBackup(string FileName, string DownloadUrl);

}
