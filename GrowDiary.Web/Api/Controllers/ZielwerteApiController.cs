using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Eine Stufe der Herkunftskette eines Ziels.</summary>
public sealed record ZielStufeDto(string Name, string? Hinweis, string? Wert, bool Gilt, bool Weg);

/// <summary>Fork AI (Grow-Plan, Schritt 2): die Alarmregel einer Messgröße, zum Bearbeiten.</summary>
public sealed record ZielAlarmRegelDto(
    string Quelle,
    double? Min,
    double? Max,
    double? NachtMin,
    double? NachtMax,
    double? Toleranz,
    double StandardToleranz,
    int KarenzMinuten,
    bool Aktiv,
    bool PlanMoeglich);

/// <summary>Fork AI (Grow-Plan, Schritt 2): ein Zielwert der laufenden Woche, zum Bearbeiten.</summary>
public sealed record ZielPlanFeldDto(
    string Feld,
    string Bezeichnung,
    string Einheit,
    double Min,
    double Max,
    double Schritt,
    double? Wert,
    double? Startwert,
    string Herkunft);

/// <summary>Ein Wert mit Ist, Band, Herkunft und Kette.</summary>
public sealed record ZielwertDto(
    string Key,
    string Name,
    string? Einheit,
    string Ist,
    double? IstZahl,
    double? Min,
    double? Max,
    string? Band,
    string Quelle,
    string? QuelleZusatz,
    string Lage,
    string? Alarm,
    IReadOnlyList<ZielStufeDto> Kette,
    ZielAlarmRegelDto? Regel = null,
    double? AlarmVon = null,
    double? AlarmBis = null,
    bool Meldet = false,
    IReadOnlyList<ZielPlanFeldDto>? PlanFelder = null);

/// <summary>Eine Gruppe der Ansicht „wo stelle ich das ein“.</summary>
public sealed record ZielGruppeDto(string Titel, string Route, string RouteText, IReadOnlyList<ZielZeileDto> Zeilen, string Hinweis);

public sealed record ZielZeileDto(string Links, string Rechts);

public sealed record ZielwerteDto(
    int? GrowId,
    string? GrowName,
    string? Phase,
    string? Woche,
    string? Programm,
    IReadOnlyList<string> Hinweise,
    IReadOnlyList<ZielwertDto> Werte,
    IReadOnlyList<ZielGruppeDto> Gruppen,
    IReadOnlyList<WochenplanUebergabeDto> Uebergabe,
    string? LetzteUebergabe,
    int? ZeltId = null,
    string? SpalteId = null,
    bool EigenerPlan = false);

/// <summary>
/// Fork AI: die Seite „Zielwerte“ — was gilt, woher es kommt, wo man es ändert.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Ein Ziel entsteht aus vier Quellen (Anbaustil,
/// Sollwertprofil, Feed-Chart-Woche, Zelt-Grenzwert), und jede spätere sticht
/// die früheren. Auf der Kachel steht davon nur das Ergebnis und ein knapper
/// Zusatz — „dein Wert“ sagt weder, WER den Wert gesetzt hat, noch, dass die
/// Wochenspalte gerade wirkungslos daneben liegt. Wer eine Zahl ändern wollte,
/// suchte sie an drei Stellen.</para>
///
/// <para><b>Nichts neu gerechnet.</b> Ist-Werte und Bänder kommen aus demselben
/// <see cref="GrowDashboardComposer"/> wie die Live-Kacheln, die Stufen der
/// Kette aus <see cref="Zielband"/> und <see cref="MischplanService"/>. Diese
/// Seite erklärt die vorhandene Auflösung, sie erfindet keine zweite — sonst
/// stünden hier andere Zahlen als auf dem Schirm daneben.</para>
/// </remarks>
[ApiController]
[Route("api/zielwerte")]
[Produces("application/json")]
public sealed class ZielwerteApiController : ApiControllerBase
{
    /// <summary>Anzeigenamen, sonst stünde dort die Kennung.</summary>
    private static readonly Dictionary<string, string> Namen = new(StringComparer.OrdinalIgnoreCase)
    {
        ["temperature"] = "Luft",
        ["humidity"] = "Luftfeuchte",
        ["vpd"] = "VPD",
        ["co2"] = "CO₂",
        ["ppfd"] = "PPFD",
        ["reservoir-ph"] = "pH",
        ["reservoir-ec"] = "EC",
        ["orp"] = "ORP",
        ["reservoir-temp"] = "Wassertemperatur",
    };

