using GrowDiary.Web.Models;
using GrowDiary.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GrowDiary.Web.Services;

public sealed class MeasurementSanityService
{
    public IReadOnlyList<RecommendationCard> GetSanityCards(GrowRun grow, Measurement measurement)
    {
        var cards = new List<RecommendationCard>();
        var profile = grow.Profile;

        CheckHumidity(cards, measurement.HumidityPercent);
        CheckAirTemperature(cards, measurement.AirTemperatureC);
        CheckHeight(cards, measurement.HeightCm);
        CheckWaterAndRunoff(cards, measurement.WaterAmountMl, measurement.RunoffAmountMl);

        /* Kein if auf profile.IsHydro mehr: GrowthProfile.IsHydro ist =&gt; true,
           also eine Konstante, und IrrigationType hat genau einen Wert. Die
           Verzweigung las sich wie eine Wahl und war keine — der else-Zweig
           (CheckSubstrate, rund 140 Zeilen) war unerreichbar.

           Darin standen acht pH- und EC-Zahlen fuer Erde. Die sehen aus wie
           fachliche Wahrheiten; die App sagt zu Erde aber nichts. Gehalten
           wird das jetzt von KeinZweigFuerEineAnbauartDieEsNichtGibtTests:
           kommt Erde zurueck, wird dort zuerst etwas rot. */
        CheckHydro(cards, measurement);

        return cards;
    }

    /// <summary>
    /// Was fuer eine Messgroesse physikalisch ueberhaupt vorkommen kann.
    /// </summary>
    /// <remarks>
    /// <b>Eine Tabelle, zwei Leser.</b> Die Sperre beim Speichern verhindert,
    /// dass so ein Wert hereinkommt; der Beurteiler des Messprotokolls nimmt
    /// ihn aus seiner Bilanz. Vorher standen die Zahlen nur im Sperr-Code —
    /// und der Beurteiler zaehlte EC 99999 und Wassertemperatur 5000 Grad als
    /// ganz normale Abweichungen mit.
    ///
    /// <b>Die Grenzen sind bewusst weit.</b> Sie fangen einen Tippfehler oder
    /// eine falsche Einheit ab und entscheiden nicht ueber Anbau. Was
    /// agronomisch sinnvoll ist, sagen die Sollwerte.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, (double Min, double Max)> PhysikalischeGrenzen =
        new Dictionary<string, (double, double)>
        {
            ["ph"] = (0, 14),
            ["ec"] = (0, 10),
            ["water-temp"] = (-5, 60),
            ["air-temp"] = (-20, 60),
            ["humidity"] = (0, 100),
            ["do"] = (0, 20),
            ["orp"] = (-1000, 1000),
            ["co2"] = (0, 30000),
            ["ppfd"] = (0, 3000),
            ["vpd"] = (0, 20),
            ["airflow"] = (0, 300),
        };

    /// <summary>
    /// Der Name, unter dem eine Live-Messgroesse (Kachel- und Alarm-Schluessel wie
    /// <c>reservoir-ph</c>) in <see cref="PhysikalischeGrenzen"/> steht — oder null.
    /// </summary>
    /// <remarks>
    /// Die beiden Seiten heissen verschieden — dort Kachel-Schluessel, hier die
    /// Namen der Tabelle. Unbekannte Groessen gelten in der Tabelle ABSICHTLICH
    /// als plausibel; ohne diese Uebersetzung liefe eine Pruefung bei fuenf von
    /// zehn Groessen ins Leere und meldete trotzdem nichts. Stand bis 02.10.2026
    /// als lokale Funktion im <c>GrowDashboardComposer</c>; jetzt hier, weil auch
    /// die Toleranz der Plan-Alarme sie braucht (eine Uebersetzung, nicht zwei).
    /// </remarks>
    public static string? PhysikSchluesselFuerMetrik(string metrikSchluessel) => metrikSchluessel switch
    {
        "temperature" => "air-temp",
        "humidity" => "humidity",
        "reservoir-ph" => "ph",
        "reservoir-ec" => "ec",
        "reservoir-temp" => "water-temp",
        "dissolved-oxygen" => "do",
        "orp" => "orp",
        "co2" => "co2",
        "ppfd" => "ppfd",
        "vpd" => "vpd",
        _ => null,
    };

