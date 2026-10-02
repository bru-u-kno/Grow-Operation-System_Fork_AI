using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>A tent snapshot that a fired auto-measurement trigger asked to capture.</summary>
public sealed record AutoSnapshotRequest(int GrowId, int TentId, DateTime ScheduledForUtc);

public sealed class AutoMeasurementExecutionService
{
    private static readonly DateTime MissingTentRunScheduleUtc = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Wie weit ein Lauf hoechstens nachgeholt wird — gemessen am geplanten
    /// Zeitpunkt (Lichtwechsel plus Verzoegerung).
    /// </summary>
    /// <remarks>
    /// <para>Vorher wurde bei JEDEM Lauf die ganze Licht-Historie des Zelts
    /// gelesen (<c>DateTime.MinValue</c>). Eine neu angelegte Vorlage legte
    /// dadurch rueckwirkend eine Messung fuer jeden Lichtwechsel seit
    /// Inbetriebnahme an, und jede Aenderung von <c>DelayMinutes</c> verschob
    /// den Schluessel <c>ScheduledForUtc</c> — alles noch einmal.</para>
    ///
    /// <para>Ein Tag reicht, um einen Neustart oder eine Nacht ohne Home
    /// Assistant zu ueberbruecken; die Verzoegerung darf selbst bis zu 1440
    /// Minuten betragen (<c>AutoMeasurementsApiController.ValidateConfig</c>).</para>
    /// </remarks>
    public static readonly TimeSpan Nachholfenster = TimeSpan.FromHours(24);

    /// <summary>
    /// Wie alt ein Lauf hoechstens sein darf, damit ihm noch ein Kamerabild
    /// angehaengt wird.
    /// </summary>
    /// <remarks>
    /// Das Bild wird JETZT aufgenommen. An einen nachgeholten Lauf von heute
    /// frueh gehaengt, zeigte es das Zelt zur falschen Zeit — womoeglich bei
    /// ausgeschaltetem Licht. Der Hintergrunddienst laeuft alle fuenf Minuten
    /// (<see cref="AutoMeasurementWorker"/>); eine Viertelstunde laesst Luft
    /// fuer einen verspaeteten Durchgang.
    /// </remarks>
    public static readonly TimeSpan BildFrische = TimeSpan.FromMinutes(15);

    private readonly GrowRepository _repository;
    private readonly SensorReadingRepository _sensorReadings;
    private readonly AutoMeasurementValueGuard _valueGuard;

    public AutoMeasurementExecutionService(
        GrowRepository repository,
        SensorReadingRepository sensorReadings,
        AutoMeasurementValueGuard valueGuard)
    {
        _repository = repository;
        _sensorReadings = sensorReadings;
        _valueGuard = valueGuard;
    }

    public IReadOnlyList<AutoSnapshotRequest> ExecuteDue(DateTime nowUtc)
    {
        nowUtc = nowUtc.ToUniversalTime();
        var snapshotRequests = new List<AutoSnapshotRequest>();

        foreach (var config in _repository.GetEnabledAutoMeasurementConfigs())
        {
            if (config.TriggerKind == AutoMeasurementTriggerKind.Manual)
            {
                continue;
            }

            var grow = _repository.GetGrow(config.GrowId);

            // Nur laufende und geplante Grows. Archivieren und Ernten schalten
            // die Vorlage nicht ab — vorher bekam ein abgeschlossener Grow
            // weiter Messungen, gelesen aus den Sensoren seines Nachfolgers im
            // selben Zelt.
            if (grow is null || grow.IsArchived)
            {
                continue;
            }

            var tentId = config.TentId ?? grow?.TentId;
            if (!tentId.HasValue)
            {
                if (TryCreateMissingTentRun(config) is { } missingTentRun)
                {
                    MarkSkipped(missingTentRun, "Kein Tent fuer AutoMeasurementConfig ermittelbar.");
                }
                continue;
            }

            var transitionKind = ToTransitionKind(config.TriggerKind);
            if (!transitionKind.HasValue)
            {
                continue;
            }

            var delay = TimeSpan.FromMinutes(config.DelayMinutes ?? 0);
            var fruehesterLauf = FruehesterLauf(config, nowUtc);
            var transitions = _repository.GetLightTransitionsByTentAndKindSince(
                tentId.Value,
                transitionKind.Value,
                fruehesterLauf - delay);

            foreach (var transition in transitions)
            {
                var scheduledForUtc = transition.OccurredAtUtc
                    .ToUniversalTime()
                    .Add(delay);

                if (scheduledForUtc > nowUtc || scheduledForUtc < fruehesterLauf)
                {
                    continue;
                }

                if (_repository.GetAutoMeasurementRun(config.Id, config.TriggerKind, scheduledForUtc) is not null)
                {
                    continue;
                }

                var run = _repository.CreateAutoMeasurementRunIfNotExists(new AutoMeasurementRun
                {
                    ConfigId = config.Id,
                    GrowId = config.GrowId,
                    TriggerKind = config.TriggerKind,
                    ScheduledForUtc = scheduledForUtc,
                    Status = AutoMeasurementRunStatus.Pending
                });

                ProcessRun(config, grow, tentId.Value, scheduledForUtc, run);

                // Das Bild nur zu einer Messung, die es wirklich gibt, und nur,
                // solange „jetzt" noch der geplante Zeitpunkt ist.
                if (config.CaptureSnapshot
                    && run.Status == AutoMeasurementRunStatus.Created
                    && nowUtc - scheduledForUtc <= BildFrische)
                {
                    snapshotRequests.Add(new AutoSnapshotRequest(config.GrowId, tentId.Value, scheduledForUtc));
                }
            }
        }

        return snapshotRequests;
    }

