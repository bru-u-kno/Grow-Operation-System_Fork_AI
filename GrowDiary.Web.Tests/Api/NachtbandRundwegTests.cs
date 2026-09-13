using System.Reflection;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Das Nachtband übersteht das Speichern — und gilt, wenn das Licht aus ist.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (13.09.2026).</b> Ein eigenes Zielband für die
/// Dunkelphase. <c>RundwegVollstaendigTests</c> nimmt
/// <c>SaveTentAlertRulesRequest</c> ausdrücklich aus — es trägt eine Liste, kein
/// flaches Feld — und verweist dafür auf einen eigenen Fall. Das ist dieser.</para>
///
/// <para><b>Warum das hier nicht theoretisch ist.</b> Gespeichert wird über
/// <c>ReplaceForTent</c>: löschen und neu schreiben. Ein Feld, das der Vertrag
/// nicht mitführt, verschwindet damit bei jedem Speichern auf der
/// Grenzwerte-Seite — lautlos, mit HTTP 200, und der Nutzer sieht erst in der
/// nächsten Nacht, dass sein Band weg ist.</para>
/// </remarks>
public sealed class NachtbandRundwegTests : IDisposable
{
    private readonly string _wurzel;
    private readonly AlertRuleRepository _repo;

    public NachtbandRundwegTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Nachtband_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        var pfade = new AppPaths(_wurzel);
        TestDatabase.InitializeWithDefaultTent(pfade);
        _repo = new AlertRuleRepository(pfade);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { }
    }

    [Fact]
    public void NachtwerteUeberlebenDasSpeichern()
    {
        // Bewusst vier verschiedene Zahlen: fiele eine auf eine andere zurück,
        // wäre der Vergleich sonst trotzdem grün.
        _repo.ReplaceForTent(1, new[]
        {
            new TentAlertRule
            {
                TentId = 1,
                MetricKey = "temperature",
                MinValue = 22,
                MaxValue = 28,
                NightMinValue = 18,
                NightMaxValue = 24,
                NotifyService = string.Empty,
                Enabled = true,
                CooldownMinutes = 60,
            },
        });

        var gelesen = _repo.GetForTent(1).Single();

        Assert.Equal(22, gelesen.MinValue);
        Assert.Equal(28, gelesen.MaxValue);
        Assert.Equal(18, gelesen.NightMinValue);
        Assert.Equal(24, gelesen.NightMaxValue);
    }

    /// <summary>
    /// Der Vertrag muss jede Grenze kennen, die das Modell hat.
    /// </summary>
    /// <remarks>
    /// Die Reihenprüfung fährt diesen Endpunkt nicht, also fällt ein vergessenes
    /// Feld hier auf — und nicht erst beim Nutzer. Neue Grenzen kommen selten
    /// allein: Toleranz kam am 10.09., das Nachtband am 13.09.
    /// </remarks>
    [Fact]
    public void JedeGrenzeDesModellsStehtAuchImVertrag()
    {
        var vertragsfelder = typeof(AlertRuleDto)
            .GetConstructors().Single()
            .GetParameters()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var modellgrenzen = typeof(TentAlertRule)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(double?))
            .Select(p => p.Name);

        foreach (var feld in modellgrenzen)
        {
            Assert.True(vertragsfelder.Contains(feld),
                $"AlertRuleDto kennt {feld} nicht — beim Speichern ginge der Wert verloren.");
        }
    }

    [Theory]
    // Nachts gilt 18–24: 20,5 °C ist in Ordnung, tagsüber (22–28) wäre es zu kalt.
    [InlineData(LightsNow.Off, 20.5, AlertEvaluationService.InRange)]
    [InlineData(LightsNow.On, 20.5, AlertEvaluationService.Below)]
    // Und andersherum: 26 °C sind tagsüber in Ordnung, nachts zu warm.
    [InlineData(LightsNow.On, 26.0, AlertEvaluationService.InRange)]
    [InlineData(LightsNow.Off, 26.0, AlertEvaluationService.Above)]
    // Unbekannter Lichtzustand zählt als Tag — lieber ein unnötiges Tag-Urteil
    // als ein stillschweigend abgesenktes Band bei ausgefallenem Sensor.
    [InlineData(LightsNow.Unknown, 20.5, AlertEvaluationService.Below)]
    public void DasGeltendeBandHaengtAmLicht(LightsNow lichter, double wert, string erwartet)
    {
        var regel = new TentAlertRule
        {
            MetricKey = "temperature",
            MinValue = 22,
            MaxValue = 28,
            NightMinValue = 18,
            NightMaxValue = 24,
        };

        var entscheidung = AlertEvaluationService.Decide(regel, wert, DateTime.UtcNow, lichter);

        Assert.Equal(erwartet, entscheidung.NewState);
    }

    /// <summary>
    /// Halbes Nachtband: die andere Seite bleibt der Tagwert.
    /// </summary>
    /// <remarks>
    /// Als Paar gelesen wäre die Obergrenze nachts still verschwunden — 40 °C
    /// hätten dann niemanden mehr gestört, weil „für die Nacht ist ja etwas
    /// eingetragen".
    /// </remarks>
    [Fact]
    public void NurDieUntergrenzeAbgesenktLaesstDieObergrenzeStehen()
    {
        var regel = new TentAlertRule
        {
            MetricKey = "temperature",
            MinValue = 22,
            MaxValue = 28,
            NightMinValue = 18,
        };

        var (min, max) = regel.GrenzenFuer(LightsNow.Off);

        Assert.Equal(18, min);
        Assert.Equal(28, max);
    }
}
