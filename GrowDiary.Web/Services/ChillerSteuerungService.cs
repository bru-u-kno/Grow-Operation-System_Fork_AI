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
        /// <summary>Fork AI: Kühler mit eigenem Thermostat (climate/number).</summary>
        public const string KuehlerSollwert = "kuehler_sollwert";
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
        /// <summary>Fork AI: schreibt das Ziel in ein Sollwert-Gerät.</summary>
        public const string SollwertAutomatik = "automation.water_chiller_sollwert";
        /// <summary>Fork AI (F-030): Einschalten ab Ziel plus diesem Abstand.</summary>
        public const string Hysterese = "input_number.chiller_hysterese";
        public const string Waechter = "automation.water_chiller_wachter";
    }

    private readonly SteuerungRepository _repo;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly SteuerungGeraeteService _geraete;
    private readonly GrowRepository _grows;
    private readonly WochenplanSyncService _wochenplan;
    private readonly ILogger<ChillerSteuerungService> _logger;
    private readonly AppSettingsRepository? _einstellungen;

    public ChillerSteuerungService(
        SteuerungRepository repo,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        SteuerungGeraeteService geraete,
        GrowRepository grows,
        WochenplanSyncService wochenplan,
        ILogger<ChillerSteuerungService> logger,
        AppSettingsRepository? einstellungen = null)
    {
        _einstellungen = einstellungen;
        _repo = repo;
        _ha = ha;
        _haSettings = haSettings;
        _geraete = geraete;
        _grows = grows;
        _wochenplan = wochenplan;
        _logger = logger;
    }

    // ------------------------------------------------ Übernahme (forkai.136)

    private const string UebernahmeSchluessel = "fork-ai:chiller:cropsteering-uebernommen";

    /// <summary>
    /// Einmalig: was ein Nutzer in Crop Steering eingetragen hatte, wird zur
    /// Rolle der Chiller-Steuerung — die Steckdose am Zelt und ein Zielgerät
    /// (climate/number). Nur, wenn für die Ansteuerung noch nichts gespeichert
    /// ist; danach nie wieder.
    /// </summary>
    public void CropSteeringUebernehmen()
    {
        if (_einstellungen is null || _einstellungen.GetValue(UebernahmeSchluessel) is not null) return;

        try
        {
            var gespeichert = _geraete.Gespeichert(Modul);
            var zelte = _grows.GetTents();
            var vorschlag = UebernahmeAus(
                gespeichert.ContainsKey(Rollen.Steckdose) || gespeichert.ContainsKey(Rollen.KuehlerSollwert),
                zelte.Select(z => z.ChillerSwitchEntityId),
                zelte.Select(z => z.WaterTargetEntityId));

            if (vorschlag.Count > 0)
            {
                var fehler = _geraete.Speichern(Modul, vorschlag.ToDictionary(p => p.Key, p => (string?)p.Value));
                _logger.LogInformation("Crop Steering übernommen: {Rollen} (Fehler: {Fehler})",
                    string.Join(", ", vorschlag.Select(p => $"{p.Key}={p.Value}")), fehler.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Übernahme aus Crop Steering fehlgeschlagen.");
        }

        _einstellungen.SetValue(UebernahmeSchluessel, DateTime.UtcNow.ToString("O"));
    }

    /// <summary>Was übernommen würde — rein, ohne Datenbank.</summary>
    public static Dictionary<string, string> UebernahmeAus(
        bool schonZugeordnet, IEnumerable<string?> steckdosen, IEnumerable<string?> zielgeraete)
    {
        var ergebnis = new Dictionary<string, string>(StringComparer.Ordinal);
        if (schonZugeordnet) return ergebnis;

        if (steckdosen.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) is { } steckdose)
        {
            ergebnis[Rollen.Steckdose] = steckdose.Trim();
        }

        // Nur echte Sollwert-Geräte. Ein input_number war ein Helfer — der
        // Fork schreibt seine Ziele schon selbst.
        if (zielgeraete.FirstOrDefault(z => z is not null
                && (z.StartsWith("climate.", StringComparison.OrdinalIgnoreCase)
                    || z.StartsWith("number.", StringComparison.OrdinalIgnoreCase))) is { } ziel)
        {
            ergebnis[Rollen.KuehlerSollwert] = ziel.Trim();
        }

        return ergebnis;
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
        var vorhanden = Gespeichert;
        if (vorhanden is { HystereseGefuehrt: true }) return vorhanden;

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return vorhanden ?? new ChillerEinstellungen();

        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var zustaende = entities.ToDictionary(
            x => x.EntityId, x => (string?)x.State, StringComparer.OrdinalIgnoreCase);
        var automatik = AutomatikFuer(AnsteuerungJetzt());
        var e = vorhanden is null
            ? AusHomeAssistant(zustaende, automatik)
            : MitHystereseAusHomeAssistant(vorhanden, zustaende);
        // Fork AI (forkai.139, F-035): Beim ersten Aufruf übernimmt der Fork die
        // Werte aus Home Assistant sofort als eigenen Stand — gespeichert wird nur
        // im Fork, nach HA wird nichts geschrieben. Nur mit echter Antwort von HA,
        // sonst stünden die Werkseinstellungen als „übernommen" da.
        if (entities.Count > 0)
        {
            e.HystereseGefuehrt = true;
            _repo.SetEinstellungen(Modul, e);
        }
        return e;
    }

    /// <summary>
    /// Fork AI (F-030): Ein gespeicherter Stand aus der Zeit vor dem
    /// Hysterese-Helfer trägt 0,3 — ein Wert, der nie gewirkt hat. Dann gilt,
    /// was in Home Assistant steht.
    /// </summary>
    public static ChillerEinstellungen MitHystereseAusHomeAssistant(
        ChillerEinstellungen gespeichert, IReadOnlyDictionary<string, string?> zustaende)
    {
        if (gespeichert.HystereseGefuehrt) return gespeichert;
        if (zustaende.TryGetValue(Entitaeten.Hysterese, out var s)
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
        {
            gespeichert.HystereseK = h;
        }
        return gespeichert;
    }

    /// <summary>
    /// Fork AI (F-032): Wie viele Geräte zugeordnet sind — gezählt werden
    /// Pflichtrollen und belegte optionale Rollen. Steckdose und Sollwert-Gerät
    /// sind Alternativen; eine bewusst leere optionale Rolle ist keine Lücke.
    /// Fehlen beide, zählt die Ansteuerung als eine offene Stelle.
    /// </summary>
    public static (int Zugeordnet, int Gesamt) GeraeteZaehlen(IReadOnlyDictionary<string, string?> geraete)
    {
        var rollen = SteuerungGeraeteRollen.FuerModul(Modul);
        bool Belegt(string schluessel) => geraete.TryGetValue(schluessel, out var id) && !string.IsNullOrWhiteSpace(id);

        var zugeordnet = rollen.Count(r => Belegt(r.Schluessel));
        var gesamt = rollen.Count(r => r.Pflicht || Belegt(r.Schluessel));
        geraete.TryGetValue(Rollen.Steckdose, out var steckdose);
        geraete.TryGetValue(Rollen.KuehlerSollwert, out var sollwert);
        if (ChillerAnsteuerung.Aus(steckdose, sollwert) == ChillerAnsteuerung.Keine) gesamt++;
        return (zugeordnet, gesamt);
    }

    /// <summary>Die Ansteuerung nach den aktuellen Rollen.</summary>
    public string AnsteuerungJetzt()
    {
        var geraete = _geraete.EntitiesFuerModul(Modul);
        geraete.TryGetValue(Rollen.Steckdose, out var steckdose);
        geraete.TryGetValue(Rollen.KuehlerSollwert, out var sollwert);
        return ChillerAnsteuerung.Aus(steckdose, sollwert);
    }

    /// <summary>
    /// Welche Automation „Automatik aktiv" meint: bei einem Sollwert-Gerät die,
    /// die das Ziel schreibt, sonst die Steckdosen-Regelung.
    /// </summary>
    public static string AutomatikFuer(string ansteuerung)
        => ansteuerung is ChillerAnsteuerung.Regelbar or ChillerAnsteuerung.Beides
            ? Entitaeten.SollwertAutomatik
            : Entitaeten.Automatik;

    /// <summary>
    /// Aus den Zuständen der Helfer Einstellungen bauen. Was fehlt oder sich
    /// nicht lesen lässt, bleibt auf der Vorgabe.
    /// </summary>
    public static ChillerEinstellungen AusHomeAssistant(
        IReadOnlyDictionary<string, string?> zustaende, string automatik = Entitaeten.Automatik)
    {
        var e = new ChillerEinstellungen();

        double? Zahl(string id) => zustaende.TryGetValue(id, out var s)
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

        if (Zahl(Entitaeten.ZielTag) is { } tag) e.ZielTagC = tag;
        if (Zahl(Entitaeten.ZielNacht) is { } nacht) e.ZielNachtC = nacht;
        if (Zahl(Entitaeten.Mindestlaufzeit) is { } lz) e.MindestlaufzeitMin = (int)Math.Round(lz);
        if (Zahl(Entitaeten.Mindestpause) is { } pa) e.MindestpauseMin = (int)Math.Round(pa);
        if (Zahl(Entitaeten.Hysterese) is { } hy) e.HystereseK = hy;
        if (zustaende.TryGetValue(automatik, out var an) && an is not null)
        {
            e.AutomatikAktiv = an is "on" or "On";
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

        // Ab jetzt führt der Fork die Hysterese (F-030).
        e.HystereseGefuehrt = true;
        _repo.SetEinstellungen(Modul, e);
        var erreicht = await NachHomeAssistantSchreibenAsync(e, ct);
        return (e, fehler, erreicht);
    }

    public async Task<bool> NachHomeAssistantSchreibenAsync(ChillerEinstellungen e, CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var zahlen = Schreibliste(e, _wochenplan.GefuehrteHelfer().Keys);

        var alles = true;
        foreach (var (entity, wert) in zahlen)
        {
            alles &= await _ha.CallEntityServiceAsync(settings, "input_number", "set_value", entity, ct,
                new Dictionary<string, object> { ["value"] = wert });
        }

        var ansteuerung = AnsteuerungJetzt();
        alles &= await _ha.CallEntityServiceAsync(
            settings, "automation", e.AutomatikAktiv ? "turn_on" : "turn_off", AutomatikFuer(ansteuerung), ct);

        // Regelt das Gerät selbst, darf die Steckdosen-Regelung nicht mehr
        // schalten — sonst greifen zwei Stellen nach demselben Kühler. Fehlt sie
        // (neue Anlage mit Sollwert-Gerät), ist das kein Fehler.
        if (ansteuerung is ChillerAnsteuerung.Regelbar or ChillerAnsteuerung.Beides)
        {
            await _ha.CallEntityServiceAsync(settings, "automation", "turn_off", Entitaeten.Automatik, ct);
        }

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

        var zustaende = nachId.ToDictionary(x => x.Key, x => (string?)x.Value.State, StringComparer.OrdinalIgnoreCase);
        var ansteuerung = ChillerAnsteuerung.Aus(Rolle(Rollen.Steckdose), Rolle(Rollen.KuehlerSollwert));
        var automatik = AutomatikFuer(ansteuerung);
        var e = Gespeichert is { } g
            ? MitHystereseAusHomeAssistant(g, zustaende)
            : AusHomeAssistant(zustaende, automatik);

        // Schaltpunkte aus dem, was in HA wirklich gilt — nicht aus dem Fork.
        var hysterese = Zahl(Entitaeten.Hysterese) ?? e.HystereseK;
        var kuehler = Rolle(Rollen.KuehlerSollwert);
        double? kuehlerSoll = null;
        if (kuehler is not null)
        {
            kuehlerSoll = kuehler.StartsWith("climate.", StringComparison.OrdinalIgnoreCase)
                ? (await _ha.GetEntityStateAsync(settings, kuehler, ct))?.AttributTemperatur
                : Zahl(kuehler);
        }

        // Der Zustand der Steckdose: erst die eigene Rolle, sonst der Schalter
        // selbst. Bei einer Shelly ist beides dieselbe Entität — eine zweite
        // Zuordnung dafür wäre eine Frage ohne Inhalt.
        var steckdoseAn = AnRolle(Rollen.SteckdoseZustand) ?? AnRolle(Rollen.Steckdose);
        var tagPhase = AnRolle(Rollen.LichtZustand);

        var zielAktiv = Zahl(Entitaeten.ZielAktiv)
            ?? (tagPhase == false ? Zahl(Entitaeten.ZielNacht) : Zahl(Entitaeten.ZielTag));

        var (rest, gewechselt) = Sperre(Text(Entitaeten.LetzterSchaltvorgang),
            e.MindestlaufzeitMin, e.MindestpauseMin, steckdoseAn == true, DateTime.Now);

        var doppelt = GrowDiary.Web.Infrastructure.ForkAiSchalter.CropSteeringAktiv
            ? Doppelsteuerung(_grows.GetTents(), Rolle(Rollen.Steckdose))
            : null;

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
            AutomatikAn: An(automatik),
            WaechterAn: An(Entitaeten.Waechter),
            ZielQuelle: ZielQuelle(Gespeichert is not null, PlanFuehrtZiel()),
            SperreRestMin: rest,
            LetzterWechsel: gewechselt,
            DoppelSteuerungEntity: doppelt,
            EinschaltenAbC: zielAktiv is { } z1 ? Math.Round(z1 + hysterese, 2) : null,
            AusschaltenBeiC: zielAktiv,
            Ansteuerung: ansteuerung,
            KuehlerEntity: kuehler,
            KuehlerSollC: kuehlerSoll,
            KuehlerZustand: kuehler is null ? null : Text(kuehler));
    }

    /// <summary>
    /// Fork AI (forkai.115, F-013): Welche Zahlen-Helfer der Kühler schreibt —
    /// ohne die, die der Wochenplan führt.
    /// </summary>
    /// <remarks>
    /// Speichern auf der Kühler-Seite schrieb bisher auch das Zielpaar. Der
    /// Wochenplan hielt das beim nächsten Lauf für eine Handänderung und zog
    /// die Wassertemperatur danach nicht mehr nach.
    /// </remarks>
    public static IReadOnlyList<(string Entity, double Wert)> Schreibliste(
        ChillerEinstellungen e, IEnumerable<string> vomWochenplanGefuehrt)
    {
        var gefuehrt = new HashSet<string>(vomWochenplanGefuehrt, StringComparer.OrdinalIgnoreCase);
        var zahlen = new (string Entity, double Wert)[]
        {
            (Entitaeten.ZielTag, e.ZielTagC),
            (Entitaeten.ZielNacht, e.ZielNachtC),
            (Entitaeten.Mindestlaufzeit, e.MindestlaufzeitMin),
            (Entitaeten.Mindestpause, e.MindestpauseMin),
            (Entitaeten.Hysterese, e.HystereseK),
        };

        return zahlen.Where(z => !gefuehrt.Contains(z.Entity)).ToList();
    }

    /// <summary>
    /// Woher das Zielpaar kommt. Noch ohne Crop-Steering-Fall: die Absenkung des
    /// Entwicklers schreibt an ihr eigenes Zielgerät, und ob das dieselben Helfer
    /// sind, entscheidet der Nutzer dort. Sobald die Zuordnung ausgelesen wird,
    /// gehört sie hierher und nicht in die Oberfläche.
    /// </summary>
    private static string ZielQuelle(bool gespeichert, bool planFuehrt = false)
        => planFuehrt || !gespeichert ? "plan" : "hand";

    private bool PlanFuehrtZiel()
    {
        var gefuehrt = _wochenplan.GefuehrteHelfer();
        return gefuehrt.ContainsKey(Entitaeten.ZielTag) || gefuehrt.ContainsKey(Entitaeten.ZielNacht);
    }

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
