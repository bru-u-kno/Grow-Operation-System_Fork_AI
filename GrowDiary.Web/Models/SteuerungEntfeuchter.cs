using GrowDiary.Web.Services;

namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (forkai.129, F-023): Der Entfeuchter (an einem Schaltport) als
/// Leitstand im Fork.
/// </summary>
/// <remarks>
/// <para><b>Pflanze oder Maschine.</b> Was die Pflanze braucht — Luftfeuchte
/// max., VPD-Band, Lufttemperatur, Blatt-Offset — gehört dem Plan und wird unter
/// „Ziele &amp; Meldungen" gepflegt. Diese Seite zeigt es nur an. Hier steht, wie
/// das GERÄT arbeitet: wie ruhig es schaltet, wie lange es mindestens läuft, wie
/// lange es auf die Außenluft wartet und ab wann es sich wegen Wärme abschaltet
/// (abgestimmt mit dem Nutzer am 21.09.2026).</para>
///
/// <para><b>Geregelt wird in Home Assistant.</b> Ein Kompressor darf nicht davon
/// abhängen, ob dieses Add-on gerade neu startet. Der Fork schreibt die Helfer
/// <c>input_number.fork_entfeuchter_*</c> / <c>input_boolean.fork_entfeuchter_*</c> (im Bestand die alten <c>trotec_*</c>, siehe <see cref="Kennungen"/>).</para>
/// </remarks>
public sealed class EntfeuchterEinstellungen
{
    /// <summary>
    /// An: Schwellen wandern mit Temperatur und VPD-Band. Aus: feste Schwellen.
    /// </summary>
    /// <remarks>
    /// <b>Standard: aus (A-017, Neuling-Test, Lücke 11).</b> Die Zielwerte der VPD-Regelung kommen aus einem Plan
    /// (<c>WochenplanSyncService</c>); ohne Plan stehen im Helfer Platzhalter (0,4/0,4 kPa, Blatt-Offset −5 K). Die
    /// Schwellen waren dann nur zufällig brauchbar — mit dem üblichen Offset von −2 K wären es 75/71 % statt 60/57 %.
    /// Bis jemand die Regelung einschaltet, gelten die festen Schwellen des Forks, die der Seite auch als „Standard"
    /// beschriftet sind. Der Standard wirkt nur bei einem Stand, der NIE gespeichert wurde und für den Home Assistant
    /// keinen Helfer kennt (<c>EntfeuchterSteuerungService.EinstellungenAsync</c>): ein gespeicherter Wert und ein
    /// vorhandener Helfer gewinnen immer.
    /// </remarks>
    public bool VpdRegelung { get; set; } = false;

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

    // --- Temperatur max. (Geräteschutz: das Gerät gibt Wärme ab) ------------

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

    // --- Arbeitsbereich (A-021, forkai.212) -----------------------------------

    /// <summary>
    /// <c>fest</c> = die Einschalt-Schwellen oben (Standard, nichts ändert sich), <c>plan</c> = EIN ist die Plan-Obergrenze
    /// der Luftfeuchte minus <see cref="VorlaufProzent"/> und wandert mit dem Wochenwechsel; AUS ist EIN minus Abstand.
    /// </summary>
    public string ArbeitsbereichQuelle { get; set; } = ArbeitsbereichQuellen.Fest;

    /// <summary>Wie weit EIN unter der Plan-Obergrenze liegt (nur bei <c>plan</c>).</summary>
    public double VorlaufProzent { get; set; } = 4;
}

/// <summary>Woher EIN und AUS der Luftfeuchte kommen.</summary>
public static class ArbeitsbereichQuellen
{
    public const string Plan = "plan";
    public const string Fest = "fest";
}

/// <summary>Die beiden Arten, Temperatur max. zu bilden.</summary>
public static class TempMaxModus
{
    public const string Plan = "plan";
    public const string Fest = "fest";
}

/// <summary>Die vier Schwellen, die „Arbeitsbereich aus dem Plan" ergibt: EIN und AUS, je für Tag und Nacht.</summary>
public readonly record struct EntfeuchterBereich(double EinTag, double AusTag, double EinNacht, double AusNacht);

/// <summary>Was die Entfeuchter-Seite an Livewerten zeigt.</summary>
/// <param name="DeckelProzent">Höchste mögliche EIN-Schwelle: Plan-Feuchte max. − Klima-Hysterese.</param>
/// <param name="PlanLuftTagC">Lufttemperatur der laufenden Plan-Woche (Tag); null ohne Plan.</param>
/// <param name="PlanLuftNachtC">Nacht: Plan-Nachtwert oder Tag − 4 K; null ohne Plan.</param>
/// <param name="TempMaxTagC">Was gerade als Temperatur max. Tag gilt (aus Modus berechnet).</param>
/// <param name="ZuluftVorrang">Läuft die Zuluft UND trocknet die Außenluft? Dann gilt die lange Wartezeit.</param>
/// <param name="RhObergrenzeNachtProzent">Plan-Obergrenze der Luftfeuchte für die Nacht; null ohne Plan (dann gilt nachts die des Tages).</param>
/// <param name="GrenzeAktivProzent">Die Plan-Obergrenze, die jetzt gilt (Tag oder Nacht, je nach Licht).</param>
/// <param name="Bremsen">Was gerade bremst (Außenluft, Übertemperatur, Kapazität …); leer, wenn nichts bremst.</param>
/// <param name="UeberGrenzeSeitMin">Minuten, seit die Feuchte ohne Unterbrechung über der aktiven Grenze liegt; null, wenn nicht darüber.</param>
/// <param name="LaeuftSeitMin">Minuten, seit der Hauptentfeuchter ohne Unterbrechung läuft; null, wenn er nicht läuft.</param>
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
    bool? ZuluftVorrang,
    double? RhObergrenzeNachtProzent = null,
    double? GrenzeAktivProzent = null,
    IReadOnlyList<EntfeuchterBremse>? Bremsen = null,
    int? UeberGrenzeSeitMin = null,
    int? LaeuftSeitMin = null);
