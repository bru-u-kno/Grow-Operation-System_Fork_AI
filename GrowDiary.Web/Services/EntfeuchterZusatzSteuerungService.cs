using System.Globalization;
using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (A-009, 06.10.2026): Der Zusatz-Entfeuchter — Einstellungen, Livebild
/// und die Anzeigenamen der beiden Entfeuchter.
/// </summary>
/// <remarks>
/// <para><b>Geregelt wird in Home Assistant</b> (Vorlage
/// <c>Vorlagen/entfeuchter-zusatz/regelung.json</c>, bei Bru die handgebaute
/// <c>automation.rdwc_trotec_zelt_shelly_plan_regelung</c>). Der Fork schreibt
/// nur die Helfer und schaltet die Automation.</para>
///
/// <para><b>Speichern schreibt nur, was geändert wurde.</b> Das Gegenstück zum
/// 06.10.2026, als ein Speichern unbemerkt die Tag-Grenze von 26,5 auf 29 °C
/// zurücksetzte: <see cref="EntfeuchterZusatzAenderung"/> trägt nur die Felder,
/// die jemand angefasst hat. Im Fork wird nur das geändert, und nach Home
/// Assistant gehen nur die genannten Felder — die aber immer, auch bei gleichem
/// Fork-Wert (Home Assistant kann abweichen).</para>
///
/// <para><b>Die Höchsttemperatur gehört dem Entfeuchter.</b> Ein Speichern mit
/// <c>tempMax*</c>-Feldern lädt den gespeicherten Stand der
/// <see cref="EntfeuchterEinstellungen"/>, führt NUR die genannten Felder nach und schreibt nach
/// Home Assistant NUR die genannten Helfer (<c>set_value</c>). Der Speicherweg des Entfeuchters
/// (<see cref="EntfeuchterSteuerungService.SpeichernAsync"/>) schreibt alle seine Helfer aus dem
/// Fork-Stand und schaltet VPD-Regelung, Tagbetrieb und die Port-7-Automation — er wird hier nie aufgerufen.</para>
///
/// <para><b>Nichts erfinden.</b> Was Home Assistant nicht liefert, ist im
/// Livebild <c>null</c> — nie 0, nie „aus".</para>
/// </remarks>
public sealed class EntfeuchterZusatzSteuerungService
{
    public const string Modul = SteuerungGeraeteRollen.ZusatzModul;

    /// <summary>Modul unter dem die Anzeigenamen in <c>ForkSteuerungEinstellungen</c> stehen.</summary>
    public const string NamenModul = "entfeuchter-namen";

    /// <summary>Modul unter dem die Einrichtung (wie viele Entfeuchter) in <c>ForkSteuerungEinstellungen</c> steht.</summary>
    public const string EinrichtungModul = "entfeuchtung";

    /// <summary>Die Rollen dieses Moduls — Zelt-Fühler und Licht kommen aus <c>entfeuchter</c>.</summary>
    public static class Rollen
    {
        public const string ZusatzSchalter = "zusatz_schalter";
        public const string ZusatzLeistung = "zusatz_leistung";
        public const string ZusatzEnergie = "zusatz_energie";
        public const string FuehrungZustand = "fuehrung_zustand";
    }

    /// <summary>Die Objekte der Steuerung selbst — sie gehören keinem Gerät (Katalog: <see cref="SteuerungBauteile"/>).</summary>
    public static class Entitaeten
    {
        public const string VpdHysterese = "input_number.trotec_zelt_vpd_hysterese";
        public const string Mindestlaufzeit = "input_number.trotec_zelt_mindestlaufzeit";
        public const string Mindestpause = "input_number.trotec_zelt_mindestpause";
        public const string FolgeAbstand = "input_number.trotec_zelt_folge_abstand";
        public const string WiederEinAbstand = "input_number.trotec_zelt_wieder_ein_abstand";
        public const string ZuschaltVerzoegerung = "input_number.trotec_zelt_zuschalt_verzogerung";
        public const string Tagbetrieb = "input_boolean.trotec_zelt_tagbetrieb_erlauben";
        public const string NachtDurchlaufen = "input_boolean.trotec_zelt_nacht_durchlaufen";
        public const string Melden = "input_boolean.trotec_zelt_melden";
        public const string MeldeGrenze = "input_number.trotec_zelt_melde_grenze_w";
        public const string MeldeDauer = "input_number.trotec_zelt_melde_dauer_min";
        public const string MeldeWiederholung = "input_number.trotec_zelt_melde_wiederholung_h";
    }

    private readonly SteuerungRepository _repo;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly SteuerungGeraeteService _geraete;
    private readonly WochenplanSyncService _wochenplan;
    private readonly EntfeuchterSteuerungService _entfeuchter;
    private readonly ILogger<EntfeuchterZusatzSteuerungService> _logger;

