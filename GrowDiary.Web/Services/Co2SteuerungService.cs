using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Services.Knowledge;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.20): Der Leitstand der CO₂-Begasung.
/// </summary>
/// <remarks>
/// <para><b>Arbeitsteilung.</b> Die Regelung läuft in Home Assistant
/// (<c>automation.co2_dosierung_rdwc_port_5</c> samt Wächter und Licht-aus-
/// Sicherung). Dieser Dienst besitzt die Sollwerte, schreibt sie in die
/// HA-Helfer, liest das Livebild zurück und schließt jeden Abend den Tag ab —
/// Journal und Kosten-Artikel. Fällt das Add-on aus, dosiert HA mit den
/// zuletzt geschriebenen Werten weiter.</para>
///
/// <para><b>Eine Wahrheit je Zahl.</b> Das Planziel kommt aus
/// <see cref="Zielband"/> — derselben Stelle, aus der Kachel und Alarm ihr
/// CO₂-Band nehmen. Hier wird es nur je Canopy-Bereich skaliert.</para>
/// </remarks>
public sealed class Co2SteuerungService
{
    public const string Modul = "co2";

    /// <summary>
    /// Die HA-Helfer, die zum Konzept der Regelung gehören: Sollwerte, abgeleitete
    /// Sensoren, Zähler, die Automation. Die legt die Steuerung selbst an, sie sind
    /// kein Gerät des Nutzers und bleiben deshalb fest.
    /// </summary>
    /// <remarks>
    /// Die <b>Geräte</b> — Fühler, Steckdose, Abluft, Licht — stehen NICHT hier,
    /// sondern als Rolle in <see cref="Models.SteuerungGeraeteRollen"/> und werden
    /// über <see cref="SteuerungGeraeteService"/> aufgelöst (forkai.21).
    /// </remarks>
    public static class Entitaeten
    {
        public const string ZielWarm = "input_number.co2_zielwert";
        public const string ZielMittel = "input_number.co2_ziel_mittel_25_bis_27_c";
        public const string ZielKuehl = "input_number.co2_ziel_kuehl_unter_25_c";
        public const string Hysterese = "input_number.co2_hysterese";
        public const string ImpulsMin = "input_number.co2_impulsdauer";
        public const string ImpulsMax = "input_number.co2_impulsdauer_max";
        public const string Wartezeit = "input_number.co2_wartezeit";
        public const string MaxImpulse = "input_number.co2_max_impulse_je_zyklus";
        public const string Autokalibrierung = "input_boolean.co2_autokalibrierung";
        public const string Zeltvolumen = "input_number.co2_zeltvolumen";
        public const string RhObergrenze = "input_number.co2_rh_obergrenze";
        public const string KlimaHysterese = "input_number.co2_klima_hysterese";
        public const string CanopyObergrenze = "input_number.co2_canopy_obergrenze";
        public const string T6Normal = "input_number.co2_t6_stufe_normal";
        public const string T6Dosierung = "input_number.co2_t6_stufe_dosierung";
        public const string T6Tief = "input_number.co2_t6_stufe_tief";
        public const string T6TiefMaxTemp = "input_number.co2_t6_tief_max_temp";
        public const string AbluftDrosseln = "input_boolean.co2_abluft_drosseln";
        public const string StartNachLichtAn = "input_number.co2_start_nach_licht_an";
        public const string EndeVorLichtAus = "input_number.co2_ende_vor_licht_aus";
        public const string Automatik = "automation.co2_dosierung_rdwc_port_5";

        public const string ZielEffektiv = "sensor.co2_ziel_effektiv";
        public const string Bedarf = "binary_sensor.co2_bedarf";
        public const string KlimaOk = "binary_sensor.co2_klima_ok";
        public const string PortModus = "select.rdwc_venti_aktiver_modus_2";
        public const string Impulse = "counter.co2_impulse_heute";
        public const string GrammProSekunde = "input_number.co2_gramm_pro_sekunde";
        public const string LetzteMessung = "input_number.co2_g_s_letzte_messung";
        public const string FlascheRest = "input_number.co2_flasche_rest";
        public const string ImpulsBedarf = "sensor.co2_impuls_bedarf";
        public const string LetzterImpuls = "input_datetime.co2_letzter_impuls";
    }

