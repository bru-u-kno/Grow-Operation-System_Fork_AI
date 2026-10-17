using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.129, F-023): Die Entfeuchter-Steuerung — Geräteverhalten,
/// Livebild und der Aus-Knopf der Regelung.
/// </summary>
/// <remarks>
/// <para><b>Erster Aufruf übernimmt, was schon da ist.</b> Solange nichts
/// gespeichert ist, kommen die Werte aus den Helfern in Home Assistant — sonst
/// überschriebe der erste Klick auf „Speichern" Werte, die dort längst stimmen
/// (dasselbe Vorgehen wie bei Zuluft und Kühler).</para>
///
/// <para><b>Pflanzenziele gehören nicht hierher.</b> VPD-Band, Luftfeuchte max.
/// und Blatt-Offset liest diese Seite nur; gepflegt werden sie unter „Ziele &amp;
/// Meldungen". Geschrieben werden hier nur Helfer, die das Gerät beschreiben.</para>
///
/// <para><b>Temperatur max. kann dem Plan folgen.</b> Steht ein Modus auf
/// <see cref="TempMaxModus.Plan"/>, rechnet der Fork Plan-Luft + Abstand und
/// schreibt das Ergebnis in den Helfer; ohne laufenden Plan gilt der feste Wert.</para>
/// </remarks>
public sealed class EntfeuchterSteuerungService : IStartwertDienst
{
    public const string Modul = "entfeuchter";

    /// <summary>Die Rollen dieses Moduls — aufgelöst über die Geräteseite.</summary>
    public static class Rollen
    {
        public const string ZeltFeuchte = "zelt_rh";
        public const string ZeltTemp = "zelt_temp";
        public const string ZeltVpd = "zelt_vpd";
        public const string PortSchalter = "port_schalter";
        public const string PortZustand = "port_zustand";
        public const string PortStatus = "port_status";
        public const string LichtZustand = "licht_zustand";
    }

    /// <summary>Die Objekte der Steuerung selbst — sie gehören keinem Gerät.</summary>
    public static class Entitaeten
    {
        public static string VpdRegelung => Kennungen.Id("input_boolean.fork_entfeuchter_vpd_regelung");
        public static string Hysterese => Kennungen.Id("input_number.fork_entfeuchter_hysterese");
        public static string Mindestlaufzeit => Kennungen.Id("input_number.fork_entfeuchter_mindestlaufzeit");
        public static string Einschaltverzoegerung => Kennungen.Id("input_number.fork_entfeuchter_einschaltverzoegerung");
        public static string WartezeitAussenluft => Kennungen.Id("input_number.fork_entfeuchter_wartezeit_aussenluft");
        public static string Tagbetrieb => Kennungen.Id("input_boolean.fork_entfeuchter_tagbetrieb_erlauben");
        public static string TempMaxTag => Kennungen.Id("input_number.fork_entfeuchter_temp_max_tag");
        public static string TempMaxNacht => Kennungen.Id("input_number.fork_entfeuchter_temp_max_nacht");
        public static string FeuchteEinTag => Kennungen.Id("input_number.fork_entfeuchter_feuchte_ein_tag");
        public static string FeuchteAusTag => Kennungen.Id("input_number.fork_entfeuchter_feuchte_aus_tag");
        public static string FeuchteEinNacht => Kennungen.Id("input_number.fork_entfeuchter_feuchte_ein_nacht");
        public static string FeuchteAusNacht => Kennungen.Id("input_number.fork_entfeuchter_feuchte_aus_nacht");
        public static string Automatik => Kennungen.Id("automation.entfeuchter_regelung_tag_nacht");

        /// <summary>
        /// Die Helfer, in die der Fork seine Einstellungen schreibt: die Zahlen und die zwei Schalter. Nach dem Anlegen
        /// bekommen die neuen davon ihren Startwert (<see cref="SteuerungStartwerteService"/>).
        /// </summary>
        public static IReadOnlyList<string> Startwerte =>
        [
            VpdRegelung, Hysterese, Mindestlaufzeit, Einschaltverzoegerung, WartezeitAussenluft, Tagbetrieb,
            TempMaxTag, TempMaxNacht, FeuchteEinTag, FeuchteAusTag, FeuchteEinNacht, FeuchteAusNacht,
        ];

        // Nur gelesen — gepflegt woanders.
        public static string EinAktiv => Kennungen.Id("sensor.fork_entfeuchter_feuchte_ein_aktiv");
        public static string AusAktiv => Kennungen.Id("sensor.fork_entfeuchter_feuchte_aus_aktiv");
        public static string TempMaxAktiv => Kennungen.Id("sensor.fork_entfeuchter_temp_max_aktiv");
        public const string VpdUnten = "input_number.vpd_ziel_unten";
        public const string VpdOben = "input_number.vpd_ziel_abschaltung";
        public const string BlattOffset = "input_number.vpd_blatt_offset";
        public const string RhObergrenze = "input_number.co2_rh_obergrenze";
        public const string KlimaHysterese = "input_number.co2_klima_hysterese";
        public const string CanopyObergrenze = "input_number.co2_canopy_obergrenze";
        public const string ZuluftBedarf = "binary_sensor.zuluft_bedarf";
        public const string Co2Drosseln = "input_boolean.co2_abluft_drosseln";
    }

    private readonly SteuerungRepository _repo;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly SteuerungGeraeteService _geraete;
    private readonly WochenplanSyncService _wochenplan;
    private readonly HelferBereichService _bereiche;
    private readonly ILogger<EntfeuchterSteuerungService> _logger;

