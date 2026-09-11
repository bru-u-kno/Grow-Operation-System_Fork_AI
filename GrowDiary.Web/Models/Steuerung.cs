namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (forkai.20): Die CO₂-Begasung als Leitstand im Fork.
/// </summary>
/// <remarks>
/// <para><b>Was hier liegt und was nicht.</b> Die Regelung selbst — Impuls,
/// Wächter, Licht-aus-Sicherung, Abluft-Drosselung — läuft in Home Assistant
/// und bleibt dort: ein Ventil an einer Gasflasche darf nicht davon abhängen,
/// ob ein Web-Add-on gerade neu startet. Der Fork besitzt die <b>Sollwerte</b>
/// und schreibt sie in die bestehenden HA-Helfer (<c>input_number.co2_*</c>,
/// <c>input_boolean.co2_*</c>); zurück liest er das Livebild und die
/// Tageswerte.</para>
///
/// <para><b>Ziel aus dem Plan.</b> Bei <see cref="Co2Einstellungen.ZielQuelle"/>
/// = <c>plan</c> kommt das Wochenziel aus <see cref="Services.Zielband"/>
/// (Untergrenze des CO₂-Bands der laufenden Phase) und wird je Canopy-Bereich
/// mit einem Prozentsatz skaliert — dieselbe Zahl, die auch Kachel und Alarm
/// nennen. Bei <c>fest</c> stehen die drei ppm-Werte direkt hier.</para>
/// </remarks>
public sealed class Co2Einstellungen
{
    /// <summary><c>fest</c> oder <c>plan</c>.</summary>
    public string ZielQuelle { get; set; } = "fest";

    /// <summary>Feste Ziele in ppm (bei ZielQuelle = fest).</summary>
    public int ZielWarmPpm { get; set; } = 920;
    public int ZielMittelPpm { get; set; } = 820;
    public int ZielKuehlPpm { get; set; } = 650;

    /// <summary>Anteil vom Planziel je Canopy-Bereich in Prozent (bei ZielQuelle = plan).</summary>
    public int AnteilWarmProzent { get; set; } = 80;
    public int AnteilMittelProzent { get; set; } = 70;
    public int AnteilKuehlProzent { get; set; } = 55;

    public int HysteresePpm { get; set; } = 50;

    // Dosierung
    public int ImpulsMinSekunden { get; set; } = 5;
    public int ImpulsMaxSekunden { get; set; } = 15;
    public int WartezeitSekunden { get; set; } = 120;
    public int MaxImpulseJeZyklus { get; set; } = 10;
    public bool Autokalibrierung { get; set; } = true;
    public double ZeltvolumenM3 { get; set; } = 5.76;

    // Klima hat Vorrang
    public double RhObergrenzeProzent { get; set; } = 65;
    public double KlimaHystereseProzent { get; set; } = 3;
    public double CanopyObergrenzeC { get; set; } = 30;
    public int T6StufeNormal { get; set; } = 7;
    public int T6StufeDosierung { get; set; } = 5;
    public int T6StufeTief { get; set; } = 4;
    public double T6TiefMaxTempC { get; set; } = 29.5;
    public bool AbluftDrosseln { get; set; } = true;

    // Zeiten (Minuten relativ zum Licht). Beide gehen als Helfer nach Home
    // Assistant; die Automation rechnet das Fenster daraus gegen die geplante
    // Aus-Zeit des Controllers. Vorher standen 15 Minuten und 16:30 Uhr fest
    // in der Automation — die Felder waeren hier sonst Attrappen gewesen.
    public int StartNachLichtAnMinuten { get; set; } = 15;
    public int EndeVorLichtAusMinuten { get; set; } = 30;

    // Buchung
    /// <summary>Kosten-Artikel, auf den die Tagessumme gebucht wird (null = nur anzeigen).</summary>
    public int? KostenArtikelId { get; set; }
    public bool JournalBuchen { get; set; } = true;

    public bool AutomatikAktiv { get; set; } = true;
}

/// <summary>Ein Tag CO₂-Begasung, wie er nach Licht-aus abgeschlossen wird.</summary>
public sealed class Co2Tag
{
    public int Id { get; set; }
    /// <summary>Kalendertag in Ortszeit, yyyy-MM-dd.</summary>
    public string Datum { get; set; } = string.Empty;
    public int? GrowId { get; set; }
    public int Impulse { get; set; }
    public double VentilSekunden { get; set; }
    public double Gramm { get; set; }
    /// <summary>HH:mm, wann das Ziel erstmals erreicht wurde — null, wenn nie.</summary>
    public string? ZielErreichtUm { get; set; }
    public double FlascheStartKg { get; set; }
    /// <summary>Was vor einem Flaschenwechsel am selben Tag schon gezählt war.</summary>
    public double GrammVorher { get; set; }
    /// <summary>True, wenn an diesem Tag die Flasche gewechselt wurde.</summary>
    public bool Flaschenwechsel { get; set; }
    public double? FlascheEndeKg { get; set; }
    public bool Abgeschlossen { get; set; }
    public int? JournalEntryId { get; set; }
    public int? VerbrauchId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AbgeschlossenUtc { get; set; }
}
