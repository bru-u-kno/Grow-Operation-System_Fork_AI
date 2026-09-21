using System.Collections.Concurrent;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services.GrowPlan;

/// <summary>
/// Fork AI (Grow-Plan, 16.09.2026): Arbeitsstände im Speicher, damit die
/// statischen Leser (Zielband, Live-Kacheln) den Plan ohne Datenbankzugriff finden.
/// </summary>
/// <remarks>
/// Statisch aus demselben Grund wie <c>MischplanService.ZielSpalteFuerGrow</c>:
/// mehrere Leser sind Singletons oder statische Helfer. Gefüllt wird das Register
/// ausschließlich vom <see cref="GrowPlanService"/>.
/// </remarks>
public static class GrowPlanRegister
{
    private static readonly ConcurrentDictionary<int, NutrientProgramDefinition> Plaene = new();

    /// <summary>Fork AI (forkai.130): der ganze Arbeitsstand — für Einstellungen neben dem Chart (Nacht wie Tag).</summary>
    private static readonly ConcurrentDictionary<int, GrowPlanInhalt> Inhalte = new();

    /// <summary>Der Arbeitsstand des Grows — oder null, wenn der Grow keinen Plan hat.</summary>
    public static GrowPlanInhalt? Inhalt(int growId)
        => Inhalte.TryGetValue(growId, out var inhalt) ? inhalt : null;

    /// <summary>Der Plan des Grows als Programm — oder null, wenn der Grow keinen hat.</summary>
    public static NutrientProgramDefinition? Programm(int growId)
        => Plaene.TryGetValue(growId, out var programm) ? programm : null;

    internal static void Setzen(int growId, GrowPlanInhalt inhalt)
    {
        Plaene[growId] = new NutrientProgramDefinition
        {
            Id = inhalt.ProgrammId,
            Name = inhalt.ProgrammName,
            FeedChart = inhalt.Chart,
        };
        Inhalte[growId] = inhalt;
    }

    /// <summary>Nimmt einen Grow aus dem Register (Tests).</summary>
    /// <remarks>
    /// Bewusst kein „alles leeren": das Register ist prozessweit, und parallel
    /// laufende Tests teilen es sich.
    /// </remarks>
    public static void Entfernen(int growId)
    {
        Plaene.TryRemove(growId, out _);
        Inhalte.TryRemove(growId, out _);
    }
}

/// <summary>Eine Zutat der Dosierung einer Woche.</summary>
public sealed record PlanDosis(string Komponente, double MlProLiter);

/// <summary>Was beim Speichern einer Planwoche übergeben wird.</summary>
public sealed record PlanSpeichernAnfrage(
    string SpalteId,
    IReadOnlyList<(string Feld, double? Wert)> Werte,
    IReadOnlyList<PlanDosis>? Dosierung,
    bool AuchInsProgramm,
    string? ProgrammName,
    string? Grund);

public sealed record PlanSpeichernErgebnis(int Aenderungen, string? ProgrammId, string? ProgrammName);

public sealed record ProgrammwechselErgebnis(string ProgrammId, string ProgrammName, int Uebernommen, int Entfallen);

/// <summary>
/// Fork AI (Grow-Plan, 16.09.2026): legt Pläne an und hält das Register aktuell.
/// </summary>
public sealed class GrowPlanService
{
    private readonly GrowPlanRepository _repo;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly TargetValueService _ziele;
    private readonly ILogger<GrowPlanService> _logger;
    private readonly EigeneProgramme? _eigene;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<int, GrowPlanInhalt> _startstaende = new();

    public GrowPlanService(
        GrowPlanRepository repo,
        KnowledgeBaseLoader wissen,
        TargetValueService ziele,
        ILogger<GrowPlanService> logger,
        EigeneProgramme? eigene = null)
    {
        _eigene = eigene;
        _repo = repo;
        _wissen = wissen;
        _ziele = ziele;
        _logger = logger;
    }

    /// <summary>Alle Arbeitsstände aus der Datenbank ins Register.</summary>
    public void RegisterLaden()
    {
        lock (_lock)
        {
            // Kein Leeren vorab: Pläne werden nie gelöscht, und das Register ist prozessweit.
            foreach (var stand in _repo.AlleStaende(GrowPlanStaende.Arbeit))
            {
                GrowPlanRegister.Setzen(stand.GrowId, stand.Inhalt);
            }
            foreach (var stand in _repo.AlleStaende(GrowPlanStaende.Start))
            {
                _startstaende[stand.GrowId] = stand.Inhalt;
            }
            // Nach einem Programmwechsel ist die Basis der Vergleichswert.
            foreach (var stand in _repo.AlleStaende(GrowPlanStaende.Basis))
            {
                _startstaende[stand.GrowId] = stand.Inhalt;
            }
        }
    }