    /// <summary>
    /// Was die Seite zeigt — und in welcher Reihenfolge.
    /// </summary>
    /// <remarks>
    /// Bewusst NICHT alles, was die Live-Seite hat: Licht, Füllstand und
    /// Sauerstoff haben in keiner der vier Quellen ein Ziel. Sie stünden hier
    /// dauerhaft mit „–“ und läsen sich wie ein Versäumnis, obwohl niemand
    /// einen solchen Sensor haben muss.
    /// </remarks>
    private static readonly string[] Reihenfolge =
    [
        "temperature", "humidity", "vpd", "co2", "ppfd",
        "reservoir-ph", "reservoir-ec", "orp", "reservoir-temp",
    ];

    /// <summary>Fork AI (Grow-Plan): welche Wochenfelder zu welcher Messgröße gehören.</summary>
    private static readonly Dictionary<string, string[]> PlanFelderJeMetrik = new(StringComparer.OrdinalIgnoreCase)
    {
        ["temperature"] = ["airTempC"],
        ["humidity"] = ["rhMax"],
        ["vpd"] = ["vpdMin", "vpdMax"],
        ["co2"] = ["co2Min", "co2Max"],
        ["ppfd"] = ["ppfdMin", "ppfdMax"],
        ["reservoir-ph"] = ["phMin", "phMax"],
        ["reservoir-ec"] = ["ecTarget", "ecMin", "ecMax"],
        ["orp"] = ["orpMin", "orpMax"],
        ["reservoir-temp"] = ["waterTempDayC", "waterTempNightC"],
    };

    private readonly GrowRepository _grows;
    private readonly HomeAssistantService _ha;
    private readonly GrowDashboardComposer _composer;
    private readonly TargetValueService _sollwerte;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly AlertRuleRepository _regeln;
    private readonly SetpointProfileRepository _profile;
    private readonly HydroSetupRepository _hydro;
    private readonly WochenplanSyncService _sync;
    private readonly GrowPlanService? _plaene;
    private readonly WochenwertUeberlagerung? _ueberlagerung;
    private readonly LightRepository? _lichter;

