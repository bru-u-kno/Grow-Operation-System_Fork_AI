using GrowDiary.Web.Infrastructure;

namespace GrowDiary.Web.Services;

/// <summary>
/// Gleicht die Abweichungs-Karten ab, sobald es für einen Grow eine neue Messung
/// gibt — im Minutentakt des Wächters, ohne dass jemand die Seite öffnen muss.
/// </summary>
/// <remarks>
/// <para><b>Warum (16.10.2026).</b> Der Abgleich lief nur, wenn jemand
/// <c>GET /api/risk-events</c> aufrief. Eine Abweichung, die mit der Messung um
/// 05:31 verschwand, blieb bis zum nächsten Seitenaufruf als „Besteht noch" stehen,
/// und „Wieder okay" erschien nicht, wann es wahr wurde, sondern wann jemand
/// hinsah.</para>
///
/// <para><b>Billig gehalten:</b> je Grow und Minute nur der Fingerabdruck der
/// Messungen (<see cref="GrowRepository.GetMeasurementFingerprint"/>, eine
/// Abfrage ohne Zeilen). Abgeglichen wird nur, wenn er sich geändert hat; der
/// erste Durchlauf nach dem Start gleicht einmal alles ab. Der Stand liegt im
/// Speicher: nach einem Neustart kostet das einen zusätzlichen, harmlosen
/// Abgleich (der Abgleich ist idempotent).</para>
///
/// <para>Scheitert der Abgleich eines Grows, wird das gemeldet und es wird in der
/// nächsten Minute noch einmal versucht. Besteht der Fehler weiter, steht die Meldung
/// jede Minute im Protokoll — das ist gewollt: Eine veraltete Karte bis zur nächsten
/// Messung, bei zwei am Tag bis zu zwölf Stunden, wäre die schlechtere Wahl.</para>
/// </remarks>
public sealed class RisikoAbgleichRunner
{
    private readonly GrowRepository _repository;
    private readonly DeviationRiskEventSyncService _sync;
    private readonly ILogger<RisikoAbgleichRunner> _logger;
    private readonly Dictionary<int, string> _gesehen = new();

    public RisikoAbgleichRunner(
        GrowRepository repository,
        DeviationRiskEventSyncService sync,
        ILogger<RisikoAbgleichRunner> logger)
    {
        _repository = repository;
        _sync = sync;
        _logger = logger;
    }

    /// <summary>Ein Durchlauf; liefert die Zahl der abgeglichenen Grows (für Tests und das Protokoll).</summary>
    public int Einmal()
    {
        var abgeglichen = 0;
        var aktiv = _repository.GetActiveGrows();
        foreach (var grow in aktiv)
        {
            string fingerabdruck;
            try
            {
                fingerabdruck = _repository.GetMeasurementFingerprint(grow.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Risiko-Abgleich: Messungen von Grow {GrowId} nicht lesbar.", grow.Id);
                continue;
            }

            if (_gesehen.TryGetValue(grow.Id, out var bekannt) && bekannt == fingerabdruck)
            {
                continue;
            }

            _gesehen[grow.Id] = fingerabdruck;
            try
            {
                _sync.SyncGrow(grow);
                abgeglichen++;
            }
            catch (Exception ex)
            {
                // Den Fingerabdruck wieder vergessen: ein einmaliger Fehler (etwa eine
                // gesperrte Datenbank) soll in der nächsten Minute noch einmal versucht
                // werden und nicht bis zur nächsten Messung, bei zwei am Tag also bis
                // zu zwölf Stunden, eine veraltete Karte stehen lassen.
                _gesehen.Remove(grow.Id);
                _logger.LogWarning(ex, "Risiko-Abgleich fehlgeschlagen: Grow {GrowId}, nächster Versuch in einer Minute.", grow.Id);
            }
        }

        // Beendete Grows vergessen, damit der Speicher nicht mit ihnen wächst.
        var aktiveIds = aktiv.Select(grow => grow.Id).ToHashSet();
        foreach (var id in _gesehen.Keys.Where(id => !aktiveIds.Contains(id)).ToList())
        {
            _gesehen.Remove(id);
        }

        return abgeglichen;
    }
}
