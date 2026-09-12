namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI: Die Licht-Steuerung („LED Top") als Leitstand im Fork.
/// </summary>
/// <remarks>
/// <para><b>Warum hier keine Regelung steht.</b> Beim CO₂ regelt Home Assistant
/// im Minutentakt, der Fork besitzt nur die Sollwerte. Beim Licht gibt es nichts
/// laufend zu regeln: den Zeitplan führt der AC-Infinity-Controller selbst aus,
/// auch wenn Add-on, Home Assistant und Internet aus sind. Geschrieben wird nur,
/// wenn jemand etwas ändert. Deshalb darf der Fork hier direkt am Gerät
/// schalten — ohne eine HA-Automation dazwischen, die nichts zu tun hätte.</para>
///
/// <para><b>Die Zeitpläne sind Vorlagen, nicht Zustände.</b> Der Controller
/// kennt genau EINE Ein- und eine Aus-Zeit. „Veggie" und „Blüte" sind zwei
/// gespeicherte Paare, die diese eine Zeit überschreiben. Welches Paar gerade
/// gilt, erkennt man nur daran, dass die Controller-Zeiten mit ihm
/// übereinstimmen — ein eigener Zustand dafür wäre eine zweite Wahrheit.</para>
///
/// <para><b>Schreibvorgaben.</b> Die AC-Infinity-Cloud verwirft parallele
/// Schreibvorgänge („Unable to update device controls"). Die Werte hier sind
/// die Lehre aus dem Fehler vom 23.08.2026: nur Abweichendes schreiben, Abstand
/// zwischen den Aufrufen, später prüfen und wiederholen.</para>
/// </remarks>
public sealed class LichtEinstellungen
{
    /// <summary>Zeitplan „Veggie" — Ein-Zeit, HH:mm.</summary>
    public string VeggieEin { get; set; } = "05:00";
    public string VeggieAus { get; set; } = "23:00";

    /// <summary>Zeitplan „Blüte" — Ein-Zeit, HH:mm.</summary>
    public string BlueteEin { get; set; } = "05:00";
    public string BlueteAus { get; set; } = "17:00";

    /// <summary>Zuletzt gewollte Leistungsstufe (1–10). Der Ist-Wert steht im Livebild.</summary>
    public int Stufe { get; set; } = 7;

    /// <summary>Abstand zwischen zwei Schreibvorgängen an den Controller, in Millisekunden.</summary>
    public int SchreibAbstandMs { get; set; } = 2000;

    /// <summary>Nach so vielen Sekunden wird geprüft, ob der Controller den Befehl übernommen hat.</summary>
    public int VerifySekunden { get; set; } = 20;

    /// <summary>Wie oft ein nicht übernommener Befehl wiederholt wird.</summary>
    public int MaxWiederholungen { get; set; } = 2;

    /// <summary>
    /// Die vier <c>input_datetime.led_top_zeitplan_*</c>-Helfer mitschreiben.
    /// Sie speisen die alte Karte im Grow-Dashboard; solange die steht, dürfen
    /// beide Oberflächen nicht auseinanderlaufen.
    /// </summary>
    public bool HelferSpiegeln { get; set; } = true;
}

/// <summary>Ein offener Schreibvorgang: was gewollt war, seit wann, wie oft schon versucht.</summary>
/// <param name="EntityId">Die Entität am Controller.</param>
/// <param name="Soll">Der gewollte Zustand als Text, so wie ihn Home Assistant meldet.</param>
/// <param name="SeitUtc">Wann zuletzt geschrieben wurde — die Prüffrist läuft ab hier.</param>
/// <param name="Versuche">Bisherige Schreibversuche, der erste zählt mit.</param>
public sealed record LichtOffen(string EntityId, string Soll, DateTime SeitUtc, int Versuche);