    public EntfeuchterSteuerungService(
        SteuerungRepository repo,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        SteuerungGeraeteService geraete,
        WochenplanSyncService wochenplan,
        HelferBereichService bereiche,
        ILogger<EntfeuchterSteuerungService> logger)
    {
        _repo = repo;
        _ha = ha;
        _haSettings = haSettings;
        _geraete = geraete;
        _wochenplan = wochenplan;
        _bereiche = bereiche;
        _logger = logger;
    }

    // ----------------------------------------------------------- Einstellungen

    /// <summary>Der gespeicherte Stand — null, solange nie gespeichert wurde.</summary>
    public EntfeuchterEinstellungen? Gespeichert => _repo.GetEinstellungen<EntfeuchterEinstellungen>(Modul);

    public async Task<EntfeuchterEinstellungen> EinstellungenAsync(CancellationToken ct)
    {
        if (Gespeichert is { } vorhanden) return vorhanden;

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return new EntfeuchterEinstellungen();

        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var uebernommen = AusHomeAssistant(entities.ToDictionary(
            x => x.EntityId, x => (string?)x.State, StringComparer.OrdinalIgnoreCase),
            AutomatikKennungen(entities));
        // Fork AI (forkai.139, F-035): Beim ersten Aufruf übernimmt der Fork die
        // Werte aus Home Assistant sofort als eigenen Stand — gespeichert wird nur
        // im Fork, nach HA wird nichts geschrieben. Nur mit echter Antwort von HA,
        // sonst stünden die Werkseinstellungen als „übernommen" da.
        if (entities.Count > 0) _repo.SetEinstellungen(Modul, uebernommen);
        return uebernommen;
    }

    /// <summary>
    /// Aus den Zuständen der Helfer Einstellungen bauen. Was fehlt oder sich
    /// nicht lesen lässt, bleibt auf der Vorgabe. Temperatur max. kommt dabei
    /// immer als „Fest" — ob der Wert einmal aus dem Plan stammte, weiß Home
    /// Assistant nicht.
    /// </summary>
    /// <param name="zustaende">Entität → Zustand aus Home Assistant.</param>
    /// <param name="automatiken">Die Regelungen, die es dort wirklich gibt (<see cref="AutomatikKennungen"/>); ohne Angabe die Katalog-Kennung.</param>
    public static EntfeuchterEinstellungen AusHomeAssistant(
        IReadOnlyDictionary<string, string?> zustaende, IReadOnlyList<string>? automatiken = null)
    {
        var e = new EntfeuchterEinstellungen();

        double? Zahl(string id) => zustaende.TryGetValue(id, out var s)
            ? Zahlenlesen.Maschine(s) : null;
        bool? An(string id) => zustaende.TryGetValue(id, out var s) && s is "on" or "off" or "On" or "Off"
            ? s is "on" or "On" : null;

        if (An(Entitaeten.VpdRegelung) is { } vpd) e.VpdRegelung = vpd;
        if (Zahl(Entitaeten.Hysterese) is { } h) e.HystereseProzent = h;
        if (Zahl(Entitaeten.Mindestlaufzeit) is { } lz) e.MindestlaufzeitMin = (int)Math.Round(lz);
        if (Zahl(Entitaeten.Einschaltverzoegerung) is { } ev) e.EinschaltverzoegerungMin = (int)Math.Round(ev);
        if (Zahl(Entitaeten.WartezeitAussenluft) is { } wa) e.WartezeitAussenluftMin = (int)Math.Round(wa);
        if (An(Entitaeten.Tagbetrieb) is { } tb) e.TagbetriebErlauben = tb;
        if (Zahl(Entitaeten.TempMaxTag) is { } tt) e.TempMaxTagFestC = tt;
        if (Zahl(Entitaeten.TempMaxNacht) is { } tn) e.TempMaxNachtFestC = tn;
        if (Zahl(Entitaeten.FeuchteEinTag) is { } fet) e.FeuchteEinTag = fet;
        if (Zahl(Entitaeten.FeuchteAusTag) is { } fat) e.FeuchteAusTag = fat;
        if (Zahl(Entitaeten.FeuchteEinNacht) is { } fen) e.FeuchteEinNacht = fen;
        if (Zahl(Entitaeten.FeuchteAusNacht) is { } fan) e.FeuchteAusNacht = fan;
        var an = (automatiken ?? [Entitaeten.Automatik]).Select(An).OfType<bool>().ToList();
        if (an.Count > 0) e.AutomatikAktiv = an.Any(x => x);
        return e;
    }

    /// <summary>
    /// Fork AI (A-016, Etappe 5): Unter welchen Entity-IDs die Entfeuchter-Regelung in Home Assistant steht — handgebaut
    /// (<see cref="Entitaeten.Automatik"/>) oder vom Fork angelegt (Entity-ID aus dem Alias der Vorlage, gefunden über
    /// ihre Konfigurations-Kennung). Vorher wurde immer die Katalog-Kennung geschaltet: bei einer vom Fork angelegten
    /// Regelung ging „Automatik aus" an einer Entität vorbei, die es nicht gibt, und die Seite zeigte nie ihren Zustand.
    /// </summary>
    /// <returns>Ohne Entitätenliste (Home Assistant stumm) die Katalog-Kennung; sonst nur, was es wirklich gibt.</returns>
    public static IReadOnlyList<string> AutomatikKennungen(IReadOnlyCollection<HomeAssistantEntity> alle)
    {
        if (alle.Count == 0) return [Entitaeten.Automatik];
        var regelung = SteuerungBauteile.FuerModul(Modul).Single(b => b.EntityId == Entitaeten.Automatik);
        return SteuerungBauteile.AutomationFinden(regelung, alle);
    }

