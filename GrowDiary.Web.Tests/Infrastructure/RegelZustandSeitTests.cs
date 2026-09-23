using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Tests.Infrastructure;

/// <summary>Fork AI (forkai.145, F-040): „seit wann" meldet eine Regel.</summary>
public sealed class RegelZustandSeitTests : IDisposable
{
    private readonly string _wurzel = Path.Combine(Path.GetTempPath(), "fork-seit-" + Guid.NewGuid().ToString("N"));
    private readonly AlertRuleRepository _repo;

    public RegelZustandSeitTests()
    {
        Directory.CreateDirectory(_wurzel);
        var pfade = new AppPaths(_wurzel);
        TestDatabase.InitializeWithDefaultTent(pfade);
        _repo = new AlertRuleRepository(pfade);
        _repo.ReplaceForTent(1, new[] { new TentAlertRule { TentId = 1, MetricKey = "humidity", MaxValue = 55, Enabled = true } });
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void DerZeitpunktWechseltNurMitDemZustand()
    {
        var regel = _repo.GetForTent(1).Single();
        Assert.Null(regel.StateChangedUtc);

        _repo.UpdateState(regel.Id, "Above", null);
        var erst = _repo.GetForTent(1).Single().StateChangedUtc;
        Assert.NotNull(erst);

        Thread.Sleep(20);
        _repo.UpdateState(regel.Id, "Above", DateTime.UtcNow);
        Assert.Equal(erst, _repo.GetForTent(1).Single().StateChangedUtc);

        Thread.Sleep(20);
        _repo.UpdateState(regel.Id, "InRange", DateTime.UtcNow);
        Assert.NotEqual(erst, _repo.GetForTent(1).Single().StateChangedUtc);
    }
}
