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
    [HttpPost("validate")]
    [ProducesResponseType(typeof(GrowExportValidationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<GrowExportValidationDto> ValidateExport([FromBody] GrowExportDto? export)
    {
        if (export is null)
        {
            return BadRequestError("invalid_export", "Export konnte nicht gelesen werden.");
        }

        var validation = BuildValidation(export);
        LogExportAudit(
            action: "grow-export-validated",
            summary: validation.IsValid ? "Grow-Export erfolgreich validiert." : "Grow-Export-Validierung fehlgeschlagen.",
            success: validation.IsValid,
            relatedGrowId: export.Grow?.Id,
            relatedFileName: export.ExportId,
            severity: validation.IsValid ? "info" : "warning");
        return Ok(validation);
    }


    private static GrowExportValidationDto BuildValidation(GrowExportDto export)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!string.Equals(export.SchemaVersion, ExportSchemaVersion, StringComparison.Ordinal))
        {
            errors.Add($"Nicht unterstützte Export-Schema-Version: {export.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(export.ExportId))
        {
            errors.Add("ExportId fehlt.");
        }

        var actualCounts = CountSections(export);
        var sectionCountsValid = export.SectionCounts is not null && SectionCountsEqual(export.SectionCounts, actualCounts);
        if (!sectionCountsValid)
        {
            errors.Add("SectionCounts stimmen nicht mit dem Export-Inhalt überein.");
        }

        var expectedHash = ComputeIntegrityHash(export);
        var integrityHashValid = !string.IsNullOrWhiteSpace(export.IntegrityHash)
            && string.Equals(export.IntegrityHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        if (!integrityHashValid)
        {
            errors.Add("IntegrityHash ist ungültig oder fehlt.");
        }

        var containsPotentialSecrets = ContainsPotentialSecrets(export);
        if (containsPotentialSecrets)
        {
            errors.Add("Export enthält potenzielle Secrets und darf nicht importiert werden.");
        }

        if (export.HydroSetupSnapshot is null)
        {
            warnings.Add("Export enthält keinen HydroSetup-Snapshot. Vergleichbarkeit kann für Legacy-Grows eingeschränkt sein.");
        }

        return new GrowExportValidationDto(
            ValidationSchema: ExportValidationSchemaVersion,
            CheckedAtUtc: DateTime.UtcNow,
            ExportSchemaVersion: export.SchemaVersion,
            ExportId: export.ExportId,
            IsValid: errors.Count == 0,
            IntegrityHashValid: integrityHashValid,
            SectionCountsValid: sectionCountsValid,
            ContainsPotentialSecrets: containsPotentialSecrets,
            DeclaredSectionCounts: export.SectionCounts,
            ActualSectionCounts: actualCounts,
            Errors: errors,
            Warnings: warnings);
    }


    private static GrowExportSectionCountsDto CountSections(GrowExportDto export)
        => new(
            Measurements: export.Measurements?.Count ?? 0,
            JournalEntries: export.JournalEntries?.Count ?? 0,
            Tasks: export.Tasks?.Count ?? 0,
            HardwareItems: export.HardwareItems?.Count ?? 0,
            AddbackLogs: export.AddbackLogs?.Count ?? 0,
            Changeouts: export.Changeouts?.Count ?? 0,
            Photos: export.Photos?.Count ?? 0);


    private static bool SectionCountsEqual(GrowExportSectionCountsDto left, GrowExportSectionCountsDto right)
        => left.Measurements == right.Measurements
           && left.JournalEntries == right.JournalEntries
           && left.Tasks == right.Tasks
           && left.HardwareItems == right.HardwareItems
           && left.AddbackLogs == right.AddbackLogs
           && left.Changeouts == right.Changeouts
           && left.Photos == right.Photos;


    private static string ComputeIntegrityHash(GrowExportDto export)
    {
        var canonical = export with { IntegrityHash = string.Empty };
        var json = JsonSerializer.Serialize(canonical, ExportJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }


    private static bool ContainsPotentialSecrets(GrowExportDto export)
    {
        var json = JsonSerializer.Serialize(export, ExportJsonOptions);
        var forbiddenTerms = new[]
        {
            "ha-config",
            "access_token",
            "refresh_token",
            "bearer ",
            "dataProtectionKeys",
            "secret-token",
            "api-token"
        };

        return forbiddenTerms.Any(term => json.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