    public ZielwerteApiController(
        GrowRepository grows,
        HomeAssistantService ha,
        GrowDashboardComposer composer,
        TargetValueService sollwerte,
        KnowledgeBaseLoader wissen,
        AlertRuleRepository regeln,
        SetpointProfileRepository profile,
        HydroSetupRepository hydro,
        WochenplanSyncService sync,
        GrowPlanService? plaene = null,
        WochenwertUeberlagerung? ueberlagerung = null,
        LightRepository? lichter = null)
    {
        _lichter = lichter;
        _plaene = plaene;
        _ueberlagerung = ueberlagerung;
        _grows = grows;
        _ha = ha;
        _composer = composer;
        _sollwerte = sollwerte;
        _wissen = wissen;
        _regeln = regeln;
        _profile = profile;
        _hydro = hydro;
        _sync = sync;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ZielwerteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ZielwerteDto>> Get(CancellationToken cancellationToken)
    {
        var grow = _grows.GetActiveGrows().FirstOrDefault();
        if (grow?.TentId is not { } zeltId || _grows.GetTent(zeltId) is not { } zelt)
        {
            return Ok(Leer());
        }

        var einstellungen = _grows.GetEffectiveHomeAssistantSettings();
        var zustaende = await _ha.GetStatesAsync(einstellungen, zelt, cancellationToken);
        var kacheln = _composer.BuildTentMetrics(zelt, zustaende, _grows.GetMeasurementsForTent(zeltId));

        var phase = GrowStageResolver.Resolve(grow, DateTime.Today);
        var regeln = _regeln.GetForTent(zeltId);

        // F-017: dieselbe Lichtphase wie die Alarmauswertung — nachts schweigen
        // VPD, CO₂ und PPFD, feste Grenzen nehmen ihr Nachtband.
        zustaende.TryGetValue("light-status", out var lichtJetzt);
        var lichtphase = LightClock.Resolve(lichtJetzt, _lichter?.GetActiveLightScheduleForTent(zeltId), DateTime.UtcNow);

        // Stufe 2: das Profil allein — ohne Wochenspalte, ohne eigene Grenzen.
        var profilId = SetpointProfileResolver.Resolve(
            grow.SetpointProfileId, SystemProfil(grow), grow.HydroStyle).ProfileId;
        var profilBand = _sollwerte.GetTargets(profilId, phase);
        var profilName = ProfilName(profilId);

        /* Stufe 3: dasselbe Band, überlagert von der laufenden Wochenspalte.
           Bewusst über Zielband.FuerGrow und NICHT über MischplanService.MitFeedchart:
           die Kette wird nirgends abgetippt (siehe EineZielbandketteTests), sonst
           nennt das Chart eine Zahl und der Bildschirm daneben eine andere. Die
           eigenen Grenzen bleiben hier weg — die sind Stufe 4 und sollen die
           Herkunft nicht schon verdecken. */
        var spalte = MischplanService.NutztWochenziele(grow)
            ? MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms)?.Spalte
            : null;
        var wochenBand = Zielband.FuerGrow(
            _sollwerte, _wissen, grow, phase, SystemProfil(grow), eigeneGrenzen: null);

        var werte = new List<ZielwertDto>();
        var hinweise = new List<string>();

        MetricCard? Kachel(string k) => kacheln.FirstOrDefault(c => c.Key == k);

        foreach (var key in Reihenfolge)
        {
            if (Kachel(key) is not { } karte) continue;

            /* Ohne Messwert ist die Zeile keine Auskunft, sondern ein Vorwurf:
               PPFD steht im Plan, aber wer kein Lichtmessgerät hat, kann daran
               nichts richtig machen. Dieselbe Begründung wie bei Sauerstoff. */
            if (karte.NumericValue is null) continue;

            var ausProfil = Zielband.FuerMetrik(key, profilBand);
            var ausKette = Zielband.FuerMetrik(key, wochenBand);

            /* Stufe 3 gilt, sobald die Wochenspalte die Messgröße NENNT — auch
               wenn sie zufällig dieselbe Zahl nennt wie das Profil.
               
               Der erste Entwurf verglich stattdessen die Bänder und schrieb bei
               Gleichstand „Profil“ daran. Rechnerisch egal, als Antwort aber
               falsch: wer VPD ändern will, wird zum Profil geschickt, obwohl
               die Woche es vorgibt und beim nächsten Wochenwechsel übersteuert. */
            (double? Min, double? Max) ausWoche = spalte is not null && NenntSpalte(spalte, key)
                ? ausKette
                : (null, null);
            var eigene = UserTargets.For(key, regeln);
            var planRegel = regeln.FirstOrDefault(r =>
                r.Enabled && r.Quelle == Grenzwertquelle.Plan
                && string.Equals(r.MetricKey, key, StringComparison.OrdinalIgnoreCase));

            // Fork AI (Grow-Plan): die Grenzen, bei denen gerade gemeldet würde.
            var aktiveRegel = regeln.FirstOrDefault(r =>
                r.Enabled && string.Equals(r.MetricKey, key, StringComparison.OrdinalIgnoreCase));
            var wirksam = aktiveRegel is null ? null : Planzielgrenzen.Wirksam(aktiveRegel, wochenBand, null);
            var (alarmVon, alarmBis) = AlarmgrenzenJetzt(wirksam, key, lichtphase);

            var kette = new List<ZielStufeDto>();

            kette.Add(new ZielStufeDto(
                profilName is null ? "Anbaustil" : $"Profil · {profilName}",
                Leise(ausProfil) is null ? "kennt diese Messgröße nicht" : $"Phase {Phasenname.Fuer(phase)}",
                Leise(ausProfil),
                Gilt: eigene is null && Leise(ausWoche) is null && Leise(ausProfil) is not null,
                Weg: Leise(ausProfil) is not null && (eigene is not null || Leise(ausWoche) is not null)));

            kette.Add(new ZielStufeDto(
                // Fork AI (Grow-Plan): mit eigenem Plan ist die Wochenspalte die des Grows.
                (GrowPlanService.HatPlan(grow.Id) ? "Plan des Grows" : "Feed-Chart")
                    + (spalte is null ? "" : $" · {spalte.Label}"),
                spalte is null
                    ? "am Grow nicht eingeschaltet"
                    : Leise(ausWoche) is null ? "erreicht das Zielband nicht" : null,
                Leise(ausWoche),
                Gilt: eigene is null && Leise(ausWoche) is not null,
                Weg: Leise(ausWoche) is not null && eigene is not null));

            kette.Add(new ZielStufeDto(
                eigene is not null ? "Zelt-Grenze · Fest" : planRegel is not null ? "Zelt-Grenze · Plan" : "Zelt-Grenze",
                eigene is not null
                    ? Gesetzt(zeltId, key)
                    : planRegel is not null
                        // F-015: die Toleranz der Regel, nicht die Werkseinstellung.
                        ? $"folgt dem Plan · Toleranz ±{Zahl(planRegel.Toleranz is { } tol && tol > 0 ? tol : Planzielgrenzen.StandardToleranz(key))}"
                        : "keine Regel — meldet nie",
                eigene is { } e ? Band(e.Min, e.Max) : null,
                Gilt: eigene is not null,
                Weg: false));

            var quelle = eigene is not null ? "Fest"
                : Leise(ausWoche) is not null ? "Plan"
                : Leise(ausProfil) is not null ? "Profil"
                : "–";

            var zusatz = eigene is not null
                ? Gesetzt(zeltId, key)
                : Leise(ausWoche) is not null ? spalte?.Label
                : Leise(ausProfil) is not null ? profilName
                : null;

            // Beim VPD entscheidet der Blattversatz mit, worauf sich die Zahl ueberhaupt
            // bezieht: mit Versatz ist es Blatt-VPD, ohne ist es Luft-VPD. Die RDWC-Baender
            // sind fuer Blatt-VPD gezeichnet, also steht ohne diesen Zusatz eine Zahl auf der
            // Seite, deren Bezugsgroesse man ihr nicht ansieht.
            if (key == "vpd")
            {
                var bezug = zelt.LeafTempOffsetC < 0
                    ? $"Blatt {zelt.LeafTempOffsetC:0.#} °C"
                    : "Luft-VPD — kein Blattversatz gesetzt";
                zusatz = string.IsNullOrWhiteSpace(zusatz) ? bezug : $"{zusatz} · {bezug}";
            }

            if (eigene is not null && Leise(ausWoche) is not null)
            {
                hinweise.Add($"{Namen.GetValueOrDefault(key, key)} steht auf Fest — die Wochenspalte kommt nicht an.");
            }

            if (eigene is null && planRegel is null && (karte.TargetMin is not null || karte.TargetMax is not null))
            {
                hinweise.Add($"{Namen.GetValueOrDefault(key, key)} hat keine Alarmregel — eine Abweichung meldet sich nirgends.");
            }

            werte.Add(new ZielwertDto(
                key,
                Namen.GetValueOrDefault(key, karte.Label),
                karte.Unit,
                karte.Value,
                karte.NumericValue,
                karte.TargetMin,
                karte.TargetMax,
                // Fertig formatiert mitgeben: die Zahl selbst wird als JSON mit
                // Punkt übertragen, und die Anzeige hätte daraus „1.3 – 1.5“
                // gemacht — in einer durchweg deutschen Oberfläche.
                Band(karte.TargetMin, karte.TargetMax),
                quelle,
                zusatz,
                Lage(karte),
                Alarmtext(regeln, key),
                kette,
                RegelDto(regeln, key),
                alarmVon,
                alarmBis,
                Meldet(alarmVon, alarmBis, karte.NumericValue),
                spalte is null ? null : PlanFelder(grow, spalte, key)));
        }

        return Ok(new ZielwerteDto(
            grow.Id,
            string.IsNullOrWhiteSpace(grow.Name) ? $"Grow {grow.Id}" : grow.Name,
            Phasenname.Fuer(phase),
            spalte?.Label,
            profilName,
            hinweise,
            werte,
            Gruppen(werte, spalte?.Label, profilName),
            _sync.Sollwerte().Select(u => new WochenplanUebergabeDto(
                u.Rolle, Rollenname(u.Rolle), u.EntityId, Zahl(u.Wert), u.Zustand)).ToList(),
            _sync.Stand.LetzterLauf,
            zeltId,
            spalte?.Id,
            GrowPlanService.HatPlan(grow.Id)));
    }

