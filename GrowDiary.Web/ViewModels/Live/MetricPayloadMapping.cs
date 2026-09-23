using GrowDiary.Web.Models;

namespace GrowDiary.Web.ViewModels.Live;

public static class MetricPayloadMapping
{
    public static MetricPayload ToPayload(this MetricCard metric)
        => new()
        {
            Key = metric.Key,
            Label = metric.Label,
            Value = metric.Value,
            Unit = metric.Unit,
            Tone = metric.Tone,
            Hint = metric.Hint,
            NumericValue = metric.NumericValue,
            TargetMin = metric.TargetMin,
            TargetMax = metric.TargetMax,
            TargetNote = metric.TargetNote,
            TargetDerived = metric.TargetDerived,
            TargetDayMin = metric.TargetDayMin,
            TargetDayMax = metric.TargetDayMax,
            TargetNightMin = metric.TargetNightMin,
            TargetNightMax = metric.TargetNightMax,
            TargetPhase = metric.TargetPhase,
            AlarmMin = metric.AlarmMin,
            AlarmMax = metric.AlarmMax,
            AlarmDayMin = metric.AlarmDayMin,
            AlarmDayMax = metric.AlarmDayMax,
            AlarmNightMin = metric.AlarmNightMin,
            AlarmNightMax = metric.AlarmNightMax,
            StatusNote = metric.StatusNote,
            LightOnAt = metric.LightOnAt,
            LightOffAt = metric.LightOffAt,
            ValueSource = metric.ValueSource,
            MeasuredAgeMinutes = metric.MeasuredAgeMinutes
        };
}
