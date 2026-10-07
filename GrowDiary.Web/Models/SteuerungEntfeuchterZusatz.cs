using System.Text.Json.Serialization;

namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (A-009, 06.10.2026): Der Zusatz-Entfeuchter — ein zweiter Trotec im
/// Zelt (Shelly-Steckdose), der neben dem Entfeuchter am AC-Infinity-Port läuft.
/// </summary>
/// <remarks>
/// <para><b>Führen und Folgen.</b> Das bestehende Gerät (Modul
/// <c>entfeuchter</c>) ist das Führungsgerät, der Shelly das Folgegerät. Nie
/// dürfen beide gleichzeitig ausgehen: der Zusatz geht wegen Wärme früher aus
/// (<see cref="EntfeuchterZusatzEinstellungen.FolgeAbstandK"/>) und wegen
/// VPD/Feuchte nur, solange die Führung läuft. Geregelt wird in Home Assistant
/// (Vorlage <c>Vorlagen/entfeuchter-zusatz/regelung.json</c>); der Fork schreibt
/// nur die Helfer.</para>
///
/// <para><b>Die Höchsttemperatur gehört beiden.</b> <c>TempMax*</c> sind
/// <i>dieselben</i> Felder wie in <see cref="EntfeuchterEinstellungen"/> — kein
/// zweiter Datensatz. Der Dienst liest sie dort und schreibt sie dorthin
/// zurück; die Kopie im eigenen Dokument ist nie die Quelle.</para>
///
/// <para><b>Speichern schreibt nur, was geändert wurde</b>
/// (<see cref="EntfeuchterZusatzAenderung"/>). Am 06.10.2026 hat ein Speichern
/// unbemerkt die Tag-Grenze von 26,5 auf 29 °C zurückgesetzt, weil die Seite
/// alle Felder schickte.</para>
/// </remarks>
public sealed class EntfeuchterZusatzEinstellungen
{
    /// <summary>
    /// <c>aus</c>, <c>sparsam</c>, <c>normal</c>, <c>kraeftig</c> — gespeichert.
    /// In der Antwort steht <see cref="EntfeuchterZusatzHilfe.Erkennen"/>: passen
    /// die Einzelwerte zu keiner Voreinstellung, heißt es <c>eigene</c>.
    /// </summary>
    public string Hilfe { get; set; } = EntfeuchterZusatzHilfe.Normal;

    /// <summary>Aus hält die Regelung an (Automation aus); das Gerät bleibt, wie es ist.</summary>
    public bool AutomatikAktiv { get; set; } = true;

    /// <summary>Aus: nur in der Dunkelphase.</summary>
    public bool TagbetriebErlauben { get; set; } = true;

    /// <summary>An: nachts läuft er, bis das Zelt zu warm wird (keine Feuchte-Schwellen).</summary>
    public bool NachtDurchlaufen { get; set; } = true;

    /// <summary>Abstand des VPD-Bands um das Plan-Ziel (EIN unter Ziel − Wert, AUS über Ziel + Wert).</summary>
    public double VpdHystereseKpa { get; set; } = 0.15;

    /// <summary>So lange muss die Führung laufen, bevor der Zusatz zuschaltet (läuft sie nicht: sofort).</summary>
    public int ZuschaltVerzoegerungMin { get; set; } = 10;

    /// <summary>Der Zusatz geht so viel früher aus als die Grenze — nur wenn die Führung läuft.</summary>
    public double FolgeAbstandK { get; set; } = 1;

    /// <summary>Wieder an erst so viel unter „Folge aus".</summary>
    public double WiederEinAbstandK { get; set; } = 1;

    /// <summary>Abschalten wegen VPD/Feuchte erst nach dieser Laufzeit.</summary>
    public int MindestlaufzeitMin { get; set; } = 15;

    /// <summary>Kompressorschutz: so lange bleibt er nach dem Ausschalten mindestens aus.</summary>
    public int MindestpauseMin { get; set; } = 10;

    public EntfeuchterZusatzMeldung Meldung { get; set; } = new();

    /// <summary><c>tank</c> oder <c>schlauch</c> — nur Anzeige (voller Tank ist nur beim Tank ein Thema).</summary>
    public string Ablauf { get; set; } = EntfeuchterZusatzAblauf.Tank;

    // --- Höchsttemperatur: gemeinsame Quelle (EntfeuchterEinstellungen) -------

    public string TempMaxTagModus { get; set; } = TempMaxModus.Fest;
    public double TempMaxTagAbstandK { get; set; } = 5;
    public double TempMaxTagFestC { get; set; } = 29;
    public string TempMaxNachtModus { get; set; } = TempMaxModus.Fest;
    public double TempMaxNachtAbstandK { get; set; } = 5;
    public double TempMaxNachtFestC { get; set; } = 25;
}

