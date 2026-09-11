using System.Text.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Fork AI: welche Ziele in der Leiste am oberen Rand stehen und in welcher Reihenfolge.
///
/// Liegt im Server und nicht im Browser-Speicher, weil dieselbe Leiste am Telefon
/// und am Schreibtisch gleich aussehen soll. Wer sie am Handy sortiert, findet sie
/// am Notebook genauso vor — sonst muesste man die Reihenfolge zweimal pflegen und
/// haette bei jedem geleerten Browser-Speicher wieder die Werkseinstellung.
///
/// Bewusst OHNE Pruefung gegen eine Liste erlaubter Pfade: die Ziele stehen im
/// Frontend (navigation.ts), und eine zweite Liste hier waere die zweite Wahrheit,
/// die beim naechsten neuen Menuepunkt auseinanderlaeuft. Das Frontend wirft
/// unbekannte Pfade beim Lesen weg — dort weiss man, welche es gibt.
/// </summary>
[ApiController]
[Route("api/navbar")]
[Produces("application/json")]
public sealed class NavBarApiController : ApiControllerBase
{
    /// <summary>Schluessel in der AppSettings-Tabelle.</summary>
    public const string SettingsKey = "forkai.navbar.order";

    /// <summary>Wohin das Haus-Zeichen zurueckspringt.</summary>
    public const string DashboardKey = "forkai.navbar.hadashboard";

    /// <summary>
    /// Voreinstellung fuer den Ruecksprung: die Startseite von Home Assistant.
    ///
    /// Nicht auf ein bestimmtes Dashboard festgenagelt — das heisst bei jedem
    /// anders. Wer ein Lieblings-Dashboard hat, traegt es in den Einstellungen
    /// ein; „lovelace" ist die Adresse, die jede Installation hat.
    /// </summary>
    public const string DashboardDefault = "/lovelace/0";

    /// <summary>Wie viele Ziele hoechstens in die Leiste passen (das sechste Feld ist „Mehr“).</summary>
    public const int MaxItems = 6; // Fork AI (forkai.20): sechs Ziele, damit „Steuerung" neben den fünf Bewährten Platz hat

    private readonly AppSettingsRepository _settings;

    public NavBarApiController(AppSettingsRepository settings)
    {
        _settings = settings;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(NavBarDto), StatusCodes.Status200OK)]
    public ActionResult<NavBarDto> Get()
    {
        var dashboard = _settings.GetValue(DashboardKey);
        if (string.IsNullOrWhiteSpace(dashboard)) dashboard = DashboardDefault;

        var raw = _settings.GetValue(SettingsKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Keine Vorgabe ist ein gueltiger Zustand, kein Fehler: dann gilt,
            // was das Frontend als Werkseinstellung mitbringt.
            return Ok(new NavBarDto(null, dashboard));
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<string>>(raw);
            return Ok(new NavBarDto(items, dashboard));
        }
        catch (JsonException)
        {
            // Kaputter Eintrag darf die Navigation nicht lahmlegen — dann eben
            // die Werkseinstellung, statt einer Fehlerseite ohne jedes Menue.
            return Ok(new NavBarDto(null, dashboard));
        }
    }

    [HttpPut("")]
    [ProducesResponseType(typeof(NavBarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<NavBarDto> Save([FromBody] SaveNavBarRequest request)
    {
        if (request is null)
        {
            return BadRequestError("navbar_invalid", "Es wurde nichts uebergeben.");
        }

        // Das Ruecksprungziel kommt eigenstaendig: wer nur das Dashboard
        // aendert, schickt keine Reihenfolge mit und darf seine trotzdem
        // behalten.
        if (request.DashboardPath is not null)
        {
            var neuesZiel = request.DashboardPath.Trim();
            _settings.SetValue(DashboardKey, neuesZiel.Length == 0 ? null : neuesZiel);
        }

        var dashboard = _settings.GetValue(DashboardKey);
        if (string.IsNullOrWhiteSpace(dashboard)) dashboard = DashboardDefault;

        if (request.Items is null)
        {
            // Nur das Dashboard geaendert — Reihenfolge unangetastet lassen.
            var bestand = _settings.GetValue(SettingsKey);
            List<string>? unveraendert = null;
            if (!string.IsNullOrWhiteSpace(bestand))
            {
                try { unveraendert = JsonSerializer.Deserialize<List<string>>(bestand); }
                catch (JsonException) { unveraendert = null; }
            }
            return Ok(new NavBarDto(unveraendert, dashboard));
        }

        // Leere Liste heisst „zurueck auf Werkseinstellung“ — das ist der Knopf
        // „Standard wiederherstellen“ und kein Fehlversuch.
        if (request.Items.Count == 0)
        {
            _settings.SetValue(SettingsKey, null);
            return Ok(new NavBarDto(null, dashboard));
        }

        var bereinigt = new List<string>();
        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            var pfad = item.Trim();
            // Doppelte wuerden in der Leiste zweimal auftauchen und in React
            // denselben Schluessel tragen.
            if (bereinigt.Contains(pfad, StringComparer.Ordinal)) continue;
            bereinigt.Add(pfad);
            if (bereinigt.Count >= MaxItems) break;
        }

        if (bereinigt.Count == 0)
        {
            return BadRequestError("navbar_invalid", "Die Reihenfolge enthielt kein gueltiges Ziel.");
        }

        _settings.SetValue(SettingsKey, JsonSerializer.Serialize(bereinigt));
        return Ok(new NavBarDto(bereinigt, dashboard));
    }
}

/// <param name="Items">Pfade in der gewuenschten Reihenfolge; <c>null</c> heisst Werkseinstellung.</param>
/// <param name="DashboardPath">Ziel des Haus-Zeichens in der Titelzeile.</param>
public sealed record NavBarDto(IReadOnlyList<string>? Items, string DashboardPath);

public sealed class SaveNavBarRequest
{
    public List<string>? Items { get; set; }

    /// <summary><c>null</c> laesst das gespeicherte Ziel unangetastet.</summary>
    public string? DashboardPath { get; set; }
}
