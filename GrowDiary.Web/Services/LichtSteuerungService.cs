using System.Collections.Concurrent;
using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI: Die Licht-Steuerung — Livebild, Zeitpläne und das Schalten am
/// AC-Infinity-Controller.
/// </summary>
/// <remarks>
/// <para><b>Warum hier direkt geschaltet wird.</b> Siehe
/// <see cref="LichtEinstellungen"/>: den Zeitplan führt der Controller selbst
/// aus. Eine HA-Automation dazwischen hätte nichts zu regeln, nur einen
/// weiteren Ort, an dem dieselbe Zeit steht.</para>
///
/// <para><b>Die drei Regeln gegen die Cloud.</b> Aus dem Fehler vom 23.08.2026
/// (Preset sprang nach einer Minute zurück): <b>nur Abweichendes</b> schreiben —
/// Veggie und Blüte mit gleicher Ein-Zeit sind ein einziger Aufruf, nicht drei;
/// <b>Abstand</b> zwischen den Aufrufen, weil die Cloud parallele Schreibvorgänge
/// verwirft; <b>später prüfen</b> und wiederholen, weil ein angenommener Aufruf
/// noch lange nicht ein übernommener ist.</para>
///
/// <para><b>Wo die Prüfung läuft.</b> Nicht im Schreib-Aufruf — der wäre eine
/// Minute lang offen. Die offenen Sollwerte stehen im Speicher; jedes Laden des
/// Livebilds prüft sie gegen den Ist-Zustand und schreibt einmal nach, wenn die
/// Frist abgelaufen ist. Die Oberfläche fragt das Livebild ohnehin regelmäßig
/// ab. Bleibt sie zu, bleibt der letzte Versuch stehen — wie zuvor in der
/// Dashboard-Karte, deren Wiederholung auch am offenen Browser hing.</para>
/// </remarks>
public sealed class LichtSteuerungService
{
    public const string Modul = "licht";

    /// <summary>Die Modus-Werte des AC-Infinity-Selects.</summary>
    public static class Modi
    {
        public const string Aus = "Off";
        public const string An = "On";
        public const string Zeitplan = "Schedule";
    }

    /// <summary>Die Rollen dieses Moduls — aufgelöst über die Geräteseite.</summary>
    public static class Rollen
    {
        public const string Modus = "licht_modus";
        public const string Stufe = "licht_stufe";
        public const string EinZeit = "licht_ein_zeit";
        public const string AusZeit = "licht_aus_zeit";
        public const string Zustand = "licht_zustand";
        public const string Status = "licht_status";
    }

    /// <summary>
    /// Die Spiegel-Helfer der alten Dashboard-Karte. Sie gehören zur Steuerung,
    /// nicht zum Gerät, und stehen deshalb fest — solange die Karte lebt.
    /// </summary>
    public static class Entitaeten
    {
        public const string VeggieEin = "input_datetime.led_top_zeitplan_veggie_ein";
        public const string VeggieAus = "input_datetime.led_top_zeitplan_veggie_aus";
        public const string BlueteEin = "input_datetime.led_top_zeitplan_blute_ein";
        public const string BlueteAus = "input_datetime.led_top_zeitplan_blute_aus";
    }

    /// <summary>Offene Sollwerte je Entität. Statisch, weil der Dienst je Anfrage neu entsteht.</summary>
    private static readonly ConcurrentDictionary<string, LichtOffen> Offene = new(StringComparer.OrdinalIgnoreCase);

    private readonly SteuerungRepository _repo;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly SteuerungGeraeteService _geraete;
    private readonly ILogger<LichtSteuerungService> _logger;

    public LichtSteuerungService(
        SteuerungRepository repo,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        SteuerungGeraeteService geraete,
        ILogger<LichtSteuerungService> logger)
    {
        _repo = repo;
        _ha = ha;
        _haSettings = haSettings;
        _geraete = geraete;
        _logger = logger;
    }