    private readonly SteuerungRepository _repo;
    private readonly KostenRepository _kosten;
    private readonly JournalRepository _journal;
    private readonly GrowRepository _grows;
    private readonly HydroSetupRepository _hydro;
    private readonly TargetValueService _targets;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly SteuerungGeraeteService _geraete;
    private readonly ILogger<Co2SteuerungService> _logger;

    public Co2SteuerungService(
        SteuerungRepository repo,
        KostenRepository kosten,
        JournalRepository journal,
        GrowRepository grows,
        HydroSetupRepository hydro,
        TargetValueService targets,
        KnowledgeBaseLoader wissen,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        SteuerungGeraeteService geraete,
        ILogger<Co2SteuerungService> logger)
    {
        _repo = repo;
        _kosten = kosten;
        _journal = journal;
        _grows = grows;
        _hydro = hydro;
        _targets = targets;
        _wissen = wissen;
        _ha = ha;
        _haSettings = haSettings;
        _geraete = geraete;
        _logger = logger;
    }

    // ----------------------------------------------------------- Einstellungen

    public Co2Einstellungen Einstellungen => _repo.GetEinstellungen<Co2Einstellungen>(Modul) ?? new Co2Einstellungen();

    /// <summary>Prüft, speichert und schreibt nach Home Assistant. Liefert die Feldfehler, wenn etwas nicht stimmt.</summary>
    public async Task<(Co2Einstellungen? Gespeichert, Dictionary<string, string> Fehler, bool HaErreicht)> SpeichernAsync(Co2Einstellungen e, CancellationToken ct)
    {
        var fehler = Pruefen(e);
        if (fehler.Count > 0) return (null, fehler, false);

        _repo.SetEinstellungen(Modul, e);
        var erreicht = await NachHomeAssistantSchreibenAsync(e, ct);
        return (e, fehler, erreicht);
    }

