using System.Globalization;
using System.Text;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;

namespace GrowDiary.Web.Services;

/// <summary>Eine Zeile im Kontext-Paket: eine Messgröße mit Ziel und Herkunft.</summary>
public sealed record AgentMetricLine(
    string Label,
    string Key,
    double? Value,
    string? Unit,
    double? TargetMin,
    double? TargetMax,
    string TargetSource,
    int? AgeMinutes,
    string Verdict);

/// <summary>Alles, was ein fremder Agent über diesen Grow wissen muss.</summary>
public sealed record AgentContext(
    string GrowName,
    string Stage,
    int? DayInStage,
    int? DayTotal,
    string HydroStyle,
    string ProfileName,
    double? ReservoirLiters,
    IReadOnlyList<string> WaterNotes,
    IReadOnlyList<AgentMetricLine> Metrics,
    IReadOnlyList<string> OpenIssues,
    IReadOnlyList<string> RecentJournal,
    IReadOnlyList<string> RecentDoses,
    DateTime GeneratedAtUtc);

/// <summary>
/// Das Kontext-Paket für einen eigenen KI-Agenten.
/// </summary>
/// <remarks>
/// <para>Nicht zu verwechseln mit dem Grow-Export: der ist ein vollständiges
/// Archiv eines abgeschlossenen Laufs, gedacht zum Aufheben. Das hier ist eine
/// Momentaufnahme, gedacht zum Vorlegen — was steht gerade an, gegen welche
/// Ziele, und was ist zuletzt passiert.</para>
///
/// <para>Der Unterschied macht die Auswahl: hier zählt, was für eine Einschätzung
/// nötig ist, und sonst nichts. Ein Agent, der zehntausend Messzeilen bekommt,
/// antwortet schlechter als einer, der zwanzig gute bekommt — und die
/// Sensormesswerte eines halben Jahres sagen über heute Abend nichts.</para>
///
/// <para>Jede Zahl trägt ihre Herkunft. Ohne sie kann ein Agent nicht
/// unterscheiden, ob 6,05 ein bewusst gesetzter eigener Wert ist oder der
/// mitgelieferte Vorschlag — und wird dem einen widersprechen, als wäre es der
/// andere.</para>
/// </remarks>
public sealed class AgentContextBuilder
{
    /// <summary>
    /// Deutsches Zahlenformat für den Bericht.
    /// </summary>
    /// <remarks>
    /// Über <see cref="AppCulture"/>, nicht über <c>GetCultureInfo</c> direkt:
    /// ohne ICU wirft der Aufruf, und in einem statischen Feld reisst das die
    /// ganze Klasse mit — der Bericht wäre dann nicht englisch formatiert,
    /// sondern gar nicht erzeugt worden.
    /// </remarks>
    private static readonly CultureInfo Deutsch = AppCulture.German;

    private readonly GrowRepository _repository;
    private readonly SensorReadingRepository _readings;
    private readonly AlertRuleRepository _alertRules;
    private readonly TargetValueService _targetValues;
    private readonly HydroSetupRepository _hydroSetups;
    private readonly SetpointProfileRepository _profiles;
    private readonly DosingRepository _dosing;
    private readonly JournalRepository _journal;
    private readonly WaterProfileStore _waterProfile;
    private readonly KnowledgeBaseLoader _knowledge;

    public AgentContextBuilder(
        GrowRepository repository,
        SensorReadingRepository readings,
        AlertRuleRepository alertRules,
        TargetValueService targetValues,
        HydroSetupRepository hydroSetups,
        SetpointProfileRepository profiles,
        DosingRepository dosing,
        JournalRepository journal,
        WaterProfileStore waterProfile,
        KnowledgeBaseLoader knowledge)
    {
        _repository = repository;
        _readings = readings;
        _alertRules = alertRules;
        _targetValues = targetValues;
        _hydroSetups = hydroSetups;
        _profiles = profiles;
        _dosing = dosing;
        _journal = journal;
        _waterProfile = waterProfile;
        _knowledge = knowledge;
    }

