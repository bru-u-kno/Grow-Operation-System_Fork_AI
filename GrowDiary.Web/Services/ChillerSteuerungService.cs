using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI: Die Kühler-Steuerung — Sollwerte, Livebild und der Aus-Knopf der
/// Regelung.
/// </summary>
/// <remarks>
/// <para><b>Erster Aufruf übernimmt, was schon da ist.</b> Wie bei der Zuluft:
/// solange nichts gespeichert ist, kommen die Einstellungen aus den vorhandenen
/// Helfern in Home Assistant und nur ersatzweise aus den Vorgaben. Sonst
/// überschriebe der erste Klick auf der Seite eine von Hand eingestellte
/// Regelung, ohne dass jemand etwas verstellt hätte.</para>
///
/// <para><b>Das Zielpaar wird hier nur angezeigt, nicht besessen.</b> Tag und
/// Nacht können aus dem Wochenplan kommen oder aus der Crop-Steering-Absenkung.
/// <see cref="NachHomeAssistantSchreibenAsync"/> schreibt sie deshalb nur, wenn
/// keine der beiden Quellen führt — sonst bliebe der Fork mit sich selbst im
/// Streit, und der nächste Sync-Lauf setzte den Wert ohnehin zurück.</para>
/// </remarks>
public sealed class ChillerSteuerungService
{
    public const string Modul = "chiller";

    /// <summary>Die Rollen dieses Moduls — aufgelöst über die Geräteseite.</summary>
    public static class Rollen
    {
        public const string WasserFuehler = "wasser_temp";
        public const string Steckdose = "steckdose";
        public const string SteckdoseZustand = "steckdose_zustand";
        public const string Leistung = "leistung";
        public const string LichtZustand = "licht_zustand";
    }

    /// <summary>Die Objekte der Steuerung selbst — sie gehören keinem Gerät.</summary>
    public static class Entitaeten
    {
        public const string ZielTag = "input_number.chiller_zieltemperatur_tag";
        public const string ZielNacht = "input_number.chiller_zieltemperatur_nacht";
        public const string Mindestlaufzeit = "input_number.chiller_mindestlaufzeit";
        public const string Mindestpause = "input_number.chiller_mindestpause";
        public const string LetzterSchaltvorgang = "input_datetime.chiller_letzter_schaltvorgang";
        public const string ZielAktiv = "sensor.chiller_zieltemperatur_aktiv";
        public const string Kuehlbedarf = "binary_sensor.chiller_kuhlbedarf";
        public const string Automatik = "automation.water_chiller_regelung";
        public const string Waechter = "automation.water_chiller_wachter";
    }

    private readonly SteuerungRepository _repo;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly SteuerungGeraeteService _geraete;
    private readonly GrowRepository _grows;
    private readonly ILogger<ChillerSteuerungService> _logger;

    public ChillerSteuerungService(
        SteuerungRepository repo,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        SteuerungGeraeteService geraete,
        GrowRepository grows,
        ILogger<ChillerSteuerungService> logger)
    {
        _repo = repo;
        _ha = ha;
        _haSettings = haSettings;
        _geraete = geraete;
        _grows = grows;
        _logger = logger;
    }

    // ----------------------------------------------------------- Einstellungen

    /// <summary>Der gespeicherte Stand — null, solange nie gespeichert wurde.</summary>
    public ChillerEinstellungen? Gespeichert => _repo.GetEinstellungen<ChillerEinstellungen>(Modul);