    public static Dictionary<string, string> Pruefen(Co2Einstellungen e)
    {
        var f = new Dictionary<string, string>();
        if (e.ZielQuelle is not ("fest" or "plan")) f[nameof(e.ZielQuelle)] = "Ziel-Quelle muss „fest“ oder „plan“ sein.";
        foreach (var (name, wert) in new[] { (nameof(e.ZielWarmPpm), e.ZielWarmPpm), (nameof(e.ZielMittelPpm), e.ZielMittelPpm), (nameof(e.ZielKuehlPpm), e.ZielKuehlPpm) })
        {
            if (wert is < 400 or > 2000) f[name] = "Ziel muss zwischen 400 und 2000 ppm liegen.";
        }
        foreach (var (name, wert) in new[] { (nameof(e.AnteilWarmProzent), e.AnteilWarmProzent), (nameof(e.AnteilMittelProzent), e.AnteilMittelProzent), (nameof(e.AnteilKuehlProzent), e.AnteilKuehlProzent) })
        {
            if (wert is < 10 or > 100) f[name] = "Anteil muss zwischen 10 und 100 % liegen.";
        }
        if (e.HysteresePpm is < 20 or > 300) f[nameof(e.HysteresePpm)] = "Hysterese muss zwischen 20 und 300 ppm liegen.";
        if (e.ImpulsMinSekunden is < 1 or > 60) f[nameof(e.ImpulsMinSekunden)] = "Impuls min muss zwischen 1 und 60 s liegen.";
        if (e.ImpulsMaxSekunden is < 5 or > 45) f[nameof(e.ImpulsMaxSekunden)] = "Impuls max muss zwischen 5 und 45 s liegen — der Wächter schließt ab 90 s.";
        if (e.ImpulsMinSekunden > e.ImpulsMaxSekunden) f[nameof(e.ImpulsMinSekunden)] = "Impuls min darf nicht über Impuls max liegen.";
        if (e.WartezeitSekunden is < 10 or > 600) f[nameof(e.WartezeitSekunden)] = "Wartezeit muss zwischen 10 und 600 s liegen.";
        if (e.MaxImpulseJeZyklus is < 1 or > 50) f[nameof(e.MaxImpulseJeZyklus)] = "Höchstens 50 Impulse je Zyklus.";
        if (e.ZeltvolumenM3 is < 1 or > 20) f[nameof(e.ZeltvolumenM3)] = "Zeltvolumen muss zwischen 1 und 20 m³ liegen.";
        if (e.RhObergrenzeProzent is < 40 or > 90) f[nameof(e.RhObergrenzeProzent)] = "Feuchte-Obergrenze muss zwischen 40 und 90 % liegen.";
        if (e.KlimaHystereseProzent is < 0 or > 10) f[nameof(e.KlimaHystereseProzent)] = "Klima-Hysterese muss zwischen 0 und 10 % liegen.";
        if (e.CanopyObergrenzeC is < 22 or > 34) f[nameof(e.CanopyObergrenzeC)] = "Canopy-Obergrenze muss zwischen 22 und 34 °C liegen.";
        foreach (var (name, wert) in new[] { (nameof(e.T6StufeNormal), e.T6StufeNormal), (nameof(e.T6StufeDosierung), e.T6StufeDosierung), (nameof(e.T6StufeTief), e.T6StufeTief) })
        {
            if (wert is < 1 or > 10) f[name] = "Stufe muss zwischen 1 und 10 liegen.";
        }
        if (e.T6StufeTief > e.T6StufeDosierung || e.T6StufeDosierung > e.T6StufeNormal) f[nameof(e.T6StufeTief)] = "Stufen müssen tief ≤ Dosierung ≤ normal sein.";
        if (e.T6TiefMaxTempC is < 24 or > 32) f[nameof(e.T6TiefMaxTempC)] = "Stufe-tief-Grenze muss zwischen 24 und 32 °C liegen.";
        if (e.StartNachLichtAnMinuten is < 0 or > 120) f[nameof(e.StartNachLichtAnMinuten)] = "Start nach Licht an: 0 bis 120 Minuten.";
        if (e.EndeVorLichtAusMinuten is < 0 or > 240) f[nameof(e.EndeVorLichtAusMinuten)] = "Ende vor Licht aus: 0 bis 240 Minuten.";
        return f;
    }

    // ------------------------------------------------------------- Planziel

    /// <summary>Der laufende Grow (der älteste mit Status Running) und seine Phase heute.</summary>
    public (GrowRun? Grow, GrowStage? Stage) LaufenderGrow()
    {
        var grow = _grows.GetActiveGrows()
            .Where(g => g.Status == GrowStatus.Running)
            .OrderBy(g => g.StartDate)
            .FirstOrDefault();
        return grow is null ? (null, null) : (grow, GrowStageResolver.Resolve(grow, DateTime.Today));
    }

    /// <summary>
    /// Das CO₂-Planziel: die Untergrenze des Bands der laufenden <b>Phase</b> —
    /// null ohne Grow oder Band.
    /// </summary>
    /// <remarks>
    /// Fork AI (forkai.20): Das ist bewusst ein Phasen- und kein Wochenwert. Das
    /// Feedchart hat keine CO₂-Spalte je Woche; der Wert kommt aus dem
    /// Sollwertprofil und wechselt mit der Phase. Die Seite muss ihn auch so
    /// beschriften — <see cref="PlanHerkunft"/> liefert den Text dafür, damit
    /// niemand eine wöchentliche Änderung erwartet, die es nicht gibt.
    /// </remarks>
    public int? PlanZielPpm()
    {
        var (grow, stage) = LaufenderGrow();
        if (grow is null || stage is null) return null;
        var systemProfil = grow.SystemId is { } systemId ? _hydro.GetSystem(systemId)?.SetpointProfileId : null;
        var band = Zielband.FuerGrow(_targets, _wissen, grow, stage.Value, systemProfil, null);
        var (min, _) = Zielband.FuerMetrik("co2", band);
        return min is { } m ? (int)Math.Round(m) : null;
    }

