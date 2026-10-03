using System.Globalization;
using System.Text.RegularExpressions;

namespace GrowDiary.Web.Services;

/// <summary>
/// Text in eine Zahl verwandeln — an einer Stelle für das ganze Backend.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (Durchsicht 01.–03.10.2026).</b> Vier Stellen lasen
/// Zustände aus Home Assistant mit <c>NumberStyles.Any</c>. Das erlaubt
/// Tausendertrenner — mit <see cref="CultureInfo.InvariantCulture"/> das
/// Komma. Ein Vorlagen-Sensor, der deutsch „5,8" schreibt, wurde damit zu
/// <b>58</b>, ohne ein Wort. Die übrigen 18 Stellen lasen mit
/// <c>NumberStyles.Float</c> und hielten „5,8" für unlesbar. Dieselbe
/// Entität ergab also je nach Dienst 58, nichts oder — im Frontend
/// (<c>maschinenZahl</c>) — 5,8.</para>
///
/// <para>Und der freie Text „Reservoir" eines Grows wurde per
/// <c>Replace(',', '.')</c> gelesen: „1.200 L" — genau so schreibt das
/// Grow-Formular 1200 Liter (<c>formatLiters</c>, de-DE) — wurde zu 1,2 L,
/// und die Nachfüll-Rechnung dosierte für einen Eimer statt für die Anlage.</para>
///
/// <para><b>Zwei Regeln, nicht eine.</b> Dieselben zwei wie im Frontend
/// (<c>GrowDiary.React/src/zahlenfeld.ts</c>), und die Fälle beider Seiten
/// stehen in EINER Tabelle (<c>GrowDiary.React/src/zahlen-leseregeln.json</c>),
/// gegen die Vitest und xUnit prüfen:
/// <list type="bullet">
/// <item><see cref="Maschine"/> — was eine Maschine schrieb (Home Assistant,
/// die eigene Datenbank): Punkt ist immer der Dezimalpunkt, ein Komma nur,
/// wenn kein Punkt dasteht.</item>
/// <item><see cref="Getippt"/> — was ein Mensch tippte: deutsche Leseregel,
/// Komma = Dezimalzeichen, Punkt in Dreiergruppen = Tausendertrenner.</item>
/// </list></para>
///
/// <para><b>Unendlich ist keine Zahl.</b> .NET liest „Infinity", „NaN" und
/// „1e400" ohne Fehler. Ein Sensor, der „nan" meldet, stünde sonst als
/// Messwert in der Datenbank.</para>
/// </remarks>
public static class Zahlenlesen
{
    /// <summary>
    /// Punkte als Tausendertrenner: eine Gruppe aus ein bis drei Ziffern (nicht
    /// mit 0 beginnend), dann nur Gruppen aus GENAU drei Ziffern. Dasselbe
    /// Muster wie <c>TAUSENDER_GRUPPIERT</c> in <c>zahlenfeld.ts</c>.
    /// </summary>
    private static readonly Regex TausenderGruppiert =
        new(@"^[+-]?[1-9]\d{0,2}(\.\d{3})+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Ein Wert, den eine Maschine geschrieben hat — oder <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Ein Zustand aus Home Assistant („1.234" Volt) hat einen Dezimalpunkt und
    /// nie Tausendertrenner. Ein Komma wird nur gelesen, wenn kein Punkt
    /// dasteht: manche Vorlagen-Sensoren schreiben deutsch („5,8").
    /// <c>unavailable</c>, <c>unknown</c>, <c>on</c> und Leeres ergeben
    /// <c>null</c>.
    /// </remarks>
    public static double? Maschine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var roh = text.Trim();
        return Endlich(roh.Contains('.') ? roh : roh.Replace(',', '.'));
    }

    /// <summary>
    /// Ein Wert, den ein Mensch getippt hat — oder <c>null</c> bei leer und
    /// bei unlesbar.
    /// </summary>
    /// <remarks>
    /// Die deutsche Leseregel, festgelegt vom Nutzer am 02.10.2026: „1.200" ist
    /// 1200, „1,5" ist 1,5, „1.200,5" ist 1200,5. Ein Punkt, der kein
    /// Tausendertrenner sein KANN („5.8", „1.20", „0.500"), ist ein
    /// Dezimalpunkt. Gemische wie „1.2,5" sind unlesbar. Die Begründung jeder
    /// Zeile steht an <c>zahlOderNull</c> in <c>zahlenfeld.ts</c>.
    /// </remarks>
    public static double? Getippt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var roh = text.Trim();

        string normalisiert;
        var teile = roh.Split(',');
        if (teile.Length > 2)
        {
            return null;                                       // „1,2,3"
        }
        else if (teile.Length == 2)
        {
            var (ganz, bruch) = (teile[0], teile[1]);
            // Vor dem Komma dürfen Punkte nur Tausendergruppen sein, dahinter gar keine.
            if (ganz.Contains('.') && !TausenderGruppiert.IsMatch(ganz)) return null;
            if (bruch.Contains('.')) return null;
            normalisiert = ganz.Replace(".", "") + "." + bruch;
        }
        else if (TausenderGruppiert.IsMatch(roh))
        {
            normalisiert = roh.Replace(".", "");               // „1.200" → „1200"
        }
        else
        {
            normalisiert = roh;                                // „5.8", „1200"
        }

        return Endlich(normalisiert);
    }

    /// <summary>Eine Ziffernfolge mit Punkten und Kommas darin, die mit einer Ziffer endet.</summary>
    private static readonly Regex ZahlImText =
        new(@"\d(?:[\d.,]*\d)?", RegexOptions.CultureInvariant);

    /// <summary>
    /// Die erste Zahl in einem freien Text („1.200 L Tank") nach der deutschen
    /// Leseregel — oder <c>null</c>, wenn keine dasteht oder sie unlesbar ist.
    /// </summary>
    /// <remarks>
    /// Für Felder, in denen Zahl und Einheit gemischt stehen. Ein Gemisch wie
    /// „1.2,5 L" ist unlesbar und ergibt <c>null</c> — lieber keine Zahl als
    /// eine geratene.
    /// </remarks>
    public static double? ErsteGetippteZahl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var treffer = ZahlImText.Match(text);
        return treffer.Success ? Getippt(treffer.Value) : null;
    }

    private static double? Endlich(string technisch)
        => double.TryParse(technisch, NumberStyles.Float, CultureInfo.InvariantCulture, out var wert)
            && double.IsFinite(wert)
            ? wert
            : null;
}
