using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (A-010, 07.10.2026): Die Messwerte des Zelts für den Probelauf — über die zentral zugeordneten
/// Zeltfühler (Rollen <c>zelt_rh</c>, <c>zelt_temp</c>, <c>zelt_vpd</c>, <c>licht_zustand</c>).
/// </summary>
/// <remarks>
/// Gelesen wird einzeln je Fühler und nicht über die ganze Entitätenliste: der Takt läuft alle fünf Sekunden,
/// und die Liste hat in einer echten Anlage hunderte Einträge. Die Grenzen-Voreinstellung (selten gebraucht)
/// kommt dagegen aus dem Livebild der Entfeuchter-Seite, die dieselben Pflanzenziele liest.
/// </remarks>
public sealed class ProbelaufMessung : IProbelaufMessung
{
    private const string Modul = EntfeuchterSteuerungService.Modul;

    private readonly HomeAssistantSettingsRepository _einstellungen;
    private readonly HomeAssistantService _ha;
    private readonly SteuerungGeraeteService _geraete;
    private readonly EntfeuchterSteuerungService _entfeuchter;
    private readonly TimeProvider _zeit;

    public ProbelaufMessung(
        HomeAssistantSettingsRepository einstellungen, HomeAssistantService ha, SteuerungGeraeteService geraete,
        EntfeuchterSteuerungService entfeuchter, TimeProvider? zeit = null)
    {
        _einstellungen = einstellungen;
        _ha = ha;
        _geraete = geraete;
        _entfeuchter = entfeuchter;
        _zeit = zeit ?? TimeProvider.System;
    }

    private HomeAssistantSettings Einstellungen => _einstellungen.GetEffectiveHomeAssistantSettings();

    private string? Fuehler(string rolle) => _geraete.Entity(Modul, rolle);

    public async Task<ProbelaufMomentaufnahme?> JetztAsync(CancellationToken ct)
    {
        var einstellungen = Einstellungen;
        if (!einstellungen.IsConfigured && !DemoData.IsEnabled) return null;

        async Task<string?> Zustand(string? entityId)
            => entityId is null ? null : (await _ha.GetEntityStateAsync(einstellungen, entityId, ct))?.State;

        var rh = await Zustand(Fuehler(EntfeuchterSteuerungService.Rollen.ZeltFeuchte));
        var temp = await Zustand(Fuehler(EntfeuchterSteuerungService.Rollen.ZeltTemp));
        var vpd = await Zustand(Fuehler(EntfeuchterSteuerungService.Rollen.ZeltVpd));
        var licht = await Zustand(Fuehler(EntfeuchterSteuerungService.Rollen.LichtZustand));

        // Ohne einen einzigen Wert antwortet Home Assistant nicht — das ist „keine Messung", nicht „leere Messung".
        if (rh is null && temp is null && vpd is null) return null;

        var werte = new ProbelaufMesswerte(_zeit.GetUtcNow().UtcDateTime, Zahl(rh), Zahl(temp), Zahl(vpd));
        bool? tag = licht is null || licht is "unknown" or "unavailable" ? null : licht is "on" or "On";
        return new ProbelaufMomentaufnahme(werte, tag);
    }

    private static double? Zahl(string? zustand) => Zahlenlesen.Maschine(zustand);

    public async Task<IReadOnlyList<ProbelaufMesswerte>> VerlaufAsync(DateTime vonUtc, DateTime bisUtc, CancellationToken ct)
    {
        var einstellungen = Einstellungen;

        async Task<List<HaVerlaufsPunkt>> Reihe(string rolle)
        {
            var id = Fuehler(rolle);
            if (id is null) return [];
            return (await _ha.GetVerlaufAsync(einstellungen, id, vonUtc, bisUtc, ct))?.ToList() ?? [];
        }

        var rh = await Reihe(EntfeuchterSteuerungService.Rollen.ZeltFeuchte);
        var temp = await Reihe(EntfeuchterSteuerungService.Rollen.ZeltTemp);
        var vpd = await Reihe(EntfeuchterSteuerungService.Rollen.ZeltVpd);

        // Auf Minuten zusammengelegt: an jedem Minutenpunkt gilt der letzte bekannte Wert.
        var ergebnis = new List<ProbelaufMesswerte>();
        for (var t = vonUtc; t <= bisUtc; t = t.AddMinutes(1))
        {
            var m = new ProbelaufMesswerte(t, WertBis(rh, t), WertBis(temp, t), WertBis(vpd, t));
            if (m.Feuchte is not null || m.Temp is not null || m.Vpd is not null) ergebnis.Add(m);
        }
        return ergebnis;
    }

    private static double? WertBis(List<HaVerlaufsPunkt> reihe, DateTime t)
    {
        HaVerlaufsPunkt? letzter = null;
        foreach (var p in reihe)
        {
            if (p.ZeitUtc > t) break;
            letzter = p;
        }
        return letzter is null ? null : Zahlenlesen.Maschine(letzter.Zustand);
    }

    public async Task<ProbelaufGrenzen> VoreinstellungAsync(CancellationToken ct)
    {
        var live = await _entfeuchter.LiveAsync(ct);
        return ProbelaufBewertung.GrenzenAusZielen(live.RhObergrenzeProzent, live.TempMaxAktivC, live.VpdUnten, live.VpdOben);
    }
}

/// <summary>Fork AI (A-010): Die Meldung aufs Handy, wenn das Zurückstellen nicht gelingt — auch in der Ruhezeit.</summary>
public sealed class ProbelaufMeldung : IProbelaufMeldung
{
    private readonly NotificationService _benachrichtigung;

    public ProbelaufMeldung(NotificationService benachrichtigung) => _benachrichtigung = benachrichtigung;

    public async Task SendenAsync(string titel, string text, CancellationToken ct)
        => await _benachrichtigung.SendAsync(NotificationCategory.System, "🌱 Grow OS · " + titel, text, ct, trotzRuhezeit: true);
}