    /// <summary>Woher das Planziel stammt, als Satz für die Seite — Phase, nicht Woche.</summary>
    public string? PlanHerkunft()
    {
        var (grow, stage) = LaufenderGrow();
        if (grow is null || stage is null) return null;
        return $"Sollwertprofil der Phase {GeltendeZieleApiController.StageLabel(stage.Value)} — gilt die ganze Phase, nicht je Woche";
    }

    /// <summary>Die drei wirksamen Ziele (warm / mittel / kühl) — bei Plan skaliert, sonst die festen Werte.</summary>
    public static (int Warm, int Mittel, int Kuehl) WirksameZiele(Co2Einstellungen e, int? planPpm)
    {
        if (e.ZielQuelle == "plan" && planPpm is { } p)
        {
            return (
                Runden(p * e.AnteilWarmProzent / 100.0),
                Runden(p * e.AnteilMittelProzent / 100.0),
                Runden(p * e.AnteilKuehlProzent / 100.0));
        }
        return (e.ZielWarmPpm, e.ZielMittelPpm, e.ZielKuehlPpm);

        static int Runden(double v) => (int)(Math.Round(v / 10.0) * 10);
    }

    // ------------------------------------------------------- nach HA schreiben

    /// <summary>Alle Sollwerte in die HA-Helfer schreiben. false, wenn HA nicht erreichbar oder ein Schreiben scheiterte.</summary>
    public async Task<bool> NachHomeAssistantSchreibenAsync(Co2Einstellungen e, CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var (warm, mittel, kuehl) = WirksameZiele(e, PlanZielPpm());
        var zahlen = new (string Entity, double Wert)[]
        {
            (Entitaeten.ZielWarm, warm), (Entitaeten.ZielMittel, mittel), (Entitaeten.ZielKuehl, kuehl),
            (Entitaeten.Hysterese, e.HysteresePpm),
            (Entitaeten.ImpulsMin, e.ImpulsMinSekunden), (Entitaeten.ImpulsMax, e.ImpulsMaxSekunden),
            (Entitaeten.Wartezeit, e.WartezeitSekunden), (Entitaeten.MaxImpulse, e.MaxImpulseJeZyklus),
            (Entitaeten.Zeltvolumen, e.ZeltvolumenM3),
            (Entitaeten.RhObergrenze, e.RhObergrenzeProzent), (Entitaeten.KlimaHysterese, e.KlimaHystereseProzent),
            (Entitaeten.CanopyObergrenze, e.CanopyObergrenzeC),
            (Entitaeten.T6Normal, e.T6StufeNormal), (Entitaeten.T6Dosierung, e.T6StufeDosierung), (Entitaeten.T6Tief, e.T6StufeTief),
            (Entitaeten.T6TiefMaxTemp, e.T6TiefMaxTempC),
            (Entitaeten.StartNachLichtAn, e.StartNachLichtAnMinuten),
            (Entitaeten.EndeVorLichtAus, e.EndeVorLichtAusMinuten),
        };

        var alles = true;
        foreach (var (entity, wert) in zahlen)
        {
            var ok = await _ha.CallEntityServiceAsync(settings, "input_number", "set_value", entity, ct,
                new Dictionary<string, object> { ["value"] = wert });
            alles &= ok;
        }
        alles &= await Schalter(Entitaeten.Autokalibrierung, e.Autokalibrierung);
        alles &= await Schalter(Entitaeten.AbluftDrosseln, e.AbluftDrosseln);
        alles &= await _ha.CallEntityServiceAsync(settings, "automation", e.AutomatikAktiv ? "turn_on" : "turn_off", Entitaeten.Automatik, ct);
        if (!alles) _logger.LogWarning("CO₂-Sollwerte: nicht alle Helfer in Home Assistant angenommen.");
        return alles;

        Task<bool> Schalter(string entity, bool an)
            => _ha.CallEntityServiceAsync(settings, "input_boolean", an ? "turn_on" : "turn_off", entity, ct);
    }

