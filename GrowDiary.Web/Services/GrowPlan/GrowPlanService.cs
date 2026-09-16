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

    /// <summary>Der Plan des Grows als Programm — oder null, wenn der Grow keinen hat.</summary>
    public static NutrientProgramDefinition? Programm(int growId)
        => Plaene.TryGetValue(growId, out var programm) ? programm : null;

    internal static void Setzen(int growId, GrowPlanInhalt inhalt)
        => Plaene[growId] = new NutrientProgramDefinition
        {
            Id = inhalt.ProgrammId,
            Name = inhalt.ProgrammName,
            FeedChart = inhalt.Chart,
        };

    /// <summary>Nimmt einen Grow aus dem Register (Tests).</summary>
    /// <remarks>
    /// Bewusst kein „alles leeren": das Register ist prozessweit, und parallel
    /// laufende Tests teilen es sich.
    /// </remarks>
    public static void Entfernen(int growId) => Plaene.TryRemove(growId, out _);
}

/// <summary>
/// Fork AI (Grow-Plan, 16.09.2026): legt Pläne an und hält das Register aktuell.
/// </summary>
public sealed class GrowPlanService
{
    private readonly GrowPlanRepository _repo;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly TargetValueService _ziele;
    private readonly ILogger<GrowPlanService> _logger;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<int, GrowPlanInhalt> _startstaende = new();

    public GrowPlanService(
        GrowPlanRepository repo,
        KnowledgeBaseLoader wissen,
        TargetValueService ziele,
        ILogger<GrowPlanService> logger)
    {
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
            var eintraege = new List<GrowPlanEintrag>();

            foreach (var (spalteId, feldName, wert) in aenderungen)
            {
                var feld = Wochenwertfelder.Finden(feldName)
                    ?? throw new ArgumentException($"Unbekanntes Feld {feldName}.");
                var spalte = arbeit.Inhalt.Chart.Columns.FirstOrDefault(
                        c => string.Equals(c.Id, spalteId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Unbekannte Woche {spalteId}.");

                var startwert = Startwert(growId, spalte.Id, feld);
                var neu = wert ?? startwert;
                var alt = feld.Lesen(spalte);
                if (Gleich(alt, neu)) continue;

                feld.Schreiben(spalte, neu);
                var herkunft = wert is null || Gleich(neu, startwert)
                    ? StartHerkunft(growId, spalte.Id, feld.Name)
                    : GrowPlanHerkunft.Eigen;
                arbeit.Inhalt.HerkunftSetzen(spalte.Id, feld.Name, herkunft);

                eintraege.Add(new GrowPlanEintrag(0, growId, zeit, GrowPlanArten.Wert, spalte.Id, feld.Name,
                    Text(alt), Text(neu), ziel, grund));
            }

            if (eintraege.Count == 0) return 0;

            _repo.Speichern([arbeit with { GeaendertUtc = zeit }], eintraege);
            GrowPlanRegister.Setzen(growId, arbeit.Inhalt);
            return eintraege.Count;
        }
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
