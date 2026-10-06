using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Fork AI (A-010): <see cref="IProbelaufHa"/> über die vorhandenen Wege zu Home Assistant.</summary>
/// <remarks>
/// Das Ausschalten läuft über den <see cref="Ausschalter"/> (Nachkontrolle und Wiederholung — die AC-Wolke
/// verwirft Aufträge auch still); das Wiederherstellen und das Schalten von Automationen folgt demselben
/// Muster: senden, nachlesen, bei Bedarf wiederholen.
/// </remarks>
public sealed class ProbelaufHa : IProbelaufHa
{
    private readonly HomeAssistantSettingsRepository _einstellungen;
    private readonly HomeAssistantService _ha;
    private readonly IAcFunk _funk;
    private readonly Ausschalter _ausschalter;
    private readonly SteuerungGeraeteService _geraete;
    private readonly Func<TimeSpan, CancellationToken, Task> _warten;

    public ProbelaufHa(
        HomeAssistantSettingsRepository einstellungen,
        HomeAssistantService ha,
        IAcFunk funk,
        Ausschalter ausschalter,
        SteuerungGeraeteService geraete,
        Func<TimeSpan, CancellationToken, Task>? warten = null)
    {
        _einstellungen = einstellungen;
        _ha = ha;
        _funk = funk;
        _ausschalter = ausschalter;
        _geraete = geraete;
        _warten = warten ?? ((dauer, token) => Task.Delay(dauer, token));
    }

    private HomeAssistantSettings Einstellungen => _einstellungen.GetEffectiveHomeAssistantSettings();

    public async Task<IReadOnlyList<string>> AutomationenAsync(string modul, ProbelaufRolle rolle, CancellationToken ct)
    {
        var bauteile = SteuerungBauteile.FuerModul(modul)
            .Where(b => b.Art == BauteilArt.Automation && b.Probelauf == rolle).ToList();
        if (bauteile.Count == 0) return [];

        var alle = await _ha.GetEntitiesAsync(Einstellungen, ct);
        return bauteile
            .SelectMany(b => SteuerungBauteile.AutomationFinden(b, alle))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? Entity(string modul, string rolle) => _geraete.Entity(modul, rolle);

    public async Task<string?> ZustandAsync(string entityId, CancellationToken ct)
        => (await _funk.ZustandAsync(Einstellungen, entityId, ct))?.State;

    public async Task<bool> AutomationSetzenAsync(string entityId, bool an, CancellationToken ct)
    {
        var dienst = an ? "turn_on" : "turn_off";
        return await SendenUndBestaetigenAsync(entityId, "automation", dienst, new Dictionary<string, object>(),
            ist => string.Equals(ist, an ? "on" : "off", StringComparison.OrdinalIgnoreCase), ct);
    }

    public Task<bool> AusschaltenAsync(string entityId, CancellationToken ct)
        => _ausschalter.AusschaltenAsync(Einstellungen, entityId, _warten);

    public async Task<bool> ZustandHerstellenAsync(string entityId, string zustand, CancellationToken ct)
    {
        var domain = entityId.Split('.', 2)[0];
        string dienst;
        var daten = new Dictionary<string, object>();
        if (domain == "select")
        {
            dienst = "select_option";
            daten["option"] = zustand;
        }
        else if (string.Equals(zustand, "on", StringComparison.OrdinalIgnoreCase)) dienst = "turn_on";
        else if (string.Equals(zustand, "off", StringComparison.OrdinalIgnoreCase)) dienst = "turn_off";
        else return true; // unavailable, unknown …: dieser Zustand lässt sich nicht herstellen und wurde nie verändert

        return await SendenUndBestaetigenAsync(entityId, domain, dienst, daten,
            ist => string.Equals(ist, zustand, StringComparison.OrdinalIgnoreCase), ct);
    }

    /// <summary>Senden, nachlesen, bei Bedarf wiederholen — wie <see cref="Ausschalter"/>, nur für jedes Ziel.</summary>
    private async Task<bool> SendenUndBestaetigenAsync(
        string entityId, string domain, string dienst, IReadOnlyDictionary<string, object> daten,
        Func<string?, bool> istErreicht, CancellationToken ct)
    {
        var nachfragen = Math.Max(1, (int)Math.Ceiling(Ausschalter.Wartezeit / Ausschalter.Nachfragetakt));
        for (var versuch = 1; versuch <= Ausschalter.Versuche; versuch++)
        {
            await _funk.SchickenAsync(Einstellungen, domain, dienst, entityId, daten, CancellationToken.None);
            for (var frage = 1; frage <= nachfragen; frage++)
            {
                await _warten(Ausschalter.Nachfragetakt, CancellationToken.None);
                var ist = (await _funk.ZustandAsync(Einstellungen, entityId, CancellationToken.None))?.State;
                if (istErreicht(ist)) return true;
            }
        }
        return false;
    }
}
