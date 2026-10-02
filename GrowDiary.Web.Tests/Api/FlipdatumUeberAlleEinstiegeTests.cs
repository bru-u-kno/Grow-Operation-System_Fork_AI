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
/// Ein eingetragenes Flipdatum kommt an — bei JEDEM Einstiegspunkt.
/// </summary>
/// <remarks>
/// <para><b>Der gemeldete Fehler (25.08.2026).</b> „Das Flipdatum wird nicht
/// übernommen." Das Formular zeigt das Feld für jeden Grow, der keine
/// Autoflower ist; das Backend nahm es nur an, wenn der Einstiegspunkt
/// <c>Flower</c> war. Der Normalfall ist der andere: ein Grow startet in der
/// Keimung oder Vegetation und wird später geflippt. Wer das Datum dann
/// eintrug, bekam HTTP 200 und einen unveränderten Wert zurück — ohne jede
/// Meldung.</para>
///
/// <para><b>Warum eine Zählung und keine Liste.</b> Eine handgeschriebene
/// Liste hätte genau den Einstiegspunkt enthalten, an den jemand gedacht hat.
/// Dieser Fall geht über <c>Enum.GetValues</c> und deckt damit auch den
/// Einstieg ab, den es erst morgen gibt.</para>
/// </remarks>
public sealed class FlipdatumUeberAlleEinstiegeTests : IDisposable
{
    private readonly string _temp;
    private readonly AppPaths _paths;
    private readonly GrowRepository _repository;
    private readonly GrowsApiController _controller;
    private readonly Tent _tent;

    public FlipdatumUeberAlleEinstiegeTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "Flipdatum_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _paths = new AppPaths(_temp);
        _tent = TestDatabase.InitializeWithDefaultTent(_paths);
        _repository = new GrowRepository(_paths);

        var loader = new KnowledgeBaseLoader(_paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        _controller = new GrowsApiController(
            _repository,
            new AuditRepository(_paths),
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

    private int GrowAnlegen(GrowEntryPoint einstieg, SeedType samen)
        => _repository.CreateGrow(new GrowRun
        {
            TentId = _tent.Id,
            Name = $"Lauf {einstieg}/{samen}",
            StartDate = new DateTime(2026, 5, 1),
            Status = GrowStatus.Running,
            SeedType = samen,
            EntryPoint = einstieg,
        });

    private GrowUpsertRequest Formular(GrowEntryPoint einstieg, SeedType samen, string? flip)
        => new()
        {
            Name = $"Lauf {einstieg}/{samen}",
            TentId = _tent.Id,
            StartDate = "2026-05-01",
            Status = GrowStatus.Running,
            SeedType = samen,
            EntryPoint = einstieg,
            FlipDate = flip,
        };

    /// <summary>
    /// Die Grundmenge: jeder Einstiegspunkt, den es gibt.
    /// </summary>
    public static TheoryData<GrowEntryPoint> AlleEinstiege()
    {
        var daten = new TheoryData<GrowEntryPoint>();
        foreach (var wert in Enum.GetValues<GrowEntryPoint>())
        {
            daten.Add(wert);
        }

        return daten;
    }

    /// <summary>Der Mengenwächter: sieht die Zählung ihre Grundmenge überhaupt?</summary>
    [Fact]
    public void DieZaehlungSiehtAlleEinstiegspunkte()
    {
        var einstiege = Enum.GetValues<GrowEntryPoint>();
        Assert.True(einstiege.Length >= 5,
            $"Nur {einstiege.Length} Einstiegspunkte gefunden — die Zählung läuft ins Leere.");
        Assert.Contains(GrowEntryPoint.Germination, einstiege);
        Assert.Contains(GrowEntryPoint.Veg, einstiege);
    }

    [Theory]
    [MemberData(nameof(AlleEinstiege))]
    public void EinEingetragenesFlipdatumKommtAn(GrowEntryPoint einstieg)
    {
        var growId = GrowAnlegen(einstieg, SeedType.Feminized);

        var antwort = _controller.Update(growId, Formular(einstieg, SeedType.Feminized, "2026-06-10"));
        Assert.IsType<OkObjectResult>(antwort.Result);

        var danach = _repository.GetGrow(growId)!;
        Assert.True(danach.FlipDate == new DateTime(2026, 6, 10),
            $"Einstieg {einstieg}: Flipdatum 2026-06-10 geschickt, gespeichert ist {danach.FlipDate?.ToString("yyyy-MM-dd") ?? "nichts"}.");
    }

    [Theory]
    [MemberData(nameof(AlleEinstiege))]
    public void EinGeaendertesFlipdatumKommtEbenfallsAn(GrowEntryPoint einstieg)
    {
        // Die Regel „die Reparatur einmal WIEDERHOLEN": nicht nur das erste
        // Eintragen zählt, sondern auch das Ändern eines schon gesetzten Werts.
        var growId = GrowAnlegen(einstieg, SeedType.Feminized);
        _controller.Update(growId, Formular(einstieg, SeedType.Feminized, "2026-06-10"));

        _controller.Update(growId, Formular(einstieg, SeedType.Feminized, "2026-06-24"));

        var danach = _repository.GetGrow(growId)!;
        Assert.True(danach.FlipDate == new DateTime(2026, 6, 24),
            $"Einstieg {einstieg}: zweite Änderung kam nicht an, gespeichert ist {danach.FlipDate?.ToString("yyyy-MM-dd") ?? "nichts"}.");
    }

    [Theory]
    [MemberData(nameof(AlleEinstiege))]
    public void EineAutoflowerBekommtNieEinFlipdatum(GrowEntryPoint einstieg)
    {
        // Eine Autoflower geht nach Tagen in die Blüte, nicht auf Kommando.
        var growId = GrowAnlegen(einstieg, SeedType.Autoflower);

        _controller.Update(growId, Formular(einstieg, SeedType.Autoflower, "2026-06-10"));

        var danach = _repository.GetGrow(growId)!;
        Assert.True(danach.FlipDate is null,
            $"Einstieg {einstieg}: Autoflower trägt jetzt ein Flipdatum ({danach.FlipDate}).");
    }

    [Fact]
    public void EinFehlendesFeldNimmtDemGrowSeinenFlipNicht()
    {
        // null heisst „das Feld kam nicht mit" — nicht „löschen". Sonst nimmt
        // ein fremder Aufrufer dem Grow seinen Flip, so wie es diese Klasse von
        // Fehlern schon mit den Meilensteinen getan hat.
        var growId = GrowAnlegen(GrowEntryPoint.Veg, SeedType.Feminized);
        var grow = _repository.GetGrow(growId)!;
        grow.FlipDate = new DateTime(2026, 6, 10);
        _repository.UpdateGrow(grow);

        _controller.Update(growId, Formular(GrowEntryPoint.Veg, SeedType.Feminized, null));

        Assert.Equal(new DateTime(2026, 6, 10), _repository.GetGrow(growId)!.FlipDate);
    }

    [Fact]
    public void EinGeleertesFeldLoeschtDenFlip()
    {
        // Bewahren heisst nicht festkleben: wer das Feld ausdrücklich leert,
        // schickt einen leeren Text — und der nimmt den Flip zurück.
        var growId = GrowAnlegen(GrowEntryPoint.Veg, SeedType.Feminized);
        var grow = _repository.GetGrow(growId)!;
        grow.FlipDate = new DateTime(2026, 6, 10);
        _repository.UpdateGrow(grow);

        _controller.Update(growId, Formular(GrowEntryPoint.Veg, SeedType.Feminized, string.Empty));

        Assert.Null(_repository.GetGrow(growId)!.FlipDate);
    }
}
