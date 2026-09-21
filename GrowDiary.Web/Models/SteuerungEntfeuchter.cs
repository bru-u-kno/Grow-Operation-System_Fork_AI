namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (forkai.129, F-023): Der Entfeuchter (Trotec am AC-Infinity-Port) als
/// Leitstand im Fork.
/// </summary>
/// <remarks>
/// <para><b>Pflanze oder Maschine.</b> Was die Pflanze braucht — Luftfeuchte
/// max., VPD-Band, Lufttemperatur, Blatt-Offset — gehört dem Plan und wird unter
/// „Ziele &amp; Meldungen" gepflegt. Diese Seite zeigt es nur an. Hier steht, wie
/// das GERÄT arbeitet: wie ruhig es schaltet, wie lange es mindestens läuft, wie
/// lange es auf die Außenluft wartet und ab wann es sich wegen Wärme abschaltet
/// (abgestimmt mit Bru am 21.09.2026).</para>
///
/// <para><b>Geregelt wird in Home Assistant.</b> Ein Kompressor darf nicht davon
/// abhängen, ob dieses Add-on gerade neu startet. Der Fork schreibt die Helfer
/// <c>input_number.trotec_*</c> / <c>input_boolean.trotec_*</c>.</para>
/// </remarks>
public sealed class EntfeuchterEinstellungen
{
    /// <summary>An: Schwellen wandern mit Temperatur und VPD-Band. Aus: feste Schwellen.</summary>
    public bool VpdRegelung { get; set; } = true;

    /// <summary>Wie weit die Feuchte unter EIN fallen muss, bevor er ausgeht (knapp 2 · normal 4 · ruhig 6).</summary>
    public double HystereseProzent { get; set; } = 4;

    /// <summary>Vorher schaltet ihn erreichte Feuchte nicht ab. Übertemperatur schon.</summary>
    public int MindestlaufzeitMin { get; set; } = 20;

    /// <summary>So lange muss die Feuchte über EIN liegen, bevor er anspringt.</summary>
    public int EinschaltverzoegerungMin { get; set; } = 10;

    /// <summary>Trocknet die Zuluft gerade, wartet er stattdessen so lange.</summary>
    public int WartezeitAussenluftMin { get; set; } = 25;

    /// <summary>Aus: nur in der Dunkelphase.</summary>
    public bool TagbetriebErlauben { get; set; } = true;

    /// <summary>Aus hält die Regelung an; der Port bleibt, wie er ist. Einziger Aus-Knopf.</summary>
    public bool AutomatikAktiv { get; set; } = true;

    // --- Temperatur max. (Geräteschutz: der Trotec gibt Wärme ab) ------------

    /// <summary><c>plan</c> = Plan-Luft + Abstand, <c>fest</c> = eigener Wert.</summary>
    public string TempMaxTagModus { get; set; } = TempMaxModus.Fest;
    public double TempMaxTagAbstandK { get; set; } = 5;
    public double TempMaxTagFestC { get; set; } = 29;

    public string TempMaxNachtModus { get; set; } = TempMaxModus.Fest;
    public double TempMaxNachtAbstandK { get; set; } = 5;
    public double TempMaxNachtFestC { get; set; } = 25;

    // --- Rückfallebene: feste Feuchte-Schwellen -------------------------------

    /// <summary>Gilt nur ohne VPD-Regelung (oder ohne VPD-Werte). Auch hier deckelt die Plan-Feuchte.</summary>
    public double FeuchteEinTag { get; set; } = 60;
    public double FeuchteAusTag { get; set; } = 57;
    public double FeuchteEinNacht { get; set; } = 62;
    public double FeuchteAusNacht { get; set; } = 60;
}

/// <summary>Die beiden Arten, Temperatur max. zu bilden.</summary>
public static class TempMaxModus
{
    public const string Plan = "plan";
    public const string Fest = "fest";
}

/// <summary>Was die Entfeuchter-Seite an Livewerten zeigt.</summary>
/// <param name="DeckelProzent">Höchste mögliche EIN-Schwelle: Plan-Feuchte max. − Klima-Hysterese.</param>
/// <param name="PlanLuftTagC">Lufttemperatur der laufenden Plan-Woche (Tag); null ohne Plan.</param>
/// <param name="PlanLuftNachtC">Nacht: Plan-Nachtwert oder Tag − 4 K; null ohne Plan.</param>
/// <param name="TempMaxTagC">Was gerade als Temperatur max. Tag gilt (aus Modus berechnet).</param>
/// <param name="ZuluftVorrang">Läuft die Zuluft UND trocknet die Außenluft? Dann gilt die lange Wartezeit.</param>
public sealed record EntfeuchterLive(
    bool HaErreichbar,
    double? FeuchteProzent,
    double? TempC,
    double? Vpd,
    bool? TagPhase,
    double? EinAktivProzent,
    double? AusAktivProzent,
    double? TempMaxAktivC,
    double? RhObergrenzeProzent,
    double? DeckelProzent,
    double? VpdUnten,
    double? VpdOben,
    double? BlattOffsetC,
    string? PlanWoche,
    double? PlanLuftTagC,
    double? PlanLuftNachtC,
    double? TempMaxTagC,
    double? TempMaxNachtC,
    double? Co2CanopyGrenzeC,
    bool? PortAn,
    bool? PortOnline,
    bool? AutomatikAn,
    bool? ZuluftVorrang);
