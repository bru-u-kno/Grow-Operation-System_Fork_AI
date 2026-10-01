namespace GrowDiary.Web.Models;

/// <summary>
/// Eine Dosis, die noch aussteht — die zweite Hälfte eines Zweikomponenten-Düngers.
/// </summary>
/// <remarks>
/// A und B dürfen sich nicht konzentriert begegnen: das Calcium aus A fällt mit
/// den Sulfaten und Phosphaten aus B als Gips aus. Was ausgeflockt ist, kommt
/// bei der Pflanze nie an — man sieht weisse Flocken und einen EC, der trotz
/// Dünger nicht steigt.
///
/// Also läuft A, dann vergeht die Trennzeit, dann B. Dazwischen steht B hier.
/// Bewusst in der Datenbank und nicht im Speicher: ein Neustart des Add-ons
/// zwischen A und B — ein Update genügt — würde B sonst wortlos verschlucken,
/// und im Becken stünde A allein. Das sähe niemand, bis die Pflanzen es zeigen.
/// </remarks>
public sealed class PendingDose
{
    public int Id { get; set; }
    public int PumpId { get; set; }
    public double Ml { get; set; }

    /// <summary>Ab wann sie gegeben werden darf.</summary>
    public DateTime DueAtUtc { get; set; }

    /// <summary>Die Dosis, aus der sie hervorging — für das Protokoll.</summary>
    public int? SourceDoseEventId { get; set; }

    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Wie oft diese Hälfte schon an einem nicht erreichbaren Home Assistant
    /// gescheitert ist (<c>Pumpenlauf.NichtGesendet</c> — nachweislich nichts
    /// geflossen).
    /// </summary>
    /// <remarks>
    /// Bis zum 01.10.2026 war die Hälfte nach dem ersten solchen Fehlschlag
    /// endgültig weg, obwohl sicher nichts geflossen war. Jetzt bleibt sie
    /// stehen — aber nicht für immer: nach
    /// <see cref="Services.PartnerDosing.MaxFehlversuche"/> Versuchen wird sie
    /// verworfen und protokolliert, sonst hielte ein dauerhaft fehlendes Home
    /// Assistant das ganze Becken an.
    /// </remarks>
    public int Fehlversuche { get; set; }
}
