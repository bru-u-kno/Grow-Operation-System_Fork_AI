using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Fork AI (A-010): Ein Messpunkt des Zelts samt der Lichtphase in diesem Moment.</summary>
public sealed record ProbelaufMomentaufnahme(ProbelaufMesswerte Werte, bool? TagPhase);

/// <summary>
/// Fork AI (A-010, 07.10.2026): Was der Probelauf vom Zelt messen will — an einer Naht, damit der
/// Dienst ohne Anlage prüfbar ist. Die Fühler werden über die zentrale Zuordnung gelesen.
/// </summary>
public interface IProbelaufMessung
{
    /// <summary>Die aktuellen Werte; <c>null</c>, wenn Home Assistant nicht antwortet.</summary>
    Task<ProbelaufMomentaufnahme?> JetztAsync(CancellationToken ct);

    /// <summary>Der Verlauf aus Home Assistant, auf Minuten zusammengelegt.</summary>
    Task<IReadOnlyList<ProbelaufMesswerte>> VerlaufAsync(DateTime vonUtc, DateTime bisUtc, CancellationToken ct);

    /// <summary>Die vorgeschlagenen Grenzen aus den Pflanzenzielen (Luftfeuchte max., Temperatur max., VPD-Band).</summary>
    Task<ProbelaufGrenzen> VoreinstellungAsync(CancellationToken ct);
}

/// <summary>Fork AI (A-010): Wie der Nutzer benachrichtigt wird, wenn das Zurückstellen nicht gelingt.</summary>
public interface IProbelaufMeldung
{
    Task SendenAsync(string titel, string text, CancellationToken ct);
}
