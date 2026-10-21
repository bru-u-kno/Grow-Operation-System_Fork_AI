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
    [HttpPost("import-plan")]
    [ProducesResponseType(typeof(GrowImportPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<GrowImportPlanDto> CreateImportPlan([FromBody] GrowExportDto? export)
    {
        if (export is null)
        {
            return BadRequestError("invalid_export", "Export konnte nicht gelesen werden.");
        }

        var plan = BuildImportPlan(export, wouldModifyDatabase: false);
        LogExportAudit(
            action: "grow-import-plan-created",
            summary: plan.Blockers.Count == 0 ? "Grow-Import-Plan erfolgreich erstellt." : "Grow-Import-Plan mit Blockern erstellt.",
            success: plan.Blockers.Count == 0,
            relatedGrowId: export.Grow?.Id,
            relatedFileName: export.ExportId,
            severity: plan.Blockers.Count == 0 ? "info" : "warning");
        return Ok(plan);
    }


    private GrowImportPlanDto BuildImportPlan(GrowExportDto export, bool wouldModifyDatabase)
    {
        var validation = BuildValidation(export);
        var blockers = new List<string>();
        var warnings = new List<string>(validation.Warnings);
        var conflicts = new List<GrowImportPlanConflictDto>();
        var plannedItems = new List<GrowImportPlanItemDto>();

        if (!validation.IsValid)
        {
            blockers.AddRange(validation.Errors);
        }
        if (export.Grow is null)
        {
            blockers.Add("Export enthält keinen Grow-Datensatz.");
        }

        var source = new GrowImportPlanSourceDto(
            OriginalGrowId: export.Grow?.Id,
            GrowName: export.Grow?.Name,
            TentName: export.TentSnapshot?.Name,
            HydroSetupName: export.HydroSetupSnapshot?.Name,
            ExportedAtUtc: export.ExportedAtUtc);

        if (validation.IsValid && export.Grow is not null)
        {
            var existingGrows = _repository.GetAllGrows();
            var sameNameAndStart = existingGrows.FirstOrDefault(grow =>
                string.Equals(grow.Name, export.Grow.Name, StringComparison.OrdinalIgnoreCase)
                && grow.StartDate.Date == export.Grow.StartDate.Date);
            if (sameNameAndStart is not null)
            {
                conflicts.Add(new GrowImportPlanConflictDto(
                    Kind: "possible-duplicate-grow",
                    Severity: "warning",
                    Message: $"Ein lokaler Grow mit gleichem Namen und Startdatum existiert bereits (Id {sameNameAndStart.Id}). Der Import legt trotzdem eine neue lokale Grow-Id an."));
            }

            if (_repository.GetGrow(export.Grow.Id) is not null)
            {
                conflicts.Add(new GrowImportPlanConflictDto(
                    Kind: "source-id-conflict",
                    Severity: "info",
                    Message: "Die Original-Grow-Id existiert lokal bereits. Der Import vergibt immer eine neue lokale Id."));
            }

            if (export.Anonymized)
            {
                warnings.Add("Der Export ist anonymisiert. Importierte Vergleichsdaten enthalten keine vollständigen Nutzer-/Strain-/Geräteangaben.");
            }

            warnings.Add(wouldModifyDatabase
                ? "Import legt einen neuen lokalen Grow an und überschreibt keine bestehenden Grows, Zelte oder HydroSetups."
                : "Import-Plan ist ein Dry-Run. Es werden keine Daten geschrieben.");

            plannedItems.Add(new GrowImportPlanItemDto("grow", "create-new-local-grow", 1, "Grow wird mit neuer lokaler ID importiert."));
            plannedItems.Add(new GrowImportPlanItemDto("tent-snapshot", "store-on-imported-grow", export.TentSnapshot is null ? 0 : 1, "Zelt-Snapshot wird am importierten Grow gespeichert und erzeugt kein produktives Zelt."));
            plannedItems.Add(new GrowImportPlanItemDto("hydro-setup-snapshot", "store-on-imported-grow", export.HydroSetupSnapshot is null ? 0 : 1, "HydroSetup-Snapshot wird am importierten Grow gespeichert und erzeugt kein produktives HydroSetup."));
            plannedItems.Add(new GrowImportPlanItemDto("measurements", "import-for-new-grow", export.Measurements?.Count ?? 0, null));
            plannedItems.Add(new GrowImportPlanItemDto("journal", "import-for-new-grow", export.JournalEntries?.Count ?? 0, "Messungsreferenzen werden auf neue lokale Measurement-Ids gemappt, falls vorhanden."));
            plannedItems.Add(new GrowImportPlanItemDto("tasks", "import-as-history", export.Tasks?.Count ?? 0, "Tasks werden historisch importiert und nicht als aktive Reminder geöffnet."));
            plannedItems.Add(new GrowImportPlanItemDto("hardware", "skip-active-inventory", export.HardwareItems?.Count ?? 0, "Hardware wird nicht als aktives lokales Inventar angelegt."));
            plannedItems.Add(new GrowImportPlanItemDto("harvest", "import-for-new-grow", export.Harvest is null ? 0 : 1, null));
            plannedItems.Add(new GrowImportPlanItemDto("addback-logs", "import-for-new-grow", export.AddbackLogs?.Count ?? 0, "HydroSetupId wird gelöst, weil kein lokales Live-HydroSetup angelegt wird."));
            plannedItems.Add(new GrowImportPlanItemDto("changeouts", "import-for-new-grow", export.Changeouts?.Count ?? 0, "HydroSetupId wird gelöst, weil kein lokales Live-HydroSetup angelegt wird."));
            plannedItems.Add(new GrowImportPlanItemDto("photos", "skip-metadata-only", export.Photos?.Count ?? 0, "JSON-Export enthält nur Foto-Metadaten, keine Bilddateien."));
        }

        return new GrowImportPlanDto(
            ImportPlanSchema: "grow-os.grow-import-plan.v1",
            CheckedAtUtc: DateTime.UtcNow,
            ExportValid: validation.IsValid,
            ImportSupported: blockers.Count == 0,
            WouldModifyDatabase: wouldModifyDatabase && blockers.Count == 0,
            IsAnonymized: export.Anonymized,
            ExportId: export.ExportId,
            ExportSchemaVersion: export.SchemaVersion,
            IntegrityHash: export.IntegrityHash,
            Source: source,
            SectionCounts: validation.ActualSectionCounts,
            PlannedItems: plannedItems,
            Conflicts: conflicts,
            Blockers: blockers,
            Warnings: warnings);
    }

}
