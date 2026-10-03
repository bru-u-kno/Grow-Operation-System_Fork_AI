using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Dosierung glaubt keinem Sensorwert, den es physikalisch nicht geben kann.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (offene Punkte 03.10.2026, B7).</b>
/// <see cref="DosingContextBuilder"/> nahm den jüngsten Rohwert ungeprüft. Das
/// Nullbild der Wassersonde ist seit forkai.160 an der Quelle abgefangen —
/// eine Sonde, die pH 15 oder EC 12 meldet, nicht. Die Automatik hätte darauf
/// dosiert.</para>
/// <para>Geprüft über den echten Bauer mit echter Datenbank: genau dort liest
/// der Dosier-Takt den Messwert.</para>
/// </remarks>
public sealed class DosierungPlausibilitaetTests : IDisposable
{
    private static readonly DateTime Jetzt = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly int _zelt;
    private readonly SensorReadingRepository _rohwerte;

    public DosierungPlausibilitaetTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "DosierPlausibel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        _zelt = TestDatabase.InitializeWithDefaultTent(_pfade).Id;
        _rohwerte = new SensorReadingRepository(_pfade);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    [Theory]
    [InlineData(DosingPurpose.PhDown, "reservoir-ph", 15.0)]
    [InlineData(DosingPurpose.PhDown, "reservoir-ph", -0.5)]
    [InlineData(DosingPurpose.Nutrient, "reservoir-ec", 12.0)]
    [InlineData(DosingPurpose.Nutrient, "reservoir-ec", -1.0)]
    public void UnmoeglicherSensorwert_IstKeinMesswert(DosingPurpose zweck, string groesse, double wert)
    {
        Schreiben(groesse, wert);

        var kontext = Bauer().Build(Pumpe(zweck), Jetzt);

        Assert.True(kontext.Context.Reading is null,
            $"Die Dosierung nimmt {groesse} = {wert} als Messwert — eine Sonde, die das meldet, ist kaputt.");
        Assert.Equal(ReadingSource.None, kontext.ReadingFrom);
    }

    /// <summary>Selbsttest: ein möglicher Wert kommt an — sonst bewiese der Fall oben nichts.</summary>
    [Theory]
    [InlineData(DosingPurpose.PhDown, "reservoir-ph", 6.4)]
    [InlineData(DosingPurpose.Nutrient, "reservoir-ec", 1.7)]
    [InlineData(DosingPurpose.Nutrient, "reservoir-ec", 0.0)]   // Osmosewasser: EC 0 ist echt
    public void MoeglicherSensorwert_KommtAn(DosingPurpose zweck, string groesse, double wert)
    {
        Schreiben(groesse, wert);

        var kontext = Bauer().Build(Pumpe(zweck), Jetzt);

        Assert.Equal(wert, kontext.Context.Reading);
        Assert.Equal(ReadingSource.Sensor, kontext.ReadingFrom);
    }

    /// <summary>
    /// Mit einem Handeintrag gilt der — auch wenn er älter ist als der kaputte
    /// Sensorwert. Ob er für eine Dosis noch frisch genug ist, entscheidet danach
    /// wie immer <c>DosingGuard</c> über das Alter.
    /// </summary>
    [Fact]
    public void UnmoeglicherSensorwert_LaesstDenHandwertGelten()
    {
        var grows = new GrowRepository(_pfade);
        var grow = grows.CreateGrow(new GrowRun
        {
            Name = "Lauf", TentId = _zelt, HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running, StartDate = DateTime.Today.AddDays(-30),
        });
        grows.CreateMeasurement(new Measurement
        {
            GrowId = grow, TakenAt = Jetzt.AddMinutes(-20).ToLocalTime(), Stage = GrowStage.Flower,
            Source = ValueOrigin.Manual, ReservoirPh = 6.1,
        });
        Schreiben("reservoir-ph", 15.0);   // jünger als der Handwert

        var kontext = Bauer().Build(Pumpe(DosingPurpose.PhDown), Jetzt);

        Assert.Equal(6.1, kontext.Context.Reading);
        Assert.Equal(ReadingSource.Manual, kontext.ReadingFrom);
        Assert.Equal(TimeSpan.FromMinutes(20), kontext.Context.ReadingAge);
    }

    private void Schreiben(string groesse, double wert)
        => _rohwerte.AddReading(new TentSensorReading
        {
            TentId = _zelt, MetricKey = groesse, Value = wert, CapturedAtUtc = Jetzt.AddMinutes(-2),
        });

    private DosingPump Pumpe(DosingPurpose zweck) => new()
    {
        Id = 1,
        TentId = _zelt,
        Name = "Testpumpe",
        Purpose = zweck,
        HaEntityId = "switch.testpumpe",
        MlPerMinute = 46,
    };

    private DosingContextBuilder Bauer()
        => new(new GrowRepository(_pfade), new DosingRepository(_pfade), _rohwerte, new AlertRuleRepository(_pfade),
            new TargetValueService(new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance)),
            new HydroSetupRepository(_pfade, new TentRepository(_pfade)));
}