/// <summary>Die Meldung „zieht nichts": Shelly an, Leistung unter der Grenze.</summary>
public sealed class EntfeuchterZusatzMeldung
{
    public bool Aktiv { get; set; } = true;

    /// <summary>Darunter zieht er nichts. Normalbetrieb gemessen 300–335 W.</summary>
    public int GrenzeW { get; set; } = 60;

    /// <summary>So lange muss es bestehen, bevor gemeldet wird.</summary>
    public int DauerMin { get; set; } = 5;

    /// <summary>Wiederholung, solange es besteht.</summary>
    public int WiederholungH { get; set; } = 2;
}

public static class EntfeuchterZusatzAblauf
{
    public const string Tank = "tank";
    public const string Schlauch = "schlauch";
}

/// <summary>
/// Was ein Speichern ändern will — <b>alle Felder optional</b>. Geschrieben wird
/// nur, was vorkommt (nicht null); alles andere bleibt unangetastet.
/// </summary>
public sealed class EntfeuchterZusatzAenderung
{
    public string? Hilfe { get; set; }
    public bool? AutomatikAktiv { get; set; }
    public bool? TagbetriebErlauben { get; set; }
    public bool? NachtDurchlaufen { get; set; }
    public double? VpdHystereseKpa { get; set; }
    public int? ZuschaltVerzoegerungMin { get; set; }
    public double? FolgeAbstandK { get; set; }
    public double? WiederEinAbstandK { get; set; }
    public int? MindestlaufzeitMin { get; set; }
    public int? MindestpauseMin { get; set; }
    public EntfeuchterZusatzMeldungAenderung? Meldung { get; set; }
    public string? Ablauf { get; set; }

    public string? TempMaxTagModus { get; set; }
    public double? TempMaxTagAbstandK { get; set; }
    public double? TempMaxTagFestC { get; set; }
    public string? TempMaxNachtModus { get; set; }
    public double? TempMaxNachtAbstandK { get; set; }
    public double? TempMaxNachtFestC { get; set; }

    /// <summary>Kommt irgendein gemeinsames <c>TempMax*</c>-Feld vor?</summary>
    [JsonIgnore]
    public bool BetrifftTempMax
        => TempMaxTagModus is not null || TempMaxTagAbstandK is not null || TempMaxTagFestC is not null
           || TempMaxNachtModus is not null || TempMaxNachtAbstandK is not null || TempMaxNachtFestC is not null;
}

public sealed class EntfeuchterZusatzMeldungAenderung
{
    public bool? Aktiv { get; set; }
    public int? GrenzeW { get; set; }
    public int? DauerMin { get; set; }
    public int? WiederholungH { get; set; }
}

/// <summary>
/// Die Hilfsstärke: eine Voreinstellung statt fünf Zahlen. Eine Tabelle, die der
/// Dienst und die Tests lesen — nicht an zwei Stellen abgetippt.
/// </summary>
public static class EntfeuchterZusatzHilfe
{
    public const string Aus = "aus";
    public const string Sparsam = "sparsam";
    public const string Normal = "normal";
    public const string Kraeftig = "kraeftig";
    public const string Eigene = "eigene";

    /// <summary>Die Werte einer Voreinstellung (ENTSCHEIDUNGEN A-009, Punkt 3).</summary>
    public sealed record Voreinstellung(
        string Name,
        double FolgeAbstandK,
        double WiederEinAbstandK,
        double VpdHystereseKpa,
        int ZuschaltVerzoegerungMin,
        int MindestpauseMin);

    public static IReadOnlyList<Voreinstellung> Alle { get; } =
    [
        new(Sparsam, 1.5, 1, 0.25, 20, 15),
        new(Normal, 1, 1, 0.15, 10, 10),
        new(Kraeftig, 0.5, 0.5, 0.10, 5, 10),
    ];

    /// <summary>Alle Namen, die ein Speichern annimmt.</summary>
    public static IReadOnlyList<string> Zulaessig { get; } = [Aus, Sparsam, Normal, Kraeftig, Eigene];

    public static Voreinstellung? Finden(string? name)
        => Alle.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));

    /// <summary>Stimmen die Einzelwerte mit der Voreinstellung überein?</summary>
    public static bool Passt(Voreinstellung v, EntfeuchterZusatzEinstellungen e)
        => Math.Abs(v.FolgeAbstandK - e.FolgeAbstandK) < 1e-6
           && Math.Abs(v.WiederEinAbstandK - e.WiederEinAbstandK) < 1e-6
           && Math.Abs(v.VpdHystereseKpa - e.VpdHystereseKpa) < 1e-6
           && v.ZuschaltVerzoegerungMin == e.ZuschaltVerzoegerungMin
           && v.MindestpauseMin == e.MindestpauseMin;

    /// <summary>
    /// Was die Seite als Hilfsstärke zeigt: <c>aus</c>, sonst die passende
    /// Voreinstellung, sonst <c>eigene</c>.
    /// </summary>
    public static string Erkennen(EntfeuchterZusatzEinstellungen e)
    {
        if (e.Hilfe == Aus) return Aus;
        return Alle.FirstOrDefault(v => Passt(v, e))?.Name ?? Eigene;
    }
}

