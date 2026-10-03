using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Controllers;

/// <summary>
/// Alt-Routen der abgeloesten MVC-Oberflaeche. Lesen leitet um, Schreiben ist
/// stillgelegt — dasselbe Containment wie im GrowsController. Die beiden
/// Speichern-POSTs waren hier noch scharf, obwohl es laengst kein
/// Views-Verzeichnis mehr gibt: zwei Schreibpfade an der API-Validierung
/// vorbei, die niemand mehr erreichen sollte.
/// </summary>
[KeinKiZugriff("Stillgelegte Alt-Routen der MVC-Oberfläche — sie antworten nur noch 410; ein Assistent benutzt die API.")]
[Route("settings")]
public sealed class SettingsController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
        => Redirect("/einstellungen");

    [HttpPost(nameof(SaveHomeAssistant))]
    [ValidateAntiForgeryToken]
    public IActionResult SaveHomeAssistant() => LegacyMutationDisabled();

    [HttpPost(nameof(SaveTent) + "/{id:int}")]
    [ValidateAntiForgeryToken]
    public IActionResult SaveTent(int id) => LegacyMutationDisabled();

    [HttpGet("backup")]
    public IActionResult BackupDatabase()
        => StatusCode(
            StatusCodes.Status410Gone,
            ApiErrorFactory.Create(
                "legacy_backup_disabled",
                "Der direkte SQLite-Download wurde deaktiviert. Nutze POST /api/system/backup, damit Backups ohne Secrets, DataProtectionKeys, Uploads und Logs erzeugt werden.",
                StatusCodes.Status410Gone,
                traceId: HttpContext?.TraceIdentifier));

    private IActionResult LegacyMutationDisabled()
        => StatusCode(
            StatusCodes.Status410Gone,
            ApiErrorFactory.Create(
                "legacy_mvc_mutation_disabled",
                "Diese alte MVC-POST-Route wurde deaktiviert. Nutze die versionierten API-Endpunkte oder die aktuelle React/PWA-Oberfläche.",
                StatusCodes.Status410Gone,
                traceId: HttpContext?.TraceIdentifier));
}
