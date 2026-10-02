using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Welche Phase ein Grow an einem Tag hat — aus dem Grow selbst, ohne dass
/// jemand gemessen haben muss.
/// </summary>
/// <remarks>
/// <para>Vorher kam die Phase aus der letzten erfassten Messung. Wer noch nie
/// von Hand gemessen hatte, bekam deshalb auf dem ganzen Live-Bildschirm keinen
/// einzigen Zielbereich.</para>
///
/// <para><b>Seit dem 02.10.2026 nur noch die Kurzform des
/// <see cref="Phasenanker"/>.</b> Hier stand bis dahin eine eigene Rechnung:
/// 14 Tage Sämling ab Keimung, Autoflower nach 28 Tagen automatisch in die
/// Blüte, der Einstiegspunkt nur ohne Keimdatum. Drei andere Stellen rechneten
/// dieselben Beginne anders. Jetzt entstehen alle Phasenbeginne im Anker; diese
/// Klasse bleibt, weil über fünfzehn Leser nur die <see cref="GrowStage"/>
/// brauchen.</para>
/// </remarks>
public static class GrowStageResolver
{
    /// <summary>Die Phase an einem Tag — siehe <see cref="Phasenanker.Fuer"/>.</summary>
    public static GrowStage Resolve(GrowRun grow, DateTime today)
        => Phasenanker.Fuer(grow, today).Stufe;
}