    // ----------------------------------------------------------- Einstellungen

    public LichtEinstellungen Einstellungen => _repo.GetEinstellungen<LichtEinstellungen>(Modul) ?? new LichtEinstellungen();

    public static Dictionary<string, string> Pruefen(LichtEinstellungen e)
    {
        var f = new Dictionary<string, string>();
        foreach (var (name, wert) in new[]
                 {
                     (nameof(e.VeggieEin), e.VeggieEin), (nameof(e.VeggieAus), e.VeggieAus),
                     (nameof(e.BlueteEin), e.BlueteEin), (nameof(e.BlueteAus), e.BlueteAus),
                 })
        {
            if (Zeit(wert) is null) f[name] = "Zeit im Format HH:mm angeben.";
        }

        if (e.Stufe is < 1 or > 10) f[nameof(e.Stufe)] = "Stufe zwischen 1 und 10.";
        if (e.SchreibAbstandMs is < 0 or > 10000) f[nameof(e.SchreibAbstandMs)] = "Abstand zwischen 0 und 10000 ms.";
        if (e.VerifySekunden is < 5 or > 120) f[nameof(e.VerifySekunden)] = "Prüffrist zwischen 5 und 120 Sekunden.";
        if (e.MaxWiederholungen is < 0 or > 5) f[nameof(e.MaxWiederholungen)] = "Höchstens 5 Wiederholungen.";
        return f;
    }

    /// <summary>
    /// Einstellungen prüfen, speichern, Helfer spiegeln — und, wenn das gerade
    /// aktive Preset bearbeitet wurde, die neuen Zeiten sofort an den Controller
    /// geben. Sonst stünde in der Oberfläche eine Zeit, nach der nichts schaltet.
    /// </summary>
    public async Task<(LichtEinstellungen? Gespeichert, Dictionary<string, string> Fehler, bool HaErreicht)> SpeichernAsync(
        LichtEinstellungen e, CancellationToken ct)
    {
        var fehler = Pruefen(e);
        if (fehler.Count > 0) return (null, fehler, false);

        var vorher = Einstellungen;
        var live = await LiveAsync(ct);
        _repo.SetEinstellungen(Modul, e);

        var erreicht = await HelferSpiegelnAsync(e, ct);
        var aktiv = AktivesPreset(vorher, live.EinZeit, live.AusZeit, live.Modus);
        if (aktiv is not null)
        {
            var (ein, aus) = Preset(e, aktiv);
            erreicht &= await SchreibenAsync(new[]
            {
                (Rollen.EinZeit, ein + ":00"),
                (Rollen.AusZeit, aus + ":00"),
            }, e, ct);
        }

        return (e, fehler, erreicht);
    }

    // --------------------------------------------------------------- Livebild

    public async Task<LichtLive> LiveAsync(CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);
        var e = Einstellungen;
        var geraete = _geraete.EntitiesFuerModul(Modul);

        string? Text(string? id) => id is not null && nachId.TryGetValue(id, out var s) ? s.State : null;
        string? Rolle(string rolle) => geraete.TryGetValue(rolle, out var id) && !string.IsNullOrWhiteSpace(id) ? Text(id) : null;

        var modus = Rolle(Rollen.Modus);
        var einZeit = Kurz(Rolle(Rollen.EinZeit));
        var ausZeit = Kurz(Rolle(Rollen.AusZeit));
        var stufe = int.TryParse(Rolle(Rollen.Stufe), NumberStyles.Integer, CultureInfo.InvariantCulture, out var st) ? st : (int?)null;
        var zustand = Rolle(Rollen.Zustand) is { } z ? z is "on" or "On" : (bool?)null;
        var online = Rolle(Rollen.Status) is { } s2 ? s2 is "on" or "On" : (bool?)null;

        var offen = await OffenePruefenAsync(nachId, geraete, e, ct);

