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
        Action<FeedChartColumn, double?> Schreiben,
        bool Optional = false);

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
        // Fork AI (forkai.130): Nachtwerte als eigene Planwerte statt fester Regel.
        // „Luft Nacht" wird einmalig mit Tag − 4 K vorbefüllt; die Feuchte nachts
        // ist optional — leer heißt „wie tags" und wird nie als „fehlt" gemeldet.
        new("airTempNightC", "Luft Nacht", "°C", 10, 40, 0.5, null, s => s.AirTempNightC, (s, v) => s.AirTempNightC = v),
        new("rhMaxNight", "RH max Nacht", "%", 20, 95, 1, null, s => s.RhMaxNight, (s, v) => s.RhMaxNight = v, Optional: true),
        new("vpdMin", "VPD von", "kPa", 0, 3, 0.05, "vpdMax", s => s.VpdMin, (s, v) => s.VpdMin = v),
        new("vpdMax", "VPD bis", "kPa", 0, 3, 0.05, null, s => s.VpdMax, (s, v) => s.VpdMax = v),
        new("co2Min", "CO₂ von", "ppm", 300, 2000, 10, "co2Max", s => s.Co2Min, (s, v) => s.Co2Min = v),
        new("co2Max", "CO₂ bis", "ppm", 300, 2000, 10, null, s => s.Co2Max, (s, v) => s.Co2Max = v),
        new("ppfdMin", "PPFD von", "µmol/m²s", 0, 2000, 10, "ppfdMax", s => s.PpfdMin, (s, v) => s.PpfdMin = v),
        new("ppfdMax", "PPFD bis", "µmol/m²s", 0, 2000, 10, null, s => s.PpfdMax, (s, v) => s.PpfdMax = v),
    ];

    public static Feld? Finden(string name)
        => Alle.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gehört das Feld zum EC-Band (<c>ecMin</c>/<c>ecMax</c>)?</summary>
    public static bool IstEcBand(string feldName)
        => Finden(feldName)?.Name is "ecMin" or "ecMax";

    /// <summary>
    /// Fork AI (02.10.2026): liegt das EC-Ziel im EC-Band? Gibt die Meldung
    /// zurück, wenn nicht — sonst null.
    /// </summary>
    /// <remarks>
    /// <c>ecTarget</c> hat kein <see cref="Feld.Paar"/>, die Paar-Prüfung der
    /// Speicherwege sah es deshalb nie: Ziel 1,2 bei Band 1,8–2,2 wurde
    /// angenommen. Das Band ist das, wogegen Alarm und Kachel messen, das Ziel
    /// das, worauf angemischt wird — liegt das Ziel außerhalb, meldet die App
    /// genau das, was der Plan verlangt. Fehlt eine der drei Zahlen, gibt es
    /// nichts zu vergleichen.
    /// </remarks>
    public static string? EcZielAusserhalb(string woche, double? ziel, double? von, double? bis)
    {
        if (ziel is not { } z) return null;
        var zahl = (double x) => x.ToString("0.##", GrowDiary.Web.Infrastructure.AppCulture.German);
        if (von is { } v && z < v - 1e-9)
            return $"{woche}: EC ({zahl(z)}) liegt unter EC von ({zahl(v)}). Bitte das EC-Band mitändern.";
        if (bis is { } b && z > b + 1e-9)
            return $"{woche}: EC ({zahl(z)}) liegt über EC bis ({zahl(b)}). Bitte das EC-Band mitändern.";
        return null;
    }

    /// <summary>
    /// Fork AI (02.10.2026): die EC-Prüfung der Speicherwege — Ziel im Band, so
    /// wie die Woche nach dem Speichern aussähe.
    /// </summary>
    /// <param name="danach">Feld → neuer Wert, nur die Felder der Anfrage (null = zurück auf den Plan, schon aufgelöst).</param>
    /// <param name="bandWandertMit">true, wenn der Speicherweg das Band mitführt
    /// (<see cref="EcBandMitfuehren"/>, nur mit Grow-Plan). Dann ist eine reine
    /// Zieländerung immer stimmig und wird nicht abgelehnt.</param>
    public static string? EcPruefen(string woche, FeedChartColumn spalte, IReadOnlyDictionary<string, double?> danach, bool bandWandertMit)
    {
        double? Wert(string name, double? vorher) => danach.TryGetValue(name, out var neu) ? neu : vorher;

        var zielKommt = danach.ContainsKey("ecTarget");
        var bandKommt = danach.Keys.Any(IstEcBand);
        if (!zielKommt && !bandKommt) return null;

        var ziel = Wert("ecTarget", spalte.EcTarget);
        if (bandWandertMit && zielKommt && !bandKommt
            && spalte.EcTarget is not null && ziel is not null && spalte.EcMin is not null && spalte.EcMax is not null)
        {
            return null;
        }

        return EcZielAusserhalb(woche, ziel, Wert("ecMin", spalte.EcMin), Wert("ecMax", spalte.EcMax));
    }

    /// <summary>
    /// Fork AI (02.10.2026): das EC-Band, nachdem das Ziel von <paramref name="altZiel"/>
    /// auf <paramref name="neuZiel"/> gewandert ist — verschoben, geklemmt und so,
    /// dass das neue Ziel darin liegt.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum mitführen statt ablehnen.</b> Die Oberfläche bietet „EC"
    /// und „EC-Band" als getrennte Felder an; wer nur das Ziel ändert, meint
    /// „die Woche etwas kräftiger" — nicht „das Ziel soll außerhalb meines
    /// Bands liegen". Das Band behält seine Breite und Lage zum Ziel.</para>
    /// <para><b>Geklemmt</b> auf die Bereiche aus <see cref="Alle"/>
    /// (<c>ecMin</c>/<c>ecMax</c>) — vorher wurde aus Ziel 0,1 bei einem Band um
    /// 1,3 ein <c>EcMin</c> von −0,2. Das Klemmen kann das Ziel nicht aus dem
    /// Band schieben, solange das Ziel selbst im Bereich liegt; lag es schon
    /// vorher außerhalb (alte Daten), wird das Band so weit geöffnet, dass es
    /// das Ziel einschließt.</para>
    /// </remarks>
    public static (double Von, double Bis) EcBandMitfuehren(double altZiel, double neuZiel, double von, double bis)
    {
        var unten = Finden("ecMin")!;
        var oben = Finden("ecMax")!;
        var delta = neuZiel - altZiel;
        var neuVon = Math.Clamp(Math.Round(von + delta, 3), unten.Min, unten.Max);
        var neuBis = Math.Clamp(Math.Round(bis + delta, 3), oben.Min, oben.Max);
        return (Math.Min(neuVon, neuZiel), Math.Max(neuBis, neuZiel));
    }
}
