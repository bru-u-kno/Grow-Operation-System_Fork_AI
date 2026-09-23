namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI: Der Wasserkühler als Leitstand im Fork.
/// </summary>
/// <remarks>
/// <para><b>Was hier liegt und was nicht.</b> Geschaltet wird weiter in Home
/// Assistant — ein Kompressor darf nicht davon abhängen, ob dieses Add-on gerade
/// neu startet. Der Fork besitzt die <b>Sollwerte</b> und schreibt sie in die
/// <c>input_number.chiller_*</c>-Helfer; zurück liest er das Livebild.</para>
///
/// <para><b>Zwei Ziele, nicht eins.</b> Tag und Nacht stehen getrennt, weil die
/// Wurzelzone nachts kühler stehen darf und der Kompressor sonst gegen die
/// Lampenwärme anrennt. Welches gilt, entscheidet der Lichtzustand und nicht die
/// Uhr — verschiebt sich die Lichtphase, verschiebt sich das Ziel mit.</para>
///
/// <para><b>Das Ziel gehört nicht dieser Seite.</b> Anders als bei der Zuluft
/// gibt es eine Plan-Quelle: der Wochenplan schreibt Tag und Nacht aus dem
/// Grow-Plan. Deshalb steht hier, WOHER der Wert kommt — und das Ändern bleibt an
/// der Quelle. Zwei Seiten, die denselben Helfer schreiben, wären genau der
/// Zustand, den der Wochenplan-Abgleich seit forkai.61 verhindert.</para>
/// </remarks>
public sealed class ChillerEinstellungen
{
    /// <summary>Wassertemperatur-Ziel, solange die Lampe brennt.</summary>
    public double ZielTagC { get; set; } = 20.0;

    /// <summary>Ziel in der Dunkelphase.</summary>
    public double ZielNachtC { get; set; } = 18.0;

    /// <summary>
    /// Fork AI (F-030, symmetrisch seit forkai.140): Abstand zum Ziel nach
    /// beiden Seiten — ein ab Ziel + Abstand, aus ab Ziel − Abstand. Liegt in
    /// <c>input_number.chiller_hysterese</c>. Gilt nur für die Steckdose.
    /// </summary>
    public double HystereseK { get; set; } = 0.3;

    /// <summary>
    /// Fork AI (F-030): True, sobald die Hysterese über den Fork gespeichert wurde.
    /// Ältere gespeicherte Stände tragen noch 0,3 aus der Zeit, als das Feld
    /// nichts bewirkte — bei ihnen gilt der Wert aus Home Assistant.
    /// </summary>
    public bool HystereseGefuehrt { get; set; }

    public int MindestlaufzeitMin { get; set; } = 5;
    public int MindestpauseMin { get; set; } = 5;

    /// <summary>
    /// Aus heißt: die HA-Automation wird abgeschaltet, die Steckdose bleibt
    /// stehen, wie sie ist. Das ist der einzige Aus-Knopf der Regelung.
    /// </summary>
    public bool AutomatikAktiv { get; set; } = true;
}

/// <summary>Was die Kühler-Seite an Livewerten zeigt.</summary>
/// <param name="ZielQuelle">
/// <c>plan</c> (Wochenplan), <c>cropsteering</c> (Absenkung über den Tag) oder
/// <c>hand</c> — wer das Zielpaar zuletzt gesetzt hat.
/// </param>
/// <param name="SperreRestMin">
/// Wie lange die Schaltsperre noch läuft. 0 heißt frei; null, wenn noch nie
/// geschaltet wurde oder der Zeitstempel fehlt.
/// </param>
public sealed record ChillerLive(
    bool HaErreichbar,
    double? WasserC,
    double? ZielAktivC,
    double? ZielTagC,
    double? ZielNachtC,
    bool? TagPhase,
    bool? Kuehlbedarf,
    bool? SteckdoseAn,
    double? LeistungW,
    bool? AutomatikAn,
    bool? WaechterAn,
    string ZielQuelle,
    int? SperreRestMin,
    DateTime? LetzterWechsel,
    /// <summary>
    /// Gesetzt, wenn die Steckdosen-Funktion der Crop-Steering-Seite dieselbe
    /// Entität schaltet wie diese Regelung. Dann greifen zwei Stellen nach
    /// demselben Kompressor, und der Minutentakt der Regelung überschreibt die
    /// andere binnen einer Minute.
    /// </summary>
    string? DoppelSteuerungEntity,
    double? EinschaltenAbC,
    /// <summary>Fork AI: Ausgeschaltet wird ab Ziel − Hysterese (symmetrisch seit forkai.140).</summary>
    double? AusschaltenBeiC,
    /// <summary>
    /// Fork AI (Chiller-Ansteuerung): <c>steckdose</c>, <c>regelbar</c>,
    /// <c>beides</c> oder <c>keine</c> — ergibt sich aus den Rollen.
    /// </summary>
    string Ansteuerung = ChillerAnsteuerung.Steckdose,
    /// <summary>Das Sollwert-Gerät, wenn eines zugeordnet ist.</summary>
    string? KuehlerEntity = null,
    /// <summary>Welchen Sollwert das Gerät gerade meldet.</summary>
    double? KuehlerSollC = null,
    /// <summary>Zustand des Sollwert-Geräts (climate: <c>cool</c>, <c>off</c> …).</summary>
    string? KuehlerZustand = null);

/// <summary>Fork AI (Chiller-Ansteuerung): Wie der Kühler seine Befehle bekommt.</summary>
public static class ChillerAnsteuerung
{
    /// <summary>Steckdose: HA schaltet ein und aus, mit Hysterese und Sperren.</summary>
    public const string Steckdose = "steckdose";
    /// <summary>Kühler mit eigenem Thermostat: HA schreibt nur das Ziel.</summary>
    public const string Regelbar = "regelbar";
    /// <summary>Sollwert ins Gerät, die Steckdose ist Not-Aus für den Wächter.</summary>
    public const string Beides = "beides";
    /// <summary>Weder Steckdose noch Sollwert-Gerät zugeordnet.</summary>
    public const string Keine = "keine";

    public static string Aus(string? steckdose, string? sollwertGeraet)
        => (string.IsNullOrWhiteSpace(steckdose), string.IsNullOrWhiteSpace(sollwertGeraet)) switch
        {
            (false, true) => Steckdose,
            (true, false) => Regelbar,
            (false, false) => Beides,
            _ => Keine,
        };
}
