namespace GrowDiary.Web.Models;

/// <summary>Wofür eine Pumpe da ist — bestimmt, gegen welchen Messwert sie dosiert.</summary>
public enum DosingPurpose
{
    /// <summary>Senkt den pH (Säure).</summary>
    PhDown,
    /// <summary>Hebt den pH (Lauge).</summary>
    PhUp,
    /// <summary>Nährlösung, hebt die EC.</summary>
    Nutrient,
    /// <summary>CalMag, hebt die EC ebenfalls — getrennt, weil eigenes Verhältnis.</summary>
    CalMag,
    /// <summary>Alles andere; wird nur von Hand ausgelöst.</summary>
    Custom
}

/// <summary>Wer die Dosis ausgelöst hat.</summary>
public enum DoseTrigger
{
    Manual,
    Calibration,
    Automatic,
    /// <summary>Die zweite Hälfte eines Zweikomponenten-Düngers, zeitversetzt gegeben.</summary>
    Partner
}

/// <summary>Wie eine Dosieranfrage ausgegangen ist.</summary>
public enum DoseOutcome
{
    /// <summary>Gelaufen.</summary>
    Done,
    /// <summary>Abgelehnt — <see cref="DoseEvent.Reason"/> sagt warum.</summary>
    Rejected,
    /// <summary>Angefangen, aber Home Assistant hat nicht mitgespielt.</summary>
    Failed
}

/// <summary>
/// Eine Peristaltikpumpe, die Cultivate Control über Home Assistant schaltet.
/// </summary>
/// <remarks>
/// Cultivate Control hat selbst keine Anschlüsse; geschaltet wird immer eine HA-Entität
/// (in der Regel ein <c>switch</c>). Alles hier Gespeicherte dient einer von
/// zwei Fragen: „wie viele Sekunden sind die gewünschten Milliliter" und „darf
/// gerade überhaupt dosiert werden".
/// </remarks>
public sealed class DosingPump
{
    public int Id { get; set; }
    public int TentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DosingPurpose Purpose { get; set; } = DosingPurpose.Custom;

    /// <summary>Was im Kanister ist — „Phosphorsäure", „Athena Pro Grow A".</summary>
    public string? Agent { get; set; }

    /// <summary>Konzentration in Prozent, wie sie auf dem Kanister steht.</summary>
    public double? ConcentrationPercent { get; set; }

    /// <summary>Die zu schaltende Entität, z. B. <c>switch.dosier_ph_minus</c>.</summary>
    public string HaEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Fördermenge aus der Kalibrierung. Ohne sie lässt sich aus Millilitern
    /// keine Laufzeit machen — dann verweigert der Dienst die Dosis, statt eine
    /// Fördermenge anzunehmen.
    /// </summary>
    public double? MlPerMinute { get; set; }

    /// <summary>
    /// Preis des Mittels in Euro je Liter — fuer die Kostenrechnung je Grow.
    /// </summary>
    /// <remarks>
    /// Am Etikett steht der Flaschenpreis; je Liter eingetragen rechnet das
    /// Dosier-Protokoll (ml) direkt in Euro um. Optional: ohne Preis fehlt in
    /// der Kostenaufstellung schlicht dieser Posten, mit Hinweis.
    /// </remarks>
    public double? CostPerLiterEur { get; set; }

    public DateTime? CalibratedAtUtc { get; set; }

    /// <summary>
    /// Wann der Schlauch zuletzt gewechselt wurde. Ein Peristaltikschlauch
    /// ermüdet und fördert mit der Zeit weniger — deshalb ein eigenes Datum
    /// neben der Kalibrierung.
    /// </summary>
    public DateTime? TubeChangedAtUtc { get; set; }

    /// <summary>Tage bis zur nächsten Kalibrierung; null = keine Erinnerung.</summary>
    public int? CalibrationIntervalDays { get; set; } = 30;

    /// <summary>Tage bis zum nächsten Schlauchwechsel; null = keine Erinnerung.</summary>
    public int? TubeIntervalDays { get; set; } = 40;

    // ---------- Anschläge ----------

    /// <summary>
    /// Mehr als das geht in einem Zug nie raus, egal was die Rechnung sagt.
    /// Der wichtigste Wert hier: er macht aus einem Rechenfehler eine
    /// Unannehmlichkeit statt eines Schadens.
    /// </summary>
    public double MaxSingleDoseMl { get; set; } = 5;

    /// <summary>
    /// Sperrfrist nach einer Dosis. Erst muss die Lösung umlaufen und neu
    /// gemessen werden — sonst dosiert man gegen einen Wert, der die vorige
    /// Dosis noch gar nicht enthält, und überschießt sicher.
    /// </summary>
    public int MinIntervalMinutes { get; set; } = 18;

    public int MaxDosesPerDay { get; set; } = 6;
    public double MaxMlPerDay { get; set; } = 25;

    /// <summary>Älter als das darf der Messwert nicht sein, gegen den dosiert wird.</summary>
    public int MaxReadingAgeMinutes { get; set; } = 10;