    private static ZielAlarmRegelDto? RegelDto(IReadOnlyList<TentAlertRule> regeln, string key)
    {
        var regel = regeln.FirstOrDefault(r => string.Equals(r.MetricKey, key, StringComparison.OrdinalIgnoreCase));
        if (regel is null) return null;
        return new ZielAlarmRegelDto(
            regel.Quelle.ToString(),
            regel.MinValue,
            regel.MaxValue,
            regel.NightMinValue,
            regel.NightMaxValue,
            regel.Toleranz,
            Planzielgrenzen.StandardToleranz(key),
            regel.CooldownMinutes,
            regel.Enabled,
            Planzielgrenzen.KenntPlanziel(key));
    }

    /// <summary>
    /// F-017: die Grenzen, gegen die die Alarmauswertung in dieser Lichtphase misst —
    /// nachts keine für VPD/CO₂/PPFD, bei festen Regeln das Nachtband.
    /// </summary>
    public static (double? Von, double? Bis) AlarmgrenzenJetzt(TentAlertRule? wirksam, string key, LightsNow lichtphase)
        => wirksam is null || (lichtphase == LightsNow.Off && LightClock.IsDaytimeOnly(key))
            ? (null, null)
            : wirksam.GrenzenFuer(lichtphase);

    /// <summary>Liegt der Ist-Wert gerade außerhalb der wirksamen Alarmgrenzen?</summary>
    /// <remarks>Nur eine Anzeige — ob wirklich eine Nachricht rausgeht, entscheidet die Alarmauswertung (Karenz, Tag/Nacht).</remarks>
    private static bool Meldet(double? von, double? bis, double? ist)
        => ist is { } wert
           && ((von is { } min && wert < min) || (bis is { } max && wert > max));

