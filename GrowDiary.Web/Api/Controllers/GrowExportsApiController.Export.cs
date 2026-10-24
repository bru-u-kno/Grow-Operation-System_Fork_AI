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
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(GrowExportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowExportDto> ExportGrow(int id, [FromQuery] bool anonymize = false, [FromQuery] bool includePhotoMetadata = true)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            LogExportAudit("grow-export-requested", $"Grow-Export für fehlenden Grow #{id} angefordert.", false, relatedGrowId: id, severity: "warning");
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        var warnings = new List<string>();
        var growDto = grow.ToDetailDto();
        if (anonymize)
        {
            growDto = growDto with
            {
                Name = $"Grow {grow.Id}",
                Strain = null,
                Breeder = null,
                CloneSource = null,
                Nutrients = null,
                Notes = null,
                LatestPhotoPath = null
            };
            warnings.Add("Export wurde anonymisiert: Name, Sorte, Breeder, Clone-Quelle, Nährstoffnotizen und freie Grow-Notizen wurden entfernt.");
        }

        var tentDto = TryReadTentSnapshotDto(grow.TentSnapshotJson);
        if (tentDto is null && grow.TentId.HasValue)
        {
            tentDto = _repository.GetTent(grow.TentId.Value)?.ToDto();
            if (!string.IsNullOrWhiteSpace(grow.TentSnapshotJson))
            {
                warnings.Add("Gespeicherter Zelt-Snapshot konnte nicht gelesen werden; Export nutzt aktuelle Zeltdaten als Fallback.");
            }
            else if (tentDto is not null)
            {
                warnings.Add("Legacy-Grow ohne gespeicherten Zelt-Snapshot; Export nutzt aktuelle Zeltdaten.");
            }
        }
        if (anonymize && tentDto is not null)
        {
            tentDto = tentDto with
            {
                Name = $"Zelt {tentDto.Id}",
                Notes = null,
                LightControllerEntityId = null,
                HvacControllerEntityId = null,
                CameraEntityId = null,
                Sensors = tentDto.Sensors.Select(sensor => sensor with
                {
                    HaEntityId = string.Empty,
                    DisplayLabel = null
                }).ToList()
            };
        }

        var hydroSetupDto = TryReadHydroSetupSnapshotDto(grow.HydroSetupSnapshotJson);
        if (hydroSetupDto is null && grow.SystemId.HasValue)
        {
            hydroSetupDto = _repository.GetHydroSetup(grow.SystemId.Value)?.ToDto();
            if (!string.IsNullOrWhiteSpace(grow.HydroSetupSnapshotJson))
            {
                warnings.Add("Gespeicherter HydroSetup-Snapshot konnte nicht gelesen werden; Export nutzt aktuelle HydroSetup-Daten als Fallback.");
            }
            else if (hydroSetupDto is not null)
            {
                warnings.Add("Legacy-Grow ohne gespeicherten HydroSetup-Snapshot; Export nutzt aktuelle HydroSetup-Daten.");
            }
        }
        if (anonymize && hydroSetupDto is not null)
        {
            hydroSetupDto = hydroSetupDto with
            {
                Name = $"HydroSetup {hydroSetupDto.Id}",
                CirculationPumpNotes = null,
                AirPumpNotes = null,
                Notes = null
            };
        }

        var hardwareItems = _repository.GetHardwareItems()
            .Where(item => item.GrowId == id || (grow.SystemId.HasValue && item.HydroSetupId == grow.SystemId.Value))
            .Select(item => item.ToDto())
            .ToList();
        if (anonymize)
        {
            hardwareItems = hardwareItems.Select(item => item with
            {
                Name = $"Hardware {item.Id}",
                HaEntityId = null,
                Manufacturer = null,
                Model = null,
                SerialNumber = null,
                Notes = null
            }).ToList();
        }

        var harvest = _harvestRepository.GetForGrow(id)?.ToDto(growDto.Name);
        var photos = includePhotoMetadata
            ? _repository.GetPhotosForGrow(id).Select(photo => photo.ToDto()).ToList()
            : new List<PhotoAssetDto>();
        if (anonymize && photos.Count > 0)
        {
            photos = photos.Select(photo => photo with
            {
                RelativePath = string.Empty,
                Caption = null
            }).ToList();
            warnings.Add("Fotodateipfade wurden im anonymisierten Export entfernt. Bilddateien selbst sind nicht Teil dieses JSON-Exports.");
        }

        if (!grow.SystemId.HasValue)
        {
            warnings.Add("Grow hat kein HydroSetup. Technische Systemdaten stammen aus Legacy-Grow-Feldern.");
        }

        var measurements = _repository.GetMeasurementsForGrow(id).Select(measurement => measurement.ToDto()).ToList();
        var journalEntries = _journalRepository.GetForGrow(id).Select(entry => entry.ToDto()).ToList();
        var tasks = _taskRepository.GetForGrow(id).Select(task => task.ToDto()).ToList();
        var addbackLogs = _repository.GetAddbackLogsForGrow(id).Select(entry => entry.ToDto()).ToList();
        var changeouts = _repository.GetChangeoutsForGrow(id).Select(entry => entry.ToDto()).ToList();
        var exportedAtUtc = DateTime.UtcNow;
        var sectionCounts = new GrowExportSectionCountsDto(
            Measurements: measurements.Count,
            JournalEntries: journalEntries.Count,
            Tasks: tasks.Count,
            HardwareItems: hardwareItems.Count,
            AddbackLogs: addbackLogs.Count,
            Changeouts: changeouts.Count,
            Photos: photos.Count);

        var export = new GrowExportDto(
            SchemaVersion: ExportSchemaVersion,
            ExportId: $"grow-{grow.Id}-{exportedAtUtc:yyyyMMddHHmmssfff}",
            ExportedAtUtc: exportedAtUtc,
            Anonymized: anonymize,
            IntegrityHash: string.Empty,
            SectionCounts: sectionCounts,
            Grow: growDto,
            TentSnapshot: tentDto,
            HydroSetupSnapshot: hydroSetupDto,
            Measurements: measurements,
            JournalEntries: journalEntries,
            Tasks: tasks,
            HardwareItems: hardwareItems,
            Harvest: harvest,
            AddbackLogs: addbackLogs,
            Changeouts: changeouts,
            Photos: photos,
            Warnings: warnings);

        var finalExport = export with { IntegrityHash = ComputeIntegrityHash(export) };
        LogExportAudit(
            action: "grow-export-created",
            summary: anonymize ? "Anonymisierter Grow-Export erstellt." : "Grow-Export erstellt.",
            success: true,
            relatedGrowId: id,
            relatedFileName: finalExport.ExportId);
        return Ok(finalExport);
    }

}