    /// <summary>
    /// Aus heisst: keine Automatik. Von Hand dosieren bleibt möglich — sonst
    /// gäbe es keinen Weg, eine frisch eingerichtete Pumpe zu prüfen.
    /// </summary>
    public bool AutomationEnabled { get; set; }

    /// <summary>
    /// Bestätigt, dass in Home Assistant eine Abschaltung eingerichtet ist, die
    /// die Pumpe von sich aus abwirft. Ohne sie bleibt die Automatik gesperrt:
    /// stürzt Cultivate Control zwischen Ein- und Ausschalten ab, läuft die Pumpe sonst
    /// weiter, und niemand ist da, der sie stoppt.
    /// </summary>
    public bool HasHomeAssistantAutoOff { get; set; }

    /// <summary>
    /// Testbetrieb: Cultivate Control rechnet, protokolliert und zeigt die Pumpe laufen —
    /// schaltet aber nichts. Es fließt nichts.
    /// </summary>
    /// <remarks>
    /// Damit lässt sich der ganze Weg ohne Hardware durchspielen: einrichten,
    /// kalibrieren, dosieren, Protokoll lesen. Testdosen sind überall als solche
    /// markiert und fließen NICHT ins Gelernte ein — sonst stünde später unter
    /// „gelernt" eine Zahl, hinter der nie ein Tropfen war.
    /// </remarks>
    public bool SimulationMode { get; set; }

    /// <summary>
    /// Die zweite Pumpe eines Zweikomponenten-Düngers (A und B).
    /// </summary>
    /// <remarks>
    /// Zweikomponenten-Dünger dürfen sich <b>nicht konzentriert begegnen</b>:
    /// das Calcium aus A fällt mit den Sulfaten und Phosphaten aus B als Gips
    /// aus, und was ausgeflockt ist, kommt bei der Pflanze nicht mehr an — man
    /// sieht weisse Flocken und einen EC, der nicht steigt. Deshalb wird nie
    /// gleichzeitig dosiert: A läuft, dann vergeht die Trennzeit, dann B.
    /// </remarks>
    public int? PartnerPumpId { get; set; }

    /// <summary>Wie viel der Partner je Milliliter dieser Pumpe bekommt — 1,0 heisst 1:1.</summary>
    public double PartnerRatio { get; set; } = 1;

    /// <summary>
    /// Minuten zwischen A und B.
    /// </summary>
    /// <remarks>
    /// Getrennt von der Mischpause: die fragt „sagt der Messwert schon etwas",
    /// diese hier fragt „ist A weit genug verteilt, dass B nicht darauf trifft".
    /// Die zweite ist kürzer und hat einen anderen Grund.
    /// </remarks>
    public int PartnerDelayMinutes { get; set; } = 5;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Der Messwert, gegen den diese Pumpe arbeitet; null bei <see cref="DosingPurpose.Custom"/>.</summary>
    public string? MetricKey => Purpose switch
    {
        DosingPurpose.PhDown or DosingPurpose.PhUp => "reservoir-ph",
        DosingPurpose.Nutrient or DosingPurpose.CalMag => "reservoir-ec",
        _ => null,
    };

    /// <summary>true, wenn die Pumpe den Zielwert senkt statt hebt.</summary>
    public bool LowersValue => Purpose == DosingPurpose.PhDown;
}

/// <summary>
/// Eine Zeile im Dosier-Protokoll — auch für abgelehnte Anfragen.
/// </summary>
/// <remarks>
/// Abgelehnte gehören dazu: sonst rätselt man, warum über Nacht nichts
/// passiert ist. Und die durchgeführten sind zugleich das Material, aus dem
/// die Wirkung je Milliliter gelernt wird.
/// </remarks>
public sealed class DoseEvent
{
    public int Id { get; set; }
    public int PumpId { get; set; }
    public int TentId { get; set; }
    public int? GrowId { get; set; }

    public DateTime OccurredAtUtc { get; set; }
    public DoseTrigger Trigger { get; set; }
    public DoseOutcome Outcome { get; set; }

    /// <summary>Was gewünscht war.</summary>
    public double RequestedMl { get; set; }

    /// <summary>Was tatsächlich gelaufen ist — nach Deckelung, 0 bei Ablehnung.</summary>
    public double DosedMl { get; set; }

    public double SecondsRun { get; set; }

    /// <summary>Der Messwert vor der Dosis, gegen den entschieden wurde.</summary>
    public double? ValueBefore { get; set; }

    /// <summary>Nachgetragen, sobald nach der Mischzeit wieder gemessen wurde.</summary>
    public double? ValueAfter { get; set; }

    public double? TargetValue { get; set; }

    /// <summary>Klartext: warum abgelehnt, oder was ausgelöst hat.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Im Testbetrieb entstanden — es ist nichts geflossen. Wird angezeigt und
    /// beim Lernen übersprungen.
    /// </summary>
    public bool Simulated { get; set; }
}