        return new LichtLive(
            HaErreichbar: entities.Count > 0,
            Modus: modus,
            Stufe: stufe,
            EinZeit: einZeit,
            AusZeit: ausZeit,
            LichtAn: zustand,
            ControllerOnline: online,
            AktivesPreset: AktivesPreset(e, einZeit, ausZeit, modus),
            NaechsterWechsel: NaechsterWechsel(modus, einZeit, ausZeit, zustand),
            Unbestaetigt: offen.Unbestaetigt,
            Fehlgeschlagen: offen.Fehlgeschlagen,
            StandUtc: DateTime.UtcNow);
    }

    // ----------------------------------------------------------------- Befehle

    /// <summary>Aus, An, ein Preset anwenden oder die Stufe setzen.</summary>
    public async Task<bool> BefehlAsync(string art, string? preset, int? stufe, CancellationToken ct)
    {
        var e = Einstellungen;
        switch (art)
        {
            case "aus":
                return await SchreibenAsync(new[] { (Rollen.Modus, Modi.Aus) }, e, ct);

            case "an":
                return await SchreibenAsync(new[] { (Rollen.Modus, Modi.An) }, e, ct);

            case "stufe":
            {
                if (stufe is not { } wert || wert is < 1 or > 10) return false;
                e.Stufe = wert;
                _repo.SetEinstellungen(Modul, e);
                return await SchreibenAsync(new[] { (Rollen.Stufe, wert.ToString(CultureInfo.InvariantCulture)) }, e, ct);
            }

            case "preset":
            {
                if (preset is not ("veggie" or "bluete")) return false;
                var (ein, aus) = Preset(e, preset);
                // Reihenfolge: erst die Zeiten, dann der Modus. Wer zuerst auf
                // Zeitplan schaltet, laesst den Controller kurz nach den alten
                // Zeiten schalten.
                return await SchreibenAsync(new[]
                {
                    (Rollen.EinZeit, ein + ":00"),
                    (Rollen.AusZeit, aus + ":00"),
                    (Rollen.Modus, Modi.Zeitplan),
                }, e, ct);
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Schreibt der Reihe nach, was vom Soll abweicht, und merkt sich die
    /// Sollwerte zur späteren Prüfung.
    /// </summary>
    private async Task<bool> SchreibenAsync(IReadOnlyList<(string Rolle, string Soll)> auftraege, LichtEinstellungen e, CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var geraete = _geraete.EntitiesFuerModul(Modul);
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x.State ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var alles = true;
        var erster = true;
        foreach (var (rolle, soll) in auftraege)
        {
            if (!geraete.TryGetValue(rolle, out var id) || string.IsNullOrWhiteSpace(id))
            {
                alles = false;
                continue;
            }

            if (nachId.TryGetValue(id, out var ist) && Gleich(ist, soll)) continue;

            if (!erster && e.SchreibAbstandMs > 0) await Task.Delay(e.SchreibAbstandMs, ct);
            erster = false;

            alles &= await SchreibenAsync(settings, id, soll, ct);
            Offene[id] = new LichtOffen(id, soll, DateTime.UtcNow, 1);
        }

        return alles;
    }

    private Task<bool> SchreibenAsync(HomeAssistantSettings settings, string entityId, string soll, CancellationToken ct)
    {
        var domaene = entityId.Split('.', 2)[0];
        return domaene switch
        {
            "select" => _ha.CallEntityServiceAsync(settings, "select", "select_option", entityId, ct,
                new Dictionary<string, object> { ["option"] = soll }),
            "time" => _ha.CallEntityServiceAsync(settings, "time", "set_value", entityId, ct,
                new Dictionary<string, object> { ["time"] = soll }),
            "number" => _ha.CallEntityServiceAsync(settings, "number", "set_value", entityId, ct,
                new Dictionary<string, object>
                {
                    ["value"] = double.TryParse(soll, NumberStyles.Float, CultureInfo.InvariantCulture, out var zahl) ? (object)zahl : soll,
                }),
            "input_datetime" => _ha.CallEntityServiceAsync(settings, "input_datetime", "set_datetime", entityId, ct,
                new Dictionary<string, object> { ["time"] = soll }),
            _ => Task.FromResult(false),
        };
    }

    /// <summary>Die vier Helfer der alten Dashboard-Karte nachziehen.</summary>
    private async Task<bool> HelferSpiegelnAsync(LichtEinstellungen e, CancellationToken ct)
    {
        if (!e.HelferSpiegeln) return true;
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var alles = true;
        foreach (var (id, wert) in new[]
                 {
                     (Entitaeten.VeggieEin, e.VeggieEin), (Entitaeten.VeggieAus, e.VeggieAus),
                     (Entitaeten.BlueteEin, e.BlueteEin), (Entitaeten.BlueteAus, e.BlueteAus),
                 })
        {
            alles &= await SchreibenAsync(settings, id, wert + ":00", ct);
        }

        return alles;
    }

    /// <summary>
    /// Die offenen Sollwerte gegen den Ist-Zustand halten: übernommen → vergessen,
    /// Frist abgelaufen → einmal nachschreiben, Versuche aufgebraucht → melden.
    /// </summary>
    private async Task<(IReadOnlyList<string> Unbestaetigt, IReadOnlyList<string> Fehlgeschlagen)> OffenePruefenAsync(
        IReadOnlyDictionary<string, HomeAssistantEntity> nachId,
        IReadOnlyDictionary<string, string?> geraete,
        LichtEinstellungen e,
        CancellationToken ct)
    {
        if (Offene.IsEmpty) return (Array.Empty<string>(), Array.Empty<string>());

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var unbestaetigt = new List<string>();
        var fehlgeschlagen = new List<string>();

        foreach (var offen in Offene.Values.ToList())
        {
            if (!geraete.Values.Any(id => string.Equals(id, offen.EntityId, StringComparison.OrdinalIgnoreCase)))
            {
                Offene.TryRemove(offen.EntityId, out _);
                continue;
            }

            var ist = nachId.TryGetValue(offen.EntityId, out var s) ? s.State ?? string.Empty : string.Empty;
            if (Gleich(ist, offen.Soll))
            {
                Offene.TryRemove(offen.EntityId, out _);
                continue;
            }

            if (DateTime.UtcNow - offen.SeitUtc < TimeSpan.FromSeconds(e.VerifySekunden))
            {
                unbestaetigt.Add(offen.EntityId);
                continue;
            }

            if (offen.Versuche > e.MaxWiederholungen)
            {
                fehlgeschlagen.Add(offen.EntityId);
                continue;
            }

            _logger.LogWarning("Licht: {Entity} steht auf {Ist}, gewollt war {Soll} — Versuch {Nummer}.",
                offen.EntityId, ist, offen.Soll, offen.Versuche + 1);
            await SchreibenAsync(settings, offen.EntityId, offen.Soll, ct);
            Offene[offen.EntityId] = offen with { SeitUtc = DateTime.UtcNow, Versuche = offen.Versuche + 1 };
            unbestaetigt.Add(offen.EntityId);
        }

        return (unbestaetigt, fehlgeschlagen);
    }

    /// <summary>Einen gemeldeten Fehlschlag wegräumen, wenn der Nutzer ihn zur Kenntnis genommen hat.</summary>
    public static void OffeneVergessen() => Offene.Clear();

    // ----------------------------------------------------------------- Rechnen

    internal static (string Ein, string Aus) Preset(LichtEinstellungen e, string preset)
        => preset == "bluete" ? (e.BlueteEin, e.BlueteAus) : (e.VeggieEin, e.VeggieAus);

    /// <summary>
    /// Welches Preset gerade gilt: Modus Zeitplan UND beide Controller-Zeiten
    /// gleich den gespeicherten. Bei gleichen Zeitpaaren gewinnt Veggie — dann
    /// sind die Presets ohnehin nicht unterscheidbar.
    /// </summary>
    internal static string? AktivesPreset(LichtEinstellungen e, string? ein, string? aus, string? modus)
    {
        if (modus != Modi.Zeitplan || ein is null || aus is null) return null;
        if (ein == e.VeggieEin && aus == e.VeggieAus) return "veggie";
        if (ein == e.BlueteEin && aus == e.BlueteAus) return "bluete";
        return null;
    }

    /// <summary>
    /// Der nächste Wechsel, lokal gerechnet — inklusive der Fälle über
    /// Mitternacht. Der Controller sagt es nicht, und ein „–" wäre die Zahl,
    /// nach der man am häufigsten schaut.
    /// </summary>
    internal static string? NaechsterWechsel(string? modus, string? ein, string? aus, bool? an)
    {
        if (modus != Modi.Zeitplan) return null;
        if (Zeit(ein) is not { } einZeit || Zeit(aus) is not { } ausZeit) return null;
        if (einZeit == ausZeit) return null;

        var jetzt = DateTime.Now.TimeOfDay;
        var lichtphase = an ?? InFenster(jetzt, einZeit, ausZeit);
        var ziel = lichtphase ? ausZeit : einZeit;
        var stunden = (ziel - jetzt + TimeSpan.FromDays(1)).TotalHours % 24;
        var rest = TimeSpan.FromHours(stunden);
        /* Nur die Restzeit (13.09.2026).
         *
         * Vorher stand hier „an 05:00 · in 7 h 5 min". Die Uhrzeit steht eine
         * Zeile darueber im Zeitplan und die Richtung gross daneben (AUS) —
         * dreimal dasselbe in zwei Zeilen. Uebrig bleibt die Zahl, nach der man
         * tatsaechlich schaut. */
        return $"in {(int)rest.TotalHours} h {rest.Minutes} min";
    }

    private static bool InFenster(TimeSpan jetzt, TimeSpan ein, TimeSpan aus)
        => ein <= aus ? jetzt >= ein && jetzt < aus : jetzt >= ein || jetzt < aus;

    /// <summary>„05:00", „05:00:00" und „5:00" gelten als dieselbe Zeit.</summary>
    private static bool Gleich(string ist, string soll)
    {
        if (string.Equals(ist, soll, StringComparison.OrdinalIgnoreCase)) return true;
        if (Zeit(ist) is { } a && Zeit(soll) is { } b) return a == b;
        return double.TryParse(ist, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(soll, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && Math.Abs(x - y) < 0.001;
    }

    internal static TimeSpan? Zeit(string? wert)
        => TimeSpan.TryParseExact(wert, new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss", @"h\:mm\:ss" },
            CultureInfo.InvariantCulture, out var t) ? t : null;

    private static string? Kurz(string? wert) => Zeit(wert) is { } t ? $"{t:hh\\:mm}" : null;
}

/// <summary>Das Livebild der Licht-Steuerung.</summary>
/// <param name="Modus">Der Modus des Controllers: <c>Off</c>, <c>On</c>, <c>Schedule</c>, …</param>
/// <param name="AktivesPreset"><c>veggie</c>, <c>bluete</c> oder null, wenn die Zeiten zu keinem passen.</param>
/// <param name="Unbestaetigt">Entitäten, deren Befehl der Controller noch nicht übernommen hat.</param>
/// <param name="Fehlgeschlagen">Entitäten, bei denen auch die Wiederholungen nichts gebracht haben.</param>
public sealed record LichtLive(
    bool HaErreichbar,
    string? Modus,
    int? Stufe,
    string? EinZeit,
    string? AusZeit,
    bool? LichtAn,
    bool? ControllerOnline,
    string? AktivesPreset,
    string? NaechsterWechsel,
    IReadOnlyList<string> Unbestaetigt,
    IReadOnlyList<string> Fehlgeschlagen,
    DateTime StandUtc);
