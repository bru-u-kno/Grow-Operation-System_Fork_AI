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
public sealed class EntfeuchterSteuerungService
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
        public const string VpdRegelung = "input_boolean.trotec_vpd_regelung";
        public const string Hysterese = "input_number.trotec_hysterese";
        public const string Mindestlaufzeit = "input_number.trotec_mindestlaufzeit";
        public const string Einschaltverzoegerung = "input_number.trotec_einschaltverzoegerung";
        public const string WartezeitAussenluft = "input_number.trotec_wartezeit_aussenluft";
        public const string Tagbetrieb = "input_boolean.trotec_tagbetrieb_erlauben";
        public const string TempMaxTag = "input_number.trotec_temp_max_tag";
        public const string TempMaxNacht = "input_number.trotec_temp_max";
        public const string FeuchteEinTag = "input_number.trotec_feuchte_ein_tag";
        public const string FeuchteAusTag = "input_number.trotec_feuchte_aus_tag";
        public const string FeuchteEinNacht = "input_number.trotec_feuchte_ein";
        public const string FeuchteAusNacht = "input_number.trotec_feuchte_aus";
        public const string Automatik = "automation.rdwc_trotec_nachtregelung_port_7_dehumi";

        // Nur gelesen — gepflegt woanders.
        public const string EinAktiv = "sensor.trotec_feuchte_ein_aktiv";
        public const string AusAktiv = "sensor.trotec_feuchte_aus_aktiv";
        public const string TempMaxAktiv = "sensor.trotec_temp_max_aktiv";
        public const string VpdUnten = "input_number.vpd_ziel_unten";
        public const string VpdOben = "input_number.vpd_ziel_abschaltung";
        public const string BlattOffset = "input_number.vpd_blatt_offset";
        public const string RhObergrenze = "input_number.co2_rh_obergrenze";
        public const string KlimaHysterese = "input_number.co2_klima_hysterese";
        public const string CanopyObergrenze = "input_number.co2_canopy_obergrenze";
        public const string ZuluftBedarf = "binary_sensor.zuluft_bedarf";
    }

    private readonly SteuerungRepository _repo;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly SteuerungGeraeteService _geraete;
    private readonly WochenplanSyncService _wochenplan;
    private readonly ILogger<EntfeuchterSteuerungService> _logger;

    public EntfeuchterSteuerungService(
        SteuerungRepository repo,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        SteuerungGeraeteService geraete,
        WochenplanSyncService wochenplan,
        ILogger<EntfeuchterSteuerungService> logger)
    {
        _repo = repo;
        _ha = ha;
        _haSettings = haSettings;
        _geraete = geraete;
        _wochenplan = wochenplan;
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
        return AusHomeAssistant(entities.ToDictionary(
            x => x.EntityId, x => (string?)x.State, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Aus den Zuständen der Helfer Einstellungen bauen. Was fehlt oder sich
    /// nicht lesen lässt, bleibt auf der Vorgabe. Temperatur max. kommt dabei
    /// immer als „Fest" — ob der Wert einmal aus dem Plan stammte, weiß Home
    /// Assistant nicht.
    /// </summary>
    public static EntfeuchterEinstellungen AusHomeAssistant(IReadOnlyDictionary<string, string?> zustaende)
    {
        var e = new EntfeuchterEinstellungen();

        double? Zahl(string id) => zustaende.TryGetValue(id, out var s)
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
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
        if (An(Entitaeten.Automatik) is { } au) e.AutomatikAktiv = au;
        return e;
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

        PruefeSchwellen(f, nameof(e.FeuchteEinTag), e.FeuchteEinTag, nameof(e.FeuchteAusTag), e.FeuchteAusTag);
        PruefeSchwellen(f, nameof(e.FeuchteEinNacht), e.FeuchteEinNacht, nameof(e.FeuchteAusNacht), e.FeuchteAusNacht);
        return f;
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

    /// <summary>Prüft, speichert und schreibt nach Home Assistant.</summary>
    public async Task<(EntfeuchterEinstellungen? Gespeichert, Dictionary<string, string> Fehler, bool HaErreicht)> SpeichernAsync(
        EntfeuchterEinstellungen e, CancellationToken ct)
    {
        var fehler = Pruefen(e);
        if (fehler.Count > 0) return (null, fehler, false);

        _repo.SetEinstellungen(Modul, e);
        var erreicht = await NachHomeAssistantSchreibenAsync(e, ct);
        return (e, fehler, erreicht);
    }

    public async Task<bool> NachHomeAssistantSchreibenAsync(EntfeuchterEinstellungen e, CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var plan = _wochenplan.PlanLuft();
        var zahlen = new (string Entity, double Wert)[]
        {
            (Entitaeten.Hysterese, e.HystereseProzent),
            (Entitaeten.Mindestlaufzeit, e.MindestlaufzeitMin),
            (Entitaeten.Einschaltverzoegerung, e.EinschaltverzoegerungMin),
            (Entitaeten.WartezeitAussenluft, e.WartezeitAussenluftMin),
            (Entitaeten.TempMaxTag, TempMax(e.TempMaxTagModus, e.TempMaxTagAbstandK, e.TempMaxTagFestC, plan?.LuftTagC)),
            (Entitaeten.TempMaxNacht, TempMax(e.TempMaxNachtModus, e.TempMaxNachtAbstandK, e.TempMaxNachtFestC, plan?.LuftNachtC)),
            (Entitaeten.FeuchteEinTag, e.FeuchteEinTag),
            (Entitaeten.FeuchteAusTag, e.FeuchteAusTag),
            (Entitaeten.FeuchteEinNacht, e.FeuchteEinNacht),
            (Entitaeten.FeuchteAusNacht, e.FeuchteAusNacht),
        };

        var alles = true;
        foreach (var (entity, wert) in zahlen)
        {
            alles &= await _ha.CallEntityServiceAsync(settings, "input_number", "set_value", entity, ct,
                new Dictionary<string, object> { ["value"] = wert });
        }

        alles &= await _ha.CallEntityServiceAsync(settings, "input_boolean", e.VpdRegelung ? "turn_on" : "turn_off", Entitaeten.VpdRegelung, ct);
        alles &= await _ha.CallEntityServiceAsync(settings, "input_boolean", e.TagbetriebErlauben ? "turn_on" : "turn_off", Entitaeten.Tagbetrieb, ct);
        alles &= await _ha.CallEntityServiceAsync(settings, "automation", e.AutomatikAktiv ? "turn_on" : "turn_off", Entitaeten.Automatik, ct);

        if (!alles) _logger.LogWarning("Entfeuchter-Sollwerte: nicht alle Helfer in Home Assistant angenommen.");
        return alles;
    }

    /// <summary>
    /// Zieht Temperatur max. nach, wenn ein Modus auf „Plan" steht und sich die
    /// Plan-Woche geändert hat. Schreibt nur, was abweicht. Aufgerufen vom
    /// Wochenplan-Worker (Wochenwechsel, täglich 06:00).
    /// </summary>
    public async Task<int> PlanNachziehenAsync(CancellationToken ct)
    {
        if (Gespeichert is not { } e) return 0;
        if (e.TempMaxTagModus != TempMaxModus.Plan && e.TempMaxNachtModus != TempMaxModus.Plan) return 0;
        if (_wochenplan.PlanLuft() is not { } plan) return 0;

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return 0;

        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x.State, StringComparer.OrdinalIgnoreCase);
        double? Ist(string id) => double.TryParse(nachId.GetValueOrDefault(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

        var geschrieben = 0;
        foreach (var (modus, entity, soll) in new[]
                 {
                     (e.TempMaxTagModus, Entitaeten.TempMaxTag, TempMax(e.TempMaxTagModus, e.TempMaxTagAbstandK, e.TempMaxTagFestC, plan.LuftTagC)),
                     (e.TempMaxNachtModus, Entitaeten.TempMaxNacht, TempMax(e.TempMaxNachtModus, e.TempMaxNachtAbstandK, e.TempMaxNachtFestC, plan.LuftNachtC)),
                 })
        {
            if (modus != TempMaxModus.Plan) continue;
            if (Ist(entity) is { } ist && Math.Abs(ist - soll) < 0.05) continue;
            if (await _ha.CallEntityServiceAsync(settings, "input_number", "set_value", entity, ct,
                    new Dictionary<string, object> { ["value"] = soll }))
            {
                geschrieben++;
            }
        }

        if (geschrieben > 0) _logger.LogInformation("Entfeuchter: Temperatur max. aus dem Plan nachgezogen ({Anzahl}).", geschrieben);
        return geschrieben;
    }

    // --------------------------------------------------------------- Livebild

    public async Task<EntfeuchterLive> LiveAsync(CancellationToken ct)
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
            TempMaxTagC: TempMax(e.TempMaxTagModus, e.TempMaxTagAbstandK, e.TempMaxTagFestC, plan?.LuftTagC),
            TempMaxNachtC: TempMax(e.TempMaxNachtModus, e.TempMaxNachtAbstandK, e.TempMaxNachtFestC, plan?.LuftNachtC),
            Co2CanopyGrenzeC: Zahl(Entitaeten.CanopyObergrenze),
            PortAn: portAn,
            PortOnline: AnRolle(Rollen.PortStatus),
            AutomatikAn: An(Entitaeten.Automatik),
            ZuluftVorrang: vorrang);
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