    public EntfeuchterZusatzSteuerungService(
        SteuerungRepository repo,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        SteuerungGeraeteService geraete,
        WochenplanSyncService wochenplan,
        EntfeuchterSteuerungService entfeuchter,
        ILogger<EntfeuchterZusatzSteuerungService> logger)
    {
        _repo = repo;
        _ha = ha;
        _haSettings = haSettings;
        _geraete = geraete;
        _wochenplan = wochenplan;
        _entfeuchter = entfeuchter;
        _logger = logger;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ----------------------------------------------------------- Einstellungen

    /// <summary>Der gespeicherte Stand — null, solange nie gespeichert wurde.</summary>
    public EntfeuchterZusatzEinstellungen? Gespeichert => _repo.GetEinstellungen<EntfeuchterZusatzEinstellungen>(Modul);

    /// <summary>
    /// Die Einstellungen, wie sie gelten: der eigene Stand (beim ersten Aufruf aus
    /// den Helfern in Home Assistant übernommen) und darüber die gemeinsame
    /// Höchsttemperatur des Entfeuchters.
    /// </summary>
    public async Task<EntfeuchterZusatzEinstellungen> EinstellungenAsync(CancellationToken ct)
    {
        var e = Gespeichert ?? await UebernehmenAsync(ct);
        MitHoechsttemperatur(e, await _entfeuchter.EinstellungenAsync(ct));
        // Die Hilfsstärke ist abgeleitet: was die Einzelwerte ergeben (oder „aus").
        e.Hilfe = EntfeuchterZusatzHilfe.Erkennen(e);
        return e;
    }

    private async Task<EntfeuchterZusatzEinstellungen> UebernehmenAsync(CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return new EntfeuchterZusatzEinstellungen();

        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var uebernommen = AusHomeAssistant(
            entities.ToDictionary(x => x.EntityId, x => (string?)x.State, StringComparer.OrdinalIgnoreCase),
            AutomatikAn(entities));
        // Wie beim Entfeuchter: nur mit echter Antwort von Home Assistant, sonst
        // stünden die Werkseinstellungen als „übernommen" da. Gespeichert wird nur
        // im Fork, nach Home Assistant wird nichts geschrieben.
        if (entities.Count > 0)
        {
            // Die Kopie der Höchsttemperatur im eigenen Dokument ist nie die Quelle (gelesen wird
            // immer beim Entfeuchter) — aber sie soll beim Anlegen nicht gleich falsch dastehen.
            MitHoechsttemperatur(uebernommen, await _entfeuchter.EinstellungenAsync(ct));
            _repo.SetEinstellungen(Modul, uebernommen);
        }
        return uebernommen;
    }

    /// <summary>
    /// Aus den Zuständen der Helfer Einstellungen bauen. Was fehlt oder sich nicht
    /// lesen lässt, bleibt auf der Vorgabe.
    /// </summary>
    /// <param name="zustaende">Entity-ID auf Zustand.</param>
    /// <param name="automatikAn">Ob die Regel-Automation läuft — null, wenn es sie nicht gibt.</param>
    public static EntfeuchterZusatzEinstellungen AusHomeAssistant(
        IReadOnlyDictionary<string, string?> zustaende, bool? automatikAn = null)
    {
        var e = new EntfeuchterZusatzEinstellungen();

        double? Zahl(string id) => zustaende.TryGetValue(id, out var s) ? Zahlenlesen.Maschine(s) : null;
        bool? An(string id) => zustaende.TryGetValue(id, out var s) ? AnAus(s) : null;

        if (Zahl(Entitaeten.VpdHysterese) is { } h) e.VpdHystereseKpa = h;
        if (Zahl(Entitaeten.Mindestlaufzeit) is { } lz) e.MindestlaufzeitMin = (int)Math.Round(lz);
        if (Zahl(Entitaeten.Mindestpause) is { } mp) e.MindestpauseMin = (int)Math.Round(mp);
        if (Zahl(Entitaeten.FolgeAbstand) is { } fa) e.FolgeAbstandK = fa;
        if (Zahl(Entitaeten.WiederEinAbstand) is { } wa) e.WiederEinAbstandK = wa;
        if (Zahl(Entitaeten.ZuschaltVerzoegerung) is { } zv) e.ZuschaltVerzoegerungMin = (int)Math.Round(zv);
        if (An(Entitaeten.Tagbetrieb) is { } tb) e.TagbetriebErlauben = tb;
        if (An(Entitaeten.NachtDurchlaufen) is { } nd) e.NachtDurchlaufen = nd;
        if (An(Entitaeten.Melden) is { } me) e.Meldung.Aktiv = me;
        if (Zahl(Entitaeten.MeldeGrenze) is { } mg) e.Meldung.GrenzeW = (int)Math.Round(mg);
        if (Zahl(Entitaeten.MeldeDauer) is { } md) e.Meldung.DauerMin = (int)Math.Round(md);
        if (Zahl(Entitaeten.MeldeWiederholung) is { } mw) e.Meldung.WiederholungH = (int)Math.Round(mw);
        if (automatikAn is { } au) e.AutomatikAktiv = au;

        // Die Hilfsstärke wird nicht gelesen, sondern erkannt: passen die Werte zu
        // einer Voreinstellung, ist es diese. „aus" gibt es in Home Assistant nicht.
        e.Hilfe = EntfeuchterZusatzHilfe.Alle.FirstOrDefault(v => EntfeuchterZusatzHilfe.Passt(v, e))?.Name
                  ?? EntfeuchterZusatzHilfe.Eigene;
        return e;
    }

    private static void MitHoechsttemperatur(EntfeuchterZusatzEinstellungen e, EntfeuchterEinstellungen quelle)
    {
        e.TempMaxTagModus = quelle.TempMaxTagModus;
        e.TempMaxTagAbstandK = quelle.TempMaxTagAbstandK;
        e.TempMaxTagFestC = quelle.TempMaxTagFestC;
        e.TempMaxNachtModus = quelle.TempMaxNachtModus;
        e.TempMaxNachtAbstandK = quelle.TempMaxNachtAbstandK;
        e.TempMaxNachtFestC = quelle.TempMaxNachtFestC;
    }

    // --------------------------------------------------------------- Prüfen

    /// <summary>
    /// Die Feldfehler einer Änderung — nur für Felder, die vorkommen. Die gemeinsame
    /// Höchsttemperatur prüft der Dienst gegen den Entfeuchter
    /// (<see cref="EntfeuchterSteuerungService.Pruefen"/>), nicht hier.
    /// </summary>
    /// <returns>Feld (C#-Name, bei der Meldung <c>Meldung.GrenzeW</c>) auf Text; leer, wenn alles passt.</returns>
    public static Dictionary<string, string> Pruefen(EntfeuchterZusatzAenderung a)
    {
        var f = new Dictionary<string, string>();

        // Die Bereiche sind die der Helfer im Katalog — dieselbe Quelle, aus der der Fork sie anlegt und
        // auf die er beim Schreiben begrenzt. Was Home Assistant ablehnen würde, nimmt der Fork nicht an.
        void Bereich(string feld, double? wert, string helfer, string name, string einheit)
        {
            if (wert is not { } w) return;
            var (min, max) = SteuerungBauteile.Spanne(helfer);
            if (!double.IsFinite(w) || w < min || w > max)
            {
                var de = CultureInfo.GetCultureInfo("de-DE");
                f[feld] = $"{name}: {min.ToString("0.##", de)} bis {max.ToString("0.##", de)} {einheit}.";
            }
        }

        if (a.Hilfe is not null && !EntfeuchterZusatzHilfe.Zulaessig.Contains(a.Hilfe))
        {
            f[nameof(a.Hilfe)] = "Hilfsstärke: aus, sparsam, normal oder kräftig.";
        }
        if (a.Ablauf is not null && a.Ablauf is not (EntfeuchterZusatzAblauf.Tank or EntfeuchterZusatzAblauf.Schlauch))
        {
            f[nameof(a.Ablauf)] = "Ablauf: Tank oder Ablaufschlauch.";
        }

        Bereich(nameof(a.FolgeAbstandK), a.FolgeAbstandK, Entitaeten.FolgeAbstand, "Folge-Abstand", "K");
        Bereich(nameof(a.WiederEinAbstandK), a.WiederEinAbstandK, Entitaeten.WiederEinAbstand, "Wieder-ein-Abstand", "K");
        Bereich(nameof(a.ZuschaltVerzoegerungMin), a.ZuschaltVerzoegerungMin, Entitaeten.ZuschaltVerzoegerung, "Zuschalt-Verzögerung", "Minuten");
        Bereich(nameof(a.MindestlaufzeitMin), a.MindestlaufzeitMin, Entitaeten.Mindestlaufzeit, "Mindestlaufzeit", "Minuten");
        Bereich(nameof(a.MindestpauseMin), a.MindestpauseMin, Entitaeten.Mindestpause, "Mindestpause", "Minuten");
        Bereich(nameof(a.VpdHystereseKpa), a.VpdHystereseKpa, Entitaeten.VpdHysterese, "VPD-Abstand", "kPa");

        if (a.Meldung is { } m)
        {
            Bereich("Meldung." + nameof(m.GrenzeW), m.GrenzeW, Entitaeten.MeldeGrenze, "Leistungsgrenze", "W");
            Bereich("Meldung." + nameof(m.DauerMin), m.DauerMin, Entitaeten.MeldeDauer, "Dauer", "Minuten");
            Bereich("Meldung." + nameof(m.WiederholungH), m.WiederholungH, Entitaeten.MeldeWiederholung, "Wiederholung", "Stunden");
        }

        return f;
    }

    /// <summary>Alle Felder einer Einstellung als Änderung — damit eine vollständige Einstellung dieselben Regeln durchläuft.</summary>
    public static EntfeuchterZusatzAenderung AlsAenderung(EntfeuchterZusatzEinstellungen e) => new()
    {
        Hilfe = e.Hilfe,
        AutomatikAktiv = e.AutomatikAktiv,
        TagbetriebErlauben = e.TagbetriebErlauben,
        NachtDurchlaufen = e.NachtDurchlaufen,
        VpdHystereseKpa = e.VpdHystereseKpa,
        ZuschaltVerzoegerungMin = e.ZuschaltVerzoegerungMin,
        FolgeAbstandK = e.FolgeAbstandK,
        WiederEinAbstandK = e.WiederEinAbstandK,
        MindestlaufzeitMin = e.MindestlaufzeitMin,
        MindestpauseMin = e.MindestpauseMin,
        Meldung = new EntfeuchterZusatzMeldungAenderung
        {
            Aktiv = e.Meldung.Aktiv, GrenzeW = e.Meldung.GrenzeW, DauerMin = e.Meldung.DauerMin, WiederholungH = e.Meldung.WiederholungH,
        },
        Ablauf = e.Ablauf,
        TempMaxTagModus = e.TempMaxTagModus,
        TempMaxTagAbstandK = e.TempMaxTagAbstandK,
        TempMaxTagFestC = e.TempMaxTagFestC,
        TempMaxNachtModus = e.TempMaxNachtModus,
        TempMaxNachtAbstandK = e.TempMaxNachtAbstandK,
        TempMaxNachtFestC = e.TempMaxNachtFestC,
    };

    // --------------------------------------------------------------- Ändern

    /// <summary>
    /// Die Änderung auf einen Stand legen — <b>nur die genannten Felder</b>. Gibt eine
    /// Kopie zurück; <paramref name="aktuell"/> bleibt unberührt. Die
    /// <c>TempMax*</c>-Felder fasst das nicht an (siehe <see cref="TempMaxEinarbeiten"/>).
    /// </summary>
    public static EntfeuchterZusatzEinstellungen Anwenden(EntfeuchterZusatzEinstellungen aktuell, EntfeuchterZusatzAenderung a)
    {
        var e = Kopie(aktuell);

        // Erst die Voreinstellung, dann die Einzelwerte darüber: nennt ein Aufruf
        // beides, gewinnt der Einzelwert.
        if (a.Hilfe is { } hilfe)
        {
            if (EntfeuchterZusatzHilfe.Finden(hilfe) is { } v)
            {
                e.FolgeAbstandK = v.FolgeAbstandK;
                e.WiederEinAbstandK = v.WiederEinAbstandK;
                e.VpdHystereseKpa = v.VpdHystereseKpa;
                e.ZuschaltVerzoegerungMin = v.ZuschaltVerzoegerungMin;
                e.MindestpauseMin = v.MindestpauseMin;
            }
            e.Hilfe = hilfe;
        }

        if (a.AutomatikAktiv is { } au) e.AutomatikAktiv = au;
        if (a.TagbetriebErlauben is { } tb) e.TagbetriebErlauben = tb;
        if (a.NachtDurchlaufen is { } nd) e.NachtDurchlaufen = nd;
        if (a.VpdHystereseKpa is { } h) e.VpdHystereseKpa = h;
        if (a.ZuschaltVerzoegerungMin is { } zv) e.ZuschaltVerzoegerungMin = zv;
        if (a.FolgeAbstandK is { } fa) e.FolgeAbstandK = fa;
        if (a.WiederEinAbstandK is { } wa) e.WiederEinAbstandK = wa;
        if (a.MindestlaufzeitMin is { } lz) e.MindestlaufzeitMin = lz;
        if (a.MindestpauseMin is { } mp) e.MindestpauseMin = mp;
        if (a.Ablauf is { } ab) e.Ablauf = ab;
        if (a.Meldung is { } m)
        {
            if (m.Aktiv is { } ma) e.Meldung.Aktiv = ma;
            if (m.GrenzeW is { } mg) e.Meldung.GrenzeW = mg;
            if (m.DauerMin is { } md) e.Meldung.DauerMin = md;
            if (m.WiederholungH is { } mw) e.Meldung.WiederholungH = mw;
        }

        return e;
    }

    /// <summary>
    /// Die gemeinsame Höchsttemperatur in den gespeicherten Entfeuchter-Stand
    /// einarbeiten — <b>nur die genannten <c>TempMax*</c>-Felder</b>, alle anderen
    /// Felder der <see cref="EntfeuchterEinstellungen"/> bleiben, wie sie sind.
    /// </summary>
    public static EntfeuchterEinstellungen TempMaxEinarbeiten(EntfeuchterEinstellungen gespeichert, EntfeuchterZusatzAenderung a)
    {
        var e = JsonSerializer.Deserialize<EntfeuchterEinstellungen>(JsonSerializer.Serialize(gespeichert, Json), Json)!;
        if (a.TempMaxTagModus is { } tm) e.TempMaxTagModus = tm;
        if (a.TempMaxTagAbstandK is { } ta) e.TempMaxTagAbstandK = ta;
        if (a.TempMaxTagFestC is { } tf) e.TempMaxTagFestC = tf;
        if (a.TempMaxNachtModus is { } nm) e.TempMaxNachtModus = nm;
        if (a.TempMaxNachtAbstandK is { } na) e.TempMaxNachtAbstandK = na;
        if (a.TempMaxNachtFestC is { } nf) e.TempMaxNachtFestC = nf;
        return e;
    }

    private static EntfeuchterZusatzEinstellungen Kopie(EntfeuchterZusatzEinstellungen e)
        => JsonSerializer.Deserialize<EntfeuchterZusatzEinstellungen>(JsonSerializer.Serialize(e, Json), Json)!;

    /// <summary>Was ein Speichern ergeben hat.</summary>
    /// <param name="Gespeichert">Der neue Stand — null bei Feldfehlern (dann wurde nichts geschrieben).</param>
    /// <param name="Fehler">Die Feldfehler.</param>
    /// <param name="HaErreicht">
    /// Aus den echten Schreibergebnissen: <c>true</c>, wenn Home Assistant jeden Aufruf angenommen hat,
    /// <c>false</c>, wenn einer scheiterte oder Home Assistant nicht eingerichtet ist — und <c>null</c>,
    /// wenn gar nichts nach Home Assistant zu schreiben war.
    /// </param>
    /// <param name="Hinweise">Was nicht ausgeführt werden konnte, ohne dass ein Feld falsch wäre.</param>
    public sealed record SpeicherErgebnis(
        EntfeuchterZusatzEinstellungen? Gespeichert,
        Dictionary<string, string> Fehler,
        bool? HaErreicht,
        IReadOnlyList<string> Hinweise)
    {
        public void Deconstruct(out EntfeuchterZusatzEinstellungen? gespeichert, out Dictionary<string, string> fehler, out bool? haErreicht)
            => (gespeichert, fehler, haErreicht) = (Gespeichert, Fehler, HaErreicht);
    }

    /// <summary>
    /// Prüft, speichert und schreibt nach Home Assistant — <b>nur die genannten Felder</b>, und die
    /// genannten <b>immer</b> (auch wenn der Fork-Stand schon denselben Wert trägt: Home Assistant
    /// kann abweichen).
    /// </summary>
    public async Task<SpeicherErgebnis> SpeichernAsync(EntfeuchterZusatzAenderung a, CancellationToken ct)
    {
        var fehler = Pruefen(a);
        // Schon an den eigenen Feldern gescheitert und keine gemeinsame Höchsttemperatur
        // dabei: nichts lesen, nichts anfassen.
        if (fehler.Count > 0 && !a.BetrifftTempMax) return new(null, fehler, null, []);

        var aktuell = await EinstellungenAsync(ct);
        var neu = Anwenden(aktuell, a);

        EntfeuchterEinstellungen? hoechstTemp = null;
        if (a.BetrifftTempMax)
        {
            hoechstTemp = TempMaxEinarbeiten(await _entfeuchter.EinstellungenAsync(ct), a);
            // Nur die TempMax-Felder: der Rest des Entfeuchters geht uns nichts an.
            foreach (var (feld, text) in EntfeuchterSteuerungService.Pruefen(hoechstTemp)
                         .Where(p => p.Key.StartsWith("TempMax", StringComparison.Ordinal)))
            {
                fehler[feld] = text;
            }
        }

        if (fehler.Count > 0) return new(null, fehler, null, []);

        // Die gemeinsame Höchsttemperatur: im Fork-Stand des Entfeuchters NUR diese Felder nachführen
        // (hoechstTemp ist der gespeicherte Stand mit genau den genannten Feldern geändert) — und nach
        // Home Assistant NUR die genannten Helfer. Der Speicherweg des Entfeuchters (alle Helfer, VPD-
        // Regelung, Tagbetrieb, Automation) ist hier ausdrücklich NICHT der richtige.
        var tempMaxSchreiben = new List<(string Domaene, string Dienst, string Entitaet, double? Wert)>();
        if (hoechstTemp is not null)
        {
            _repo.SetEinstellungen(EntfeuchterSteuerungService.Modul, hoechstTemp);
            MitHoechsttemperatur(neu, hoechstTemp);
            tempMaxSchreiben.AddRange(TempMaxSchreibliste(hoechstTemp, a, _wochenplan.PlanLuft()));
        }

        neu.Hilfe = EntfeuchterZusatzHilfe.Erkennen(neu);
        _repo.SetEinstellungen(Modul, neu);
        var (erreicht, hinweise) = await NachHomeAssistantSchreibenAsync(aktuell, neu, a, tempMaxSchreiben, ct);
        return new(neu, fehler, erreicht, hinweise);
    }

    /// <summary>
    /// Die Helfer der gemeinsamen Höchsttemperatur, die ein Speichern nach Home Assistant schreibt —
    /// <b>nur die, deren Felder im Body stehen</b>: Tag, wenn ein Tag-Feld vorkommt, Nacht entsprechend.
    /// Der Wert ist der gültige Sollwert (Plan-Luft + Abstand oder fest), auf die Spanne des Helfers begrenzt.
    /// </summary>
    public static IReadOnlyList<(string Domaene, string Dienst, string Entitaet, double? Wert)> TempMaxSchreibliste(
        EntfeuchterEinstellungen e, EntfeuchterZusatzAenderung a, (string Woche, double LuftTagC, double LuftNachtC)? plan)
    {
        var liste = new List<(string, string, string, double?)>();
        if (a.TempMaxTagModus is not null || a.TempMaxTagAbstandK is not null || a.TempMaxTagFestC is not null)
        {
            liste.Add(("input_number", "set_value", EntfeuchterSteuerungService.Entitaeten.TempMaxTag,
                EntfeuchterSteuerungService.TempMaxFuer(EntfeuchterSteuerungService.Entitaeten.TempMaxTag,
                    e.TempMaxTagModus, e.TempMaxTagAbstandK, e.TempMaxTagFestC, plan?.LuftTagC)));
        }
        if (a.TempMaxNachtModus is not null || a.TempMaxNachtAbstandK is not null || a.TempMaxNachtFestC is not null)
        {
            liste.Add(("input_number", "set_value", EntfeuchterSteuerungService.Entitaeten.TempMaxNacht,
                EntfeuchterSteuerungService.TempMaxFuer(EntfeuchterSteuerungService.Entitaeten.TempMaxNacht,
                    e.TempMaxNachtModus, e.TempMaxNachtAbstandK, e.TempMaxNachtFestC, plan?.LuftNachtC)));
        }
        return liste;
    }

    /// <summary>
    /// Was beim Wechsel von <paramref name="alt"/> auf <paramref name="neu"/> in Home Assistant
    /// geschrieben werden muss: was sich geändert hat — und, wenn die Änderung <paramref name="a"/>
    /// bekannt ist, <b>zusätzlich jedes Feld, das im Body stand</b> (auch bei gleichem Wert). Eine
    /// Voreinstellung zählt als Nennung ihrer fünf Werte. Eigene Methode, damit die Tests an der Stelle
    /// ansetzen können, an der die Entscheidung fällt.
    /// </summary>
    public static IReadOnlyList<(string Domaene, string Dienst, string Entitaet, double? Wert)> Schreibliste(
        EntfeuchterZusatzEinstellungen alt, EntfeuchterZusatzEinstellungen neu, EntfeuchterZusatzAenderung? a = null)
    {
        var liste = new List<(string, string, string, double?)>();
        var staerke = a?.Hilfe is { } h && EntfeuchterZusatzHilfe.Finden(h) is not null;

        void Zahl(string entity, double vorher, double nachher, bool genannt)
        {
            if (!genannt && Math.Abs(vorher - nachher) < 1e-9) return;
            liste.Add(("input_number", "set_value", entity, SteuerungBauteile.AufSpanne(entity, nachher)));
        }

        void Schalter(string entity, bool vorher, bool nachher, bool genannt)
        {
            if (!genannt && vorher == nachher) return;
            liste.Add(("input_boolean", nachher ? "turn_on" : "turn_off", entity, null));
        }

        Zahl(Entitaeten.VpdHysterese, alt.VpdHystereseKpa, neu.VpdHystereseKpa, staerke || a?.VpdHystereseKpa is not null);
        Zahl(Entitaeten.Mindestlaufzeit, alt.MindestlaufzeitMin, neu.MindestlaufzeitMin, a?.MindestlaufzeitMin is not null);
        Zahl(Entitaeten.Mindestpause, alt.MindestpauseMin, neu.MindestpauseMin, staerke || a?.MindestpauseMin is not null);
        Zahl(Entitaeten.FolgeAbstand, alt.FolgeAbstandK, neu.FolgeAbstandK, staerke || a?.FolgeAbstandK is not null);
        Zahl(Entitaeten.WiederEinAbstand, alt.WiederEinAbstandK, neu.WiederEinAbstandK, staerke || a?.WiederEinAbstandK is not null);
        Zahl(Entitaeten.ZuschaltVerzoegerung, alt.ZuschaltVerzoegerungMin, neu.ZuschaltVerzoegerungMin, staerke || a?.ZuschaltVerzoegerungMin is not null);
        Schalter(Entitaeten.Tagbetrieb, alt.TagbetriebErlauben, neu.TagbetriebErlauben, a?.TagbetriebErlauben is not null);
        Schalter(Entitaeten.NachtDurchlaufen, alt.NachtDurchlaufen, neu.NachtDurchlaufen, a?.NachtDurchlaufen is not null);
        Schalter(Entitaeten.Melden, alt.Meldung.Aktiv, neu.Meldung.Aktiv, a?.Meldung?.Aktiv is not null);
        Zahl(Entitaeten.MeldeGrenze, alt.Meldung.GrenzeW, neu.Meldung.GrenzeW, a?.Meldung?.GrenzeW is not null);
        Zahl(Entitaeten.MeldeDauer, alt.Meldung.DauerMin, neu.Meldung.DauerMin, a?.Meldung?.DauerMin is not null);
        Zahl(Entitaeten.MeldeWiederholung, alt.Meldung.WiederholungH, neu.Meldung.WiederholungH, a?.Meldung?.WiederholungH is not null);
        return liste;
    }

    /// <summary>Läuft die Regel-Automation, wenn dieser Stand gilt? „aus" hält sie an.</summary>
    public static bool AutomatikWirksam(EntfeuchterZusatzEinstellungen e)
        => e.AutomatikAktiv && e.Hilfe != EntfeuchterZusatzHilfe.Aus;

    /// <summary>Nur diese Domänen schaltet der Fork aus, wenn die Hilfsstärke „aus" ist — sie kennen <c>turn_off</c> ohne Nebenwirkung.</summary>
    public static bool IstSchaltbar(string? entityId)
        => SteuerungGeraeteService.Domain(entityId) is "switch" or "input_boolean";

    private async Task<(bool? Erreicht, IReadOnlyList<string> Hinweise)> NachHomeAssistantSchreibenAsync(
        EntfeuchterZusatzEinstellungen alt, EntfeuchterZusatzEinstellungen neu, EntfeuchterZusatzAenderung a,
        IReadOnlyList<(string Domaene, string Dienst, string Entitaet, double? Wert)> tempMax, CancellationToken ct)
    {
        var liste = Schreibliste(alt, neu, a).Concat(tempMax).ToList();
        var automatikGenannt = a.AutomatikAktiv is not null || a.Hilfe is not null;
        var automatikSchreiben = automatikGenannt || AutomatikWirksam(alt) != AutomatikWirksam(neu);
        var ausSchalten = a.Hilfe == EntfeuchterZusatzHilfe.Aus;
        var hinweise = new List<string>();
        if (liste.Count == 0 && !automatikSchreiben && !ausSchalten) return (null, hinweise);

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured)
        {
            if (ausSchalten) hinweise.Add("Home Assistant ist nicht eingerichtet — der Zusatz-Entfeuchter wurde nicht ausgeschaltet.");
            return (false, hinweise);
        }

        var alles = true;
        foreach (var (domaene, dienst, entitaet, wert) in liste)
        {
            alles &= await _ha.CallEntityServiceAsync(settings, domaene, dienst, entitaet, ct,
                wert is { } w ? new Dictionary<string, object> { ["value"] = w } : null);
        }

        if (automatikSchreiben)
        {
            var kennungen = AutomatikKennungen(await _ha.GetEntitiesAsync(settings, ct));
            foreach (var automation in kennungen)
            {
                alles &= await _ha.CallEntityServiceAsync(settings, "automation",
                    AutomatikWirksam(neu) ? "turn_on" : "turn_off", automation, ct);
            }
        }

        // „aus" heißt: der Zusatz bleibt aus — nicht nur „die Regelung greift nicht ein". Geschaltet wird
        // nur ein switch oder input_boolean; alles andere (ein select, ein Klima-Gerät …) kennt kein
        // einfaches Aus, und ein falsch gedeuteter Befehl wäre schlimmer als keiner.
        if (ausSchalten)
        {
            var schalter = _geraete.Entity(Modul, Rollen.ZusatzSchalter);
            if (schalter is null)
            {
                hinweise.Add("Dem Zusatz-Entfeuchter ist kein Schalter zugeordnet — er wurde nicht ausgeschaltet.");
                alles = false;
            }
            else if (!IstSchaltbar(schalter))
            {
                hinweise.Add($"„{schalter}“ ist kein switch und kein input_boolean — der Zusatz-Entfeuchter wurde nicht ausgeschaltet.");
                alles = false;
            }
            else
            {
                alles &= await _ha.CallEntityServiceAsync(settings, SteuerungGeraeteService.Domain(schalter)!, "turn_off", schalter, ct);
            }
        }

        if (!alles) _logger.LogWarning("Zusatz-Entfeuchter: nicht alles in Home Assistant angenommen.");
        return (alles, hinweise);
    }

