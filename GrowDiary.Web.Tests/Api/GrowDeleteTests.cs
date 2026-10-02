using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Einen Grow löschen darf nicht mit einem Fehler enden, wenn es geklappt hat.
/// </summary>
/// <remarks>
/// <para>Gefunden beim Aufräumen eigener Testdaten: der Aufruf meldete 500,
/// der Grow war danach trotzdem weg. Ursache war die Reihenfolge — der
/// „gelöscht"-Eintrag im Prüfprotokoll wurde NACH dem Löschen geschrieben,
/// und sein Fremdschlüssel zeigte auf eine Zeile, die es nicht mehr gab.</para>
///
/// <para>Der Schaden ist nicht der Statuscode, sondern was der Nutzer daraus
/// macht: eine Fehlermeldung für eine gelungene Handlung lädt zum zweiten
/// Versuch ein.</para>
/// </remarks>
public sealed class GrowDeleteTests : IDisposable
{
    private readonly string _temp;
    private readonly GrowRepository _repository;
    private readonly GrowsApiController _controller;
    private readonly Tent _tent;
    private readonly SystemAuditRepository _systemAudit;

    public GrowDeleteTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "GrowDelete_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        var paths = new AppPaths(_temp);
        _tent = TestDatabase.InitializeWithDefaultTent(paths);
        _repository = new GrowRepository(paths);
        _systemAudit = new SystemAuditRepository(paths);

        var loader = new KnowledgeBaseLoader(paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        _controller = new GrowsApiController(
            _repository,
            new AuditRepository(paths),
            new DeviationAnalyzerService(new TargetValueService(loader)),
            new TreatmentRecommender(loader),
            new SetupRepository(paths),
            new HydroSetupRepository(paths, new TentRepository(paths)),
            _systemAudit);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    [Fact]
    public void DeletingAGrowWithPlantsReportsSuccess()
    {
        var growId = _repository.CreateGrow(new GrowRun
        {
            TentId = _tent.Id,
            Name = "Zum Loeschen",
            StartDate = new DateTime(2026, 6, 1),
            Status = GrowStatus.Running,
        });
        _repository.CreatePlant(new PlantInstance
        {
            GrowId = growId,
            Label = "Pflanze 1",
            PlantRole = PlantRole.Production,
            PlantStatus = PlantStatus.Active,
        });

        // Vorher: 500 — der Grow verschwand, die Meldung sagte „Fehler".
        Assert.IsType<NoContentResult>(_controller.Delete(growId));
        Assert.Null(_repository.GetGrow(growId));
    }

    [Fact]
    public void DeletingAGrowWithoutPlantsStillWorks()
    {
        var growId = _repository.CreateGrow(new GrowRun
        {
            TentId = _tent.Id,
            Name = "Leer",
            StartDate = new DateTime(2026, 6, 1),
            Status = GrowStatus.Planning,
        });

        Assert.IsType<NoContentResult>(_controller.Delete(growId));
    }

    [Fact]
    public void DeletingAMissingGrowStillSaysNotFound()
    {
        var result = _controller.Delete(9999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("grow_not_found", Assert.IsType<GrowDiary.Web.Api.Contracts.ApiError>(notFound.Value).Code);
    }
    /// <summary>
    /// Das Löschen muss nachlesbar bleiben — auch nachdem der Grow weg ist.
    /// </summary>
    /// <remarks>
    /// Vorher stand der Eintrag in <c>AuditEntries</c>, und die Tabelle hängt mit
    /// <c>ON DELETE CASCADE</c> am Grow: der Nachweis verschwand im selben
    /// Augenblick wie das, was er nachweisen sollte.
    /// </remarks>
    [Fact]
    public void DasLoeschenUeberlebtImSystemProtokoll()
    {
        var growId = _repository.CreateGrow(new GrowRun
        {
            TentId = _tent.Id,
            Name = "Spurensuche 7",
            StartDate = new DateTime(2026, 6, 1),
            Status = GrowStatus.Completed,
        });

        Assert.IsType<NoContentResult>(_controller.Delete(growId));
        Assert.Null(_repository.GetGrow(growId));

        // Frisch gelesen, nicht aus dem Speicher: was zählt, steht in der Datei.
        var eintraege = _systemAudit.GetRecent(limit: 50, eventType: GrowsApiController.GrowGeloeschtProtokollTyp);
        var eintrag = Assert.Single(eintraege, e => e.Summary.Contains("Spurensuche 7", StringComparison.Ordinal));
        Assert.Equal("Grow geloescht", eintrag.Action);
        Assert.Contains($"Id {growId}", eintrag.Summary, StringComparison.Ordinal);
        Assert.True(eintrag.Success);
    }
}