    // --------------------------------------------------------------- Livebild

    public async Task<Co2Live> LiveAsync(CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);
        var e = Einstellungen;
        var plan = PlanZielPpm();
        var (warm, mittel, kuehl) = WirksameZiele(e, plan);

        double? Zahl(string id) => nachId.TryGetValue(id, out var s) && double.TryParse(s.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        string? Text(string id) => nachId.TryGetValue(id, out var s) ? s.State : null;
        bool? An(string id) => Text(id) is { } t ? t is "on" or "On" : null;

        // Fork AI (forkai.21): Geräte kommen aus der Zuordnung, nicht aus dem Code.
        // Eine Rolle ohne Gerät liefert null — dann steht in der Kachel ein „–",
        // statt dass eine fremde Entität einspringt.
        var geraete = _geraete.EntitiesFuerModul(Modul);
        double? ZahlRolle(string rolle) => geraete.TryGetValue(rolle, out var id) && id is not null ? Zahl(id) : null;
        bool? AnRolle(string rolle) => geraete.TryGetValue(rolle, out var id) && id is not null ? An(id) : null;

        var co2 = ZahlRolle("co2_sensor");
        var ziel = Zahl(Entitaeten.ZielEffektiv);
        return new Co2Live(
            HaErreichbar: entities.Count > 0,
            Co2Ppm: co2,
            ZielPpm: ziel is { } z ? (int)z : null,
            ZielQuelle: e.ZielQuelle,
            PlanPpm: plan,
            PlanHerkunft: PlanHerkunft(),
            ZielWarm: warm, ZielMittel: mittel, ZielKuehl: kuehl,
            HysteresePpm: e.HysteresePpm,
            NachschubUnterPpm: ziel is { } z2 ? (int)z2 - e.HysteresePpm : null,
            Bedarf: An(Entitaeten.Bedarf),
            KlimaOk: An(Entitaeten.KlimaOk),
            VentilOffen: AnRolle("port_zustand"),
            AutomatikAn: An(Entitaeten.Automatik),
            LichtAn: AnRolle("licht"),
            T6Stufe: ZahlRolle("abluft_stufe") is { } t6 ? (int)t6 : null,
            CanopyC: ZahlRolle("canopy"),
            RhProzent: ZahlRolle("rh"),
            Vpd: ZahlRolle("vpd"),
            ImpulseHeute: Zahl(Entitaeten.Impulse) is { } n ? (int)n : null,
            GrammProSekunde: Zahl(Entitaeten.GrammProSekunde),
            LetzteMessungGps: Zahl(Entitaeten.LetzteMessung),
            FlascheRestKg: Zahl(Entitaeten.FlascheRest),
            ImpulsBedarfSekunden: Zahl(Entitaeten.ImpulsBedarf) is { } ib ? (int)ib : null,
            LetzterImpuls: Text(Entitaeten.LetzterImpuls));
    }

    // -------------------------------------------------------------- Tageslauf