    /// <summary>
    /// Die Einstellungen, wie die Seite sie zeigen soll: gespeichert, sonst aus
    /// den vorhandenen Helfern übernommen, sonst Vorgabe.
    /// </summary>
    public async Task<ChillerEinstellungen> EinstellungenAsync(CancellationToken ct)
    {
        if (Gespeichert is { } vorhanden) return vorhanden;

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return new ChillerEinstellungen();

        var entities = await _ha.GetEntitiesAsync(settings, ct);
        return AusHomeAssistant(entities.ToDictionary(
            x => x.EntityId, x => (string?)x.State, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Aus den Zuständen der Helfer Einstellungen bauen. Was fehlt oder sich
    /// nicht lesen lässt, bleibt auf der Vorgabe.
    /// </summary>
    public static ChillerEinstellungen AusHomeAssistant(IReadOnlyDictionary<string, string?> zustaende)
    {
        var e = new ChillerEinstellungen();

        double? Zahl(string id) => zustaende.TryGetValue(id, out var s)
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

        if (Zahl(Entitaeten.ZielTag) is { } tag) e.ZielTagC = tag;
        if (Zahl(Entitaeten.ZielNacht) is { } nacht) e.ZielNachtC = nacht;
        if (Zahl(Entitaeten.Mindestlaufzeit) is { } lz) e.MindestlaufzeitMin = (int)Math.Round(lz);
        if (Zahl(Entitaeten.Mindestpause) is { } pa) e.MindestpauseMin = (int)Math.Round(pa);
        if (zustaende.TryGetValue(Entitaeten.Automatik, out var automatik) && automatik is not null)
        {
            e.AutomatikAktiv = automatik is "on" or "On";
        }

        return e;
    }

    public static Dictionary<string, string> Pruefen(ChillerEinstellungen e)
    {
        var f = new Dictionary<string, string>();

        // 4 °C ist die Grenze, unter der Wurzeln die Aufnahme einstellen; über
        // 26 °C hilft Kühlen nicht mehr gegen Wurzelfäule, sondern nur noch der
        // Sauerstoff. Dazwischen ist alles erlaubt, auch Ungewöhnliches.
        if (e.ZielTagC is < 4 or > 30) f[nameof(e.ZielTagC)] = "Ziel Tag muss zwischen 4 und 30 °C liegen.";
        if (e.ZielNachtC is < 4 or > 30) f[nameof(e.ZielNachtC)] = "Ziel Nacht muss zwischen 4 und 30 °C liegen.";

        // Ein Totband unter 0,1 K lässt den Kompressor im Messrauschen takten.
        if (e.HystereseK is < 0.1 or > 3) f[nameof(e.HystereseK)] = "Hysterese muss zwischen 0,1 und 3 K liegen.";

        if (e.MindestlaufzeitMin is < 0 or > 120) f[nameof(e.MindestlaufzeitMin)] = "Mindestlaufzeit: 0 bis 120 Minuten.";
        if (e.MindestpauseMin is < 0 or > 120) f[nameof(e.MindestpauseMin)] = "Mindestpause: 0 bis 120 Minuten.";
        return f;
    }

    /// <summary>Prüft, speichert und schreibt nach Home Assistant.</summary>
    public async Task<(ChillerEinstellungen? Gespeichert, Dictionary<string, string> Fehler, bool HaErreicht)> SpeichernAsync(
        ChillerEinstellungen e, CancellationToken ct)
    {
        var fehler = Pruefen(e);
        if (fehler.Count > 0) return (null, fehler, false);

        _repo.SetEinstellungen(Modul, e);
        var erreicht = await NachHomeAssistantSchreibenAsync(e, ct);
        return (e, fehler, erreicht);
    }

    public async Task<bool> NachHomeAssistantSchreibenAsync(ChillerEinstellungen e, CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var zahlen = new (string Entity, double Wert)[]
        {
            (Entitaeten.ZielTag, e.ZielTagC),
            (Entitaeten.ZielNacht, e.ZielNachtC),
            (Entitaeten.Mindestlaufzeit, e.MindestlaufzeitMin),
            (Entitaeten.Mindestpause, e.MindestpauseMin),
        };

        var alles = true;
        foreach (var (entity, wert) in zahlen)
        {
            alles &= await _ha.CallEntityServiceAsync(settings, "input_number", "set_value", entity, ct,
                new Dictionary<string, object> { ["value"] = wert });
        }

        alles &= await _ha.CallEntityServiceAsync(
            settings, "automation", e.AutomatikAktiv ? "turn_on" : "turn_off", Entitaeten.Automatik, ct);

        if (!alles) _logger.LogWarning("Kühler-Sollwerte: nicht alle Helfer in Home Assistant angenommen.");
        return alles;
    }

    // --------------------------------------------------------------- Livebild

    public async Task<ChillerLive> LiveAsync(CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);

        string? Text(string id) => nachId.TryGetValue(id, out var s) ? s.State : null;
        double? Zahl(string id) => double.TryParse(Text(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        bool? An(string id) => Text(id) is { } t && t is not ("unknown" or "unavailable") ? t is "on" or "On" : null;

        var geraete = _geraete.EntitiesFuerModul(Modul);
        string? Rolle(string rolle) => geraete.TryGetValue(rolle, out var id) ? id : null;
        double? ZahlRolle(string rolle) => Rolle(rolle) is { } id ? Zahl(id) : null;
        bool? AnRolle(string rolle) => Rolle(rolle) is { } id ? An(id) : null;

        var e = Gespeichert ?? AusHomeAssistant(
            nachId.ToDictionary(x => x.Key, x => (string?)x.Value.State, StringComparer.OrdinalIgnoreCase));

        // Der Zustand der Steckdose: erst die eigene Rolle, sonst der Schalter
        // selbst. Bei einer Shelly ist beides dieselbe Entität — eine zweite
        // Zuordnung dafür wäre eine Frage ohne Inhalt.
        var steckdoseAn = AnRolle(Rollen.SteckdoseZustand) ?? AnRolle(Rollen.Steckdose);
        var tagPhase = AnRolle(Rollen.LichtZustand);

        var zielAktiv = Zahl(Entitaeten.ZielAktiv)
            ?? (tagPhase == false ? Zahl(Entitaeten.ZielNacht) : Zahl(Entitaeten.ZielTag));

        var (rest, gewechselt) = Sperre(Text(Entitaeten.LetzterSchaltvorgang),
            e.MindestlaufzeitMin, e.MindestpauseMin, steckdoseAn == true, DateTime.Now);

        var doppelt = Doppelsteuerung(_grows.GetTents(), Rolle(Rollen.Steckdose));

        return new ChillerLive(
            HaErreichbar: entities.Count > 0,
            WasserC: ZahlRolle(Rollen.WasserFuehler),
            ZielAktivC: zielAktiv,
            ZielTagC: Zahl(Entitaeten.ZielTag),
            ZielNachtC: Zahl(Entitaeten.ZielNacht),
            TagPhase: tagPhase,
            Kuehlbedarf: An(Entitaeten.Kuehlbedarf),
            SteckdoseAn: steckdoseAn,
            LeistungW: ZahlRolle(Rollen.Leistung),
            AutomatikAn: An(Entitaeten.Automatik),
            WaechterAn: An(Entitaeten.Waechter),
            ZielQuelle: ZielQuelle(Gespeichert is not null),
            SperreRestMin: rest,
            LetzterWechsel: gewechselt,
            DoppelSteuerungEntity: doppelt,
            EinschaltenAbC: zielAktiv is { } z1 ? Math.Round(z1 + e.HystereseK, 2) : null,
            AusschaltenUnterC: zielAktiv is { } z2 ? Math.Round(z2 - e.HystereseK, 2) : null);
    }

    /// <summary>
    /// Woher das Zielpaar kommt. Noch ohne Crop-Steering-Fall: die Absenkung des
    /// Entwicklers schreibt an ihr eigenes Zielgerät, und ob das dieselben Helfer
    /// sind, entscheidet der Nutzer dort. Sobald die Zuordnung ausgelesen wird,
    /// gehört sie hierher und nicht in die Oberfläche.
    /// </summary>
    private static string ZielQuelle(bool gespeichert) => gespeichert ? "hand" : "plan";

    /// <summary>
    /// Schaltet die Steckdosen-Funktion der Crop-Steering-Seite dieselbe
    /// Entität wie diese Regelung?
    /// </summary>
    /// <remarks>
    /// <b>Warum das keine Kleinigkeit ist.</b> Beide Wege sind für sich richtig
    /// gebaut, aber sie wissen nichts voneinander: der Minutentakt der
    /// HA-Regelung holt jede Fremdschaltung binnen einer Minute zurück. Das
    /// Ergebnis ist eine Steckdose, die scheinbar von selbst umspringt — und ein
    /// Kompressor, dem die Mindestpause nichts mehr nützt, weil die Befehle aus
    /// zwei Quellen kommen. Die Seite sagt das, statt es passieren zu lassen.
    /// </remarks>
    public static string? Doppelsteuerung(IEnumerable<Tent> zelte, string? steckdose)
    {
        if (string.IsNullOrWhiteSpace(steckdose)) return null;

        return zelte
            .Where(z => z.ChillerControlEnabled)
            .Select(z => z.ChillerSwitchEntityId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)
                && string.Equals(id, steckdose, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Wie lange die Schaltsperre noch läuft — läuft der Kühler, zählt die
    /// Mindestlaufzeit, sonst die Mindestpause.
    /// </summary>
    /// <remarks>
    /// Gerechnet wird gegen den Zeitstempel-Helfer und nicht gegen
    /// <c>last_changed</c>: eine Funksteckdose fällt bei WLAN-Aussetzern kurz
    /// aus, und jeder dieser Aussetzer setzte <c>last_changed</c> neu. Beim
    /// Kompressor ist das keine Kosmetik — zu kurze Pausen kosten ihn das Leben.
    /// </remarks>
    public static (int? RestMin, DateTime? Letzter) Sperre(
        string? zeitstempel, int laufzeitMin, int pauseMin, bool an, DateTime jetzt)
    {
        if (!DateTime.TryParse(zeitstempel, CultureInfo.InvariantCulture, DateTimeStyles.None, out var letzter))
        {
            return (null, null);
        }

        var gate = TimeSpan.FromMinutes(an ? laufzeitMin : pauseMin);
        var alter = jetzt - letzter;
        var rest = gate - alter;
        return (rest <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(rest.TotalMinutes), letzter);
    }
}
