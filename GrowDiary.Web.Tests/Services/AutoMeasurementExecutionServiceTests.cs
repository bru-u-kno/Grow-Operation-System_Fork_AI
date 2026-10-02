using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

public sealed class AutoMeasurementExecutionServiceTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly AppPaths _paths;
    private readonly GrowRepository _repository;
    private readonly SensorReadingRepository _sensorReadings;
    private readonly AutoMeasurementExecutionService _service;

    public AutoMeasurementExecutionServiceTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"grow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        _paths = new AppPaths(_contentRoot);
        GrowDiary.Web.Tests.TestDatabase.InitializeWithDefaultTent(_paths);
        _repository = new GrowRepository(_paths);
        _sensorReadings = new SensorReadingRepository(_paths);
        _service = new AutoMeasurementExecutionService(_repository, _sensorReadings, new AutoMeasurementValueGuard());
    }

    public void Dispose()
    {
        try { Directory.Delete(_contentRoot, recursive: true); } catch { }
    }

    [Fact]
    public void ExecuteDue_CreatesOneMeasurementForLightOnDelay()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature", delayMinutes: 5);
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "temperature", 24.2, occurredAt.AddMinutes(4));

        _service.ExecuteDue(occurredAt.AddMinutes(5));

        var measurement = Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Equal(ValueOrigin.HomeAssistant, measurement.Source);
        Assert.Equal(24.2, measurement.AirTemperatureC);
        var run = Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
        Assert.Equal(AutoMeasurementRunStatus.Created, run.Status);
        Assert.Equal(measurement.Id, run.MeasurementId);
    }

    [Fact]
    public void ExecuteDue_CreatesOneMeasurementForLightOffDelay()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOffDelay, AutoMeasurementField.HumidityPercent, "humidity");
        var occurredAt = Utc(2026, 5, 7, 20, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOff, occurredAt);
        AddReading(context.TentId, "humidity", 58, occurredAt);

        _service.ExecuteDue(occurredAt);

        Assert.Equal(58, Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId)).HumidityPercent);
        Assert.Equal(AutoMeasurementRunStatus.Created, Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId)).Status);
    }

    [Fact]
    public void ExecuteDue_RepeatedRunDoesNotCreateDuplicateMeasurement()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.ReservoirPh, "reservoir-ph");
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "reservoir-ph", 5.8, occurredAt);

        _service.ExecuteDue(occurredAt);
        _service.ExecuteDue(occurredAt.AddMinutes(10));

        Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
    }

    [Fact]
    public void ExecuteDue_RespectsDelayMinutes()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.ReservoirEc, "reservoir-ec", delayMinutes: 15);
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "reservoir-ec", 1.4, occurredAt.AddMinutes(10));

        _service.ExecuteDue(occurredAt.AddMinutes(14));
        Assert.Empty(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Empty(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));

        _service.ExecuteDue(occurredAt.AddMinutes(15));
        Assert.Equal(1.4, Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId)).ReservoirEc);
    }

    [Fact]
    public void ExecuteDue_RequiredMappingWithoutValueSkipsRunAndCreatesNoMeasurement()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.ReservoirWaterTempC, "reservoir-temp", isRequired: true);
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);

        _service.ExecuteDue(occurredAt);

        Assert.Empty(_repository.GetMeasurementsForGrow(context.GrowId));
        var run = Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
        Assert.Equal(AutoMeasurementRunStatus.Skipped, run.Status);
        Assert.Contains("reservoir-temp", run.ErrorMessage);
    }

    [Fact]
    public void ExecuteDue_OptionalMappingWithoutValueDoesNotBlockCreatedMeasurement()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.ReservoirLevelLiters, "reservoir-level");
        _repository.ReplaceAutoMeasurementFieldMappings(context.ConfigId, new[]
        {
            new AutoMeasurementFieldMapping
            {
                MeasurementField = AutoMeasurementField.ReservoirLevelLiters,
                MetricKey = "reservoir-level",
                Aggregation = AutoMeasurementAggregation.Latest,
                IsRequired = true
            },
            new AutoMeasurementFieldMapping
            {
                MeasurementField = AutoMeasurementField.Co2Ppm,
                MetricKey = "co2",
                Aggregation = AutoMeasurementAggregation.Latest,
                IsRequired = false
            }
        });
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "reservoir-level", 42, occurredAt);

        _service.ExecuteDue(occurredAt);

        var measurement = Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Equal(42, measurement.ReservoirLevelLiters);
        Assert.Null(measurement.Co2Ppm);
        Assert.Equal(AutoMeasurementRunStatus.Created, Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId)).Status);
    }

    [Fact]
    public void ExecuteDue_SkipsWhenNoMappedFieldCanBeSet()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.PpfdMol, "ppfd", isRequired: false);
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);

        _service.ExecuteDue(occurredAt);

        Assert.Empty(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Equal(AutoMeasurementRunStatus.Skipped, Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId)).Status);
    }

    [Fact]
    public void ExecuteDue_LatestUsesLastValueInsideWindow()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.OrpMv, "orp", aggregation: AutoMeasurementAggregation.Latest);
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "orp", 280, occurredAt.AddMinutes(-2));
        AddReading(context.TentId, "orp", 300, occurredAt);

        _service.ExecuteDue(occurredAt);

        Assert.Equal(300, Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId)).OrpMv);
    }

    [Fact]
    public void ExecuteDue_AverageCalculatesMean()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.DissolvedOxygenMgL, "dissolved-oxygen", aggregation: AutoMeasurementAggregation.Average);
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "dissolved-oxygen", 7, occurredAt.AddMinutes(-1));
        AddReading(context.TentId, "dissolved-oxygen", 9, occurredAt);

        _service.ExecuteDue(occurredAt);

        Assert.Equal(8, Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId)).DissolvedOxygenMgL);
    }

    [Fact]
    public void ExecuteDue_MedianCalculatesMedian()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.PpfdMol, "ppfd", aggregation: AutoMeasurementAggregation.Median);
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "ppfd", 500, occurredAt.AddMinutes(-2));
        AddReading(context.TentId, "ppfd", 700, occurredAt.AddMinutes(-1));
        AddReading(context.TentId, "ppfd", 900, occurredAt);

        _service.ExecuteDue(occurredAt);

        Assert.Equal(700, Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId)).PpfdMol);
    }

    [Fact]
    public void ExecuteDue_NoTentSkipsRunAndCreatesNoMeasurement()
    {
        var growId = _repository.CreateGrow(new GrowRun { TentId = null, Name = "No Tent", StartDate = new DateTime(2026, 5, 1), Status = GrowStatus.Running });
        var config = _repository.CreateAutoMeasurementConfig(new AutoMeasurementConfig
        {
            GrowId = growId,
            TentId = null,
            Name = "No Tent Config",
            Status = AutoMeasurementStatus.Enabled,
            TriggerKind = AutoMeasurementTriggerKind.LightOnDelay,
            DelayMinutes = 0,
            WindowMinutes = 20
        });

        _service.ExecuteDue(Utc(2026, 5, 7, 8, 0));

        Assert.Empty(_repository.GetMeasurementsForGrow(growId));
        var run = Assert.Single(_repository.GetAutoMeasurementRunsByConfig(config.Id));
        Assert.Equal(AutoMeasurementRunStatus.Skipped, run.Status);
        Assert.Contains("Tent", run.ErrorMessage);
    }

    [Fact]
    public void ExecuteDue_DisabledConfigIsIgnored()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature", status: AutoMeasurementStatus.Disabled);
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "temperature", 24, occurredAt);

        _service.ExecuteDue(occurredAt);

        Assert.Empty(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Empty(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
    }

    [Fact]
    public void ExecuteDue_ManualConfigIsIgnored()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.Manual, AutoMeasurementField.AirTemperatureC, "temperature");
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "temperature", 24, occurredAt);

        _service.ExecuteDue(occurredAt);

        Assert.Empty(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Empty(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
    }

    [Fact]
    public void ExecuteDue_RequiredRejectedValueSkipsRunAndCreatesNoMeasurement()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.ReservoirPh, "reservoir-ph");
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "reservoir-ph", 0, occurredAt);

        _service.ExecuteDue(occurredAt);

        Assert.Empty(_repository.GetMeasurementsForGrow(context.GrowId));
        var run = Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
        Assert.Equal(AutoMeasurementRunStatus.Skipped, run.Status);
        Assert.Contains(nameof(AutoMeasurementField.ReservoirPh), run.ErrorMessage);
        Assert.Contains("0", run.ErrorMessage);
    }

    [Fact]
    public void ExecuteDue_OptionalRejectedValueIsOmittedWhenAnotherFieldIsValid()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature");
        _repository.ReplaceAutoMeasurementFieldMappings(context.ConfigId, new[]
        {
            new AutoMeasurementFieldMapping
            {
                MeasurementField = AutoMeasurementField.AirTemperatureC,
                MetricKey = "temperature",
                Aggregation = AutoMeasurementAggregation.Latest,
                IsRequired = true
            },
            new AutoMeasurementFieldMapping
            {
                MeasurementField = AutoMeasurementField.ReservoirPh,
                MetricKey = "reservoir-ph",
                Aggregation = AutoMeasurementAggregation.Latest,
                IsRequired = false
            }
        });
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "temperature", 24, occurredAt);
        AddReading(context.TentId, "reservoir-ph", 0, occurredAt);

        _service.ExecuteDue(occurredAt);

        var measurement = Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Equal(24, measurement.AirTemperatureC);
        Assert.Null(measurement.ReservoirPh);
        var run = Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
        Assert.Equal(AutoMeasurementRunStatus.Created, run.Status);
        Assert.Contains(nameof(AutoMeasurementField.ReservoirPh), run.ErrorMessage);
    }

    [Fact]
    public void ExecuteDue_WarningValueCreatesMeasurementAndStoresWarning()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.DissolvedOxygenMgL, "dissolved-oxygen");
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "dissolved-oxygen", 2, occurredAt);

        _service.ExecuteDue(occurredAt);

        var measurement = Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Equal(2, measurement.DissolvedOxygenMgL);
        var run = Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
        Assert.Equal(AutoMeasurementRunStatus.Created, run.Status);
        Assert.Contains("Warnung", run.ErrorMessage);
        Assert.Contains(nameof(AutoMeasurementField.DissolvedOxygenMgL), run.ErrorMessage);
    }

    [Fact]
    public void ExecuteDue_AllValuesRejectedOrMissingSkipsRunAndCreatesNoMeasurement()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.ReservoirPh, "reservoir-ph", isRequired: false);
        _repository.ReplaceAutoMeasurementFieldMappings(context.ConfigId, new[]
        {
            new AutoMeasurementFieldMapping
            {
                MeasurementField = AutoMeasurementField.ReservoirPh,
                MetricKey = "reservoir-ph",
                Aggregation = AutoMeasurementAggregation.Latest,
                IsRequired = false
            },
            new AutoMeasurementFieldMapping
            {
                MeasurementField = AutoMeasurementField.Co2Ppm,
                MetricKey = "co2",
                Aggregation = AutoMeasurementAggregation.Latest,
                IsRequired = false
            }
        });
        var occurredAt = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, occurredAt);
        AddReading(context.TentId, "reservoir-ph", 0, occurredAt);

        _service.ExecuteDue(occurredAt);

        Assert.Empty(_repository.GetMeasurementsForGrow(context.GrowId));
        var run = Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
        Assert.Equal(AutoMeasurementRunStatus.Skipped, run.Status);
        Assert.Contains(nameof(AutoMeasurementField.ReservoirPh), run.ErrorMessage);
        Assert.Contains("Keine", run.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // Datenluecken (02.10.2026): was die Vorlage NICHT tun darf.
    // ------------------------------------------------------------------

    [Fact]
    public void ExecuteDue_NeueVorlageMisstKeineLichtwechselVorIhrerAnlage()
    {
        // Angelegt am 07.05. um 12 Uhr. Die Lichtwechsel der Tage davor
        // gehoeren nicht ihr — vorher bekam jeder eine Messung, mit den
        // Sensorwerten von damals und dem Stadium von heute.
        var angelegt = Utc(2026, 5, 7, 12, 0);
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature", angelegtUtc: angelegt);
        foreach (var tagZurueck in new[] { 2, 1 })
        {
            var alt = Utc(2026, 5, 7, 8, 0).AddDays(-tagZurueck);
            AddTransition(context.TentId, LightTransitionKind.LightOn, alt);
            AddReading(context.TentId, "temperature", 20 + tagZurueck, alt);
        }
        // Heute frueh, aber VOR der Anlage — auch nicht.
        AddTransition(context.TentId, LightTransitionKind.LightOn, Utc(2026, 5, 7, 8, 0));
        AddReading(context.TentId, "temperature", 23.5, Utc(2026, 5, 7, 8, 0));
        // Nach der Anlage: der einzige Lauf, den sie machen soll.
        var neu = Utc(2026, 5, 7, 14, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, neu);
        AddReading(context.TentId, "temperature", 25.1, neu);

        _service.ExecuteDue(neu.AddMinutes(1));

        var messung = Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Equal(25.1, messung.AirTemperatureC);
        var lauf = Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
        Assert.Equal(neu, lauf.ScheduledForUtc);
    }

    [Fact]
    public void ExecuteDue_GeaenderteVerzoegerungMisstNichtsDoppelt()
    {
        // Gegen die Wanduhr, weil UpdateAutoMeasurementConfig mit ihr stempelt.
        var jetzt = DateTime.UtcNow;
        var lichtAn = jetzt.AddHours(-3);
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature",
            delayMinutes: 0, angelegtUtc: jetzt.AddHours(-6));
        AddTransition(context.TentId, LightTransitionKind.LightOn, lichtAn);
        AddReading(context.TentId, "temperature", 24, lichtAn);
        AddReading(context.TentId, "temperature", 24.5, lichtAn.AddMinutes(14));

        _service.ExecuteDue(jetzt);
        Assert.True(_repository.GetMeasurementsForGrow(context.GrowId).Count >= 1, "Mengenwaechter: der erste Lauf muss messen.");

        // Verzoegerung 0 → 15 Minuten. Der Lichtwechsel ist derselbe, der
        // Schluessel ScheduledForUtc ein anderer — vorher: zweite Messung.
        var vorlage = _repository.GetAutoMeasurementConfig(context.ConfigId)!;
        vorlage.DelayMinutes = 15;
        _repository.UpdateAutoMeasurementConfig(vorlage);

        _service.ExecuteDue(DateTime.UtcNow.AddMinutes(1));

        Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
    }

    [Fact]
    public void ExecuteDue_HoltHoechstensEinenTagNach()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature");
        var jetzt = Utc(2026, 5, 10, 12, 0);
        var vorDreiTagen = jetzt.AddDays(-3);
        var vorZweiStunden = jetzt.AddHours(-2);
        foreach (var zeit in new[] { vorDreiTagen, vorZweiStunden })
        {
            AddTransition(context.TentId, LightTransitionKind.LightOn, zeit);
            AddReading(context.TentId, "temperature", 24, zeit);
        }

        _service.ExecuteDue(jetzt);

        var lauf = Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
        Assert.Equal(vorZweiStunden, lauf.ScheduledForUtc);
        Assert.True(jetzt - vorDreiTagen > AutoMeasurementExecutionService.Nachholfenster,
            "Der alte Lichtwechsel muss ausserhalb des Fensters liegen, sonst prueft der Fall nichts.");
    }

    [Fact]
    public void ExecuteDue_BildNurZurAngelegtenMessung()
    {
        // Pflichtwert fehlt → Lauf uebersprungen → kein Bild. Vorher hing an
        // jedem Lauf eins, auch an einem, der gar keine Messung hatte.
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature",
            captureSnapshot: true);
        var lichtAn = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, lichtAn);

        var bilder = _service.ExecuteDue(lichtAn.AddMinutes(1));

        Assert.Equal(AutoMeasurementRunStatus.Skipped, Assert.Single(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId)).Status);
        Assert.Empty(bilder);
    }

    [Fact]
    public void ExecuteDue_BildZurFrischenMessungAberNichtZurNachgeholten()
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature",
            captureSnapshot: true);
        var heuteFrueh = Utc(2026, 5, 7, 8, 0);
        var eben = Utc(2026, 5, 7, 20, 0);
        foreach (var zeit in new[] { heuteFrueh, eben })
        {
            AddTransition(context.TentId, LightTransitionKind.LightOn, zeit);
            AddReading(context.TentId, "temperature", 24, zeit);
        }

        var bilder = _service.ExecuteDue(eben.AddMinutes(2));

        // Beide Messungen entstehen — nachgeholt wird weiter.
        Assert.Equal(2, _repository.GetMeasurementsForGrow(context.GrowId).Count);
        // Das Bild von jetzt gehoert nur zu der von eben.
        var bild = Assert.Single(bilder);
        Assert.Equal(eben, bild.ScheduledForUtc);
    }

    [Theory]
    [InlineData(GrowStatus.Completed)]
    [InlineData(GrowStatus.Aborted)]
    public void ExecuteDue_ArchivierterGrowBekommtKeineMessungenMehr(GrowStatus status)
    {
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature",
            growStatus: status);
        var lichtAn = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, lichtAn);
        AddReading(context.TentId, "temperature", 24, lichtAn);

        _service.ExecuteDue(lichtAn.AddMinutes(1));

        Assert.Empty(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Empty(_repository.GetAutoMeasurementRunsByConfig(context.ConfigId));
    }

    [Fact]
    public void ExecuteDue_NachArchivierenMisstNurNochDerNachfolger()
    {
        // Derselbe Ablauf wie im Betrieb: Grow laeuft, misst, wird archiviert;
        // im selben Zelt startet der naechste mit eigener Vorlage.
        var alt = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature");
        var tag1 = Utc(2026, 5, 7, 8, 0);
        AddTransition(alt.TentId, LightTransitionKind.LightOn, tag1);
        AddReading(alt.TentId, "temperature", 24, tag1);
        _service.ExecuteDue(tag1.AddMinutes(1));
        Assert.Single(_repository.GetMeasurementsForGrow(alt.GrowId));

        var grow = _repository.GetGrow(alt.GrowId)!;
        grow.Status = GrowStatus.Completed;
        _repository.UpdateGrow(grow);
        var nachfolger = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature",
            angelegtUtc: tag1.AddHours(1));

        var tag2 = tag1.AddDays(1);
        AddTransition(alt.TentId, LightTransitionKind.LightOn, tag2);
        AddReading(alt.TentId, "temperature", 26, tag2);
        _service.ExecuteDue(tag2.AddMinutes(1));

        Assert.Single(_repository.GetMeasurementsForGrow(alt.GrowId));
        Assert.Equal(26, Assert.Single(_repository.GetMeasurementsForGrow(nachfolger.GrowId)).AirTemperatureC);
    }

    [Fact]
    public void ExecuteDue_StadiumIstDasDesMesstags()
    {
        // Geflippt am 01.06. Ein Lichtwechsel vom 07.05. gehoert in die Zeit
        // davor, auch wenn der Durchgang erst heute laeuft.
        var context = CreateContext(AutoMeasurementTriggerKind.LightOnDelay, AutoMeasurementField.AirTemperatureC, "temperature");
        var grow = _repository.GetGrow(context.GrowId)!;
        grow.FlipDate = new DateTime(2026, 6, 1);
        _repository.UpdateGrow(grow);
        var lichtAn = Utc(2026, 5, 7, 8, 0);
        AddTransition(context.TentId, LightTransitionKind.LightOn, lichtAn);
        AddReading(context.TentId, "temperature", 24, lichtAn);

        _service.ExecuteDue(lichtAn.AddMinutes(1));

        var messung = Assert.Single(_repository.GetMeasurementsForGrow(context.GrowId));
        Assert.Equal(GrowStageResolver.Resolve(_repository.GetGrow(context.GrowId)!, lichtAn.ToLocalTime().Date), messung.Stage);
        Assert.NotEqual(GrowStage.Flower, messung.Stage);
    }

    private TestContext CreateContext(
        AutoMeasurementTriggerKind triggerKind,
        AutoMeasurementField field,
        string metricKey,
        int? delayMinutes = 0,
        int windowMinutes = 20,
        bool isRequired = true,
        AutoMeasurementAggregation aggregation = AutoMeasurementAggregation.Latest,
        AutoMeasurementStatus status = AutoMeasurementStatus.Enabled,
        GrowStatus growStatus = GrowStatus.Running,
        bool captureSnapshot = false,
        DateTime? angelegtUtc = null)
    {
        var tent = _repository.GetTents().Single();
        var growId = _repository.CreateGrow(new GrowRun
        {
            TentId = tent.Id,
            Name = $"Auto {Guid.NewGuid():N}",
            StartDate = new DateTime(2026, 5, 1),
            Status = growStatus
        });
        var config = _repository.CreateAutoMeasurementConfig(new AutoMeasurementConfig
        {
            GrowId = growId,
            TentId = tent.Id,
            Name = "Auto",
            Status = status,
            TriggerKind = triggerKind,
            DelayMinutes = delayMinutes,
            WindowMinutes = windowMinutes,
            CaptureSnapshot = captureSnapshot
        });
        // Die Vorlage gilt erst ab ihrer Anlage. Die Faelle hier spielen im
        // Mai 2026 — angelegt wird sie deshalb davor, sonst laege jeder
        // Lichtwechsel vor ihr und wuerde zu Recht nicht gemessen.
        Rueckdatieren(config.Id, angelegtUtc ?? Utc(2026, 5, 1, 0, 0));
        _repository.ReplaceAutoMeasurementFieldMappings(config.Id, new[]
        {
            new AutoMeasurementFieldMapping
            {
                MeasurementField = field,
                MetricKey = metricKey,
                Aggregation = aggregation,
                IsRequired = isRequired
            }
        });

        return new TestContext(tent.Id, growId, config.Id);
    }

    /// <summary>Anlage und letzte Aenderung einer Vorlage auf einen Zeitpunkt setzen.</summary>
    /// <remarks>Das Repository stempelt beides mit der Wanduhr; der Fall braucht einen festen Zeitpunkt.</remarks>
    private void Rueckdatieren(int configId, DateTime utc)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AutoMeasurementConfigs SET CreatedAtUtc = $t, UpdatedAtUtc = $t WHERE Id = $id;";
        command.Parameters.AddWithValue("$t", utc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", configId);
        Assert.Equal(1, command.ExecuteNonQuery());
        Assert.Equal(utc.ToUniversalTime(), _repository.GetAutoMeasurementConfig(configId)!.UpdatedAtUtc);
    }

    private void AddTransition(int tentId, LightTransitionKind kind, DateTime occurredAtUtc)
    {
        _repository.CreateLightTransitionIfNotDuplicate(new LightTransitionEvent
        {
            TentId = tentId,
            Kind = kind,
            OccurredAtUtc = occurredAtUtc,
            Source = LightSource.HomeAssistant,
            RawState = kind == LightTransitionKind.LightOn ? "on" : "off"
        });
    }

    private void AddReading(int tentId, string metricKey, double value, DateTime capturedAtUtc)
    {
        _sensorReadings.AddReading(new TentSensorReading
        {
            TentId = tentId,
            MetricKey = metricKey,
            Value = value,
            CapturedAtUtc = capturedAtUtc
        });
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private sealed record TestContext(int TentId, int GrowId, int ConfigId);
}