    private static string HeuteKey() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Ein Takt: Tagesdatensatz anlegen, „Ziel erreicht" festhalten, nach Licht-aus abschließen.
    /// Liefert den Tag, den es bearbeitet hat (oder null, wenn HA nicht antwortet).
    /// </summary>
    public async Task<Co2Tag?> TaktAsync(CancellationToken ct)
    {
        var live = await LiveAsync(ct);
        if (!live.HaErreichbar) return null;

        var heute = HeuteKey();
        var tag = _repo.GetCo2Tag(heute);

        if (live.LichtAn == true)
        {
            if (tag is null)
            {
                var (grow, _) = LaufenderGrow();
                tag = new Co2Tag
                {
                    Datum = heute,
                    GrowId = grow?.Id,
                    FlascheStartKg = live.FlascheRestKg ?? 0,
                };
                tag.Id = _repo.CreateCo2Tag(tag);
            }

            if (!tag.Abgeschlossen)
            {
                if (tag.ZielErreichtUm is null && live.Co2Ppm is { } c && live.ZielPpm is { } z && c >= z)
                {
                    tag.ZielErreichtUm = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
                }
                Zwischenstand(tag, live);
                _repo.UpdateCo2Tag(tag);
            }
            return tag;
        }

        // Licht aus: den jüngsten offenen Tag abschließen — auch, wenn das
        // Add-on den Abend verschlafen hat und erst am nächsten Morgen wieder da ist.
        var offen = _repo.GetOffenerCo2Tag();
        if (offen is not null && !offen.Abgeschlossen)
        {
            if (offen.Datum == heute) Zwischenstand(offen, live);
            Abschliessen(offen);
            _repo.UpdateCo2Tag(offen);
            return offen;
        }
        return tag;
    }

    /// <summary>
    /// Den Tag fortschreiben. Zählt in Abschnitten, damit ein Flaschenwechsel
    /// mitten am Tag nicht den ganzen Tag kostet.
    /// </summary>
    /// <remarks>
    /// <para>Der Verbrauch kommt aus dem Fall von <c>co2_flasche_rest</c>. Wird
    /// die Flasche gewechselt, springt der Helfer wieder nach <b>oben</b> — die
    /// Differenz wäre negativ. Vorher wurde sie auf 0 gedeckelt: der
    /// Tagesverbrauch fiel auf null, Journal und Kostenbuchung fielen aus.</para>
    /// <para>Jetzt wird jeder Anstieg als Flaschenwechsel gelesen: das bis dahin
    /// Gezählte bleibt stehen (<see cref="Co2Tag.GrammVorher"/>), und ab dem
    /// neuen Stand wird weitergezählt. Nur der Bruchteil zwischen letzter
    /// Ablesung und Wechsel geht verloren — statt des ganzen Tages.</para>
    /// <para>Die Ventilzeit rechnet aus den Gramm zurück und ist damit keine
    /// zweite Messung, sondern dieselbe Zahl in anderer Einheit. Sie steht in
    /// der Anzeige, weil Minuten anschaulicher sind als Gramm — nicht als
    /// Beleg.</para>
    /// </remarks>
    public static void Zwischenstand(Co2Tag tag, Co2Live live)
        => Fortschreiben(tag, live.ImpulseHeute, live.FlascheRestKg, live.GrammProSekunde);

    /// <summary>Dieselbe Rechnung ohne Livebild — so ist sie ohne Home Assistant prüfbar.</summary>
    public static void Fortschreiben(Co2Tag tag, int? impulseHeute, double? flascheRestKg, double? grammProSekunde)
    {
        if (impulseHeute is { } n && n >= tag.Impulse) tag.Impulse = n;
        if (flascheRestKg is not { } rest) return;

        // Anstieg um mehr als 50 g = neue Flasche. Kleinere Sprünge sind
        // Rundung oder eine Korrektur von Hand und laufen als Nullverbrauch.
        if (rest > tag.FlascheStartKg + 0.05)
        {
            tag.GrammVorher = tag.Gramm;
            tag.FlascheStartKg = rest;
            tag.Flaschenwechsel = true;
        }

        tag.FlascheEndeKg = rest;
        var imAbschnitt = Math.Max(0, (tag.FlascheStartKg - rest) * 1000);
        tag.Gramm = tag.GrammVorher + imAbschnitt;
        if (grammProSekunde is > 0)
        {
            tag.VentilSekunden = tag.Gramm / grammProSekunde.Value;
        }
    }

