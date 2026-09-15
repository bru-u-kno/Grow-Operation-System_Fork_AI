using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

public sealed class MeasurementUpsertRequest
{
    /// <summary>Zeitpunkt der Messung in Ortszeit (<c>yyyy-MM-ddTHH:mm</c>).</summary>
    /// <remarks>
    /// Darf fehlen. Beim Anlegen gilt dann die Gegenwart, beim Ändern bleibt der
    /// gespeicherte Zeitpunkt stehen. Früher stand hier ein Standardwert
    /// <c>DateTime.Now</c> — ein PUT ohne Zeitangabe schob die Messung damit
    /// still auf den Moment des Speicherns (15.09.2026,
    /// <c>MessungBehaeltZeitpunktBeimAendernTests</c>).
    /// </remarks>
    public string? TakenAtLocal { get; set; }

    public GrowStage Stage { get; set; } = GrowStage.Veg;
    public ValueOrigin Source { get; set; } = ValueOrigin.Manual;
    public string? Notes { get; set; }
    public double? AirTemperatureC { get; set; }
    public double? HumidityPercent { get; set; }
    public double? HeightCm { get; set; }
    public double? WaterAmountMl { get; set; }
    public double? RunoffAmountMl { get; set; }
    public double? IrrigationPh { get; set; }
    public double? IrrigationEc { get; set; }
    public double? DrainPh { get; set; }
    public double? DrainEc { get; set; }
    public double? ReservoirPh { get; set; }
    public double? ReservoirEc { get; set; }
    public double? ReservoirWaterTempC { get; set; }
    public double? ReservoirLevelCm { get; set; }
    public double? ReservoirLevelLiters { get; set; }
    public double? DissolvedOxygenMgL { get; set; }
    public double? AirflowAtLeafMPerMin { get; set; }
    /// <summary>„Weak", „Moderate" oder „Strong" — bewusst Stufen statt Litern.</summary>
    public string? WaterFlow { get; set; }
    public double? OrpMv { get; set; }
    public double? TopOffLiters { get; set; }
    public double? AddbackEc { get; set; }
    public bool SolutionChange { get; set; }
    public double? PpfdMol { get; set; }
    public double? Co2Ppm { get; set; }
}
