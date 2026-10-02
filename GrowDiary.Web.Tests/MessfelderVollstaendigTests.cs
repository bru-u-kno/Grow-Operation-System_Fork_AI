using System.Reflection;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Jedes Zahlenfeld einer Messung braucht eine Sperre gegen Unmögliches.
///
/// <para><b>Der Anlass.</b> Die Messung hat 21 Zahlenfelder. Die Sperre beim
/// Speichern deckte 14 davon ab. Die restlichen fünf standen auf keiner Liste,
/// und niemandem ist aufgefallen, dass sie fehlen — durchgerutscht sind CO₂ =
/// −500 ppm, Wassertemperatur 5000 °C und Lufttemperatur 9000 °C. Alle drei
/// standen wochenlang in der Datenbank, die −500 ppm sogar auf einer Kachel
/// der Startseite.</para>
///
/// <para><b>Warum das keiner der Audits gefunden hat.</b> Sie hatten alle eine
/// Linse: Überlauf, Kontrast, Layout, Erreichbarkeit. Ein Wert von −500
/// rendert tadellos — richtige Schrift, richtiger Kontrast, kein Überlauf.
/// Jede Prüfung sagte „in Ordnung", weil keine nach Physik gefragt hat.</para>
///
/// <para><b>Warum dieser Test anders gebaut ist.</b> Er ist eine ZÄHLUNG, keine
/// Liste. Er geht über die Felder des Modells und verlangt für jedes entweder
/// eine Sperre oder einen ausgeschriebenen Grund. Ein Test, der die abgedeckten
/// Felder aufzählt, kann nur an dem scheitern, was schon dransteht — genau
/// deshalb ist die Lücke nie aufgefallen. Wer künftig ein Feld hinzufügt,
/// bekommt hier einen roten Test, bis er entschieden hat.</para>
/// </summary>
public sealed class MessfelderVollstaendigTests
{
    private readonly MeasurementSanityService _svc = new();

    /// <summary>
    /// Felder, für die es bewusst keine Sperre gibt — jeweils mit Grund.
    /// </summary>
    /// <remarks>
    /// Wer hier etwas einträgt, schreibt dazu, warum. Ein Eintrag ohne Grund
    /// ist beim nächsten Durchgang ein Befund.
    /// </remarks>
    private static readonly Dictionary<string, string> GewollteAusnahmen = new()
    {
        // Gießmenge und Runoff sind nach oben offen: wer einen 200-Liter-Tank
        // durchspült, trägt große Zahlen ein, und das ist keine Fehleingabe.
        // Nach unten sind beide über ValidateNonNegative gesperrt.
        [nameof(Measurement.WaterAmountMl)] = "Nach oben offen — große Spülmengen sind echt; nach unten über ValidateNonNegative gesperrt",
        [nameof(Measurement.RunoffAmountMl)] = "Nach oben offen; nach unten gesperrt, dazu die Regel Runoff darf Gießmenge nicht übersteigen",
        [nameof(Measurement.HeightCm)] = "Nach oben offen — Sativa im Zelt kann groß werden; nach unten gesperrt",
        [nameof(Measurement.ReservoirLevelLiters)] = "Nach oben offen — Tankgrößen reichen von 20 bis über 1000 Liter; nach unten gesperrt",
        [nameof(Measurement.ReservoirLevelCm)] = "Nach oben offen; nach unten gesperrt",
        [nameof(Measurement.TopOffLiters)] = "Nach oben offen; nach unten gesperrt",
    };

    /// <summary>
    /// Ein Wert, der für dieses Feld physikalisch unmöglich ist.
    /// </summary>
    /// <remarks>
    /// Bewusst grotesk: der Test fragt nicht, ob die Grenze gut gewählt ist,
    /// sondern ob es überhaupt eine gibt.
    /// </remarks>
    private static double UnmoeglicherWert(string feld) => feld switch
    {
        nameof(Measurement.HumidityPercent) => 500,
        nameof(Measurement.IrrigationPh) or nameof(Measurement.DrainPh) or nameof(Measurement.ReservoirPh) => 99,
        nameof(Measurement.DissolvedOxygenMgL) => 900,
        nameof(Measurement.OrpMv) => 99999,
        nameof(Measurement.AirTemperatureC) or nameof(Measurement.ReservoirWaterTempC) => 9000,
        nameof(Measurement.Co2Ppm) => -500,
        nameof(Measurement.PpfdMol) => 99999,
        nameof(Measurement.AirflowAtLeafMPerMin) => 99999,
        _ => -99999,
    };

    public static IEnumerable<object[]> Zahlenfelder()
        => typeof(Measurement)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(double?))
            .Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(Zahlenfelder))]
    public void Jedes_Zahlenfeld_ist_gegen_Unmoegliches_gesperrt_oder_ausdruecklich_ausgenommen(string feld)
    {
        if (GewollteAusnahmen.ContainsKey(feld)) return;

        // Das Feld muss es auch im Formular geben, sonst kann die Sperre es gar
        // nicht melden.
        var imFormular = typeof(MeasurementFormViewModel).GetProperty(feld);
        Assert.True(imFormular is not null, $"{feld} gibt es am Modell, aber nicht am Formular — die Sperre könnte es nicht melden.");

        var grow = new GrowRun { Id = 1, Name = "Test", IrrigationType = IrrigationType.ActiveHydro, HydroStyle = HydroStyle.RDWC };
        var messung = new Measurement { GrowId = 1, TakenAt = DateTime.Now, Stage = GrowStage.Veg };
        typeof(Measurement).GetProperty(feld)!.SetValue(messung, UnmoeglicherWert(feld));

        var modelState = new ModelStateDictionary();
        _svc.ApplyBlockingValidation(modelState, grow, messung);

        Assert.False(modelState.IsValid,
            $"{feld} nimmt den unmöglichen Wert {UnmoeglicherWert(feld)} widerspruchslos an. "
            + "Entweder eine Sperre in MeasurementSanityService.ApplyBlockingValidation ergänzen "
            + "oder das Feld mit Grund in GewollteAusnahmen eintragen.");
    }

    [Fact]
    public void Der_Test_sieht_ueberhaupt_Felder()
    {
        // Sonst prüft er nichts und ist trotzdem grün — die Falle, in die seine
        // Vorgänger gelaufen sind.
        Assert.True(Zahlenfelder().Count() >= 20);
    }
}