    /// <summary>Die Messgrößen, die in das Paket gehören — Reihenfolge wie im Kopf eines Growers.</summary>
    private static readonly (string Key, string Label, string? Unit)[] Metrics =
    [
        ("reservoir-ph", "pH", null),
        ("reservoir-ec", "EC", "mS/cm"),
        ("reservoir-temp", "Wassertemperatur", "°C"),
        ("dissolved-oxygen", "Sauerstoff", "mg/L"),
        ("orp", "ORP", "mV"),
        ("temperature", "Lufttemperatur", "°C"),
        ("humidity", "Luftfeuchte", "%"),
        ("vpd", "VPD", "kPa"),
        ("co2", "CO₂", "ppm"),
        ("ppfd", "PPFD", "µmol/m²/s"),
        // Die Raumluft um das Zelt (z. B. der Fühler am Controller) — ohne Ziel,
        // aber der Grund, warum das Zeltklima ist, wie es ist. Ausgeschrieben,
        // damit der Assistent sie nicht für die Luft im Zelt oder von draußen hält.
        // Ohne Messwert fällt die Zeile weg (siehe BuildMetrics).
        ("outside-temperature", "Raumluft außerhalb des Zelts: Temperatur", "°C"),
        ("outside-humidity", "Raumluft außerhalb des Zelts: Luftfeuchte", "%"),
        ("outside-vpd", "Raumluft außerhalb des Zelts: VPD", "kPa"),
    ];

    public AgentContext? Build(int growId, DateTime nowUtc)
    {
        var grow = _repository.GetGrow(growId);
        if (grow is null) return null;

        var stage = GrowStageResolver.Resolve(grow, DateTime.Today);
        var systemProfil = grow.SystemId is { } systemId
            ? _hydroSetups.GetSystem(systemId)?.SetpointProfileId
            : null;
        var resolved = SetpointProfileResolver.Resolve(
            grow.SetpointProfileId, systemProfil, grow.HydroStyle);

        // Dasselbe Ziel wie auf dem Bildschirm — ein Berater, der ein anderes
        // EC-Ziel liest als der Betreiber sieht, raet konsequent daneben. Die
        // Kette steht in Zielband und nur dort.
        var targets = Zielband.FuerGrow(
            _targetValues, _knowledge, grow, stage, systemProfil, null);
        var rules = grow.TentId is { } tentId ? _alertRules.GetForTent(tentId) : null;

        return new AgentContext(
            GrowName: grow.Name,
            Stage: StageLabel(stage),
            DayInStage: null,
            DayTotal: grow.StartDate is { } start ? (int)(DateTime.Today - start.Date).TotalDays + 1 : null,
            HydroStyle: grow.HydroStyle.ToString(),
            ProfileName: ProfileLabel(resolved),
            ReservoirLiters: grow.SystemId is { } id ? _hydroSetups.GetSystem(id)?.ReservoirLiters : null,
            WaterNotes: BuildWaterNotes(grow),
            Metrics: BuildMetrics(grow, targets, rules, nowUtc),
            OpenIssues: BuildIssues(grow),
            RecentJournal: BuildJournal(grow),
            RecentDoses: BuildDoses(grow, nowUtc),
            GeneratedAtUtc: nowUtc);
    }