    public GrowPlanStand? Stand(int growId, string stand) => _repo.Laden(growId, stand);

    public IReadOnlyList<GrowPlanEintrag> Buch(int growId) => _repo.Buch(growId);

    /// <summary>
    /// Legt Start- und Arbeitsstand aus dem Programm des Grows an.
    /// </summary>
    /// <returns>Den Arbeitsstand — oder null, wenn der Grow kein bekanntes Programm hat
    /// oder schon einen Plan besitzt.</returns>
    public GrowPlanStand? Anlegen(GrowRun grow, string? vermerk = null, DateTime? jetztUtc = null)
    {
        if (string.IsNullOrWhiteSpace(grow.FeedProgramId)) return null;

        var programm = _wissen.NutrientPrograms.FirstOrDefault(
            p => string.Equals(p.Id, grow.FeedProgramId, StringComparison.OrdinalIgnoreCase));
        if (programm is null)
        {
            _logger.LogWarning("Grow-Plan: Programm {Programm} von Grow {Grow} nicht gefunden.", grow.FeedProgramId, grow.Id);
            return null;
        }

        lock (_lock)
        {
            if (_repo.Laden(grow.Id, GrowPlanStaende.Arbeit) is not null) return null;

            var profilId = TargetValueService.ProfileIdFor(grow.HydroStyle);
            var inhalt = GrowPlanBauer.AusProgramm(
                programm,
                stage => _ziele.GetTargets(profilId, stage),
                VegiWochen(grow),
                Bluetewochen(grow));

            var zeit = jetztUtc ?? DateTime.UtcNow;
            var start = new GrowPlanStand(grow.Id, GrowPlanStaende.Start, inhalt, vermerk, zeit, zeit);
            var arbeit = start with { Stand = GrowPlanStaende.Arbeit, Inhalt = GrowPlanBauer.Kopie(inhalt) };
            var eintrag = new GrowPlanEintrag(0, grow.Id, zeit, GrowPlanArten.Angelegt, null, null, null,
                $"{programm.Name} · {inhalt.Chart.Columns.Count} Wochen", null, vermerk);

            _repo.Speichern([start, arbeit], [eintrag]);
            GrowPlanRegister.Setzen(grow.Id, arbeit.Inhalt);
            _startstaende[grow.Id] = start.Inhalt;
            _logger.LogInformation("Grow-Plan für Grow {Grow} aus {Programm} angelegt.", grow.Id, programm.Id);
            return arbeit;
        }
    }

    /// <summary>Hat der Grow einen Plan?</summary>
    public static bool HatPlan(int growId) => GrowPlanRegister.Programm(growId) is not null;

    /// <summary>Der Wert eines Felds im Startstand — der „Planwert", gegen den Änderungen gemessen werden.</summary>
    public double? Startwert(int growId, string spalteId, Wochenwertfelder.Feld feld)
    {
        if (!_startstaende.TryGetValue(growId, out var start)) return null;
        var spalte = start.Chart.Columns.FirstOrDefault(
            c => string.Equals(c.Id, spalteId, StringComparison.OrdinalIgnoreCase));
        return spalte is null ? null : feld.Lesen(spalte);
    }

    /// <summary>
    /// Setzt Zielwerte im Arbeitsstand; <c>null</c> stellt den Startwert wieder her.
    /// Jede tatsächliche Änderung bekommt einen Eintrag im Änderungsbuch.
    /// </summary>
    /// <remarks>
    /// Geprüft (Bereiche, Paare) wird vorher vom Aufrufer — hier wird nur
    /// geschrieben. Ein abgeschlossener Grow hat keinen Arbeitsstand im Sinne
    /// des Bearbeitens mehr; das sperrt Schritt 6.
    /// </remarks>
    /// <returns>Anzahl der Felder, die sich wirklich geändert haben.</returns>
    public int WerteSetzen(
        int growId,
        IEnumerable<(string SpalteId, string Feld, double? Wert)> aenderungen,
        string ziel = "grow",
        string? grund = null,
        DateTime? jetztUtc = null)
    {
        lock (_lock)
        {
            var arbeit = _repo.Laden(growId, GrowPlanStaende.Arbeit)
                ?? throw new InvalidOperationException($"Grow {growId} hat keinen Plan.");
            var zeit = jetztUtc ?? DateTime.UtcNow;
            var eintraege = WerteAnwenden(growId, arbeit.Inhalt, aenderungen, zeit, ziel, grund);

            if (eintraege.Count == 0) return 0;

            _repo.Speichern([arbeit with { GeaendertUtc = zeit }], eintraege);
            GrowPlanRegister.Setzen(growId, arbeit.Inhalt);
            return eintraege.Count;
        }
    }