    // ---------------------------------------------------------- Automation

    /// <summary>
    /// Unter welchen Entity-IDs die Regel-Automation in Home Assistant steht —
    /// handgebaut (<c>automation.rdwc_trotec_zelt_shelly_plan_regelung</c>) oder vom
    /// Fork angelegt (Entity-ID aus dem Alias der Vorlage). Ohne Entitätenliste
    /// (Home Assistant antwortet nicht) die Katalog-Kennung; leer, wenn es sie nicht gibt.
    /// </summary>
    public static IReadOnlyList<string> AutomatikKennungen(IReadOnlyCollection<HomeAssistantEntity> alle)
    {
        var regelung = SteuerungBauteile.FuerModul(Modul).Single(b => b.VorlagenDatei == "regelung");
        return alle.Count == 0 ? [regelung.EntityId] : SteuerungBauteile.AutomationFinden(regelung, alle);
    }

    private static bool? AutomatikAn(IReadOnlyCollection<HomeAssistantEntity> alle)
    {
        var zustaende = AutomatikKennungen(alle)
            .Select(id => alle.FirstOrDefault(x => string.Equals(x.EntityId, id, StringComparison.OrdinalIgnoreCase))?.State)
            .Select(AnAus)
            .Where(z => z is not null)
            .ToList();
        return zustaende.Count == 0 ? null : zustaende.Any(z => z == true);
    }

