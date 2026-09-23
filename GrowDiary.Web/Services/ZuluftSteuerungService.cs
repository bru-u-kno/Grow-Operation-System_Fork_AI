using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.76): Die Zuluft-Steuerung — Sollwerte, Livebild und der
/// Aus-Knopf der Regelung.
/// </summary>
/// <remarks>
/// <para><b>Erster Aufruf übernimmt, was schon da ist.</b> Wer die Regelung von
/// Hand gebaut hat, hat die Helfer längst eingestellt. Stünden beim ersten
/// Speichern die Werkseinstellungen in der Datenbank, würde der erste Klick auf
/// der Seite die eingestellten Werte überschreiben, ohne dass jemand etwas
/// verstellt hätte. Solange nichts gespeichert ist, kommen die Einstellungen
/// deshalb aus Home Assistant und nur ersatzweise aus den Vorgaben.</para>
///
/// <para><b>Geschrieben wird nur beim Speichern.</b> Es gibt keinen Hintergrundlauf
/// wie beim Wochenplan — die Zuluft hat keine Plan-Quelle, die sich von allein
/// ändert. Damit kann der Fork auch nichts hinter dem Rücken überschreiben.</para>
/// </remarks>
public sealed class ZuluftSteuerungService
{
    public const string Modul = "zuluft";

    /// <summary>Fork AI (forkai.128): Vorgabe für die Kälte-Pause, wenn der Helfer fehlt.</summary>
    public const double ZeltTemperaturMinVorgabe = 21;

    /// <summary>Die Rollen dieses Moduls — aufgelöst über die Geräteseite.</summary>
    public static class Rollen
    {
        public const string AussenTemp = "aussen_temp";
        public const string AussenRh = "aussen_rh";
        public const string KellerTemp = "keller_temp";
        public const string KellerRh = "keller_rh";
        public const string PortZustand = "port_zustand";
        public const string PortStatus = "port_status";
        public const string PortIstStufe = "port_ist_stufe";

        /// <summary>Fork AI (forkai.128): Zelttemperatur für die Kälte-Pause.</summary>
        public const string ZeltTemp = "zelt_temp";
    }

    /// <summary>Die Objekte der Steuerung selbst — sie gehören keinem Gerät.</summary>
    public static class Entitaeten
    {
        public const string MindestDifferenz = "input_number.zuluft_mindest_differenz";
        public const string AussentemperaturMin = "input_number.zuluft_aussentemperatur_min";
        public const string StufeMin = "input_number.zuluft_stufe_min";
        public const string StufeMax = "input_number.zuluft_stufe_max";
        public const string Mindestlaufzeit = "input_number.zuluft_mindestlaufzeit";
        public const string Mindestpause = "input_number.zuluft_mindestpause";
        public const string ZeltTemperaturMin = "input_number.zuluft_zelttemperatur_min";
        public const string LetzterSchaltvorgang = "input_datetime.zuluft_letzter_schaltvorgang";
        public const string AbsolutDraussen = "sensor.absolute_feuchte_draussen";
        public const string AbsolutKeller = "sensor.absolute_feuchte_keller";
        public const string Differenz = "sensor.zuluft_differenz";

        /// <summary>
        /// Optional: geglättete Differenz (Statistik-Helfer, 10 min). Gibt es sie,
        /// rechnet der Bedarf damit — die rohe Differenz springt um 0,5 g/m³ in einer Minute.
        /// </summary>
        public const string DifferenzGeglaettet = "sensor.zuluft_differenz_geglattet";
        public const string Zielstufe = "sensor.zuluft_zielstufe";
        public const string Bedarf = "binary_sensor.zuluft_bedarf";
        public const string Automatik = "automation.zuluft_keller_regelung";
    }

    private readonly SteuerungRepository _repo;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly SteuerungGeraeteService _geraete;
    private readonly ILogger<ZuluftSteuerungService> _logger;

    public ZuluftSteuerungService(
        SteuerungRepository repo,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        SteuerungGeraeteService geraete,
        ILogger<ZuluftSteuerungService> logger)
    {
        _repo = repo;
        _ha = ha;
        _haSettings = haSettings;
        _geraete = geraete;
        _logger = logger;
    }

    // ----------------------------------------------------------- Einstellungen