    /// <summary>Meldet einen Wert, der die physikalische Grenze verlässt.</summary>
    /// <summary>Ein Feldfehler, wenn der Wert für diese Größe physikalisch nicht vorkommen kann.</summary>
    /// <remarks>Öffentlich, damit Wasserwechsel, Addback und Alarm-Grenzen dieselbe Tabelle lesen (Fork AI, 02.10.2026).</remarks>
    public static void PhysikGrenze(ModelStateDictionary modelState, string feld, string groesse, double? wert, string bezeichnung)
    {
        if (wert is not { } v || IstPhysikalischMoeglich(groesse, v)) return;
        var g = PhysikalischeGrenzen[groesse];
        modelState.AddModelError(feld, $"{bezeichnung} liegt ausserhalb dessen, was physikalisch vorkommen kann ({g.Min:0}–{g.Max:0}). Bitte Messgerät oder Einheit prüfen.");
    }

    /// <summary>Ist dieser Wert fuer diese Groesse ueberhaupt moeglich?</summary>
    /// <remarks>
    /// Unbekannte Groessen gelten als plausibel: lieber einen Wert zaehlen,
    /// den niemand pruefen konnte, als ihn stillschweigend zu verschlucken.
    /// </remarks>
    public static bool IstPhysikalischMoeglich(string groesse, double wert)
        => !PhysikalischeGrenzen.TryGetValue(groesse, out var g) || (wert >= g.Min && wert <= g.Max);

