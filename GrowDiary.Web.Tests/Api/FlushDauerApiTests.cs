using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Der Flush vor der Ernte — eine oder zwei Wochen — über die API.
/// </summary>
/// <remarks>
/// Gespeichert wird 7 oder 14; ohne Angabe bleibt beim Ändern das Bisherige und beim Anlegen
/// gilt die Vorgabe 14. Alles andere lehnt die API ab: eine still zur Vorgabe gemachte 10
/// wäre eine Antwort, der niemand ansieht, dass sie nicht stimmt.
/// </remarks>
public sealed class FlushDauerApiTests : IDisposable
{
    private readonly string _temp;
    private readonly GrowRepository _repository;
    private readonly GrowsApiController _controller;
    private readonly Tent _tent;

    public FlushDauerApiTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "FlushDauerApi_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        var paths = new AppPaths(_temp);
        _tent = TestDatabase.InitializeWithDefaultTent(paths);
        _repository = new GrowRepository(paths);

        var loader = new KnowledgeBaseLoader(paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        _controller = new GrowsApiController(
            _repository,
            new AuditRepository(paths),
            new DeviationAnalyzerService(new TargetValueService(loader)),
            new TreatmentRecommender(loader),
            new SetupRepository(paths),
            new HydroSetupRepository(paths, new TentRepository(paths)),
            new SystemAuditRepository(paths));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    private int NeuerGrow(int flushTage = GrowRun.FlushTageStandard)
        => _repository.CreateGrow(new GrowRun
        {
            TentId = _tent.Id,
            Name = "Lauf",
            StartDate = new DateTime(2026, 5, 1),
            Status = GrowStatus.Running,
            FlushTage = flushTage,
        });

    private GrowUpsertRequest Anfrage(int? flushTage) => new()
    {
        Name = "Lauf",
        TentId = _tent.Id,
        StartDate = "2026-05-01",
        Status = GrowStatus.Running,
        SeedType = SeedType.Feminized,
        EntryPoint = GrowEntryPoint.Germination,
        FlushTage = flushTage,
    };

    [Fact]
    public void NeuesGrowOhneAngabe_FlushtZweiWochen()
    {
        var id = NeuerGrow();
        Assert.Equal(14, _repository.GetGrow(id)!.FlushTage);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(14)]
    public void ChangeSpeichertEineOderZweiWochen_AnzeigeUndListeSagenDasGleiche(int tage)
    {
        var id = NeuerGrow(tage == 7 ? 14 : 7);

        Assert.IsType<OkObjectResult>(_controller.Update(id, Anfrage(tage)).Result);

        var grow = _repository.GetGrow(id)!;
        Assert.Equal(tage, grow.FlushTage);
        Assert.Equal(tage, grow.ToDetailDto().FlushTage);
        Assert.Equal(tage, grow.ToSummaryDto().FlushTage);
        Assert.Equal(tage, _repository.GetActiveGrows().Single(g => g.Id == id).FlushTage);
    }

    [Fact]
    public void ChangeOhneAngabe_BehaeltDenBisherigenFlush()
    {
        var id = NeuerGrow(7);

        _controller.Update(id, Anfrage(null));

        // Ein Formular, das das Feld nicht kennt (älterer Client), darf den Flush nicht auf 14 zurücksetzen.
        Assert.Equal(7, _repository.GetGrow(id)!.FlushTage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(21)]
    [InlineData(-7)]
    public void UngueltigeDauer_WirdAbgelehnt_UndNichtGespeichert(int unsinn)
    {
        var id = NeuerGrow(7);

        var antwort = _controller.Update(id, Anfrage(unsinn));

        var fehler = Assert.IsAssignableFrom<ObjectResult>(antwort.Result);
        Assert.Equal(400, fehler.StatusCode);
        Assert.Equal(7, _repository.GetGrow(id)!.FlushTage);
    }

    [Fact]
    public void AnfrageZuGrow_TraegtDenFlushDurch_OhneAngabeZweiWochen()
    {
        // Anlegen verlangt ein Hydro-System; die Abbildung Anfrage → Grow (die jedes Anlegen und
        // Ändern benutzt) lässt sich ohne fahren.
        Assert.Equal(7, Anfrage(7).ToFormModel().ToGrow().FlushTage);
        Assert.Equal(14, Anfrage(14).ToFormModel().ToGrow().FlushTage);
        Assert.Equal(14, Anfrage(null).ToFormModel().ToGrow().FlushTage);
    }

    [Fact]
    public void AlteExporteOhneFlushFeld_GeltenAlsZweiWochen()
    {
        // Ein Export aus der Zeit vor dem Feld kennt es nicht: gelesen wird die Vorgabe.
        var altesJson = "{\"name\":\"Alt\"}";
        var alt = System.Text.Json.JsonSerializer.Deserialize<GrowRun>(
            altesJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(14, alt.FlushTage);
    }
}