    /// <summary>
    /// Der gespeicherte Stand — null, solange nie gespeichert wurde oder der Stand
    /// von vor forkai.128 stammt (ohne Zelttemperatur, siehe F-022).
    /// </summary>
    public ZuluftEinstellungen? Gespeichert => GueltigGespeichert(_repo.GetEinstellungen<ZuluftEinstellungen>(Modul));

    /// <summary>
    /// Ein Stand ohne Zelttemperatur stammt von vor forkai.128. Er gilt wie „nie
    /// gespeichert": die Werte in Home Assistant sind seitdem womöglich geändert
    /// worden, und das nächste Speichern würde sie sonst zurückdrehen.
    /// </summary>
    public static ZuluftEinstellungen? GueltigGespeichert(ZuluftEinstellungen? gelesen)
        => gelesen?.ZeltTemperaturMinC is null ? null : gelesen;

    /// <summary>
    /// Die Einstellungen, wie die Seite sie zeigen soll: gespeichert, sonst aus
    /// den vorhandenen Helfern übernommen, sonst Vorgabe.
    /// </summary>
    public async Task<ZuluftEinstellungen> EinstellungenAsync(CancellationToken ct)
    {
        if (Gespeichert is { } vorhanden) return vorhanden;

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return new ZuluftEinstellungen();

        var entities = await _ha.GetEntitiesAsync(settings, ct);
        // Der Umweg ueber string? ist noetig: IReadOnlyDictionary ist im Wert
        // invariant, ein Dictionary<string, string> passt nicht auf <string, string?>.
        var uebernommen = AusHomeAssistant(entities.ToDictionary(
            x => x.EntityId, x => (string?)x.State, StringComparer.OrdinalIgnoreCase));
        // Fork AI (forkai.139, F-035): Beim ersten Aufruf übernimmt der Fork die
        // Werte aus Home Assistant sofort als eigenen Stand — gespeichert wird nur
        // im Fork, nach HA wird nichts geschrieben. Nur mit echter Antwort von HA,
        // sonst stünden die Werkseinstellungen als „übernommen" da.
        if (entities.Count > 0) _repo.SetEinstellungen(Modul, uebernommen);
        return uebernommen;
    }

    /// <summary>
    /// Aus den Zuständen der Helfer Einstellungen bauen. Was fehlt oder sich
    /// nicht lesen lässt, bleibt auf der Vorgabe.
    /// </summary>
    public static ZuluftEinstellungen AusHomeAssistant(IReadOnlyDictionary<string, string?> zustaende)
    {
        var e = new ZuluftEinstellungen();

        double? Zahl(string id) => zustaende.TryGetValue(id, out var s)
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

        if (Zahl(Entitaeten.MindestDifferenz) is { } md) e.MindestDifferenzGm3 = md;
        if (Zahl(Entitaeten.AussentemperaturMin) is { } at) e.AussentemperaturMinC = at;
        if (Zahl(Entitaeten.StufeMin) is { } smin) e.StufeMin = (int)Math.Round(smin);
        if (Zahl(Entitaeten.StufeMax) is { } smax) e.StufeMax = (int)Math.Round(smax);
        if (Zahl(Entitaeten.Mindestlaufzeit) is { } lz) e.MindestlaufzeitMin = (int)Math.Round(lz);
        if (Zahl(Entitaeten.Mindestpause) is { } pa) e.MindestpauseMin = (int)Math.Round(pa);
        e.ZeltTemperaturMinC = Zahl(Entitaeten.ZeltTemperaturMin) ?? ZeltTemperaturMinVorgabe;
        if (zustaende.TryGetValue(Entitaeten.Automatik, out var automatik) && automatik is not null)
        {
            e.AutomatikAktiv = automatik is "on" or "On";
        }

        return e;
    }