    /// <summary>
    /// Was das Ausgangswasser mitbringt — nur bei Leitungs- oder Mischwasser.
    /// </summary>
    /// <remarks>
    /// <para>Ohne diese Zeilen liest ein Berater „EC 0,28 vor dem Düngen" als
    /// Rest-Salz und rät zum Wasserwechsel. Mit ihnen weiss er: das ist das
    /// Wasser selbst.</para>
    ///
    /// <para>Nur Fakten aus dem Bericht, keine Ratschläge — die Härte-Einordnung
    /// (weich/mittel/hart) folgt den gesetzlichen Bereichen des Wasch- und
    /// Reinigungsmittelgesetzes, nicht einer eigenen Meinung.</para>
    /// </remarks>
    private List<string> BuildWaterNotes(GrowRun grow)
    {
        if (grow.WaterSource == WaterSource.RO) return [];
        if (_waterProfile.Get() is not { HasAnyValue: true } wasser) return [];

        var zeilen = new List<string>();
        var quelle = grow.WaterSource == WaterSource.Mixed ? "Leitungswasser (gemischt mit RO)" : "Leitungswasser";
        var herkunft = string.IsNullOrWhiteSpace(wasser.SourceLabel) ? "" : $" — Quelle: {wasser.SourceLabel}";
        zeilen.Add($"{quelle}{herkunft}");

        if (wasser.ConductivityUsCm is { } us)
        {
            zeilen.Add($"Start-EC {(us / 1000).ToString("0.00", Deutsch)} mS/cm — "
                + "ein Messwert in dieser Höhe vor dem Düngen ist das Wasser selbst, kein Rest-Salz.");
        }

        if (wasser.TotalHardnessDh is { } haerte)
        {
            var bereich = haerte < 8.4 ? "weich" : haerte <= 14 ? "mittel" : "hart";
            zeilen.Add($"Gesamthärte {haerte.ToString("0.#", Deutsch)} °dH (Härtebereich {bereich})"
                + (wasser.CarbonateHardnessDh is { } kh
                    ? $", Karbonathärte {kh.ToString("0.#", Deutsch)} °dH"
                    : ""));
        }

        if (wasser.CalciumMgL is not null || wasser.MagnesiumMgL is not null
            || wasser.SodiumMgL is not null || wasser.NitrateMgL is not null)
        {
            var teile = new List<string>();
            if (wasser.CalciumMgL is { } c) teile.Add($"Calcium {c.ToString("0.#", Deutsch)} mg/L");
            if (wasser.MagnesiumMgL is { } mg) teile.Add($"Magnesium {mg.ToString("0.#", Deutsch)} mg/L");
            if (wasser.SodiumMgL is { } na) teile.Add($"Natrium {na.ToString("0.#", Deutsch)} mg/L");
            if (wasser.NitrateMgL is { } no3) teile.Add($"Nitrat {no3.ToString("0.#", Deutsch)} mg/L");
            zeilen.Add(string.Join(" · ", teile));
        }

        if (wasser.Ph is { } ph)
        {
            zeilen.Add($"pH des Leitungswassers {ph.ToString("0.0", Deutsch)}");
        }

        if (!string.IsNullOrWhiteSpace(wasser.Disinfection))
        {
            zeilen.Add($"Desinfektion laut Bericht: {wasser.Disinfection}");
        }

        // Die Bewertung zum Schluss, und nur wenn es etwas zu sagen gibt: die
        // Zeilen darueber sind Fakten aus dem Bericht, das hier ist unsere
        // Lesart davon — deshalb steht die Quelle der Schwelle dabei.
        foreach (var punkt in WasserAmpelService.Bewerten(wasser).Punkte)
        {
            if (punkt.Stufe == "gut") continue;
            var marke = punkt.Stufe == "warnung" ? "Achtung" : "Hinweis";
            zeilen.Add($"{marke} — {punkt.Label} ({punkt.Wert}): {punkt.Aussage} [Schwelle: {punkt.Quelle}]");
        }

        return zeilen;
    }

    private List<AgentMetricLine> BuildMetrics(
        GrowRun grow, HydroTargetValues? targets, IReadOnlyList<TentAlertRule>? rules, DateTime nowUtc)
    {
        var lines = new List<AgentMetricLine>();
        if (grow.TentId is not { } tentId) return lines;

        foreach (var (key, label, unit) in Metrics)
        {
            var reading = _readings.GetNewestReading(tentId, key);
            var (min, max, source) = TargetFor(key, targets, rules);

            // Messgroessen ohne Wert UND ohne Ziel weglassen: eine Zeile
            // „ORP: — (kein Ziel)" traegt nichts bei und kostet Aufmerksamkeit.
            if (reading is null && min is null && max is null) continue;

            lines.Add(new AgentMetricLine(
                label, key, reading?.Value, unit ?? reading?.Unit, min, max, source,
                reading is { } r ? (int)(nowUtc - r.CapturedAtUtc).TotalMinutes : null,
                Verdict(reading?.Value, min, max)));
        }

        return lines;
    }

    private (double? Min, double? Max, string Source) TargetFor(
        string key, HydroTargetValues? targets, IReadOnlyList<TentAlertRule>? rules)
    {
        if (UserTargets.For(key, rules) is { } eigen)
        {
            return (eigen.Min, eigen.Max, "vom Nutzer eingetragen");
        }

        if (targets is null) return (null, null, "keins");

        return key switch
        {
            "reservoir-ph" => (targets.PhMin, targets.PhMax, "Phasen-Profil"),
            "reservoir-ec" => (targets.EcMin, targets.EcMax, "Phasen-Profil"),
            "reservoir-temp" => (targets.WaterTempNightC, targets.WaterTempDayC, "Phasen-Profil"),
            "orp" => (targets.OrpMin, targets.OrpMax, "Phasen-Profil"),
            "vpd" => (targets.VpdMin, targets.VpdMax, "Phasen-Profil"),
            "ppfd" => (targets.PpfdMin, targets.PpfdMax, "Phasen-Profil"),
            "co2" => (targets.Co2Min, targets.Co2Max, "Phasen-Profil"),
            _ => (null, null, "keins"),
        };
    }

    /// <summary>Kurzurteil je Zeile — damit ein Agent nicht selbst vergleichen muss.</summary>
    private static string Verdict(double? value, double? min, double? max)
    {
        if (value is not { } wert) return "kein Messwert";
        if (min is null && max is null) return "kein Ziel hinterlegt";
        if (min is { } untere && wert < untere) return "unter dem Ziel";
        if (max is { } obere && wert > obere) return "über dem Ziel";
        return "im Ziel";
    }

    private List<string> BuildIssues(GrowRun grow)
        => _repository.GetRiskEvents()
            .Where(risk => risk.GrowId == grow.Id || (grow.TentId is { } tentId && risk.TentId == tentId))
            .Where(risk => risk.Status is RiskEventStatus.Open or RiskEventStatus.Acknowledged)
            .OrderByDescending(risk => risk.CreatedAtUtc)
            .Take(10)
            // Ortszeit, nicht UTC: der Bericht wird von einem Menschen gelesen,
            // und kurz vor Mitternacht stuende sonst der falsche Tag da.
            .Select(risk => $"{risk.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} · {risk.Severity} · {risk.Title}")
            .ToList();

    private List<string> BuildJournal(GrowRun grow)
        => _journal.GetForGrow(grow.Id)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .Take(8)
            // Ein Titel ist freiwillig — die meisten Eintraege sind blosser Text.
            // Fehlt er, stand hier vorher „2026-07-28 ·  — Trichome sind so weit",
            // mit Gedankenstrich ins Leere.
            .Select(entry =>
            {
                var datum = entry.OccurredAtUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var titel = Kurz(entry.Title, 60);
                var text = Kurz(entry.Body, 200);
                return string.IsNullOrWhiteSpace(titel)
                    ? $"{datum} · {text}"
                    : string.IsNullOrWhiteSpace(text) ? $"{datum} · {titel}" : $"{datum} · {titel} — {text}";
            })
            .ToList();

    private List<string> BuildDoses(GrowRun grow, DateTime nowUtc)
    {
        if (grow.TentId is not { } tentId) return [];

        var namen = _dosing.GetPumps(tentId).ToDictionary(pump => pump.Id, pump => pump.Name);
        return _dosing.GetEvents(tentId: tentId, limit: 12)
            .Where(dose => dose.Outcome == DoseOutcome.Done && dose.DosedMl > 0)
            .Select(dose =>
            {
                var wirkung = dose.ValueBefore is { } vor && dose.ValueAfter is { } nach
                    ? $", {vor.ToString("0.00", Deutsch)} → {nach.ToString("0.00", Deutsch)}"
                    : string.Empty;
                var test = dose.Simulated ? " (Testbetrieb, nichts geflossen)" : string.Empty;
                return $"{dose.OccurredAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} · {namen.GetValueOrDefault(dose.PumpId, "Pumpe")} · {dose.DosedMl.ToString("0.##", Deutsch)} ml{wirkung}{test}";
            })
            .ToList();
    }

    private string ProfileLabel(ResolvedProfile resolved)
    {
        var name = _profiles.GetAll().FirstOrDefault(profile => profile.ReferenceId == resolved.ProfileId)?.Name
            ?? resolved.ProfileId;
        var herkunft = resolved.Origin switch
        {
            ProfileOrigin.Grow => "am Grow gesetzt",
            ProfileOrigin.System => "vom Hydro-System geerbt",
            _ => "Standard des Anbaustils",
        };
        return $"{name} ({herkunft})";
    }

    private static string StageLabel(GrowStage stage) => stage switch
    {
        GrowStage.Seedling => "Sämling",
        GrowStage.Clone => "Steckling",
        GrowStage.Veg => "Vegetativ",
        GrowStage.Transition => "Transition",
        GrowStage.Flower => "Blüte",
        GrowStage.Finish => "Finish",
        _ => stage.ToString(),
    };

    private static string Kurz(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var einzeilig = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return einzeilig.Length <= max ? einzeilig : einzeilig[..max] + "…";
    }

    /// <summary>
    /// Dasselbe als Text — das Format, das ein Agent tatsächlich vorgelegt bekommt.
    /// </summary>
    /// <remarks>
    /// Markdown und nicht JSON: der Nutzer soll die Datei öffnen und lesen
    /// können, bevor er sie weitergibt. Wer sie an einen Agenten übergibt, gibt
    /// Angaben über seinen Grow aus der Hand — dann soll er vorher sehen, was
    /// drinsteht.
    ///
    /// Die Zahlen werden fest deutsch formatiert, nicht in der Kultur des
    /// Rechners. Der Text ist deutsch; ein „6.2" mittendrin wäre schon falsch,
    /// und schlimmer: dieselbe Datei sähe je nach Container anders aus. Genau
    /// daran ist der erste Anlauf gescheitert — lokal 6,2, auf dem Bau-Rechner
    /// 6.2.
    /// </remarks>
    public static string ToMarkdown(AgentContext context)
    {
        var text = new StringBuilder();
        text.AppendLine($"# Grow OS — Lagebericht: {context.GrowName}");
        text.AppendLine();
        text.AppendLine($"Stand: {context.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC");
        text.AppendLine($"Phase: {context.Stage}"
            + (context.DayTotal is { } tag ? $" · Tag {tag} seit Start" : string.Empty));
        text.AppendLine($"System: {context.HydroStyle}"
            + (context.ReservoirLiters is { } liter ? $" · {liter.ToString("0.#", Deutsch)} L Reservoir" : string.Empty));
        text.AppendLine($"Sollwert-Profil: {context.ProfileName}");
        text.AppendLine();

        // Vor den Messwerten, nicht dahinter: wer die Werte liest, muss vorher
        // wissen, was davon schon das Ausgangswasser ist.
        if (context.WaterNotes.Count > 0)
        {
            text.AppendLine("## Ausgangswasser");
            text.AppendLine();
            foreach (var zeile in context.WaterNotes)
            {
                text.AppendLine($"- {zeile}");
            }
            text.AppendLine();
        }

        text.AppendLine("## Aktuelle Werte");
        text.AppendLine();
        text.AppendLine("| Messgröße | Wert | Alter | Ziel | Ziel kommt von | Urteil |");
        text.AppendLine("|---|---|---|---|---|---|");
        foreach (var line in context.Metrics)
        {
            var wert = line.Value is { } v ? $"{v.ToString("0.##", Deutsch)}{(line.Unit is null ? "" : " " + line.Unit)}" : "—";
            var alter = line.AgeMinutes is { } m ? $"{m} min" : "—";
            var ziel = (line.TargetMin, line.TargetMax) switch
            {
                ({ } min, { } max) => $"{min.ToString("0.##", Deutsch)}–{max.ToString("0.##", Deutsch)}",
                ({ } min, null) => $"ab {min.ToString("0.##", Deutsch)}",
                (null, { } max) => $"bis {max.ToString("0.##", Deutsch)}",
                _ => "—",
            };
            text.AppendLine($"| {line.Label} | {wert} | {alter} | {ziel} | {line.TargetSource} | {line.Verdict} |");
        }
        text.AppendLine();

        Abschnitt(text, "Offene Auffälligkeiten", context.OpenIssues, "Keine offenen Punkte.");
        Abschnitt(text, "Letzte Dosen", context.RecentDoses, "Es wurde noch nichts dosiert.");
        Abschnitt(text, "Letzte Journal-Einträge", context.RecentJournal, "Noch keine Einträge.");

        text.AppendLine("---");
        text.AppendLine();
        text.AppendLine("Erzeugt von Grow OS. Die Zielwerte stammen entweder aus den Grenzwerten, die");
        text.AppendLine("der Betreiber selbst eingetragen hat, oder aus dem hinterlegten Phasen-Profil —");
        text.AppendLine("die Spalte „Ziel kommt von“ sagt, welches. Ein selbst eingetragener Wert ist");
        text.AppendLine("eine bewusste Entscheidung und kein Vorschlag.");

        return text.ToString();
    }

    private static void Abschnitt(StringBuilder text, string titel, IReadOnlyList<string> zeilen, string wennLeer)
    {
        text.AppendLine($"## {titel}");
        text.AppendLine();
        if (zeilen.Count == 0)
        {
            text.AppendLine(wennLeer);
        }
        else
        {
            foreach (var zeile in zeilen) text.AppendLine($"- {zeile}");
        }
        text.AppendLine();
    }
}
