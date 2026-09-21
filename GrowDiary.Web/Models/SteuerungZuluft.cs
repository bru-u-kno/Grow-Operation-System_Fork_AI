namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (forkai.76): Die Kellerzuluft als Leitstand im Fork.
/// </summary>
/// <remarks>
/// <para><b>Was hier liegt und was nicht.</b> Wie bei der CO₂-Begasung läuft die
/// Regelung selbst in Home Assistant und bleibt dort: sie rechnet aus zwei
/// Fühlerpaaren die absolute Feuchte, und sie soll weiterlaufen, wenn dieses
/// Add-on gerade neu startet. Der Fork besitzt die <b>Sollwerte</b> und schreibt
/// sie in die <c>input_number.zuluft_*</c>-Helfer; zurück liest er das Livebild.</para>
///
/// <para><b>Warum absolute Feuchte.</b> 88 % bei 12 °C tragen weniger Wasser als
/// 57 % bei 22 °C. Nach Prozenten zu regeln hieße, den Keller mit Luft zu fluten,
/// die ihn feuchter macht. Die Schwelle steht deshalb in g/m³, nicht in Prozent.</para>
///
/// <para><b>Kein Planziel.</b> Anders als CO₂ hat die Zuluft keine Entsprechung im
/// Wochenplan — wie trocken die Außenluft ist, entscheidet das Wetter und nicht
/// die Blütewoche. Die Werte stehen deshalb fest und gehören dem Nutzer.</para>
/// </remarks>
public sealed class ZuluftEinstellungen
{
    /// <summary>Ab wie viel g/m³ Unterschied angesaugt wird.</summary>
    public double MindestDifferenzGm3 { get; set; } = 1.0;

    /// <summary>
    /// Frostschutz: darunter bleibt der Lüfter aus, egal wie trocken es draußen ist.
    /// </summary>
    /// <remarks>
    /// Fork AI (forkai.128): Vorgabe 0 statt 5 °C. Vor dem Auskühlen schützt jetzt
    /// <see cref="ZeltTemperaturMinC"/> — die Außentemperatur war dafür nur ein
    /// Umweg, und sie sperrte die Zuluft gerade in kalten Nächten, in denen die
    /// Außenluft am trockensten ist.
    /// </remarks>
    public double AussentemperaturMinC { get; set; } = 0;

    /// <summary>
    /// Fork AI (forkai.128): Fällt das Zelt unter diese Temperatur, pausiert die
    /// Zuluft; sie läuft wieder ab diesem Wert + 1 °C.
    /// </summary>
    /// <remarks>
    /// <b>null heißt: gespeichert vor forkai.128.</b> Ein solcher Stand gilt wie
    /// „nie gespeichert" — die Seite zeigt wieder die Werte aus Home Assistant.
    /// Sonst schriebe das nächste Speichern die alten Werte zurück, die inzwischen
    /// in Home Assistant geändert wurden (Fehlerregister F-022).
    /// </remarks>
    public double? ZeltTemperaturMinC { get; set; }

    /// <summary>Stufe bei knapper Differenz.</summary>
    public int StufeMin { get; set; } = 3;

    /// <summary>Stufe bei großer Differenz.</summary>
    public int StufeMax { get; set; } = 8;

    public int MindestlaufzeitMin { get; set; } = 10;
    public int MindestpauseMin { get; set; } = 10;

    /// <summary>
    /// Aus heißt: die HA-Automation wird abgeschaltet, der Port bleibt stehen,
    /// wie er ist. Das ist der einzige Aus-Knopf der Regelung.
    /// </summary>
    public bool AutomatikAktiv { get; set; } = true;
}

/// <summary>Was die Zuluft-Seite an Livewerten zeigt.</summary>
/// <param name="PauseZeltKalt">
/// Fork AI (forkai.128): true, wenn die Außenluft trocknen würde, die Zuluft aber
/// pausiert, weil das Zelt zu kalt ist.
/// </param>
/// <param name="SperreRestMin">
/// Wie lange die Schaltsperre noch läuft. 0 heißt frei; null, wenn noch nie
/// geschaltet wurde oder der Zeitstempel fehlt.
/// </param>
public sealed record ZuluftLive(
    bool HaErreichbar,
    double? DifferenzGm3,
    double? AussenAbsolutGm3,
    double? KellerAbsolutGm3,
    double? AussenTempC,
    double? AussenRhProzent,
    double? KellerTempC,
    double? KellerRhProzent,
    bool? Bedarf,
    int? Zielstufe,
    int? IstStufe,
    bool? PortAn,
    bool? PortOnline,
    bool? AutomatikAn,
    int? SperreRestMin,
    DateTime? LetzterWechsel,
    double? ZeltTempC = null,
    bool? PauseZeltKalt = null);