    public static Dictionary<string, string> Pruefen(ZuluftEinstellungen e)
    {
        var f = new Dictionary<string, string>();

        if (e.MindestDifferenzGm3 is < 0.2 or > 10)
        {
            f[nameof(e.MindestDifferenzGm3)] = "Mindest-Differenz muss zwischen 0,2 und 10 g/m³ liegen.";
        }

        if (e.AussentemperaturMinC is < -10 or > 25)
        {
            f[nameof(e.AussentemperaturMinC)] = "Außentemperatur-Minimum muss zwischen -10 und 25 °C liegen.";
        }

        if (e.ZeltTemperaturMinC is < 10 or > 30)
        {
            f[nameof(e.ZeltTemperaturMinC)] = "Zelttemperatur-Minimum muss zwischen 10 und 30 °C liegen.";
        }

        if (e.StufeMin is < 1 or > 10) f[nameof(e.StufeMin)] = "Stufe muss zwischen 1 und 10 liegen.";
        if (e.StufeMax is < 1 or > 10) f[nameof(e.StufeMax)] = "Stufe muss zwischen 1 und 10 liegen.";

        // Gleich ist erlaubt — das ist die feste Stufe. Verdreht ist es nicht:
        // die Zielstufe rechnet zwischen min und max hoch und liefe sonst nach
        // unten, je trockener es draußen wird.
        if (e.StufeMin > e.StufeMax) f[nameof(e.StufeMin)] = "Stufe min darf nicht über Stufe max liegen.";

        if (e.MindestlaufzeitMin is < 0 or > 120) f[nameof(e.MindestlaufzeitMin)] = "Mindestlaufzeit: 0 bis 120 Minuten.";
        if (e.MindestpauseMin is < 0 or > 120) f[nameof(e.MindestpauseMin)] = "Mindestpause: 0 bis 120 Minuten.";
        return f;
    }

    /// <summary>Prüft, speichert und schreibt nach Home Assistant.</summary>
    public async Task<(ZuluftEinstellungen? Gespeichert, Dictionary<string, string> Fehler, bool HaErreicht)> SpeichernAsync(
        ZuluftEinstellungen e, CancellationToken ct)
    {
        var fehler = Pruefen(e);
        if (fehler.Count > 0) return (null, fehler, false);

        _repo.SetEinstellungen(Modul, e);
        var erreicht = await NachHomeAssistantSchreibenAsync(e, ct);
        return (e, fehler, erreicht);
    }

    public async Task<bool> NachHomeAssistantSchreibenAsync(ZuluftEinstellungen e, CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var zahlen = new List<(string Entity, double Wert)>
        {
            (Entitaeten.MindestDifferenz, e.MindestDifferenzGm3),
            (Entitaeten.AussentemperaturMin, e.AussentemperaturMinC),
            (Entitaeten.StufeMin, e.StufeMin),
            (Entitaeten.StufeMax, e.StufeMax),
            (Entitaeten.Mindestlaufzeit, e.MindestlaufzeitMin),
            (Entitaeten.Mindestpause, e.MindestpauseMin),
        };
        if (e.ZeltTemperaturMinC is { } zelt) zahlen.Add((Entitaeten.ZeltTemperaturMin, zelt));

        var alles = true;
        foreach (var (entity, wert) in zahlen)
        {
            alles &= await _ha.CallEntityServiceAsync(settings, "input_number", "set_value", entity, ct,
                new Dictionary<string, object> { ["value"] = wert });
        }

        alles &= await _ha.CallEntityServiceAsync(
            settings, "automation", e.AutomatikAktiv ? "turn_on" : "turn_off", Entitaeten.Automatik, ct);

        if (!alles) _logger.LogWarning("Zuluft-Sollwerte: nicht alle Helfer in Home Assistant angenommen.");
        return alles;
    }

    // --------------------------------------------------------------- Livebild

    public async Task<ZuluftLive> LiveAsync(CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);