    private List<ZielPlanFeldDto>? PlanFelder(GrowRun grow, FeedChartColumn spalte, string key)
    {
        if (!PlanFelderJeMetrik.TryGetValue(key, out var namen)) return null;
        var hatPlan = GrowPlanService.HatPlan(grow.Id);
        var plan = hatPlan ? _plaene?.Stand(grow.Id, GrowPlanStaende.Arbeit) : null;

        return namen
            .Select(Wochenwertfelder.Finden)
            .OfType<Wochenwertfelder.Feld>()
            .Select(feld => new ZielPlanFeldDto(
                feld.Name,
                feld.Bezeichnung,
                feld.Einheit,
                feld.Min,
                feld.Max,
                feld.Schritt,
                feld.Lesen(spalte),
                hatPlan ? _plaene?.Startwert(grow.Id, spalte.Id, feld) : _ueberlagerung?.Planwert(spalte, feld),
                plan?.Inhalt.HerkunftVon(spalte.Id, feld.Name) ?? GrowPlanHerkunft.Programm))
            .ToList();
    }

    /// <summary>Die Ansicht „wo stelle ich das ein“ — nach Quelle statt nach Messgröße.</summary>
    private static List<ZielGruppeDto> Gruppen(List<ZielwertDto> werte, string? woche, string? profil)
    {
        var gruppen = new List<ZielGruppeDto>();

        var ausWoche = werte.Where(w => w.Quelle == "Plan").ToList();
        if (ausWoche.Count > 0)
        {
            gruppen.Add(new ZielGruppeDto(
                woche is null ? "Feed-Chart" : $"Feed-Chart · {woche}",
                "/wissen", "Wissen ›",
                ausWoche.Select(w => new ZielZeileDto(w.Name, Band(w.Min, w.Max) ?? "–")).ToList(),
                "Wandert beim Wochenwechsel von selbst weiter."));
        }

        var ausProfil = werte.Where(w => w.Quelle == "Profil").ToList();
        if (ausProfil.Count > 0)
        {
            gruppen.Add(new ZielGruppeDto(
                profil is null ? "Sollwertprofil" : $"Profil · {profil}",
                "/zielwerte?tab=profile", "Profile ›",
                ausProfil.Select(w => new ZielZeileDto(w.Name, Band(w.Min, w.Max) ?? "–")).ToList(),
                "Je Phase, nicht je Woche — springt erst beim Phasenwechsel."));
        }

        var fest = werte.Where(w => w.Quelle == "Fest").ToList();
        if (fest.Count > 0)
        {
            gruppen.Add(new ZielGruppeDto(
                "Zelt · feste Grenzen",
                "/zielwerte?tab=grenzwerte", "Grenzwerte ›",
                fest.Select(w => new ZielZeileDto(w.Name, Band(w.Min, w.Max) ?? "–")).ToList(),
                "Feste Zahlen sind zugleich das angezeigte Ziel — sie stechen Profil und Wochenspalte."));
        }

        return gruppen;
    }

    /// <summary>Nennt die Wochenspalte diese Messgröße überhaupt?</summary>
    /// <remarks>
    /// Nur eine Anwesenheitsprüfung an der Spalte — das Überlagern selbst
    /// bleibt bei <see cref="Zielband"/>, sonst stünde die Kette zweimal im
    /// Code (siehe <c>EineZielbandketteTests</c>).
    /// </remarks>
    private static bool NenntSpalte(FeedChartColumn spalte, string key) => key switch
    {
        "reservoir-ph" => spalte.PhMin is not null || spalte.PhMax is not null,
        "reservoir-ec" => spalte.EcTarget is not null,
        "reservoir-temp" => spalte.WaterTempDayC is not null || spalte.WaterTempNightC is not null,
        "vpd" => spalte.VpdMin is not null || spalte.VpdMax is not null,
        "co2" => spalte.Co2Min is not null || spalte.Co2Max is not null,
        "ppfd" => spalte.PpfdMin is not null || spalte.PpfdMax is not null,
        "orp" => spalte.OrpMin is not null || spalte.OrpMax is not null,
        _ => false,
    };