    /// <summary>
    /// Der frueheste geplante Zeitpunkt, den diese Vorlage noch abarbeitet.
    /// </summary>
    /// <remarks>
    /// <para><b>Nicht vor der letzten Aenderung der Vorlage.</b> Wer sie heute
    /// anlegt, will ab jetzt messen — nicht fuer jeden Lichtwechsel der
    /// letzten Tage eine Zeile mit den Werten von damals. Und wer
    /// <c>DelayMinutes</c> aendert, verschiebt jeden geplanten Zeitpunkt; ohne
    /// diese Grenze waere jeder alte Lichtwechsel unter dem neuen Schluessel
    /// ein zweites Mal gemessen worden.</para>
    ///
    /// <para><b>Nicht weiter zurueck als <see cref="Nachholfenster"/>.</b>
    /// Damit bleibt auch die Abfrage begrenzt, statt mit jedem Tag Betrieb zu
    /// wachsen.</para>
    /// </remarks>
    private static DateTime FruehesterLauf(AutoMeasurementConfig config, DateTime nowUtc)
    {
        var geaendert = config.UpdatedAtUtc.ToUniversalTime();
        var fenster = nowUtc.ToUniversalTime() - Nachholfenster;
        return geaendert > fenster ? geaendert : fenster;
    }

    private AutoMeasurementRun? TryCreateMissingTentRun(AutoMeasurementConfig config)
    {
        if (_repository.GetAutoMeasurementRun(config.Id, config.TriggerKind, MissingTentRunScheduleUtc) is not null)
        {
            return null;
        }

        return _repository.CreateAutoMeasurementRunIfNotExists(new AutoMeasurementRun
        {
            ConfigId = config.Id,
            GrowId = config.GrowId,
            TriggerKind = config.TriggerKind,
            ScheduledForUtc = MissingTentRunScheduleUtc,
            Status = AutoMeasurementRunStatus.Pending
        });
    }

