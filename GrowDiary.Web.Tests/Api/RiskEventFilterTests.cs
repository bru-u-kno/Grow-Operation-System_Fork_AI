using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Filter müssen sich kombinieren lassen, nicht gegenseitig verschlucken.
/// </summary>
/// <remarks>
/// <para><b>Der Fehler.</b> Die Auswahl war eine einzige if-else-Kette, in der
/// <c>openOnly</c> ganz vorne stand. <c>?growId=4&amp;openOnly=true</c> lieferte
/// damit die offenen Risiken <i>aller</i> Grows — der Grow-Filter wurde nie
/// erreicht.</para>
///
/// <para>In der Weboberfläche fiel das nie auf, weil sie die beiden Filter nie
/// zusammen schickt. Der MCP-Server tut genau das: die eigene KI bekam auf die
/// Frage „welche Risiken hat Grow 4?" die Lage des ganzen Hauses — und konnte
/// den Unterschied nicht sehen, weil die Antwort plausibel aussah.</para>
///
/// <para>Das ist die gefährlichere Sorte Fehler: keine Fehlermeldung, kein
/// Absturz, nur eine Antwort auf eine andere Frage als die gestellte.</para>
/// </remarks>
public sealed class RiskEventFilterTests : IDisposable
{
    private readonly string _temp;
    private readonly GrowRepository _repository;
    private readonly RiskEventsApiController _controller;
    private readonly int _growA;
    private readonly int _growB;

    public RiskEventFilterTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "RiskFilter_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        var paths = new AppPaths(_temp);
        var tent = TestDatabase.InitializeWithDefaultTent(paths);
        _repository = new GrowRepository(paths);
        _controller = new RiskEventsApiController(_repository, new TaskRepository(paths), null!, null!);

        _growA = Grow(tent.Id, "Grow A");
        _growB = Grow(tent.Id, "Grow B");

        Risiko(_growA, "A-offen", RiskEventStatus.Open);
        Risiko(_growA, "A-erledigt", RiskEventStatus.Resolved);
        Risiko(_growB, "B-offen", RiskEventStatus.Open);
        Risiko(_growB, "B-offen-zwei", RiskEventStatus.Open);
    }

    private int Grow(int tentId, string name) => _repository.CreateGrow(new GrowRun
    {
        TentId = tentId,
        Name = name,
        StartDate = new DateTime(2026, 5, 1),
        Status = GrowStatus.Running,
    });

    private void Risiko(int growId, string titel, RiskEventStatus status) => _repository.CreateRiskEvent(new RiskEvent
    {
        GrowId = growId,
        Title = titel,
        Severity = RiskEventSeverity.Warning,
        Status = status,
    });

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    private IReadOnlyList<RiskEventDto> Hole(int? growId = null, bool openOnly = false)
        => (IReadOnlyList<RiskEventDto>)((OkObjectResult)_controller.List(openOnly: openOnly, growId: growId).Result!).Value!;

    /// <summary>
    /// Der Stand der Pruefung gehoert nur an OFFENE ABWEICHUNGS-Karten mit Grow:
    /// nur sie behaupten „besteht noch" aufgrund einer Messung (16.10.2026).
    /// </summary>
    [Fact]
    public void DerStandDerPruefungHaengtNurAnOffenenAbweichungsKarten()
    {
        var jetzt = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            _repository.CreateMeasurement(new Measurement
            {
                GrowId = _growA,
                TakenAt = jetzt.AddHours(-40 - 12 * i),
                Stage = GrowStage.Veg,
                Source = ValueOrigin.HomeAssistant,
            });
        }

        var abweichung = _repository.CreateRiskEvent(new RiskEvent
        {
            GrowId = _growA, Title = "EC", Source = RiskEventSource.Deviation, Status = RiskEventStatus.Open,
            DedupeKey = "deviation:grow:test:hydro.ec",
        });
        var erledigt = _repository.CreateRiskEvent(new RiskEvent
        {
            GrowId = _growA, Title = "pH", Source = RiskEventSource.Deviation, Status = RiskEventStatus.Resolved,
            DedupeKey = "deviation:grow:test:hydro.ph",
        });

        var alle = Hole(growId: _growA);

        var offene = alle.Single(r => r.Id == abweichung.Id);
        Assert.NotNull(offene.LatestMeasurementAtUtc);
        Assert.Equal(720, offene.MeasurementIntervalMinutes);
        Assert.True(offene.MeasurementStale);   // 40 h > 18 h
        Assert.Null(offene.NextMeasurementExpectedAtUtc);   // überfällig: keine „nächste Messung"

        // Von Hand angelegt, erledigt oder ohne Grow: kein Stand.
        Assert.All(alle.Where(r => r.Id != abweichung.Id), r =>
        {
            Assert.Null(r.LatestMeasurementAtUtc);
            Assert.False(r.MeasurementStale);
        });
        Assert.Null(alle.Single(r => r.Id == erledigt.Id).LatestMeasurementAtUtc);
    }

    [Fact]
    public void GrowFilterAndOpenOnlyWorkTogether()
    {
        var nurA = Hole(growId: _growA, openOnly: true);

        // Genau ein Ereignis: A hat eins offen und eins erledigt.
        Assert.Single(nurA);
        Assert.Equal("A-offen", nurA[0].Title);
    }

    [Fact]
    public void TheGrowFilterAloneKeepsResolvedOnes()
    {
        Assert.Equal(2, Hole(growId: _growA).Count);
    }

    [Fact]
    public void OpenOnlyWithoutAGrowStillSpansEverything()
    {
        // Diese Form benutzt die Aufgaben-Seite — sie soll alles sehen.
        Assert.Equal(3, Hole(openOnly: true).Count);
    }

    [Fact]
    public void OneGrowsRisksNeverAppearUnderAnother()
    {
        var nurB = Hole(growId: _growB, openOnly: true);

        Assert.Equal(2, nurB.Count);
        Assert.DoesNotContain(nurB, r => r.Title.StartsWith("A-", StringComparison.Ordinal));
    }
}