    private string? SystemProfil(GrowRun grow)
        => grow.SystemId is { } id ? _hydro.GetSystem(id)?.SetpointProfileId : null;

    private string? ProfilName(string profilId)
        => SetpointProfile.IdFromReference(profilId) is { } eigen ? _profile.Get(eigen)?.Name : null;

    /// <summary>
    /// Wer die feste Grenze gesetzt hat: der Wochenplan oder ein Mensch.
    /// </summary>
    /// <remarks>
    /// Genau der Unterschied, den „dein Wert“ auf der Kachel verschweigt. Der
    /// Sync merkt sich, was er zuletzt geschrieben hat — steht das noch drin,
    /// stammt die Zahl von ihm.
    /// </remarks>
    private string Gesetzt(int zeltId, string key)
    {
        var stand = _sync.Stand;
        var vonPlan = stand.Geschrieben.Keys.Any(k => k.StartsWith($"zelt:{zeltId}/{key}/", StringComparison.OrdinalIgnoreCase));
        var vonHand = stand.VonDir.Any(k => k.StartsWith($"zelt:{zeltId}/{key}/", StringComparison.OrdinalIgnoreCase));

        if (vonHand) return "von dir gesetzt — der Plan lässt sie in Ruhe";
        return vonPlan ? "vom Wochenplan nachgezogen" : "von dir eingetragen";
    }

    private static string? Alarmtext(IReadOnlyList<TentAlertRule> regeln, string key)
    {
        var regel = regeln.FirstOrDefault(r => r.Enabled && string.Equals(r.MetricKey, key, StringComparison.OrdinalIgnoreCase));
        if (regel is null) return null;

        return regel.Quelle == Grenzwertquelle.Plan
            ? $"Plan ±{Zahl(regel.Toleranz ?? Planzielgrenzen.StandardToleranz(key))}"
            : Band(regel.MinValue, regel.MaxValue);
    }

    private static string Lage(MetricCard karte)
    {
        if (karte.NumericValue is not { } wert) return "unbekannt";
        if (karte.TargetMin is null && karte.TargetMax is null) return "kein Ziel";
        if (karte.TargetMin is { } min && wert < min) return "darunter";
        if (karte.TargetMax is { } max && wert > max) return "darüber";
        return "im Ziel";
    }

    private static string? Leise((double? Min, double? Max) band)
        => Band(band.Min, band.Max);

    private static string? Band(double? min, double? max)
    {
        if (min is null && max is null) return null;
        if (min is null) return $"bis {Zahl(max!.Value)}";
        if (max is null) return $"ab {Zahl(min.Value)}";
        return $"{Zahl(min.Value)} – {Zahl(max.Value)}";
    }

    private static string Zahl(double wert) => wert.ToString("0.##", AppCulture.German);

    private static string Rollenname(string rolle) => rolle switch
    {
        WochenplanSyncService.Rollen.WasserTag => "Chiller Tag",
        WochenplanSyncService.Rollen.WasserNacht => "Chiller Nacht",
        WochenplanSyncService.Rollen.RhObergrenze => "RH-Obergrenze",
        WochenplanSyncService.Rollen.Co2Ziel => "CO₂-Ziel",
        WochenplanSyncService.Rollen.LuftUnten => "Alarmgrenze Luft unten",
        WochenplanSyncService.Rollen.LuftNachtUnten => "Alarmgrenze Luft unten (Nacht)",
        WochenplanSyncService.Rollen.LuftNachtOben => "Alarmgrenze Luft oben (Nacht)",
        WochenplanSyncService.Rollen.LuftOben => "Alarmgrenze Luft oben",
        WochenplanSyncService.Rollen.FeuchteOben => "Alarmgrenze Luftfeuchte",
        _ => rolle,
    };

    private static ZielwerteDto Leer() => new(
        null, null, null, null, null,
        Array.Empty<string>(), Array.Empty<ZielwertDto>(), Array.Empty<ZielGruppeDto>(),
        Array.Empty<WochenplanUebergabeDto>(), null);
}