    private List<GrowPlanEintrag> WerteAnwenden(
        int growId,
        GrowPlanInhalt inhalt,
        IEnumerable<(string SpalteId, string Feld, double? Wert)> aenderungen,
        DateTime zeit,
        string ziel,
        string? grund)
    {
        var eintraege = new List<GrowPlanEintrag>();
        var liste = aenderungen.ToList();

        foreach (var (spalteId, feldName, wert) in liste)
        {
                var feld = Wochenwertfelder.Finden(feldName)
                    ?? throw new ArgumentException($"Unbekanntes Feld {feldName}.");
                var spalte = inhalt.Chart.Columns.FirstOrDefault(
                        c => string.Equals(c.Id, spalteId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Unbekannte Woche {spalteId}.");

                var startwert = Startwert(growId, spalte.Id, feld);
                var neu = wert ?? startwert;
                var alt = feld.Lesen(spalte);
                if (Gleich(alt, neu)) continue;

                // Wandert das EC-Ziel und fasst niemand das Band an, wandert das Band mit.
                if (feld.Name == "ecTarget" && alt is { } altZiel && neu is { } neuZiel
                    && !liste.Any(a => a.SpalteId == spalteId && a.Feld is "ecMin" or "ecMax")
                    && spalte.EcMin is { } von && spalte.EcMax is { } bis)
                {
                    var delta = neuZiel - altZiel;
                    spalte.EcMin = Math.Round(von + delta, 3);
                    spalte.EcMax = Math.Round(bis + delta, 3);
                }

                feld.Schreiben(spalte, neu);
                var herkunft = wert is null || Gleich(neu, startwert)
                    ? StartHerkunft(growId, spalte.Id, feld.Name)
                    : GrowPlanHerkunft.Eigen;
                inhalt.HerkunftSetzen(spalte.Id, feld.Name, herkunft);

                eintraege.Add(new GrowPlanEintrag(0, growId, zeit, GrowPlanArten.Wert, spalte.Id, feld.Name,
                    Text(alt), Text(neu), ziel, grund));
        }

        return eintraege;
    }

    /// <summary>Ersetzt die Dosierung einer Woche und schreibt je Unterschied einen Eintrag.</summary>
    private static List<GrowPlanEintrag> DosierungAnwenden(
        int growId, FeedChartColumn spalte, IReadOnlyList<PlanDosis> neu, DateTime zeit, string ziel, string? grund)
    {
        var eintraege = new List<GrowPlanEintrag>();
        var alt = spalte.Items.ToDictionary(i => i.Component, StringComparer.OrdinalIgnoreCase);
        var neuNamen = new HashSet<string>(neu.Select(d => d.Komponente), StringComparer.OrdinalIgnoreCase);

        foreach (var weg in spalte.Items.Where(i => !neuNamen.Contains(i.Component)))
        {
            eintraege.Add(new GrowPlanEintrag(0, growId, zeit, GrowPlanArten.Dosierung, spalte.Id, weg.Component,
                Text(weg.MinMlPerLiter), null, ziel, grund));
        }

        foreach (var dosis in neu)
        {
            if (alt.TryGetValue(dosis.Komponente, out var vorher))
            {
                if (Gleich(vorher.MinMlPerLiter, dosis.MlProLiter) && Gleich(vorher.MaxMlPerLiter, dosis.MlProLiter)) continue;
                eintraege.Add(new GrowPlanEintrag(0, growId, zeit, GrowPlanArten.Dosierung, spalte.Id, dosis.Komponente,
                    Text(vorher.MinMlPerLiter), Text(dosis.MlProLiter), ziel, grund));
            }
            else
            {
                eintraege.Add(new GrowPlanEintrag(0, growId, zeit, GrowPlanArten.Dosierung, spalte.Id, dosis.Komponente,
                    null, Text(dosis.MlProLiter), ziel, grund));
            }
        }

        if (eintraege.Count > 0 || !spalte.Items.Select(i => i.Component).SequenceEqual(neu.Select(d => d.Komponente)))
        {
            spalte.Items = neu
                .Select(d => new FeedChartItem { Component = d.Komponente, MinMlPerLiter = d.MlProLiter, MaxMlPerLiter = d.MlProLiter })
                .ToList();
        }

        return eintraege;
    }

    /// <summary>
    /// Speichert eine Woche des Plans: Zielwerte und (optional) die ganze
    /// Dosierung. Mit <see cref="PlanSpeichernAnfrage.AuchInsProgramm"/> gehen
    /// genau diese Änderungen zusätzlich in ein eigenes Programm.
    /// </summary>
    /// <remarks>Geprüft wird vorher vom Aufrufer (Bereiche, Paare, Namen).</remarks>
    public PlanSpeichernErgebnis Speichern(int growId, PlanSpeichernAnfrage anfrage, DateTime? jetztUtc = null)
    {
        lock (_lock)
        {
            if (_repo.Laden(growId, GrowPlanStaende.Ende) is not null)
                throw new InvalidOperationException("Der Grow ist abgeschlossen — sein Plan ist eingefroren.");
            var arbeit = _repo.Laden(growId, GrowPlanStaende.Arbeit)
                ?? throw new InvalidOperationException($"Grow {growId} hat keinen Plan.");
            var spalte = arbeit.Inhalt.Chart.Columns.FirstOrDefault(
                    c => string.Equals(c.Id, anfrage.SpalteId, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unbekannte Woche {anfrage.SpalteId}.");
            var zeit = jetztUtc ?? DateTime.UtcNow;

            string? programmId = null, programmName = null;
            if (anfrage.AuchInsProgramm)
            {
                var programm = InsProgramm(arbeit.Inhalt, spalte.Id, anfrage);
                programmId = programm.Id;
                programmName = programm.Name;
                arbeit.Inhalt.EigenesProgrammId = programm.Id;
            }

            var ziel = programmId is null ? "grow" : $"programm:{programmId}";
            var eintraege = WerteAnwenden(growId, arbeit.Inhalt,
                anfrage.Werte.Select(w => (spalte.Id, w.Feld, w.Wert)), zeit, ziel, anfrage.Grund);
            if (anfrage.Dosierung is { } dosierung)
            {
                eintraege.AddRange(DosierungAnwenden(growId, spalte, dosierung, zeit, ziel, anfrage.Grund));
            }

            if (eintraege.Count > 0 || programmId is not null)
            {
                _repo.Speichern([arbeit with { GeaendertUtc = zeit }], eintraege);
                GrowPlanRegister.Setzen(growId, arbeit.Inhalt);
            }

            return new PlanSpeichernErgebnis(eintraege.Count, programmId, programmName);
        }
    }

    /// <summary>
    /// Fork AI (forkai.130): „Nachts gelten die Tageswerte" — als Standard für alle
    /// Wochen und/oder für eine einzelne Woche.
    /// </summary>
    /// <param name="standard">Neuer Standard; null lässt ihn, wie er ist.</param>
    /// <param name="spalteId">Woche, die abweichen soll; null = nur Standard.</param>
    /// <param name="woche">true/false = diese Woche abweichend; null = Woche folgt wieder dem Standard.</param>
    public bool NachtEinstellen(int growId, bool? standard, string? spalteId, bool? woche, DateTime? jetztUtc = null)
    {
        lock (_lock)
        {
            if (_repo.Laden(growId, GrowPlanStaende.Ende) is not null)
                throw new InvalidOperationException("Der Grow ist abgeschlossen — sein Plan ist eingefroren.");
            var arbeit = _repo.Laden(growId, GrowPlanStaende.Arbeit)
                ?? throw new InvalidOperationException($"Grow {growId} hat keinen Plan.");
            var inhalt = arbeit.Inhalt;

            if (standard is { } neu) inhalt.NachtWieTag = neu;
            if (!string.IsNullOrWhiteSpace(spalteId))
            {
                if (!inhalt.Chart.Columns.Any(c => string.Equals(c.Id, spalteId, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Unbekannte Woche {spalteId}.");
                // Gleich wie der Standard heißt: keine Abweichung — dann soll die Woche
                // einem späteren Umschalten des Standards folgen.
                if (woche is { } eigen && eigen != inhalt.NachtWieTag) inhalt.NachtWieTagJeWoche[spalteId] = eigen;
                else inhalt.NachtWieTagJeWoche.Remove(spalteId);
            }

            _repo.Speichern([arbeit with { GeaendertUtc = jetztUtc ?? DateTime.UtcNow }], []);
            GrowPlanRegister.Setzen(growId, inhalt);
            return true;
        }
    }

    /// <summary>Übernimmt die Änderungen dieser Anfrage in das eigene Programm des Plans (legt es bei Bedarf an).</summary>
    private NutrientProgramDefinition InsProgramm(GrowPlanInhalt inhalt, string spalteId, PlanSpeichernAnfrage anfrage)
    {
        if (_eigene is null) throw new InvalidOperationException("Eigene Programme sind hier nicht verfügbar.");

        var zielId = inhalt.EigenesProgrammId ?? (EigeneProgramme.IstEigen(inhalt.ProgrammId) ? inhalt.ProgrammId : null);
        var programm = zielId is null ? null : _eigene.Finden(zielId);
        if (programm is null)
        {
            var vorlage = _eigene.Finden(inhalt.ProgrammId) ?? new NutrientProgramDefinition
            {
                Id = inhalt.ProgrammId,
                Name = inhalt.ProgrammName,
                FeedChart = GrowPlanBauer.Kopie(inhalt.Chart),
            };
            programm = _eigene.Anlegen(vorlage, anfrage.ProgrammName ?? $"{inhalt.ProgrammName} (eigen)");
        }

        programm.FeedChart ??= new FeedChartDefinition();
        var spalte = programm.FeedChart.Columns.FirstOrDefault(c => string.Equals(c.Id, spalteId, StringComparison.OrdinalIgnoreCase));
        if (spalte is null)
        {
            // Das Programm kennt die Woche nicht (z. B. erzeugtes Raster): die Woche des Plans übernehmen.
            var ausPlan = inhalt.Chart.Columns.First(c => string.Equals(c.Id, spalteId, StringComparison.OrdinalIgnoreCase));
            spalte = new FeedChartColumn { Id = ausPlan.Id, Label = ausPlan.Label, Stage = ausPlan.Stage, Week = ausPlan.Week };
            programm.FeedChart.Columns.Add(spalte);
        }

        foreach (var (feldName, wert) in anfrage.Werte)
        {
            Wochenwertfelder.Finden(feldName)?.Schreiben(spalte, wert);
        }
        if (anfrage.Dosierung is { } dosierung)
        {
            spalte.Items = dosierung
                .Select(d => new FeedChartItem { Component = d.Komponente, MinMlPerLiter = d.MlProLiter, MaxMlPerLiter = d.MlProLiter })
                .ToList();
        }

        _eigene.Speichern(programm);
        return _eigene.Finden(programm.Id) ?? programm;
    }

    /// <summary>Wie viele eigene Änderungen der Arbeitsstand gegenüber seiner Basis trägt.</summary>
    /// <remarks>Zählt geänderte Felder und Wochen mit geänderter Dosierung — für die Frage beim Programmwechsel.</remarks>
    public int EigeneAenderungen(int growId)
    {
        if (_repo.Laden(growId, GrowPlanStaende.Arbeit) is not { } arbeit) return 0;
        var felder = arbeit.Inhalt.Herkunft.Values.Sum(f => f.Values.Count(h => h == GrowPlanHerkunft.Eigen));
        return felder + DosierungGeaendert(growId, arbeit.Inhalt).Count;
    }

    private List<string> DosierungGeaendert(int growId, GrowPlanInhalt arbeit)
    {
        if (!_startstaende.TryGetValue(growId, out var basis)) return [];
        return arbeit.Chart.Columns
            .Where(spalte => basis.Chart.Columns.FirstOrDefault(b => b.Id == spalte.Id) is not { } alt
                             || !GleicheDosierung(alt.Items, spalte.Items))
            .Select(s => s.Id)
            .ToList();
    }

    private static bool GleicheDosierung(IReadOnlyList<FeedChartItem> a, IReadOnlyList<FeedChartItem> b)
        => a.Count == b.Count && a.Zip(b).All(p =>
            string.Equals(p.First.Component, p.Second.Component, StringComparison.OrdinalIgnoreCase)
            && Gleich(p.First.MinMlPerLiter, p.Second.MinMlPerLiter)
            && Gleich(p.First.MaxMlPerLiter, p.Second.MaxMlPerLiter));

    /// <summary>
    /// Wechselt das Programm eines laufenden Grows. Der Arbeitsstand wird aus dem
    /// neuen Programm aufgebaut; mit <paramref name="aenderungenBehalten"/> gehen
    /// eigene Werte und geänderte Dosierungen in die gleichnamigen Wochen mit.
    /// </summary>
    /// <remarks>
    /// Der Startstand bleibt unberührt (Auswertung), die neue Programmkopie wird
    /// als <see cref="GrowPlanStaende.Basis"/> abgelegt und ist ab dann der
    /// Vergleichswert. Wochen, die das neue Programm nicht kennt, entfallen samt
    /// ihrer Änderungen — die Zahl steht im Ergebnis.
    /// </remarks>
    public ProgrammwechselErgebnis ProgrammWechseln(
        GrowRun grow, string neuesProgrammId, bool aenderungenBehalten, DateTime? jetztUtc = null)
    {
        lock (_lock)
        {
            if (_repo.Laden(grow.Id, GrowPlanStaende.Ende) is not null)
                throw new InvalidOperationException("Der Grow ist abgeschlossen — sein Plan ist eingefroren.");
            var arbeit = _repo.Laden(grow.Id, GrowPlanStaende.Arbeit)
                ?? throw new InvalidOperationException($"Grow {grow.Id} hat keinen Plan.");
            var programm = _wissen.NutrientPrograms.FirstOrDefault(
                    p => string.Equals(p.Id, neuesProgrammId, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Das Programm „{neuesProgrammId}“ gibt es nicht.");

            var profilId = TargetValueService.ProfileIdFor(grow.HydroStyle);
            var basis = GrowPlanBauer.AusProgramm(
                programm, stage => _ziele.GetTargets(profilId, stage), VegiWochen(grow), Bluetewochen(grow));
            var neu = GrowPlanBauer.Kopie(basis);
            neu.EigenesProgrammId = EigeneProgramme.IstEigen(programm.Id) ? programm.Id : null;

            var uebernommen = 0;
            var entfallen = 0;
            if (aenderungenBehalten)
            {
                foreach (var alt in arbeit.Inhalt.Chart.Columns)
                {
                    var eigeneFelder = arbeit.Inhalt.Herkunft.TryGetValue(alt.Id, out var h)
                        ? h.Where(x => x.Value == GrowPlanHerkunft.Eigen).Select(x => x.Key).ToList()
                        : [];
                    var dosisGeaendert = DosierungGeaendert(grow.Id, arbeit.Inhalt).Contains(alt.Id);
                    if (eigeneFelder.Count == 0 && !dosisGeaendert) continue;

                    var ziel = neu.Chart.Columns.FirstOrDefault(c => string.Equals(c.Id, alt.Id, StringComparison.OrdinalIgnoreCase));
                    if (ziel is null)
                    {
                        entfallen += eigeneFelder.Count + (dosisGeaendert ? 1 : 0);
                        continue;
                    }

                    foreach (var name in eigeneFelder)
                    {
                        if (Wochenwertfelder.Finden(name) is not { } feld) continue;
                        feld.Schreiben(ziel, feld.Lesen(alt));
                        neu.HerkunftSetzen(ziel.Id, name, GrowPlanHerkunft.Eigen);
                        uebernommen++;
                    }
                    if (dosisGeaendert)
                    {
                        ziel.Items = alt.Items
                            .Select(i => new FeedChartItem { Component = i.Component, MinMlPerLiter = i.MinMlPerLiter, MaxMlPerLiter = i.MaxMlPerLiter })
                            .ToList();
                        uebernommen++;
                    }
                }
            }

            var zeit = jetztUtc ?? DateTime.UtcNow;
            var eintrag = new GrowPlanEintrag(0, grow.Id, zeit, GrowPlanArten.Programmwechsel, null, null,
                arbeit.Inhalt.ProgrammName, programm.Name, "grow",
                aenderungenBehalten
                    ? $"Änderungen übernommen ({uebernommen}{(entfallen > 0 ? $", {entfallen} entfallen" : "")})"
                    : "Änderungen verworfen");

            _repo.Speichern(
                [
                    new GrowPlanStand(grow.Id, GrowPlanStaende.Basis, basis, null, zeit, zeit),
                    arbeit with { Inhalt = neu, GeaendertUtc = zeit },
                ],
                [eintrag]);
            _startstaende[grow.Id] = basis;
            GrowPlanRegister.Setzen(grow.Id, neu);
            _logger.LogInformation("Grow-Plan {Grow}: Programmwechsel {Alt} → {Neu} ({Wahl}).",
                grow.Id, arbeit.Inhalt.ProgrammId, programm.Id, aenderungenBehalten ? "behalten" : "verworfen");
            return new ProgrammwechselErgebnis(programm.Id, programm.Name, uebernommen, entfallen);
        }
    }

    /// <summary>
    /// Hält den Endstand passend zum Status des Grows: abgeschlossen ⇒ einfrieren,
    /// wieder geöffnet ⇒ Einfrieren aufheben. Beides steht im Änderungsbuch.
    /// </summary>
    /// <remarks>
    /// Aufgerufen von allen Wegen, die den Status setzen (Formular, Archivieren,
    /// Ernte) und beim Start für alle Pläne — so bleibt kein Abschluss ohne
    /// eingefrorenen Plan, auch wenn ein neuer Weg dazukommt.
    /// </remarks>
    /// <returns>Was passiert ist: <c>eingefroren</c>, <c>wiedergeoeffnet</c> oder null.</returns>
    public string? Abgleichen(GrowRun grow, DateTime? jetztUtc = null)
    {
        lock (_lock)
        {
            if (_repo.Laden(grow.Id, GrowPlanStaende.Arbeit) is not { } arbeit) return null;
            var ende = _repo.Laden(grow.Id, GrowPlanStaende.Ende);
            var zeit = jetztUtc ?? DateTime.UtcNow;

            if (grow.IsArchived && ende is null)
            {
                var status = grow.Status == GrowStatus.Aborted ? "abgebrochen" : "abgeschlossen";
                _repo.Speichern(
                    [new GrowPlanStand(grow.Id, GrowPlanStaende.Ende, GrowPlanBauer.Kopie(arbeit.Inhalt), status, zeit, zeit)],
                    [new GrowPlanEintrag(0, grow.Id, zeit, GrowPlanArten.Eingefroren, null, null, null, status, null, null)]);
                _logger.LogInformation("Grow-Plan {Grow} eingefroren ({Status}).", grow.Id, status);
                return GrowPlanArten.Eingefroren;
            }

            if (!grow.IsArchived && ende is not null)
            {
                _repo.StandEntfernen(grow.Id, GrowPlanStaende.Ende,
                    new GrowPlanEintrag(0, grow.Id, zeit, GrowPlanArten.Wiedergeoeffnet, null, null, null, null, null, null));
                _logger.LogInformation("Grow-Plan {Grow} wieder geöffnet.", grow.Id);
                return GrowPlanArten.Wiedergeoeffnet;
            }

            return null;
        }
    }

    /// <summary>Alle Pläne mit ihrem Grow abgleichen (beim Start).</summary>
    public int AlleAbgleichen(Func<int, GrowRun?> growLaden)
    {
        var geaendert = 0;
        foreach (var stand in _repo.AlleStaende(GrowPlanStaende.Arbeit))
        {
            if (growLaden(stand.GrowId) is { } grow && Abgleichen(grow) is not null) geaendert++;
        }
        return geaendert;
    }

    /// <summary>
    /// Legt aus dem Endstand (sonst dem Arbeitsstand) ein eigenes Programm an —
    /// die Vorlage für den nächsten Grow.
    /// </summary>
    public NutrientProgramDefinition AlsProgrammSpeichern(int growId, string name, DateTime? jetztUtc = null)
    {
        if (_eigene is null) throw new InvalidOperationException("Eigene Programme sind hier nicht verfügbar.");
        var stand = _repo.Laden(growId, GrowPlanStaende.Ende) ?? _repo.Laden(growId, GrowPlanStaende.Arbeit)
            ?? throw new InvalidOperationException($"Grow {growId} hat keinen Plan.");
        var bibliothek = _eigene.Finden(stand.Inhalt.ProgrammId);
        var vorlage = new NutrientProgramDefinition
        {
            SchemaVersion = bibliothek?.SchemaVersion ?? "1",
            Id = stand.Inhalt.ProgrammId,
            Name = stand.Inhalt.ProgrammName,
            Manufacturer = bibliothek?.Manufacturer ?? string.Empty,
            Category = bibliothek?.Category ?? string.Empty,
            Summary = $"Endstand eines abgeschlossenen Grows.",
            BestFor = bibliothek?.BestFor ?? string.Empty,
            WaterGuidance = bibliothek?.WaterGuidance ?? string.Empty,
            PhGuidance = bibliothek?.PhGuidance ?? string.Empty,
            EcGuidance = bibliothek?.EcGuidance ?? string.Empty,
            FeedChart = GrowPlanBauer.Kopie(stand.Inhalt.Chart),
        };
        var programm = _eigene.Anlegen(vorlage, name);
        var zeit = jetztUtc ?? DateTime.UtcNow;
        _repo.Speichern([], [new GrowPlanEintrag(0, growId, zeit, GrowPlanArten.AlsProgramm, null, null, null, programm.Name, $"programm:{programm.Id}", null)]);
        return programm;
    }

    /// <summary>
    /// Trägt in bestehende Pläne nach, was spätere Versionen neu im Plan führen
    /// (das EC-Band, seit forkai.130 „Luft Nacht"). Kein Eintrag im Änderungsbuch: das ist Technik, keine Änderung
    /// am Ziel — das Band entspricht dem, was bisher aus dem Standard kam.
    /// </summary>
    public int FehlendeFelderNachtragen(IEnumerable<GrowRun> grows)
    {
        var angepasst = 0;
        lock (_lock)
        {
            foreach (var grow in grows)
            {
                var profilId = TargetValueService.ProfileIdFor(grow.HydroStyle);
                foreach (var name in new[] { GrowPlanStaende.Start, GrowPlanStaende.Arbeit })
                {
                    if (_repo.Laden(grow.Id, name) is not { } stand) continue;
                    var geaendert = false;
                    foreach (var spalte in stand.Inhalt.Chart.Columns)
                    {
                        geaendert |= GrowPlanBauer.EcBandFuellen(
                            stand.Inhalt, spalte, _ziele.GetTargets(profilId, GrowPlanBauer.Phase(spalte.Stage)));
                        // Fork AI (forkai.130): Luft Nacht als Planwert nachtragen.
                        geaendert |= GrowPlanBauer.NachtLuftFuellen(stand.Inhalt, spalte);
                    }
                    if (!geaendert) continue;

                    _repo.Nachtragen(stand);
                    if (name == GrowPlanStaende.Arbeit) GrowPlanRegister.Setzen(grow.Id, stand.Inhalt);
                    else _startstaende[grow.Id] = stand.Inhalt;
                    angepasst++;
                }
            }
        }
        return angepasst;
    }

    private string StartHerkunft(int growId, string spalteId, string feld)
        => _startstaende.TryGetValue(growId, out var start)
            ? start.HerkunftVon(spalteId, feld)
            : GrowPlanHerkunft.Programm;

    private static bool Gleich(double? a, double? b)
        => a is null ? b is null : b is not null && Math.Abs(a.Value - b.Value) < 1e-9;

    private static string? Text(double? wert)
        => wert?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Legt für alle laufenden Grows mit Programm einen Plan an, die noch keinen haben.
    /// </summary>
    /// <remarks>
    /// Einmalige Übernahme beim Start. Der Startstand ist dann der heutige Stand
    /// des Programms (inklusive der bisherigen Wochenwert-Abweichungen) — den Stand
    /// vom Grow-Start gibt es nicht mehr. Deshalb der Vermerk.
    /// Abgeschlossene Grows bekommen keinen Plan: ein heutiger Schnappschuss
    /// würde ihnen etwas unterstellen, das damals nicht galt.
    /// </remarks>
    public int FehlendePlaeneAnlegen(IEnumerable<GrowRun> laufendeGrows, DateTime? jetztUtc = null)
    {
        var angelegt = 0;
        foreach (var grow in laufendeGrows)
        {
            if (grow.IsArchived || HatPlan(grow.Id)) continue;
            if (Anlegen(grow, Nachtraeglich, jetztUtc) is not null) angelegt++;
        }
        return angelegt;
    }

    public const string Nachtraeglich = "nachträglich angelegt";

    public static int VegiWochen(GrowRun grow)
        => grow.PlannedVegDays is int tage && tage > 0
            ? (int)Math.Ceiling(tage / 7.0)
            : GrowPlanBauer.StandardVegiWochen;

    public static int Bluetewochen(GrowRun grow)
        => grow.BreederFlowerWeeksMax is int wochen && wochen > 0
            ? wochen
            : GrowPlanBauer.StandardBluetewochen;
}