    private void Abschliessen(Co2Tag tag)
    {
        var e = Einstellungen;
        tag.Abgeschlossen = true;
        tag.AbgeschlossenUtc = DateTime.UtcNow;

        var grow = tag.GrowId is { } gid ? _grows.GetGrow(gid) : null;

        if (e.JournalBuchen && grow is not null && tag.JournalEntryId is null && tag.Impulse > 0)
        {
            var eintrag = new JournalEntry
            {
                GrowId = grow.Id,
                Title = "CO₂-Begasung",
                Body = Tagestext(tag),
                EntryType = JournalEntryType.Action,
                Source = ValueOrigin.HomeAssistant,
                OccurredAtUtc = DateTime.UtcNow,
            };
            tag.JournalEntryId = _journal.Create(eintrag);
        }

        if (e.KostenArtikelId is { } artikelId && tag.VerbrauchId is null && tag.Gramm > 0)
        {
            var artikel = _kosten.GetArtikel(artikelId);
            if (artikel is not null)
            {
                var menge = InEinheit(tag.Gramm, artikel.Einheit);
                tag.VerbrauchId = _kosten.CreateVerbrauch(new Verbrauch
                {
                    ArtikelId = artikelId,
                    GrowId = tag.GrowId,
                    ZeitpunktUtc = DateTime.UtcNow,
                    Menge = menge,
                    Quelle = "co2-steuerung",
                    Notiz = $"{tag.Datum}: {tag.Impulse} Impulse, {Math.Round(tag.VentilSekunden / 60)} min Ventil",
                });
            }
        }
        _logger.LogInformation("CO₂-Tag {Datum} abgeschlossen: {Impulse} Impulse, {Gramm} g.", tag.Datum, tag.Impulse, Math.Round(tag.Gramm));
    }

    /// <summary>Gramm in die Einheit des Artikels — kg oder g; andere Einheiten bleiben Gramm.</summary>
    public static double InEinheit(double gramm, string einheit) => einheit switch
    {
        "kg" => Math.Round(gramm / 1000, 4),
        _ => Math.Round(gramm, 1),
    };

    public static string Tagestext(Co2Tag tag)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        var teile = new List<string>
        {
            $"{tag.Impulse} Impulse",
            $"Ventil {Math.Round(tag.VentilSekunden / 60).ToString("0", de)} min offen",
            $"{Math.Round(tag.Gramm).ToString("0", de)} g CO₂",
        };
        teile.Add(tag.ZielErreichtUm is { } z ? $"Ziel erstmals um {z} Uhr erreicht" : "Ziel heute nicht erreicht");
        if (tag.Flaschenwechsel) teile.Add("Flasche gewechselt");
        return string.Join(" · ", teile);
    }

    public List<Co2Tag> LetzteTage(int anzahl) => _repo.GetCo2Tage(anzahl);
}

/// <summary>Das Livebild, wie es die Seite oben zeigt.</summary>
public sealed record Co2Live(
    bool HaErreichbar,
    double? Co2Ppm,
    int? ZielPpm,
    string ZielQuelle,
    int? PlanPpm,
    string? PlanHerkunft,
    int ZielWarm,
    int ZielMittel,
    int ZielKuehl,
    int HysteresePpm,
    int? NachschubUnterPpm,
    bool? Bedarf,
    bool? KlimaOk,
    bool? VentilOffen,
    bool? AutomatikAn,
    bool? LichtAn,
    int? T6Stufe,
    double? CanopyC,
    double? RhProzent,
    double? Vpd,
    int? ImpulseHeute,
    double? GrammProSekunde,
    double? LetzteMessungGps,
    double? FlascheRestKg,
    int? ImpulsBedarfSekunden,
    string? LetzterImpuls);
