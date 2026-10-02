using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Infrastructure;

/// <summary>
/// Fork AI (02.10.2026): wer die Zeltregeln speichert, setzt den Alarmzustand
/// der unveränderten Regeln nicht zurück.
/// </summary>
/// <remarks>
/// Vorher fiel <c>LastState</c>/<c>LastNotifiedUtc</c> bei jedem Speichern für
/// ALLE Regeln auf NULL: jede gerade verletzte Regel meldete sofort ein zweites
/// Mal, und „meldet seit" begann von vorn.
/// </remarks>
public sealed class RegelZustandBleibtBeimSpeichernTests : IDisposable
{
    private readonly string _wurzel = Path.Combine(Path.GetTempPath(), "fork-zustand-" + Guid.NewGuid().ToString("N"));
    private readonly AlertRuleRepository _repo;

    public RegelZustandBleibtBeimSpeichernTests()
    {
        Directory.CreateDirectory(_wurzel);
        var pfade = new AppPaths(_wurzel);
        TestDatabase.InitializeWithDefaultTent(pfade);
        _repo = new AlertRuleRepository(pfade);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static List<TentAlertRule> Satz(double feuchteMax, double phTol) =>
    [
        new() { TentId = 1, MetricKey = "humidity", MaxValue = feuchteMax, Enabled = true },
        new() { TentId = 1, MetricKey = "temperature", MinValue = 18, MaxValue = 28, NightMinValue = 16, Enabled = true },
        new() { TentId = 1, MetricKey = "reservoir-ph", Quelle = Grenzwertquelle.Plan, Toleranz = phTol, Enabled = true },
    ];

    [Fact]
    public void UnveraenderteRegelnBehaltenIhrenZustandGeaenderteStartenFrisch()
    {
        _repo.ReplaceForTent(1, Satz(feuchteMax: 60, phTol: 0.2));
        var gemeldet = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
        foreach (var regel in _repo.GetForTent(1)) _repo.UpdateState(regel.Id, "Above", gemeldet);
        var seit = _repo.GetForTent(1).ToDictionary(r => r.MetricKey, r => r.StateChangedUtc);
        Assert.Equal(3, seit.Count);
        Assert.All(seit.Values, wert => Assert.NotNull(wert));

        // Nur die Feuchte bekommt eine neue Grenze.
        _repo.ReplaceForTent(1, Satz(feuchteMax: 65, phTol: 0.2));
        var danach = _repo.GetForTent(1).ToDictionary(r => r.MetricKey);

        foreach (var key in new[] { "temperature", "reservoir-ph" })
        {
            Assert.Equal("Above", danach[key].LastState);
            Assert.Equal(gemeldet, danach[key].LastNotifiedUtc);
            Assert.Equal(seit[key], danach[key].StateChangedUtc);
        }
        Assert.Null(danach["humidity"].LastState);
        Assert.Null(danach["humidity"].LastNotifiedUtc);

        // Zweites Mal speichern, jetzt ändert sich die Toleranz der Plan-Regel.
        _repo.ReplaceForTent(1, Satz(feuchteMax: 65, phTol: 0.3));
        danach = _repo.GetForTent(1).ToDictionary(r => r.MetricKey);
        Assert.Null(danach["reservoir-ph"].LastState);
        Assert.Equal("Above", danach["temperature"].LastState);
    }
}
