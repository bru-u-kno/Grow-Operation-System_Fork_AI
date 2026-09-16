using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Eine Wächter-Automation in Home Assistant, wie die Meldungs-Übersicht sie zeigt.</summary>
public sealed record HaWaechterDto(string EntityId, string Name, bool Aktiv, string? Zweck, bool Bekannt);

/// <summary>
/// Fork AI (Umbau „Ziele &amp; Meldungen“, Schritt 4): was außer Grow OS noch
/// Meldungen schickt.
/// </summary>
/// <remarks>
/// <para>Die Wächter der Steuerungen (Kühler, CO₂) laufen bewusst in Home
/// Assistant und senden von dort — sie sollen auch melden, wenn das Add-on
/// steht. Der Fork schaltet sie nicht und ändert sie nicht; er zeigt sie nur,
/// damit die Übersicht aller Absender vollständig ist.</para>
/// <para>Gefunden wird jede Automation mit „Wächter“ im Namen (auch „Wachter“,
/// „Waechter“) — so erscheinen später angelegte von selbst. Die bekannten
/// bekommen einen Satz dazu, was sie tun.</para>
/// </remarks>
[ApiController]
[Route("api/meldungen")]
[Produces("application/json")]
public sealed class MeldungenApiController : ApiControllerBase
{
    private static readonly Dictionary<string, string> Bekannte = new(StringComparer.OrdinalIgnoreCase)
    {
        [ChillerSteuerungService.Entitaeten.Waechter] =
            "Wasserfühler 5 min stumm → Kühler aus · Wasser über 24 °C für 15 min → Meldung",
        ["automation.co2_wachter_rdwc_port_5"] =
            "CO₂-Ventil länger als 90 s offen → wird zwangsweise geschlossen und gemeldet",
        ["automation.co2_wachter"] =
            "CO₂-Ventil länger als erlaubt offen → wird zwangsweise geschlossen und gemeldet",
    };

    private readonly HomeAssistantService _ha;
    private readonly GrowRepository _grows;

    public MeldungenApiController(HomeAssistantService ha, GrowRepository grows)
    {
        _ha = ha;
        _grows = grows;
    }

    /// <summary>Die Wächter-Automationen in Home Assistant (nur lesen).</summary>
    [HttpGet("ha-waechter")]
    [ProducesResponseType(typeof(IReadOnlyList<HaWaechterDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HaWaechterDto>>> HaWaechter(CancellationToken ct)
    {
        var entitaeten = await _ha.GetEntitiesAsync(_grows.GetEffectiveHomeAssistantSettings(), ct);
        return Ok(Auswahl(entitaeten.Select(e => (e.EntityId, e.FriendlyName, e.State))));
    }

    /// <summary>Die Auswahl selbst — ohne HA, damit sie sich prüfen lässt.</summary>
    public static List<HaWaechterDto> Auswahl(IEnumerable<(string EntityId, string? Name, string? State)> entitaeten)
        => entitaeten
            .Where(e => e.EntityId.StartsWith("automation.", StringComparison.OrdinalIgnoreCase))
            .Where(e => Bekannte.ContainsKey(e.EntityId) || IstWaechter(e.Name) || IstWaechter(e.EntityId))
            .Select(e => new HaWaechterDto(
                e.EntityId,
                string.IsNullOrWhiteSpace(e.Name) ? e.EntityId : e.Name!,
                string.Equals(e.State, "on", StringComparison.OrdinalIgnoreCase),
                Bekannte.GetValueOrDefault(e.EntityId),
                Bekannte.ContainsKey(e.EntityId)))
            .OrderByDescending(w => w.Bekannt)
            .ThenBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static bool IstWaechter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.ToLowerInvariant();
        return t.Contains("wächter") || t.Contains("waechter") || t.Contains("wachter");
    }
}