    public void ApplyBlockingValidation(ModelStateDictionary modelState, GrowRun grow, Measurement measurement)
    {
        ValidatePh(modelState, nameof(MeasurementFormViewModel.IrrigationPh), measurement.IrrigationPh, "Gießwasser-pH");
        ValidatePh(modelState, nameof(MeasurementFormViewModel.DrainPh), measurement.DrainPh, "Drain-pH");
        ValidatePh(modelState, nameof(MeasurementFormViewModel.ReservoirPh), measurement.ReservoirPh, "Reservoir-pH");

        ValidateRange(modelState, nameof(MeasurementFormViewModel.HumidityPercent), measurement.HumidityPercent, 0, 100, "Luftfeuchtigkeit");
        ValidateNonNegative(modelState, nameof(MeasurementFormViewModel.HeightCm), measurement.HeightCm, "Höhe");
        ValidateNonNegative(modelState, nameof(MeasurementFormViewModel.WaterAmountMl), measurement.WaterAmountMl, "Gießmenge");
        ValidateNonNegative(modelState, nameof(MeasurementFormViewModel.RunoffAmountMl), measurement.RunoffAmountMl, "Runoff");
        ValidateNonNegative(modelState, nameof(MeasurementFormViewModel.ReservoirLevelCm), measurement.ReservoirLevelCm, "Wasserstand cm");
        ValidateNonNegative(modelState, nameof(MeasurementFormViewModel.ReservoirLevelLiters), measurement.ReservoirLevelLiters, "Wasserstand Liter");
        ValidateNonNegative(modelState, nameof(MeasurementFormViewModel.TopOffLiters), measurement.TopOffLiters, "Top-Off Liter");

        // Ab hier liest die Sperre die TABELLE, statt die Zahlen ein zweites Mal
        // hinzuschreiben.
        //
        // Genau das war der Fehler: die Tabelle oben entstand als „eine Tabelle,
        // zwei Leser" — und die Sperre zwanzig Zeilen darunter tippte die Zahlen
        // trotzdem ab. Sie hatte damit genau einen fremden Leser
        // (MeasurementAssessmentService) und war für ihren eigenen Nachbarn tot.
        // Fork AI (02.10.2026): EC nach oben begrenzt. Seit Zahlen deutsch gelesen
        // werden, ist „1.250" tausendzweihundertfünfzig — vorher kam das als 1,25
        // an. Ohne Obergrenze stand dann still EC 1250 in der Datenbank. Die
        // Tabelle sperrt auch negative Werte (EC 0–10), das ValidateNonNegative
        // davor entfällt.
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.IrrigationEc), "ec", measurement.IrrigationEc, "Die Gießwasser-EC");
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.DrainEc), "ec", measurement.DrainEc, "Die Drain-EC");
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.ReservoirEc), "ec", measurement.ReservoirEc, "Die Reservoir-EC");
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.AddbackEc), "ec", measurement.AddbackEc, "Die Addback-EC");
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.DissolvedOxygenMgL), "do", measurement.DissolvedOxygenMgL, "Der Sauerstoffwert");
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.OrpMv), "orp", measurement.OrpMv, "Der ORP-Wert");
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.AirTemperatureC), "air-temp", measurement.AirTemperatureC, "Die Lufttemperatur");
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.ReservoirWaterTempC), "water-temp", measurement.ReservoirWaterTempC, "Die Wassertemperatur");
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.Co2Ppm), "co2", measurement.Co2Ppm, "Der CO₂-Wert");
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.PpfdMol), "ppfd", measurement.PpfdMol, "Der PPFD-Wert");

        // Die fuenf Felder, die bis beta.50 auf KEINER Liste standen.
        //
        // Durchgerutscht sind: CO2 = -500 ppm, Wassertemperatur 5000 Grad,
        // Lufttemperatur 9000 Grad. Alle drei standen wochenlang in der
        // Datenbank und auf dem Bildschirm; die -500 ppm sogar auf einer
        // Kachel der Startseite. Kein Audit hat sie gefunden, weil sie sich
        // tadellos rendern liessen — die Pruefungen sahen auf Ueberlauf,
        // Kontrast und Layout, nicht auf Physik.
        //
        // Die Grenzen sind bewusst weit: sie sollen einen Tippfehler oder
        // eine falsche Einheit abfangen, nicht ueber Anbau entscheiden. Was
        // agronomisch sinnvoll ist, sagen die Sollwerte, nicht diese Sperre.
        PhysikGrenze(modelState, nameof(MeasurementFormViewModel.AirflowAtLeafMPerMin), "airflow", measurement.AirflowAtLeafMPerMin, "Der Luftstrom");

        if (measurement.RunoffAmountMl is { } runoff && measurement.WaterAmountMl is { } water && runoff > water)
        {
            modelState.AddModelError(nameof(MeasurementFormViewModel.RunoffAmountMl), "Runoff kann normalerweise nicht höher als die eingetragene Gießmenge sein. Bitte Eingabe prüfen.");
        }
    }

    private static void CheckHydro(List<RecommendationCard> cards, Measurement measurement)
    {
        // pH, EC (moderat), WaterTemp und DO werden in RecommendationEngine.EvaluateHydro
        // mit stage-spezifischen Schwellenwerten abgedeckt. Hier nur noch echter Plausibilitäts-
        // check (Messfehler-Verdacht) und hilfreiche Hinweise ohne agronomisches Duplikat.

        if (measurement.ReservoirEc is { } ec && ec >= 3.2)
        {
            cards.Add(Critical("Reservoir-EC extrem hoch", $"Mit {ec:0.00} EC ist die Lösung für rezirkulierendes Hydro sehr aggressiv. Prüfe, ob ein Messfehler, ein falsches Ziel oder akuter Salzstress vorliegt."));
        }

        if (measurement.TopOffLiters is { } topOff && topOff > 0 && measurement.AddbackEc is null)
        {
            cards.Add(Info("Top-Off ohne Addback-EC", "Du hast Top-Off eingetragen, aber keinen Addback-EC. Für saubere RDWC-Entscheidungen ist genau diese Kombination sehr wertvoll."));
        }
    }

    private static void CheckHumidity(List<RecommendationCard> cards, double? humidityPercent)
    {
        if (humidityPercent is not { } rh)
        {
            return;
        }

        if (rh < 20)
        {
            cards.Add(Warning("Luftfeuchtigkeit sehr niedrig", $"Mit {rh:0} % ist die Luft sehr trocken. Das erhöht Verdunstungsstress deutlich."));
        }
        else if (rh < 35)
        {
            cards.Add(Info("Luftfeuchtigkeit niedrig", $"Die Luftfeuchtigkeit liegt bei {rh:0} %. Beobachte Transpiration und Spitzenstress."));
        }
        else if (rh > 85)
        {
            cards.Add(Warning("Luftfeuchtigkeit sehr hoch", $"Mit {rh:0} % ist die Luft sehr feucht. Schimmel- und Transpirationsprobleme werden wahrscheinlicher."));
        }
    }

    private static void CheckAirTemperature(List<RecommendationCard> cards, double? airTemperatureC)
    {
        if (airTemperatureC is not { } temp)
        {
            return;
        }

        if (temp >= 35)
        {
            cards.Add(Critical("Lufttemperatur kritisch hoch", $"Mit {temp:0.0} °C ist deutlicher Hitzestress wahrscheinlich."));
        }
        else if (temp >= 31)
        {
            cards.Add(Warning("Lufttemperatur hoch", $"Mit {temp:0.0} °C bist du klar im Hitzestress-Risiko."));
        }
        else if (temp > 30)
        {
            cards.Add(Warning("Lufttemperatur hoch", $"Die Lufttemperatur liegt bei {temp:0.0} °C. Prüfe Lampenabstand, Abluft und VPD."));
        }
        else if (temp < 14)
        {
            cards.Add(Warning("Lufttemperatur sehr niedrig", $"Mit {temp:0.0} °C wird das Wachstum deutlich träger."));
        }
    }

    private static void CheckHeight(List<RecommendationCard> cards, double? heightCm)
    {
        if (heightCm is { } height && height > 400)
        {
            cards.Add(Info("Höhe ungewöhnlich groß", $"Mit {height:0.0} cm ist die eingetragene Höhe sehr hoch. Prüfe Einheit und Eingabe, falls das nicht bewusst so gemeint war."));
        }
    }

    private static void CheckWaterAndRunoff(List<RecommendationCard> cards, double? waterAmountMl, double? runoffAmountMl)
    {
        if (waterAmountMl is { } water && water > 0 && water < 50)
        {
            cards.Add(Info("Sehr kleine Gießmenge", $"Mit {water:0} ml ist die Gießmenge sehr klein. Das kann in sehr frühen Stadien okay sein, sollte aber zur Phase passen."));
        }

        if (runoffAmountMl is { } runoff && waterAmountMl is { } givenWater && runoff > givenWater)
        {
            cards.Add(Critical("Runoff größer als Gießmenge", "Die dokumentierte Runoff-Menge ist höher als die eingetragene Gießmenge. Das spricht fast sicher für einen Eingabefehler."));
        }
    }

    private static void ValidatePh(ModelStateDictionary modelState, string fieldName, double? value, string label)
    {
        if (value is < 0 or > 14)
        {
            modelState.AddModelError(fieldName, $"{label} muss zwischen 0 und 14 liegen.");
        }
    }

    private static void ValidateRange(ModelStateDictionary modelState, string fieldName, double? value, double min, double max, string label)
    {
        if (value is null)
        {
            return;
        }

        if (value < min || value > max)
        {
            modelState.AddModelError(fieldName, $"{label} muss zwischen {min:0} und {max:0} liegen.");
        }
    }

    private static void ValidateNonNegative(ModelStateDictionary modelState, string fieldName, double? value, string label)
    {
        if (value is < 0)
        {
            modelState.AddModelError(fieldName, $"{label} darf nicht negativ sein.");
        }
    }

    private static RecommendationCard Info(string title, string message)
        => new() { Severity = Kartenschwere.Hinweis, Title = title, Message = message };

    private static RecommendationCard Warning(string title, string message)
        => new() { Severity = Kartenschwere.Warnung, Title = title, Message = message };

    private static RecommendationCard Critical(string title, string message)
        => new() { Severity = Kartenschwere.Gefahr, Title = title, Message = message };
}