    // --------------------------------------------------------------- Livebild

    /// <param name="mitEnergie">Auch „Energie heute" aus dem Verlauf holen — ein zusätzlicher Aufruf; die Übersicht lässt es weg.</param>
    public async Task<EntfeuchterZusatzLive> LiveAsync(CancellationToken ct, bool mitEnergie = true)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);

        string? Text(string? id) => id is not null && nachId.TryGetValue(id, out var s) ? s.State : null;
        double? Zahl(string? id) => Zahlenlesen.Maschine(Text(id));

        var geraete = _geraete.EntitiesFuerModul(Modul);
        string? Rolle(string rolle) => geraete.TryGetValue(rolle, out var id) ? id : null;

        var e = await EinstellungenAsync(ct);

        double? energieHeute = null;
        if (mitEnergie && Rolle(Rollen.ZusatzEnergie) is { } energie && entities.Count > 0)
        {
            var mitternacht = DateTime.Now.Date.ToUniversalTime();
            energieHeute = EnergieHeute(await _ha.GetVerlaufAsync(settings, energie, mitternacht, DateTime.UtcNow, ct));
        }

        var namen = await NamenAsync(entities, ct);

        return Berechnen(e, new ZusatzEingang(
            HaErreichbar: entities.Count > 0,
            FuehrungName: namen.Fuehrung.Anzeigename,
            ZusatzName: namen.Zusatz.Anzeigename,
            Plan: _wochenplan.PlanLuft(),
            TempC: Zahl(Rolle("zelt_temp")),
            FeuchteProzent: Zahl(Rolle("zelt_rh")),
            Vpd: Zahl(Rolle("zelt_vpd")),
            TagPhase: AnAus(Text(Rolle("licht_zustand"))),
            VpdUnten: Zahl(EntfeuchterSteuerungService.Entitaeten.VpdUnten),
            VpdOben: Zahl(EntfeuchterSteuerungService.Entitaeten.VpdOben),
            FeuchteEin: Zahl(EntfeuchterSteuerungService.Entitaeten.EinAktiv),
            FeuchteAus: Zahl(EntfeuchterSteuerungService.Entitaeten.AusAktiv),
            ZusatzZustand: Text(Rolle(Rollen.ZusatzSchalter)),
            LeistungW: Zahl(Rolle(Rollen.ZusatzLeistung)),
            EnergieHeuteKwh: energieHeute,
            FuehrungZustand: Text(Rolle(Rollen.FuehrungZustand)),
            AutomatikAn: entities.Count > 0 ? AutomatikAn(entities) : null,
            ZusatzSeitUtc: Rolle(Rollen.ZusatzSchalter) is { } schalterId && nachId.TryGetValue(schalterId, out var schalterZustand)
                ? schalterZustand.LastChangedUtc : null,
            RhObergrenzeProzent: Zahl(EntfeuchterSteuerungService.Entitaeten.RhObergrenze)));
    }

    /// <summary>Was das Livebild aus Home Assistant und dem Plan braucht — gelesen, nicht gerechnet.</summary>
    public sealed record ZusatzEingang(
        bool HaErreichbar,
        string FuehrungName,
        string ZusatzName,
        (string Woche, double LuftTagC, double LuftNachtC)? Plan,
        double? TempC,
        double? FeuchteProzent,
        double? Vpd,
        bool? TagPhase,
        double? VpdUnten,
        double? VpdOben,
        double? FeuchteEin,
        double? FeuchteAus,
        string? ZusatzZustand,
        double? LeistungW,
        double? EnergieHeuteKwh,
        string? FuehrungZustand,
        bool? AutomatikAn,
        DateTime? ZusatzSeitUtc = null,
        DateTime? JetztUtc = null,
        double? RhObergrenzeProzent = null);

    /// <summary>
    /// Das Livebild aus den gelesenen Werten und den Einstellungen. Rein rechnend —
    /// <b>fehlt ein Wert, fehlt er auch im Ergebnis</b>.
    /// </summary>
    public static EntfeuchterZusatzLive Berechnen(EntfeuchterZusatzEinstellungen e, ZusatzEingang x)
    {
        // Höchsttemperatur: dieselbe Rechnung wie beim Entfeuchter (Plan + Abstand oder fest).
        var tagMax = EntfeuchterSteuerungService.TempMaxFuer(EntfeuchterSteuerungService.Entitaeten.TempMaxTag,
            e.TempMaxTagModus, e.TempMaxTagAbstandK, e.TempMaxTagFestC, x.Plan?.LuftTagC);
        var nachtMax = EntfeuchterSteuerungService.TempMaxFuer(EntfeuchterSteuerungService.Entitaeten.TempMaxNacht,
            e.TempMaxNachtModus, e.TempMaxNachtAbstandK, e.TempMaxNachtFestC, x.Plan?.LuftNachtC);
        var folgeTag = Math.Round(tagMax - e.FolgeAbstandK, 1);
        var folgeNacht = Math.Round(nachtMax - e.FolgeAbstandK, 1);

        // Schaltgröße: das VPD-Ziel des Plans, sonst dessen Luftfeuchte, sonst nichts.
        var hatVpd = x.VpdUnten is not null && x.VpdOben is not null;
        var hatFeuchte = x.FeuchteEin is not null && x.FeuchteAus is not null;
        var groesse = hatVpd ? EntfeuchterZusatzSchaltgroesse.Vpd
            : hatFeuchte ? EntfeuchterZusatzSchaltgroesse.Feuchte
            : EntfeuchterZusatzSchaltgroesse.Keine;

        var zusatzAn = AnAus(x.ZusatzZustand);
        // „Zieht nichts": der Zusatz läuft seit mindestens der eingestellten Dauer (die Anlaufphase eines
        // Kompressors zählt nicht), die Leistung liegt unter der Grenze, und die Meldung ist eingeschaltet.
        // Fehlt etwas davon — Zustand, Zeitpunkt, Messwert —, ist es false: es wird nichts geraten.
        var jetzt = x.JetztUtc ?? DateTime.UtcNow;
        var ziehtNichts = e.Meldung.Aktiv
                          && zusatzAn == true
                          && x.ZusatzSeitUtc is { } seit && jetzt - seit >= TimeSpan.FromMinutes(e.Meldung.DauerMin)
                          && x.LeistungW is { } w && w < e.Meldung.GrenzeW;

        return new EntfeuchterZusatzLive(
            HaErreichbar: x.HaErreichbar,
            FuehrungName: x.FuehrungName,
            ZusatzName: x.ZusatzName,
            PlanWoche: x.Plan?.Woche,
            PlanLuftTagC: x.Plan?.LuftTagC,
            PlanLuftNachtC: x.Plan?.LuftNachtC,
            TempMaxTagC: tagMax,
            TempMaxNachtC: nachtMax,
            FolgeAusTagC: folgeTag,
            FolgeAusNachtC: folgeNacht,
            WiederEinTagC: Math.Round(folgeTag - e.WiederEinAbstandK, 1),
            WiederEinNachtC: Math.Round(folgeNacht - e.WiederEinAbstandK, 1),
            TempC: x.TempC,
            FeuchteProzent: x.FeuchteProzent,
            Vpd: x.Vpd,
            TagPhase: x.TagPhase,
            Schaltgroesse: groesse,
            VpdZiel: hatVpd ? Math.Round((x.VpdUnten!.Value + x.VpdOben!.Value) / 2, 2) : null,
            VpdEinSchwelle: hatVpd ? Math.Round(x.VpdUnten!.Value - e.VpdHystereseKpa, 2) : null,
            VpdAusSchwelle: hatVpd ? Math.Round(x.VpdOben!.Value + e.VpdHystereseKpa, 2) : null,
            FeuchteEinProzent: x.FeuchteEin,
            FeuchteAusProzent: x.FeuchteAus,
            ZusatzAn: zusatzAn,
            ZusatzOnline: Online(x.ZusatzZustand),
            LeistungW: x.LeistungW,
            EnergieHeuteKwh: x.EnergieHeuteKwh,
            FuehrungAn: AnAus(x.FuehrungZustand),
            ZiehtNichts: ziehtNichts,
            // Nur mit Home Assistant am Hörer: ohne Verbindung wissen wir nichts über den Plan.
            PlanUnvollstaendig: x.HaErreichbar && groesse == EntfeuchterZusatzSchaltgroesse.Keine,
            AutomatikAn: x.AutomatikAn,
            RhObergrenzeProzent: x.RhObergrenzeProzent);
    }

    /// <summary>
    /// <c>true</c> bei „on", <c>false</c> bei jedem anderen echten Zustand, <c>null</c>
    /// ohne Zustand (fehlt, unbekannt, nicht verfügbar). Ein select meldet „On"/„Off".
    /// </summary>
    public static bool? AnAus(string? zustand)
        => zustand is null or "" or "unknown" or "unavailable" ? null
            : string.Equals(zustand, "on", StringComparison.OrdinalIgnoreCase);

    /// <summary>Erreichbar? Null, wenn es keinen Zustand gibt oder er „unknown" ist.</summary>
    private static bool? Online(string? zustand)
        => zustand is null or "" or "unknown" ? null : zustand != "unavailable";

    /// <summary>
    /// Der Verbrauch seit Mitternacht aus dem Verlauf eines Zählers (<c>total_increasing</c>):
    /// die Summe der Anstiege; fällt der Zähler zurück, beginnt er bei 0. Null ohne Verlauf.
    /// </summary>
    public static double? EnergieHeute(IReadOnlyList<HaVerlaufsPunkt>? punkte)
    {
        if (punkte is null) return null;
        var werte = punkte.Select(p => Zahlenlesen.Maschine(p.Zustand)).Where(v => v is not null).Select(v => v!.Value).ToList();
        if (werte.Count == 0) return null;

        var summe = 0.0;
        for (var i = 1; i < werte.Count; i++)
        {
            summe += werte[i] >= werte[i - 1] ? werte[i] - werte[i - 1] : werte[i];
        }
        return Math.Round(summe, 2);
    }

    // ------------------------------------------------------------------ Namen

    /// <summary>Die Anzeigenamen beider Entfeuchter-Geräte — gespeichert, sonst der Name aus Home Assistant.</summary>
    public async Task<EntfeuchterNamenDto> NamenAsync(CancellationToken ct)
        => await NamenAsync(await _ha.GetEntitiesAsync(_haSettings.GetEffectiveHomeAssistantSettings(), ct), ct);

    private Task<EntfeuchterNamenDto> NamenAsync(IReadOnlyList<HomeAssistantEntity> entities, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);
        string? Anzeige(string? id) => id is not null && nachId.TryGetValue(id, out var s) ? s.FriendlyName : null;

        var fuehrungEntitaet = _geraete.Entity(EntfeuchterSteuerungService.Modul, EntfeuchterSteuerungService.Rollen.PortSchalter);
        var zusatzEntitaet = _geraete.Entity(Modul, Rollen.ZusatzSchalter);
        var gespeichert = _repo.GetEinstellungen<EntfeuchterNamen>(NamenModul);

        return Task.FromResult(NamenBilden(gespeichert, Anzeige(fuehrungEntitaet), Anzeige(zusatzEntitaet)));
    }

    /// <summary>Die Namen aus dem gespeicherten Stand und den Namen der Home-Assistant-Entitäten. Rein rechnend.</summary>
    public static EntfeuchterNamenDto NamenBilden(EntfeuchterNamen? gespeichert, string? fuehrungHa, string? zusatzHa)
    {
        var fuehrung = GeraeteName(fuehrungHa) ?? "Entfeuchter";
        var zusatz = GeraeteName(zusatzHa) ?? "Zusatz-Entfeuchter";
        return new EntfeuchterNamenDto(
            new EntfeuchterNameDto(Nichtleer(gespeichert?.Fuehrung) ?? fuehrung, fuehrung),
            new EntfeuchterNameDto(Nichtleer(gespeichert?.Zusatz) ?? zusatz, zusatz));
    }

    private static string? Nichtleer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static readonly string[] Endungen = [" Aktiver Modus", " Zustand", " Status", " Schalter", " Modus"];

    /// <summary>
    /// Aus dem Namen einer Entität den des Geräts: „RDWC Dehumi Aktiver Modus" →
    /// „RDWC Dehumi". Home Assistant hängt den Entitätsnamen an den Gerätenamen.
    /// </summary>
    public static string? GeraeteName(string? entitaetsName)
    {
        var name = Nichtleer(entitaetsName);
        if (name is null) return null;
        foreach (var endung in Endungen)
        {
            if (name.EndsWith(endung, StringComparison.OrdinalIgnoreCase) && name.Length > endung.Length)
            {
                return name[..^endung.Length].Trim();
            }
        }
        return name;
    }

    /// <summary>Wie viele Entfeuchter es gibt — was der Nutzer gesagt hat (A-015); <c>null</c> = nach der Zuordnung entscheiden.</summary>
    public EntfeuchtungEinrichtungDto Einrichtung()
        => new(_repo.GetEinstellungen<EntfeuchtungEinrichtung>(EinrichtungModul)?.ZusatzVorhanden);

    /// <summary>Speichert, ob es einen Zusatz-Entfeuchter gibt. Die Regelung in Home Assistant bleibt unberührt.</summary>
    public EntfeuchtungEinrichtungDto EinrichtungSpeichern(bool? zusatzVorhanden)
    {
        var stand = _repo.GetEinstellungen<EntfeuchtungEinrichtung>(EinrichtungModul) ?? new EntfeuchtungEinrichtung();
        stand.ZusatzVorhanden = zusatzVorhanden;
        _repo.SetEinstellungen(EinrichtungModul, stand);
        return Einrichtung();
    }

    /// <summary>Prüft und speichert die Anzeigenamen — nur die genannten. Leer oder null setzt auf die Vorgabe.</summary>
    public Dictionary<string, string> NamenSpeichern(EntfeuchterNamenAenderung a)
    {
        var fehler = new Dictionary<string, string>();
        string? Pruefen(string feld, string? wert)
        {
            var text = Nichtleer(wert);
            if (text is null) return null;
            if (text.Length > 60) fehler[feld] = "Name: höchstens 60 Zeichen.";
            else if (text.Any(char.IsControl)) fehler[feld] = "Name: keine Steuerzeichen.";
            return text;
        }

        var fuehrung = a.FuehrungGesetzt ? Pruefen(nameof(a.Fuehrung), a.Fuehrung) : null;
        var zusatz = a.ZusatzGesetzt ? Pruefen(nameof(a.Zusatz), a.Zusatz) : null;
        if (fehler.Count > 0) return fehler;

        var stand = _repo.GetEinstellungen<EntfeuchterNamen>(NamenModul) ?? new EntfeuchterNamen();
        if (a.FuehrungGesetzt) stand.Fuehrung = fuehrung;
        if (a.ZusatzGesetzt) stand.Zusatz = zusatz;
        _repo.SetEinstellungen(NamenModul, stand);
        return fehler;
    }
}