        string? Text(string id) => nachId.TryGetValue(id, out var s) ? s.State : null;
        double? Zahl(string id) => double.TryParse(Text(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        bool? An(string id) => Text(id) is { } t && t is not ("unknown" or "unavailable") ? t is "on" or "On" : null;

        // Geräte kommen aus der Zuordnung, nicht aus dem Code: eine Rolle ohne
        // Gerät liefert null, dann steht in der Kachel ein „–".
        var geraete = _geraete.EntitiesFuerModul(Modul);
        string? Rolle(string rolle) => geraete.TryGetValue(rolle, out var id) ? id : null;
        double? ZahlRolle(string rolle) => Rolle(rolle) is { } id ? Zahl(id) : null;
        bool? AnRolle(string rolle) => Rolle(rolle) is { } id ? An(id) : null;

        var e = Gespeichert ?? AusHomeAssistant(
            nachId.ToDictionary(x => x.Key, x => (string?)x.Value.State, StringComparer.OrdinalIgnoreCase));

        var (rest, gewechselt) = Sperre(Text(Entitaeten.LetzterSchaltvorgang),
            e.MindestlaufzeitMin, e.MindestpauseMin, AnRolle(Rollen.PortZustand) == true, DateTime.Now);

        var zeltTemp = ZahlRolle(Rollen.ZeltTemp);
        var pause = PauseWegenZeltkaelte(
            An(Entitaeten.Bedarf),
            Zahl(Entitaeten.DifferenzGeglaettet) ?? Zahl(Entitaeten.Differenz),
            e.MindestDifferenzGm3,
            zeltTemp,
            e.ZeltTemperaturMinC ?? Zahl(Entitaeten.ZeltTemperaturMin));

        return new ZuluftLive(
            HaErreichbar: entities.Count > 0,
            DifferenzGm3: Zahl(Entitaeten.Differenz),
            AussenAbsolutGm3: Zahl(Entitaeten.AbsolutDraussen),
            KellerAbsolutGm3: Zahl(Entitaeten.AbsolutKeller),
            AussenTempC: ZahlRolle(Rollen.AussenTemp),
            AussenRhProzent: ZahlRolle(Rollen.AussenRh),
            KellerTempC: ZahlRolle(Rollen.KellerTemp),
            KellerRhProzent: ZahlRolle(Rollen.KellerRh),
            Bedarf: An(Entitaeten.Bedarf),
            Zielstufe: Zahl(Entitaeten.Zielstufe) is { } z ? (int)Math.Round(z) : null,
            IstStufe: ZahlRolle(Rollen.PortIstStufe) is { } i ? (int)Math.Round(i) : null,
            PortAn: AnRolle(Rollen.PortZustand),
            PortOnline: AnRolle(Rollen.PortStatus),
            AutomatikAn: An(Entitaeten.Automatik),
            SperreRestMin: rest,
            LetzterWechsel: gewechselt,
            ZeltTempC: zeltTemp,
            PauseZeltKalt: pause);
    }

    /// <summary>
    /// Fork AI (forkai.128): Pausiert die Zuluft gerade nur, weil das Zelt zu kalt ist?
    /// </summary>
    /// <remarks>
    /// Ja, wenn die Außenluft trocknen würde (Differenz über der Schwelle), der
    /// Bedarf aber aus ist und das Zelt unter Minimum + 1 °C liegt — das ist das
    /// Band, in dem der Bedarf wegen der Kälte aus bleibt. null, wenn einer der
    /// Werte fehlt: dann lässt sich der Grund nicht sagen.
    /// </remarks>
    public static bool? PauseWegenZeltkaelte(
        bool? bedarf, double? differenz, double schwelle, double? zeltTemp, double? zeltMin)
    {
        if (bedarf is null || differenz is null || zeltTemp is null || zeltMin is null) return null;
        return bedarf == false && differenz >= schwelle && zeltTemp < zeltMin + 1;
    }

    /// <summary>
    /// Wie lange die Schaltsperre noch läuft — läuft der Lüfter, zählt die
    /// Mindestlaufzeit, sonst die Mindestpause.
    /// </summary>
    /// <remarks>
    /// Gerechnet wird gegen den Zeitstempel-Helfer und nicht gegen
    /// <c>last_changed</c> des Ports: die AC-Infinity-Entitäten fallen bei
    /// Verbindungsabbrüchen des Controllers kurz aus, und jeder dieser Aussetzer
    /// setzte <c>last_changed</c> neu.
    /// </remarks>
    public static (int? RestMin, DateTime? Letzter) Sperre(
        string? zeitstempel, int laufzeitMin, int pauseMin, bool portAn, DateTime jetzt)
    {
        if (!DateTime.TryParse(zeitstempel, CultureInfo.InvariantCulture, DateTimeStyles.None, out var letzter))
        {
            return (null, null);
        }

        var gate = TimeSpan.FromMinutes(portAn ? laufzeitMin : pauseMin);
        var alter = jetzt - letzter;
        var rest = gate - alter;
        return (rest <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(rest.TotalMinutes), letzter);
    }
}
