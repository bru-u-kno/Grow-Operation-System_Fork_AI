using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// „Notiz geändert, speichern" darf dem Grow nichts nehmen.
/// </summary>
/// <remarks>
/// <para>Der Audit-Fund dahinter: PUT /api/grows/{id} baut die Zeile komplett
/// neu aus dem Formular — und das Formular kennt weder die bestätigten
/// Meilensteine (Keimung, Veg, Finish) noch das Enddatum noch die
/// Nachtabsenkung. Ohne Bewahrung setzte jedes harmlose Bearbeiten den
/// Wochenzähler zurück, nahm dem Archiv die Laufzeit und der Rampe ihren
/// Schalter — still, ohne jede Meldung.</para>
/// </remarks>
public sealed class GrowUpdatePreservationTests : IDisposable
{
    private readonly string _temp;
    private readonly AppPaths _paths;
    private readonly GrowRepository _repository;
    private readonly GrowsApiController _controller;
    private readonly Tent _tent;

    public GrowUpdatePreservationTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "GrowUpdatePreservation_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _paths = new AppPaths(_temp);
        _tent = TestDatabase.InitializeWithDefaultTent(_paths);
        _repository = new GrowRepository(_paths);

        var loader = new KnowledgeBaseLoader(_paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        _controller = new GrowsApiController(
            _repository,
            new AuditRepository(_paths),
            new WeekCounterService(),
            new DeviationAnalyzerService(new TargetValueService(loader)),
            new TreatmentRecommender(loader),
            new SetupRepository(_paths),
            new HydroSetupRepository(_paths, new TentRepository(_paths)),
            new SystemAuditRepository(_paths));
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    [Fact]
    public void EditingANoteKeepsMilestonesEndDateAndNightRamp()
    {
        var growId = _repository.CreateGrow(new GrowRun
        {
            TentId = _tent.Id,
            Name = "Lauf 12",
            StartDate = new DateTime(2026, 5, 1),
            Status = GrowStatus.Running,
            SeedType = SeedType.Feminized,
        });

        // Was Workflow-Knoepfe und eigene Endpunkte im Lauf der Wochen setzen:
        var grow = _repository.GetGrow(growId)!;
        grow.GerminatedAt = new DateTime(2026, 5, 3);
        grow.RootedAt = new DateTime(2026, 5, 8);
        grow.VegStartedAt = new DateTime(2026, 5, 12);
        grow.FlipDate = new DateTime(2026, 6, 10);
        grow.FinishStartedAt = new DateTime(2026, 8, 1);
        grow.EndDate = new DateTime(2026, 8, 15);
        grow.NightRampEnabled = true;
        grow.NightRampFloorC = 16.5;
        grow.UseFeedChartTargets = true;
        _repository.UpdateGrow(grow);

        // Der Nutzer aendert nur die Notiz. Das Formular schickt, was es kennt —
        // UseFeedChartTargets fehlt (das Feld schickt die Seite nicht mit).
        var antwort = _controller.Update(growId, new GrowUpsertRequest
        {
            Name = "Lauf 12",
            TentId = _tent.Id,
            StartDate = "2026-05-01",
            Status = GrowStatus.Running,
            SeedType = SeedType.Feminized,
            EntryPoint = GrowEntryPoint.Germination,
            Notes = "Blattlage heute deutlich besser.",
        });

        Assert.IsType<OkObjectResult>(antwort.Result);
        var danach = _repository.GetGrow(growId)!;

        Assert.Equal(new DateTime(2026, 5, 3), danach.GerminatedAt);
        Assert.Equal(new DateTime(2026, 5, 8), danach.RootedAt);
        Assert.Equal(new DateTime(2026, 5, 12), danach.VegStartedAt);
        Assert.Equal(new DateTime(2026, 6, 10), danach.FlipDate);
        Assert.Equal(new DateTime(2026, 8, 1), danach.FinishStartedAt);
        Assert.Equal(new DateTime(2026, 8, 15), danach.EndDate);
        Assert.True(danach.NightRampEnabled);
        Assert.Equal(16.5, danach.NightRampFloorC);
        Assert.True(danach.UseFeedChartTargets);
        Assert.Equal("Blattlage heute deutlich besser.", danach.Notes);
    }

    [Fact]
    public void AnExplicitOptOutStillTurnsTheFeedChartTargetsOff()
    {
        var growId = _repository.CreateGrow(new GrowRun
        {
            TentId = _tent.Id,
            Name = "Lauf 13",
            StartDate = new DateTime(2026, 5, 1),
            Status = GrowStatus.Running,
        });
        var grow = _repository.GetGrow(growId)!;
        grow.UseFeedChartTargets = true;
        _repository.UpdateGrow(grow);

        // Bewahren heisst nicht festkleben: wer ausdruecklich false schickt,
        // schaltet ab.
        _controller.Update(growId, new GrowUpsertRequest
        {
            Name = "Lauf 13",
            TentId = _tent.Id,
            StartDate = "2026-05-01",
            Status = GrowStatus.Running,
            UseFeedChartTargets = false,
        });

        Assert.False(_repository.GetGrow(growId)!.UseFeedChartTargets);
    }

    [Fact]
    public void AFlowerEntryFormCanStillEditTheFlipDate()
    {
        var growId = _repository.CreateGrow(new GrowRun
        {
            TentId = _tent.Id,
            Name = "Mid-Grow-Einstieg",
            StartDate = new DateTime(2026, 6, 1),
            Status = GrowStatus.Running,
            EntryPoint = GrowEntryPoint.Flower,
            FlipDate = new DateTime(2026, 6, 1),
        });

        // Einstieg "Bluete": hier ZEIGT das Formular das Flip-Feld — ein neuer
        // Wert muss ankommen, die Bewahrung darf nur greifen, wo das Feld fehlt.
        _controller.Update(growId, new GrowUpsertRequest
        {
            Name = "Mid-Grow-Einstieg",
            TentId = _tent.Id,
            StartDate = "2026-06-01",
            Status = GrowStatus.Running,
            EntryPoint = GrowEntryPoint.Flower,
            FlipDate = "2026-06-05",
        });

        Assert.Equal(new DateTime(2026, 6, 5), _repository.GetGrow(growId)!.FlipDate);
    }
}
