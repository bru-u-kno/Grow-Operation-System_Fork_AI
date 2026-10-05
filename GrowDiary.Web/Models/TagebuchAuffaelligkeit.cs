namespace GrowDiary.Web.Models;

/// <summary>
/// Ein gemerkter Sprung im Sensorverlauf eines Zelts (A-006, Grow-Tagebuch).
/// </summary>
/// <remarks>
/// <para><b>Warum gespeichert und nicht jedes Mal gerechnet.</b> Die Rohwerte
/// leben sieben Tage (<c>HomeAssistantSnapshotWorker.CleanupOldReadingsAsync</c>),
/// danach bleiben nur Tageswerte — und in Min/Median/Max ist eine Stufe von
/// 16:10 bis 16:35 nicht mehr zu sehen. Gerechnet am Lesen verschwände Brus
/// „EC fiel um 0,13" nach einer Woche still aus dem Tagebuch. Deshalb wird ein
/// Sprung beim Erkennen festgehalten — beim Öffnen des Tagebuchs und jede Nacht
/// vor dem Aufräumen der Rohwerte.</para>
///
/// <para><b>Was hier NICHT steht:</b> ob es einen passenden Eintrag gibt. Das
/// entscheidet das Tagebuch beim Lesen — wer das Nachfüllen später einträgt,
/// soll die Zeile verschwinden sehen, ohne dass hier etwas umgeschrieben wird.</para>
/// </remarks>
public sealed class TagebuchAuffaelligkeit
{
    public int Id { get; set; }
    public int TentId { get; set; }
    public string MetricKey { get; set; } = string.Empty;

    /// <summary>Der letzte ruhige Wert vor der Stufe (UTC).</summary>
    public DateTime BeginnUtc { get; set; }

    /// <summary>Der erste ruhige Wert danach (UTC).</summary>
    public DateTime EndeUtc { get; set; }

    public double Vorher { get; set; }
    public double Nachher { get; set; }
    public DateTime ErkanntAmUtc { get; set; } = DateTime.UtcNow;

    /// <summary>„War nichts" — gesetzt, wenn der Bediener die Zeile ausgeblendet hat.</summary>
    public DateTime? VerworfenAmUtc { get; set; }
}
