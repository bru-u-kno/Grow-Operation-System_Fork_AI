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

    /// <summary>Leert das Register (Neuladen, Tests).</summary>
    public static void Leeren() => Plaene.Clear();
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
            GrowPlanRegister.Leeren();
            foreach (var stand in _repo.AlleStaende(GrowPlanStaende.Arbeit))
            {
                GrowPlanRegister.Setzen(stand.GrowId, stand.Inhalt);
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
            _logger.LogInformation("Grow-Plan für Grow {Grow} aus {Programm} angelegt.", grow.Id, programm.Id);
            return arbeit;
        }
    }

    public static int VegiWochen(GrowRun grow)
        => grow.PlannedVegDays is int tage && tage > 0
            ? (int)Math.Ceiling(tage / 7.0)
            : GrowPlanBauer.StandardVegiWochen;

    public static int Bluetewochen(GrowRun grow)
        => grow.BreederFlowerWeeksMax is int wochen && wochen > 0
            ? wochen
            : GrowPlanBauer.StandardBluetewochen;
}