    public static Dictionary<string, string> Pruefen(EntfeuchterEinstellungen e)
    {
        var f = new Dictionary<string, string>();

        // Unter 1 % taktet er im Messrauschen (der Fühler springt um ~1 %/min),
        // über 10 % entfeuchtet er weit am Ziel vorbei.
        if (e.HystereseProzent is < 1 or > 10) f[nameof(e.HystereseProzent)] = "Abstand EIN → AUS: 1 bis 10 %.";
        if (e.MindestlaufzeitMin is < 0 or > 60) f[nameof(e.MindestlaufzeitMin)] = "Mindestlaufzeit: 0 bis 60 Minuten.";
        if (e.EinschaltverzoegerungMin is < 0 or > 60) f[nameof(e.EinschaltverzoegerungMin)] = "Einschaltverzögerung: 0 bis 60 Minuten.";
        if (e.WartezeitAussenluftMin is < 0 or > 120) f[nameof(e.WartezeitAussenluftMin)] = "Wartezeit Außenluft: 0 bis 120 Minuten.";

        // Wer auf Außenluft wartet, soll nicht kürzer warten als ohne sie —
        // sonst dreht sich die Bedeutung um.
        if (e.WartezeitAussenluftMin < e.EinschaltverzoegerungMin)
        {
            f[nameof(e.WartezeitAussenluftMin)] = "Die Wartezeit auf Außenluft darf nicht kürzer sein als die Einschaltverzögerung.";
        }

        PruefeTempMax(f, nameof(e.TempMaxTagModus), e.TempMaxTagModus, nameof(e.TempMaxTagAbstandK), e.TempMaxTagAbstandK, nameof(e.TempMaxTagFestC), e.TempMaxTagFestC);
        PruefeTempMax(f, nameof(e.TempMaxNachtModus), e.TempMaxNachtModus, nameof(e.TempMaxNachtAbstandK), e.TempMaxNachtAbstandK, nameof(e.TempMaxNachtFestC), e.TempMaxNachtFestC);

        if (e.ArbeitsbereichQuelle is not (ArbeitsbereichQuellen.Plan or ArbeitsbereichQuellen.Fest)) f[nameof(e.ArbeitsbereichQuelle)] = "Arbeitsbereich: „plan“ oder „fest“.";
        if (e.VorlaufProzent is < 0 or > 20) f[nameof(e.VorlaufProzent)] = "Vorlauf unter der Plan-Obergrenze: 0 bis 20 %.";

        PruefeSchwellen(f, nameof(e.FeuchteEinTag), e.FeuchteEinTag, nameof(e.FeuchteAusTag), e.FeuchteAusTag);
        PruefeSchwellen(f, nameof(e.FeuchteEinNacht), e.FeuchteEinNacht, nameof(e.FeuchteAusNacht), e.FeuchteAusNacht);
        return f;
    }

    /// <summary>
    /// Die Schwellen, die bei <c>ArbeitsbereichQuelle = plan</c> gelten: EIN = Plan-Obergrenze minus Vorlauf, AUS = EIN minus Abstand.
    /// <c>null</c>, wenn der Modus <c>fest</c> ist oder der Plan keine Obergrenze liefert — dann gelten die gespeicherten Werte.
    /// </summary>
    /// <remarks>
    /// EIN bleibt in der Spanne, die das Backend prüft (30–90 %) und lässt AUS über der Untergrenze: kleinster Wert 31 plus Abstand.
    /// Auf eine Nachkommastelle gerundet, damit 54 − 4 nie als 49,99999 in Home Assistant landet.
    /// </remarks>
    public static (double Ein, double Aus)? ArbeitsbereichAusPlan(EntfeuchterEinstellungen e, double? rhObergrenze)
    {
        if (e.ArbeitsbereichQuelle != ArbeitsbereichQuellen.Plan || rhObergrenze is not { } rh || !double.IsFinite(rh)) return null;
        var kleinstes = 31 + Math.Ceiling(e.HystereseProzent);
        var ein = Math.Round(Math.Clamp(rh - e.VorlaufProzent, kleinstes, 90), 1, MidpointRounding.AwayFromZero);
        var aus = Math.Round(Math.Max(30, ein - e.HystereseProzent), 1, MidpointRounding.AwayFromZero);
        return (ein, aus);
    }

    /// <summary>
    /// Wie <see cref="ArbeitsbereichAusPlan(EntfeuchterEinstellungen, double?)"/>, aber für Tag und Nacht einzeln: Der Plan nennt
    /// für die Nacht eine eigene Obergrenze der Luftfeuchte, und die Entfeuchter sollen sie nachts genauso einhalten wie die
    /// Tages-Obergrenze am Tag. Nennt der Plan keine Nachtgrenze, gilt nachts die des Tages.
    /// </summary>
    public static EntfeuchterBereich? ArbeitsbereichAusPlan(EntfeuchterEinstellungen e, double? rhTag, double? rhNacht)
    {
        if (ArbeitsbereichAusPlan(e, rhTag) is not { } tag) return null;
        var nacht = ArbeitsbereichAusPlan(e, rhNacht) ?? tag;
        return new EntfeuchterBereich(tag.Ein, tag.Aus, nacht.Ein, nacht.Aus);
    }