    private void ProcessRun(
        AutoMeasurementConfig config,
        GrowRun? grow,
        int tentId,
        DateTime scheduledForUtc,
        AutoMeasurementRun run)
    {
        try
        {
            var mappings = _repository.GetAutoMeasurementFieldMappings(config.Id);
            var measurement = new Measurement
            {
                GrowId = config.GrowId,
                // TakenAt ist eine Ortszeit-Spalte (AssumeLocal beim Lesen).
                // Der UTC-Wandwert unkonvertiert haette jede Auto-Messung um
                // den Zeitzonen-Versatz rueckdatiert — Tageszaehler und
                // SOP-Faelligkeit haetten sie dem falschen Tag zugerechnet.
                TakenAt = scheduledForUtc.ToLocalTime(),
                // Die Phase von heute. Vorher wurde die der LETZTEN Messung
                // abgeschrieben: einmal falsch gestempelt — oder schlicht nach
                // einem Flip ohne Handmessung —, und ab da trug jede weitere
                // automatische Zeile denselben veralteten Wert weiter.
                //
                // „Heute" ist dabei der Tag der Messung, nicht der des
                // Durchgangs: ein nachgeholter Lauf von gestern Abend gehoert
                // in die Phase von gestern.
                Stage = grow is null ? GrowStage.Veg : GrowStageResolver.Resolve(grow, scheduledForUtc.ToLocalTime().Date),
                Source = ValueOrigin.HomeAssistant,
                Notes = $"AutoMeasurement {config.TriggerKind}"
            };

            var anyValueSet = false;
            var runMessages = new List<string>();
            foreach (var mapping in mappings)
            {
                var readings = _sensorReadings.GetReadings(
                    tentId,
                    mapping.MetricKey,
                    scheduledForUtc.AddMinutes(-config.WindowMinutes),
                    scheduledForUtc);

                if (readings.Count == 0)
                {
                    if (mapping.IsRequired)
                    {
                        MarkSkipped(run, $"Pflicht-Metrik '{mapping.MetricKey}' hat keine Sensorwerte im Zeitfenster.");
                        return;
                    }

                    continue;
                }

                var value = Aggregate(readings, mapping.Aggregation);
                var guardResult = _valueGuard.Check(mapping.MeasurementField, value);
                if (guardResult.Severity == AutoMeasurementValueSeverity.Reject)
                {
                    var message = guardResult.Message ?? $"{mapping.MeasurementField} Wert {value} wurde verworfen.";
                    if (mapping.IsRequired)
                    {
                        MarkSkipped(run, $"Pflichtfeld abgelehnt: {message}");
                        return;
                    }

                    runMessages.Add($"Optionaler Wert verworfen: {message}");
                    continue;
                }

                if (guardResult.Severity == AutoMeasurementValueSeverity.Warning && !string.IsNullOrWhiteSpace(guardResult.Message))
                {
                    runMessages.Add($"Warnung: {guardResult.Message}");
                }

                if (ApplyValue(measurement, mapping.MeasurementField, value))
                {
                    anyValueSet = true;
                }
            }

            if (!anyValueSet)
            {
                var message = runMessages.Count > 0
                    ? $"Keine AutoMeasurement-Felder konnten gesetzt werden. {string.Join(" | ", runMessages)}"
                    : "Keine AutoMeasurement-Felder konnten aus Sensorwerten gesetzt werden.";
                MarkSkipped(run, message);
                return;
            }

            var measurementId = _repository.CreateMeasurement(measurement);
            run.MeasurementId = measurementId;
            run.Status = AutoMeasurementRunStatus.Created;
            run.ErrorMessage = runMessages.Count > 0 ? string.Join(" | ", runMessages) : null;
            _repository.UpdateAutoMeasurementRun(run);
        }
        catch (Exception ex)
        {
            run.Status = AutoMeasurementRunStatus.Failed;
            run.ErrorMessage = ex.Message;
            _repository.UpdateAutoMeasurementRun(run);
        }
    }

    private static LightTransitionKind? ToTransitionKind(AutoMeasurementTriggerKind triggerKind)
        => triggerKind switch
        {
            AutoMeasurementTriggerKind.LightOnDelay => LightTransitionKind.LightOn,
            AutoMeasurementTriggerKind.LightOffDelay => LightTransitionKind.LightOff,
            _ => null
        };

    private static double Aggregate(IReadOnlyList<TentSensorReading> readings, AutoMeasurementAggregation aggregation)
        => aggregation switch
        {
            AutoMeasurementAggregation.Average => readings.Average(reading => reading.Value),
            AutoMeasurementAggregation.Median => Median(readings.Select(reading => reading.Value).ToList()),
            _ => readings.OrderBy(reading => reading.CapturedAtUtc).Last().Value
        };

    private static double Median(List<double> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }

    private static bool ApplyValue(Measurement measurement, AutoMeasurementField field, double value)
    {
        switch (field)
        {
            case AutoMeasurementField.AirTemperatureC:
                measurement.AirTemperatureC = value;
                return true;
            case AutoMeasurementField.HumidityPercent:
                measurement.HumidityPercent = value;
                return true;
            case AutoMeasurementField.ReservoirPh:
                measurement.ReservoirPh = value;
                return true;
            case AutoMeasurementField.ReservoirEc:
                measurement.ReservoirEc = value;
                return true;
            case AutoMeasurementField.ReservoirWaterTempC:
                measurement.ReservoirWaterTempC = value;
                return true;
            case AutoMeasurementField.ReservoirLevelLiters:
                measurement.ReservoirLevelLiters = value;
                return true;
            case AutoMeasurementField.ReservoirLevelCm:
                measurement.ReservoirLevelCm = value;
                return true;
            case AutoMeasurementField.DissolvedOxygenMgL:
                measurement.DissolvedOxygenMgL = value;
                return true;
            case AutoMeasurementField.OrpMv:
                measurement.OrpMv = value;
                return true;
            case AutoMeasurementField.PpfdMol:
                measurement.PpfdMol = value;
                return true;
            case AutoMeasurementField.Co2Ppm:
                measurement.Co2Ppm = value;
                return true;
            default:
                return false;
        }
    }

    private void MarkSkipped(AutoMeasurementRun run, string errorMessage)
    {
        run.Status = AutoMeasurementRunStatus.Skipped;
        run.ErrorMessage = errorMessage;
        _repository.UpdateAutoMeasurementRun(run);
    }
}
