using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (F-004, forkai.112): Ein Wochenwert, den der Nutzer vom Plan abweichend gesetzt hat.
/// </summary>
/// <remarks>
/// Gespeichert wird nur die Abweichung — die ausgelieferte Programmdatei bleibt
/// unberührt. So erreichen Korrekturen am Programm den Nutzer weiterhin, und
/// „auf Plan zurück" ist schlicht das Löschen der Zeile.
/// </remarks>
public sealed record Wochenwert(string ProgrammId, string SpalteId, string Feld, double Wert);

/// <summary>Eine Änderung aus dem Bearbeiten-Modus. <c>Wert = null</c> heißt: zurück auf den Plan.</summary>
public sealed class WochenwertAenderung
{
    public string SpalteId { get; set; } = string.Empty;
    public string Feld { get; set; } = string.Empty;
    public double? Wert { get; set; }
}

/// <summary>Alle Änderungen eines Speichervorgangs — sie gelten gemeinsam oder gar nicht.</summary>
/// <remarks>Das Programm kommt aus dem Grow in der Adresse, nicht aus der Anfrage.</remarks>
public sealed class WochenwerteSpeichernRequest
{
    public List<WochenwertAenderung> Aenderungen { get; set; } = [];
}

/// <summary>
/// Die Felder einer Wochenspalte, die sich bearbeiten lassen — mit erlaubtem Bereich.
/// </summary>
/// <remarks>
/// <para><b>Warum eine feste Liste.</b> Das Schreiben geht über einen Feldnamen
/// aus der Anfrage. Ohne Liste könnte eine Anfrage jedes Feld der Spalte
/// treffen; mit ihr gilt nur, was hier steht.</para>
///
/// <para><b>Die Bereiche</b> sind Plausibilitätsgrenzen, keine Empfehlungen:
/// sie sollen Vertipper abfangen (EC 14 statt 1,4), nicht fachlich urteilen.
/// Dosierungen (ml/L) sind bewusst nicht dabei.</para>
/// </remarks>
public static class Wochenwertfelder
{
    public sealed record Feld(
        string Name,
        string Bezeichnung,
        string Einheit,
        double Min,
        double Max,
        double Schritt,
        string? Paar,
        Func<FeedChartColumn, double?> Lesen,
        Action<FeedChartColumn, double?> Schreiben);

    public static readonly IReadOnlyList<Feld> Alle =
    [
        new("ecTarget", "EC", "mS/cm", 0, 5, 0.1, null, s => s.EcTarget, (s, v) => s.EcTarget = v),
        new("ecMin", "EC von", "mS/cm", 0, 5, 0.05, "ecMax", s => s.EcMin, (s, v) => s.EcMin = v),
        new("ecMax", "EC bis", "mS/cm", 0, 5, 0.05, null, s => s.EcMax, (s, v) => s.EcMax = v),
        new("phMin", "pH von", "", 3, 9, 0.1, "phMax", s => s.PhMin, (s, v) => s.PhMin = v),
        new("phMax", "pH bis", "", 3, 9, 0.1, null, s => s.PhMax, (s, v) => s.PhMax = v),
        new("waterTempDayC", "Wasser Tag", "°C", 10, 30, 0.5, null, s => s.WaterTempDayC, (s, v) => s.WaterTempDayC = v),
        new("waterTempNightC", "Wasser Nacht", "°C", 10, 30, 0.5, null, s => s.WaterTempNightC, (s, v) => s.WaterTempNightC = v),
        new("orpMin", "ORP von", "mV", 0, 800, 10, "orpMax", s => s.OrpMin, (s, v) => s.OrpMin = v),
        new("orpMax", "ORP bis", "mV", 0, 800, 10, null, s => s.OrpMax, (s, v) => s.OrpMax = v),
        new("rhMax", "RH max", "%", 20, 95, 1, null, s => s.RhMax, (s, v) => s.RhMax = v),
        new("airTempC", "Luft", "°C", 10, 40, 0.5, null, s => s.AirTempC, (s, v) => s.AirTempC = v),
        new("vpdMin", "VPD von", "kPa", 0, 3, 0.05, "vpdMax", s => s.VpdMin, (s, v) => s.VpdMin = v),
        new("vpdMax", "VPD bis", "kPa", 0, 3, 0.05, null, s => s.VpdMax, (s, v) => s.VpdMax = v),
        new("co2Min", "CO₂ von", "ppm", 300, 2000, 10, "co2Max", s => s.Co2Min, (s, v) => s.Co2Min = v),
        new("co2Max", "CO₂ bis", "ppm", 300, 2000, 10, null, s => s.Co2Max, (s, v) => s.Co2Max = v),
        new("ppfdMin", "PPFD von", "µmol/m²s", 0, 2000, 10, "ppfdMax", s => s.PpfdMin, (s, v) => s.PpfdMin = v),
        new("ppfdMax", "PPFD bis", "µmol/m²s", 0, 2000, 10, null, s => s.PpfdMax, (s, v) => s.PpfdMax = v),
    ];

    public static Feld? Finden(string name)
        => Alle.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}