    private static void PruefeTempMax(Dictionary<string, string> f, string modusFeld, string? modus, string abstandFeld, double abstand, string festFeld, double fest)
    {
        if (modus is not (TempMaxModus.Plan or TempMaxModus.Fest)) f[modusFeld] = "Modus muss „plan“ oder „fest“ sein.";
        if (abstand is < 0 or > 15) f[abstandFeld] = "Abstand zum Plan: 0 bis 15 K.";
        if (fest is < 15 or > 35) f[festFeld] = "Temperatur max.: 15 bis 35 °C.";
    }

    private static void PruefeSchwellen(Dictionary<string, string> f, string einFeld, double ein, string ausFeld, double aus)
    {
        if (ein is < 30 or > 90) f[einFeld] = "EIN-Schwelle: 30 bis 90 %.";
        if (aus is < 30 or > 90) f[ausFeld] = "AUS-Schwelle: 30 bis 90 %.";
        if (aus >= ein) f[ausFeld] = "AUS muss unter EIN liegen.";
    }

    /// <summary>
    /// Temperatur max., wie sie gerade gilt: Plan-Luft + Abstand, sonst der feste Wert.
    /// </summary>
    /// <remarks>Ohne Plan-Wert fällt „Plan" auf den festen Wert zurück, statt keinen zu liefern.</remarks>
    public static double TempMax(string? modus, double abstandK, double festC, double? planLuftC)
        => modus == TempMaxModus.Plan && planLuftC is { } plan ? Math.Round(plan + abstandK, 1) : festC;

    /// <summary>
    /// <see cref="TempMax"/>, begrenzt auf die Spanne des Helfers, in den sie geschrieben wird.
    /// </summary>
    /// <remarks>
    /// Fork AI (02.10.2026): Plan-Luft + Abstand (bis 15 K) kann über das Ende
    /// des Helfers (35 °C) hinausgehen. Home Assistant lehnte dann jedes
    /// <c>set_value</c> ab — bei jedem Abgleich und jedem Wochenwechsel wieder,
    /// und die Seite zeigte eine Grenze, die nie galt. Anzeige, Schreiben und
    /// Nachziehen gehen deshalb alle hierüber.
    /// </remarks>
    public static double TempMaxFuer(string helfer, string? modus, double abstandK, double festC, double? planLuftC)
        => SteuerungBauteile.AufSpanne(helfer, TempMax(modus, abstandK, festC, planLuftC));

    /// <summary>Prüft, speichert und schreibt nach Home Assistant.</summary>
    public async Task<(EntfeuchterEinstellungen? Gespeichert, Dictionary<string, string> Fehler, bool HaErreicht)> SpeichernAsync(
        EntfeuchterEinstellungen e, CancellationToken ct)
    {
        var fehler = Pruefen(e);
        if (fehler.Count > 0) return (null, fehler, false);

        // Bei „Arbeitsbereich aus dem Plan" stehen im gespeicherten Stand die gerechneten Schwellen, nicht die alten festen —
        // sonst zeigte die Seite nach dem Speichern etwas anderes, als in Home Assistant liegt.
        if (ArbeitsbereichAusPlan(e, await RhObergrenzeAsync(ct), _wochenplan.PlanFeuchte()?.RhNacht) is { } plan)
        {
            e.FeuchteEinTag = plan.EinTag;
            e.FeuchteAusTag = plan.AusTag;
            e.FeuchteEinNacht = plan.EinNacht;
            e.FeuchteAusNacht = plan.AusNacht;
        }

        _repo.SetEinstellungen(Modul, e);
        var erreicht = await NachHomeAssistantSchreibenAsync(e, ct);
        return (e, fehler, erreicht);
    }

    /// <summary>Die Plan-Obergrenze der Luftfeuchte aus dem Helfer, in den der Wochenplan sie schreibt; <c>null</c>, wenn Home Assistant stumm ist.</summary>
    private async Task<double?> RhObergrenzeAsync(CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return null;
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        return Zahlenlesen.Maschine(entities.FirstOrDefault(x => string.Equals(x.EntityId, Entitaeten.RhObergrenze, StringComparison.OrdinalIgnoreCase))?.State);
    }

    /// <summary>
    /// Die Zahlen, die in den Helfern stehen sollen: die gespeicherten Einstellungen, bei „Plan" mit den Werten aus dem
    /// Plan gerechnet. EINE Rechnung für das Schreiben und für den Vergleich mit Home Assistant (<see cref="AbweichungenAsync"/>).
    /// </summary>
    private async Task<(string Entity, double Wert)[]> SollZahlenAsync(EntfeuchterEinstellungen e, CancellationToken ct)
    {
        var plan = _wochenplan.PlanLuft();
        // „Arbeitsbereich aus dem Plan": EIN und AUS kommen aus der Plan-Obergrenze, nicht aus den gespeicherten festen Werten.
        var bereich = ArbeitsbereichAusPlan(e, await RhObergrenzeAsync(ct), _wochenplan.PlanFeuchte()?.RhNacht);
        return new (string Entity, double Wert)[]
        {
            (Entitaeten.Hysterese, e.HystereseProzent),
            (Entitaeten.Mindestlaufzeit, e.MindestlaufzeitMin),
            (Entitaeten.Einschaltverzoegerung, e.EinschaltverzoegerungMin),
            (Entitaeten.WartezeitAussenluft, e.WartezeitAussenluftMin),
            (Entitaeten.TempMaxTag, TempMaxFuer(Entitaeten.TempMaxTag, e.TempMaxTagModus, e.TempMaxTagAbstandK, e.TempMaxTagFestC, plan?.LuftTagC)),
            (Entitaeten.TempMaxNacht, TempMaxFuer(Entitaeten.TempMaxNacht, e.TempMaxNachtModus, e.TempMaxNachtAbstandK, e.TempMaxNachtFestC, plan?.LuftNachtC)),
            (Entitaeten.FeuchteEinTag, bereich?.EinTag ?? e.FeuchteEinTag),
            (Entitaeten.FeuchteAusTag, bereich?.AusTag ?? e.FeuchteAusTag),
            (Entitaeten.FeuchteEinNacht, bereich?.EinNacht ?? e.FeuchteEinNacht),
            (Entitaeten.FeuchteAusNacht, bereich?.AusNacht ?? e.FeuchteAusNacht),
        };
    }

