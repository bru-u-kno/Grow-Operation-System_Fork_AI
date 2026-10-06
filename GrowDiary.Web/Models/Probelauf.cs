namespace GrowDiary.Web.Models;

/// <summary>Fork AI (A-010): In welchem Abschnitt ein Probelauf gerade ist.</summary>
/// <remarks>Die Zahlen stehen so in der Datenbank — nie umnummerieren.</remarks>
public enum ProbelaufStatus
{
    /// <summary>Der Eingriff ist aktiv, die Grenzen werden geprüft.</summary>
    Laeuft = 0,

    /// <summary>Zurückgestellt; der Nachlauf wird noch aufgezeichnet.</summary>
    Nachlauf = 1,

    /// <summary>Planmäßig zu Ende und ausgewertet.</summary>
    Fertig = 2,

    /// <summary>Vorzeitig beendet (Grenze, Fühler, Hand) und ausgewertet.</summary>
    Abgebrochen = 3,

    /// <summary>Das Zurückstellen ist nicht bestätigt — es wird weiter versucht.</summary>
    RueckstellungOffen = 4,
}

/// <summary>
/// Fork AI (A-010): Die Grenzen, bei deren Überschreiten ein Probelauf abbricht.
/// <c>null</c> heißt: dieser Wert wird nicht überwacht.
/// </summary>
public sealed record ProbelaufGrenzen(double? FeuchteMax, double? TempMax, double? VpdMin, double? VpdMax);

/// <summary>Fork AI (A-010): Ein Messpunkt des Zelts. <c>null</c> heißt: der Fühler lieferte keinen Wert.</summary>
public sealed record ProbelaufMesswerte(DateTime ZeitUtc, double? Feuchte, double? Temp, double? Vpd);

/// <summary>Fork AI (A-010): Welche Grenze verletzt wurde, und warum — in einem deutschen Satz.</summary>
public sealed record ProbelaufVerletzung(string Groesse, string Grund);

/// <summary>Fork AI (A-010): Was sich bei einem Wert im Lauf getan hat.</summary>
/// <param name="Groesse">„Feuchte", „Temperatur" oder „VPD".</param>
/// <param name="AenderungProMinute">Ende minus Start, geteilt durch die Minuten des Eingriffs.</param>
/// <param name="ErholungMinuten">Minuten nach dem Ende, bis der Wert wieder am Niveau vor dem Lauf liegt; <c>null</c>, wenn nicht erreicht.</param>
public sealed record ProbelaufKennzahl(
    string Groesse, double Start, double Spitze, double Ende, double AenderungProMinute, double? ErholungMinuten);

/// <summary>Fork AI (A-010): Die aufgezeichneten Messpunkte eines Laufs in drei Abschnitten.</summary>
public sealed record ProbelaufMessreihe(
    List<ProbelaufMesswerte> Vorlauf, List<ProbelaufMesswerte> Waehrend, List<ProbelaufMesswerte> Nachlauf)
{
    public static ProbelaufMessreihe Leer() => new([], [], []);
}

/// <summary>Fork AI (A-010): Das Ergebnis eines Laufs — Kennzahlen und Hinweise.</summary>
public sealed record ProbelaufAuswertung(
    IReadOnlyList<ProbelaufKennzahl> Kennzahlen,
    IReadOnlyList<string> Hinweise,
    bool? TagPhaseBeiStart,
    bool? TagPhaseBeiEnde);

/// <summary>
/// Fork AI (A-010, 07.10.2026): Ein Probelauf — vom Start bis zur Auswertung, so wie er in der Datenbank steht.
/// </summary>
public sealed class ProbelaufLauf
{
    public long Id { get; set; }

    /// <summary>Die Steuerung, an der eingegriffen wurde (<c>entfeuchter</c>, <c>chiller</c> …).</summary>
    public string Modul { get; set; } = string.Empty;

    public ProbelaufStatus Status { get; set; }

    public DateTime StartUtc { get; set; }

    /// <summary>Wann der Eingriff planmäßig endet.</summary>
    public DateTime GeplantesEndeUtc { get; set; }

    /// <summary>Spätestens dann wird zurückgestellt, was auch passiert — auch nach einem Neustart.</summary>
    public DateTime HartesEndeUtc { get; set; }

    /// <summary>Wann zurückgestellt wurde (der Eingriff endete); danach läuft nur noch der Nachlauf.</summary>
    public DateTime? EingriffEndeUtc { get; set; }

    /// <summary>Wann der Lauf ganz abgeschlossen war.</summary>
    public DateTime? EndeUtc { get; set; }

    public string? AbbruchGrund { get; set; }

    public ProbelaufGrenzen Grenzen { get; set; } = new(null, null, null, null);

    /// <summary>Der Zustand vor dem Eingriff (JSON, mit den damals aufgelösten Entity-IDs) — damit wird zurückgestellt.</summary>
    public string Ausgangszustand { get; set; } = "{}";

    public ProbelaufMessreihe Messreihe { get; set; } = ProbelaufMessreihe.Leer();

    public ProbelaufAuswertung? Auswertung { get; set; }

    /// <summary>Die Empfehlung, die die KI zu diesem Lauf abgelegt hat (nur bei „KI an").</summary>
    public string? Empfehlung { get; set; }

    public bool? TagPhaseBeiStart { get; set; }

    /// <summary>Wie oft das Zurückstellen schon versucht wurde (für die Meldung an den Nutzer).</summary>
    public int RueckstellVersuche { get; set; }

    /// <summary>Seit wann ein Fühler nichts mehr meldet — für die Abbruchregel „über eine Minute".</summary>
    public DateTime? FuehlerLosSeitUtc { get; set; }

    /// <summary>Wann zuletzt ein Messpunkt aufgezeichnet wurde.</summary>
    public DateTime? LetzteMessungUtc { get; set; }
}