/// <summary>Womit der Fork schaltet: das VPD-Ziel des Plans, sonst dessen Luftfeuchte, sonst nichts.</summary>
public static class EntfeuchterZusatzSchaltgroesse
{
    public const string Vpd = "vpd";
    public const string Feuchte = "feuchte";
    public const string Keine = "keine";
}

/// <summary>
/// Das Livebild des Zusatz-Entfeuchters. <b>Was Home Assistant nicht liefert, ist
/// <c>null</c></b> — nie 0 erfinden.
/// </summary>
/// <remarks>
/// Die App lässt <c>null</c> sonst aus dem JSON weg. Hier steht es ausdrücklich da
/// (<see cref="JsonIgnoreCondition.Never"/>): die Seite unterscheidet „unbekannt" (<c>null</c>)
/// von „aus"/„0", und ein fehlendes Feld ist in JavaScript <c>undefined</c>, nicht <c>null</c>.
/// </remarks>
public sealed record EntfeuchterZusatzLive(
    bool HaErreichbar,
    string FuehrungName,
    string ZusatzName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? PlanWoche,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? PlanLuftTagC,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? PlanLuftNachtC,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? TempMaxTagC,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? TempMaxNachtC,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? FolgeAusTagC,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? FolgeAusNachtC,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? WiederEinTagC,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? WiederEinNachtC,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? TempC,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? FeuchteProzent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Vpd,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? TagPhase,
    string Schaltgroesse,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? VpdZiel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? VpdEinSchwelle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? VpdAusSchwelle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? FeuchteEinProzent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? FeuchteAusProzent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? ZusatzAn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? ZusatzOnline,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? LeistungW,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? EnergieHeuteKwh,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? FuehrungAn,
    bool ZiehtNichts,
    bool PlanUnvollstaendig,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? AutomatikAn,
    /// <summary>A-014: Obergrenze der Luftfeuchte aus dem Plan — dasselbe Ziel wie auf der Seite des Hauptentfeuchters, damit beide Seiten dieselbe Zone zeigen.</summary>
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? RhObergrenzeProzent = null);

public sealed record EntfeuchterZusatzSeiteDto(EntfeuchterZusatzEinstellungen Einstellungen, EntfeuchterZusatzLive Live)
{
    /// <summary>Nach dem Speichern: ob Home Assistant alles angenommen hat, was geschrieben werden musste (null beim Lesen).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? HaAngenommen { get; init; }

    /// <summary>True, wenn die Werte bei diesem Aufruf aus den vorhandenen Helfern übernommen wurden (oder Home Assistant fehlt) statt aus der Datenbank.</summary>
    public bool AusHomeAssistantUebernommen { get; init; }

    public int GeraeteZugeordnet { get; init; }
    public int GeraeteGesamt { get; init; }

    /// <summary>Was beim Speichern nicht ausgeführt werden konnte (z. B. der Shelly ließ sich nicht ausschalten) — leer, wenn alles ging.</summary>
    public IReadOnlyList<string> Hinweise { get; init; } = [];
}

// ------------------------------------------------------------------- Namen

/// <summary>Der Anzeigename eines Entfeuchter-Geräts — und was gilt, wenn keiner gesetzt ist.</summary>
public sealed record EntfeuchterNameDto(string Anzeigename, string Vorgabe);

public sealed record EntfeuchterNamenDto(EntfeuchterNameDto Fuehrung, EntfeuchterNameDto Zusatz);

/// <summary>Gespeichert in <c>ForkSteuerungEinstellungen</c> (Modul <c>entfeuchter-namen</c>). Leer = Vorgabe.</summary>
public sealed class EntfeuchterNamen
{
    public string? Fuehrung { get; set; }
    public string? Zusatz { get; set; }
}

/// <summary>
/// Was ein Speichern der Namen ändern will. <b>Fehlt ein Feld im Body, bleibt der
/// Name; <c>null</c> oder leer setzt ihn auf die Vorgabe.</b> Der Unterschied
/// zwischen „nicht genannt" und „genannt als null" steckt in den
/// <c>…Gesetzt</c>-Flags, die der Deserialisierer über die Setter füllt.
/// </summary>
public sealed class EntfeuchterNamenAenderung
{
    private string? _fuehrung;
    private string? _zusatz;

    public string? Fuehrung
    {
        get => _fuehrung;
        set { _fuehrung = value; FuehrungGesetzt = true; }
    }

    public string? Zusatz
    {
        get => _zusatz;
        set { _zusatz = value; ZusatzGesetzt = true; }
    }

    [JsonIgnore] public bool FuehrungGesetzt { get; private set; }
    [JsonIgnore] public bool ZusatzGesetzt { get; private set; }
}