    /// <summary>Ein Helfer in Home Assistant, dessen Wert vom Wert der Einstellungen im Fork abweicht.</summary>
    public sealed record Abweichung(string Entity, string Name, double ForkWert, double HaWert, string Einheit);

    /// <summary>
    /// Anzeigename und Einheit je Helfer. Der Schlüssel ist die Kennung im Katalog (neuer Raum); gesucht wird über
    /// <see cref="Kennungen.Id(string)"/> zur Laufzeit, denn die Entity-ID hängt vom Kennungsraum ab (Bestand trägt alte Namen).
    /// </summary>
    private static readonly (string NeueId, string Name, string Einheit)[] Beschriftung =
    [
        ("input_number.fork_entfeuchter_hysterese", "Hysterese", "%"),
        ("input_number.fork_entfeuchter_mindestlaufzeit", "Mindestlaufzeit", "min"),
        ("input_number.fork_entfeuchter_einschaltverzoegerung", "Einschaltverzögerung", "min"),
        ("input_number.fork_entfeuchter_wartezeit_aussenluft", "Wartezeit bei Außenluft", "min"),
        ("input_number.fork_entfeuchter_temp_max_tag", "Höchsttemperatur Tag", "°C"),
        ("input_number.fork_entfeuchter_temp_max_nacht", "Höchsttemperatur Nacht", "°C"),
        ("input_number.fork_entfeuchter_feuchte_ein_tag", "Feuchte EIN · Tag", "%"),
        ("input_number.fork_entfeuchter_feuchte_aus_tag", "Feuchte AUS · Tag", "%"),
        ("input_number.fork_entfeuchter_feuchte_ein_nacht", "Feuchte EIN · Nacht", "%"),
        ("input_number.fork_entfeuchter_feuchte_aus_nacht", "Feuchte AUS · Nacht", "%"),
    ];

    /// <summary>Anzeigename und Einheit eines Helfers; unbekannt: die Entity-ID selbst, ohne Einheit.</summary>
    public static (string Name, string Einheit) BeschriftungFuer(string entity)
    {
        foreach (var (neueId, name, einheit) in Beschriftung)
        {
            if (string.Equals(Kennungen.Id(neueId), entity, StringComparison.OrdinalIgnoreCase)) return (name, einheit);
        }
        return (entity, string.Empty);
    }

    /// <summary>
    /// Wo die Helfer in Home Assistant von den Einstellungen des Fork abweichen. Neuling-Test 17.10.2026: Die Seite zeigte 60 %,
    /// Home Assistant regelte mit 30 % — und niemand sah es. Ein Helfer, den es nicht gibt, zählt hier nicht (das meldet der
    /// Bestand); ohne Antwort von Home Assistant ist die Liste leer, nicht voll.
    /// </summary>
    public async Task<IReadOnlyList<Abweichung>> AbweichungenAsync(EntfeuchterEinstellungen e, CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return [];

        var entities = await _ha.GetEntitiesAsync(settings, ct);
        if (entities.Count == 0) return [];

        var zustaende = entities.ToDictionary(x => x.EntityId, x => x.State, StringComparer.OrdinalIgnoreCase);
        var liste = new List<Abweichung>();
        foreach (var (entity, wert) in await SollZahlenAsync(e, ct))
        {
            if (!zustaende.TryGetValue(entity, out var roh) || Zahlenlesen.Maschine(roh) is not { } ha) continue;

            // Verglichen wird, was Home Assistant überhaupt annehmen kann (Spanne des Helfers) — wie beim Schreiben.
            var soll = SteuerungBauteile.AufSpanne(entity, wert);
            if (Math.Abs(soll - ha) <= 0.05) continue;

            var (name, einheit) = BeschriftungFuer(entity);
            liste.Add(new Abweichung(entity, name, soll, ha, einheit));
        }

        return liste;
    }

    string IStartwertDienst.StartModul => Modul;

    async Task<object> IStartwertDienst.StandFesthaltenAsync(CancellationToken ct) => await EinstellungenAsync(ct);

    async Task<(int Geschrieben, bool Alles)> IStartwertDienst.StartwerteSchreibenAsync(object stand, IReadOnlySet<string> neuAngelegt, CancellationToken ct)
    {
        var e = (EntfeuchterEinstellungen)stand;
        var umfang = Schreibumfang(await SollZahlenAsync(e, ct), neuAngelegt);
        var anzahl = umfang.Zahlen.Count + (umfang.VpdRegelung ? 1 : 0) + (umfang.Tagbetrieb ? 1 : 0);
        if (anzahl == 0) return (0, true);

        var alles = await NachHomeAssistantSchreibenAsync(e, ct, neuAngelegt);
        return (alles ? anzahl : 0, alles);
    }

    /// <summary>Was ein Schreibvorgang anfasst — Ergebnis von <see cref="Schreibumfang"/>.</summary>
    public sealed record Umfang(IReadOnlyList<(string Entity, double Wert)> Zahlen, bool VpdRegelung, bool Tagbetrieb, bool RegelungSchalten);

    /// <summary>
    /// Welche Helfer geschrieben werden: alle (<paramref name="nur"/> = <c>null</c>, wie beim Speichern der Einstellungen,
    /// dazu die Regelung schalten) oder nur die genannten (frisch angelegte; die Regelung bleibt unberührt). Eine reine
    /// Entscheidung ohne Home Assistant — damit prüfbar, dass ein bestehender Helfer nicht überschrieben wird.
    /// </summary>
    public static Umfang Schreibumfang(IReadOnlyList<(string Entity, double Wert)> zahlen, IReadOnlySet<string>? nur)
    {
        bool Gewollt(string entity) => nur is null || nur.Contains(entity);
        return new Umfang(
            zahlen.Where(z => Gewollt(z.Entity)).ToArray(),
            Gewollt(Entitaeten.VpdRegelung),
            Gewollt(Entitaeten.Tagbetrieb),
            nur is null);
    }

    /// <summary>
    /// Schreibt die Einstellungen in die Helfer in Home Assistant.
    /// </summary>
    /// <param name="nur">
    /// Neuling-Test 17.10.2026: Mit einer Menge schreibt der Aufruf <b>nur diese Helfer</b> und lässt alles andere
    /// (auch die Regelung selbst) in Ruhe. So bekommen frisch angelegte Helfer die Werte des Fork, ohne dass ein
    /// Helfer überschrieben wird, den es schon gab. <c>null</c>: alles, wie beim Speichern der Einstellungen.
    /// </param>
    public async Task<bool> NachHomeAssistantSchreibenAsync(EntfeuchterEinstellungen e, CancellationToken ct, IReadOnlySet<string>? nur = null)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var umfang = Schreibumfang(await SollZahlenAsync(e, ct), nur);
        var zahlen = umfang.Zahlen;

        // Ein von Hand angelegter Helfer kann engere Grenzen tragen als der Katalog (Nacht-EIN stand auf 50–85, gewollt war 48): Home
        // Assistant lehnt den Wert dann ab. Vorher auf den Bereich des Katalogs weiten, sonst bleibt der alte Wert stehen.
        await _bereiche.SichernAsync(settings, zahlen.Select(z => (z.Entity, SteuerungBauteile.AufSpanne(z.Entity, z.Wert))).ToList(), ct);

        var alles = true;
        foreach (var (entity, wert) in zahlen)
        {
            // Jeder Wert auf die Spanne seines Helfers — ein Wert außerhalb lehnt
            // Home Assistant ab (SteuerungBauteile.AufSpanne).
            alles &= await _ha.CallEntityServiceAsync(settings, "input_number", "set_value", entity, ct,
                new Dictionary<string, object> { ["value"] = SteuerungBauteile.AufSpanne(entity, wert) });
        }

        if (umfang.VpdRegelung)
            alles &= await _ha.CallEntityServiceAsync(settings, "input_boolean", e.VpdRegelung ? "turn_on" : "turn_off", Entitaeten.VpdRegelung, ct);
        if (umfang.Tagbetrieb)
            alles &= await _ha.CallEntityServiceAsync(settings, "input_boolean", e.TagbetriebErlauben ? "turn_on" : "turn_off", Entitaeten.Tagbetrieb, ct);
        // Die Regelung unter der Kennung schalten, unter der sie wirklich steht. Gibt es keine, bleibt das Speichern
        // der Sollwerte trotzdem ein Erfolg: sie lassen sich auch vor dem Anlegen der Regelung eintragen.
        // Beim Schreiben einzelner Helfer (nur) bleibt die Regelung unberührt.
        if (umfang.RegelungSchalten)
        {
            foreach (var automatik in AutomatikKennungen(await _ha.GetEntitiesAsync(settings, ct)))
            {
                alles &= await _ha.CallEntityServiceAsync(settings, "automation", e.AutomatikAktiv ? "turn_on" : "turn_off", automatik, ct);
            }
        }

        if (!alles) _logger.LogWarning("Entfeuchter-Sollwerte: nicht alle Helfer in Home Assistant angenommen.");
        return alles;
    }

    /// <summary>
    /// Zieht nach, was dem Plan folgt, wenn sich die Plan-Woche geändert hat: Temperatur max. (Modus „Plan +") und
    /// der Arbeitsbereich der Luftfeuchte (Quelle „plan": EIN = Plan-Obergrenze minus Vorlauf, AUS = EIN minus Abstand).
    /// Schreibt nur, was abweicht. Aufgerufen vom Wochenplan-Worker (Wochenwechsel, täglich 06:00) — nach der Übergabe der
    /// Plan-Obergrenze an Home Assistant, damit sie dort schon steht.
    /// </summary>
    public async Task<int> PlanNachziehenAsync(CancellationToken ct)
    {
        if (Gespeichert is not { } e) return 0;
        var temperaturPlan = e.TempMaxTagModus == TempMaxModus.Plan || e.TempMaxNachtModus == TempMaxModus.Plan;
        var bereichPlan = e.ArbeitsbereichQuelle == ArbeitsbereichQuellen.Plan;
        if (!temperaturPlan && !bereichPlan) return 0;

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return 0;

        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x.State, StringComparer.OrdinalIgnoreCase);
        double? Ist(string id) => Zahlenlesen.Maschine(nachId.GetValueOrDefault(id));

        var geschrieben = 0;
        async Task SchreibenFallsAbweichendAsync(string entity, double soll)
        {
            soll = SteuerungBauteile.AufSpanne(entity, soll);
            if (Ist(entity) is { } ist && Math.Abs(ist - soll) < 0.05) return;
            var werte = new Dictionary<string, object> { ["value"] = soll };
            var angenommen = await _ha.CallEntityServiceAsync(settings, "input_number", "set_value", entity, ct, werte);
            if (!angenommen && (await _bereiche.SichernAsync(settings, new[] { (entity, soll) }, ct)).Count > 0)
            {
                angenommen = await _ha.CallEntityServiceAsync(settings, "input_number", "set_value", entity, ct, werte);
            }

            if (angenommen) geschrieben++;
        }

        if (temperaturPlan && _wochenplan.PlanLuft() is { } plan)
        {
            foreach (var (modus, entity, soll) in new[]
                     {
                         (e.TempMaxTagModus, Entitaeten.TempMaxTag, TempMaxFuer(Entitaeten.TempMaxTag, e.TempMaxTagModus, e.TempMaxTagAbstandK, e.TempMaxTagFestC, plan.LuftTagC)),
                         (e.TempMaxNachtModus, Entitaeten.TempMaxNacht, TempMaxFuer(Entitaeten.TempMaxNacht, e.TempMaxNachtModus, e.TempMaxNachtAbstandK, e.TempMaxNachtFestC, plan.LuftNachtC)),
                     })
            {
                if (modus != TempMaxModus.Plan) continue;
                await SchreibenFallsAbweichendAsync(entity, soll);
            }
        }

        if (bereichPlan && ArbeitsbereichAusPlan(e, Ist(Entitaeten.RhObergrenze), _wochenplan.PlanFeuchte()?.RhNacht) is { } bereich)
        {
            foreach (var (entity, soll) in new[]
                     {
                         (Entitaeten.FeuchteEinTag, bereich.EinTag), (Entitaeten.FeuchteAusTag, bereich.AusTag),
                         (Entitaeten.FeuchteEinNacht, bereich.EinNacht), (Entitaeten.FeuchteAusNacht, bereich.AusNacht),
                     })
            {
                await SchreibenFallsAbweichendAsync(entity, soll);
            }

            // Auch der gespeicherte Stand folgt, sonst zeigte die Seite nach dem Wochenwechsel noch die alten Schwellen.
            if (Math.Abs(e.FeuchteEinTag - bereich.EinTag) > 0.05 || Math.Abs(e.FeuchteAusTag - bereich.AusTag) > 0.05
                || Math.Abs(e.FeuchteEinNacht - bereich.EinNacht) > 0.05 || Math.Abs(e.FeuchteAusNacht - bereich.AusNacht) > 0.05)
            {
                e.FeuchteEinTag = bereich.EinTag;
                e.FeuchteAusTag = bereich.AusTag;
                e.FeuchteEinNacht = bereich.EinNacht;
                e.FeuchteAusNacht = bereich.AusNacht;
                _repo.SetEinstellungen(Modul, e);
            }
        }

        if (geschrieben > 0) _logger.LogInformation("Entfeuchter: aus dem Plan nachgezogen ({Anzahl} Helfer).", geschrieben);
        return geschrieben;
    }

    // --------------------------------------------------------------- Livebild

    public async Task<EntfeuchterLive> LiveAsync(CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);

        string? Text(string id) => nachId.TryGetValue(id, out var s) ? s.State : null;
        double? Zahl(string id) => Zahlenlesen.Maschine(Text(id));
        bool? An(string id) => Text(id) is { } t && t is not ("unknown" or "unavailable") ? t is "on" or "On" : null;

        var geraete = _geraete.EntitiesFuerModul(Modul);
        string? Rolle(string rolle) => geraete.TryGetValue(rolle, out var id) ? id : null;
        double? ZahlRolle(string rolle) => Rolle(rolle) is { } id ? Zahl(id) : null;
        bool? AnRolle(string rolle) => Rolle(rolle) is { } id ? An(id) : null;

        var automatiken = AutomatikKennungen(entities);
        var e = Gespeichert ?? AusHomeAssistant(
            nachId.ToDictionary(x => x.Key, x => (string?)x.Value.State, StringComparer.OrdinalIgnoreCase), automatiken);
        var plan = _wochenplan.PlanLuft();

        var rhMax = Zahl(Entitaeten.RhObergrenze);
        var deckel = rhMax is { } r ? r - (Zahl(Entitaeten.KlimaHysterese) ?? 3) : (double?)null;

        // Der Port-Schalter bei AC Infinity ist ein Auswahlpunkt (On/Off/Auto …):
        // „läuft" liest die Seite am Zustand, nicht am Modus.
        var portAn = AnRolle(Rollen.PortZustand);

        // Die lange Wartezeit gilt nur, wenn die Zuluft läuft UND die Außenluft
        // trocknet — dieselbe Bedingung wie in der HA-Automation.
        bool? vorrang = An(Entitaeten.ZuluftBedarf) is { } bedarf
            ? bedarf && (ZuluftLaeuft(nachId) ?? false)
            : null;

        // Welche Plan-Obergrenze gerade gilt: nachts die eigene des Plans, sonst die des Tages.
        var rhNacht = _wochenplan.PlanFeuchte()?.RhNacht ?? rhMax;
        var tagPhase = AnRolle(Rollen.LichtZustand);
        var grenze = tagPhase == false ? rhNacht : rhMax;
        var feuchte = ZahlRolle(Rollen.ZeltFeuchte);

        // Dauer nur dann aus dem Verlauf holen, wenn die Feuchte jetzt über der Grenze liegt — sonst gibt es nichts zu melden.
        int? ueberSeit = null, laeuftSeit = null;
        if (feuchte is { } fj && grenze is { } gj && fj > gj && Rolle(Rollen.ZeltFeuchte) is { } feuchteId)
        {
            var jetzt = DateTime.UtcNow;
            var von = jetzt - EntfeuchterBremsen.VerlaufFenster;
            ueberSeit = EntfeuchterBremsen.UeberGrenzeSeitMin(await _ha.GetVerlaufAsync(settings, feuchteId, von, jetzt, ct), gj, jetzt);
            if (Rolle(Rollen.PortZustand) is { } portId)
            {
                laeuftSeit = EntfeuchterBremsen.LaeuftSeitMin(await _ha.GetVerlaufAsync(settings, portId, von, jetzt, ct), jetzt);
            }
        }

        // Die Drosselung der CO₂-Steuerung wirkt nur bei Licht und wenn der Abluft-Port wirklich unter seiner normalen Stufe steht.
        var co2Rollen = _geraete.EntitiesFuerModul(Co2SteuerungService.Modul);
        var abluftStufe = co2Rollen.TryGetValue("abluft_stufe", out var abluftId) && abluftId is not null ? Zahl(abluftId) : null;
        var drosselungWirkt = EntfeuchterBremsen.DrosselungWirkt(
            An(Entitaeten.Co2Drosseln), tagPhase, abluftStufe, Zahl(Co2SteuerungService.Entitaeten.T6Normal));

        var bremsen = EntfeuchterBremsen.Pruefen(new BremsEingabe(
            FeuchteProzent: feuchte,
            GrenzeProzent: grenze,
            EinProzent: Zahl(Entitaeten.EinAktiv),
            TempC: ZahlRolle(Rollen.ZeltTemp),
            TempMaxC: Zahl(Entitaeten.TempMaxAktiv),
            TagPhase: tagPhase,
            TagbetriebErlaubt: e.TagbetriebErlauben,
            AutomatikAn: automatiken.Select(An).OfType<bool>().ToList() is { Count: > 0 } aktiv ? aktiv.Any(x => x) : null,
            PortAn: portAn,
            ZuluftVorrang: vorrang,
            Co2Drosselung: drosselungWirkt,
            EinschaltverzoegerungMin: e.EinschaltverzoegerungMin,
            WartezeitAussenluftMin: e.WartezeitAussenluftMin,
            UeberGrenzeSeitMin: ueberSeit,
            LaeuftSeitMin: laeuftSeit));

        return new EntfeuchterLive(
            HaErreichbar: entities.Count > 0,
            FeuchteProzent: ZahlRolle(Rollen.ZeltFeuchte),
            TempC: ZahlRolle(Rollen.ZeltTemp),
            Vpd: ZahlRolle(Rollen.ZeltVpd),
            TagPhase: AnRolle(Rollen.LichtZustand),
            EinAktivProzent: Zahl(Entitaeten.EinAktiv),
            AusAktivProzent: Zahl(Entitaeten.AusAktiv),
            TempMaxAktivC: Zahl(Entitaeten.TempMaxAktiv),
            RhObergrenzeProzent: rhMax,
            DeckelProzent: deckel,
            VpdUnten: Zahl(Entitaeten.VpdUnten),
            VpdOben: Zahl(Entitaeten.VpdOben),
            BlattOffsetC: Zahl(Entitaeten.BlattOffset),
            PlanWoche: plan?.Woche,
            PlanLuftTagC: plan?.LuftTagC,
            PlanLuftNachtC: plan?.LuftNachtC,
            TempMaxTagC: TempMaxFuer(Entitaeten.TempMaxTag, e.TempMaxTagModus, e.TempMaxTagAbstandK, e.TempMaxTagFestC, plan?.LuftTagC),
            TempMaxNachtC: TempMaxFuer(Entitaeten.TempMaxNacht, e.TempMaxNachtModus, e.TempMaxNachtAbstandK, e.TempMaxNachtFestC, plan?.LuftNachtC),
            Co2CanopyGrenzeC: Zahl(Entitaeten.CanopyObergrenze),
            PortAn: portAn,
            PortOnline: AnRolle(Rollen.PortStatus),
            AutomatikAn: automatiken.Select(An).OfType<bool>().ToList() is { Count: > 0 } laeuft ? laeuft.Any(x => x) : null,
            ZuluftVorrang: vorrang,
            RhObergrenzeNachtProzent: rhNacht,
            GrenzeAktivProzent: grenze,
            Bremsen: bremsen,
            UeberGrenzeSeitMin: ueberSeit,
            LaeuftSeitMin: laeuftSeit);
    }

    /// <summary>Läuft der Zuluft-Lüfter? Gelesen über die Rolle der Zuluft-Steuerung.</summary>
    private bool? ZuluftLaeuft(IReadOnlyDictionary<string, HomeAssistantEntity> nachId)
    {
        var zuluft = _geraete.EntitiesFuerModul(ZuluftSteuerungService.Modul);
        if (!zuluft.TryGetValue(ZuluftSteuerungService.Rollen.PortZustand, out var id) || id is null) return null;
        return nachId.TryGetValue(id, out var s) && s.State is { } t && t is not ("unknown" or "unavailable")
            ? t is "on" or "On" : null;
    }
}
