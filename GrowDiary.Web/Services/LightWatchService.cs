using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Was Grow OS aus den beobachteten Lichtflanken macht.
/// </summary>
/// <remarks>
/// Zwei Aufgaben: den Zyklus lernen (fuer die Kachel und den Abgleich mit der
/// Phase) und den Einbruch in die Dunkelphase melden. Beides aus Daten, die
/// ohnehin schon aufgezeichnet wurden — es hat sie nur nie jemand gelesen.
/// </remarks>
public sealed class LightWatchService
{
    private readonly LightCycleReader _cycles;
    private readonly NotificationService _notifications;
    private readonly ILogger<LightWatchService> _logger;

    public LightWatchService(
        LightCycleReader cycles,
        NotificationService notifications,
        ILogger<LightWatchService> logger)
    {
        _cycles = cycles;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>
    /// Eine frische Einschaltflanke pruefen — und bei Einbruch sofort pushen.
    /// </summary>
    /// <remarks>
    /// Sofort heisst sofort: ein Push jetzt kann die Nacht noch retten, ein
    /// Blick ins Protokoll uebermorgen nicht.
    /// </remarks>
    public async Task CheckIntrusionAsync(Tent tent, LightTransitionEvent transition, CancellationToken cancellationToken)
    {
        if (transition.Kind != LightTransitionKind.LightOn) return;
        if (tent.ActiveGrows.Count == 0) return;

        var cycle = _cycles.CycleFor(tent.Id, transition.OccurredAtUtc);
        var lokal = TimeOnly.FromDateTime(transition.OccurredAtUtc + _cycles.LocalOffset(tent.Id));

        /* JEDER Grow im Zelt, nicht der erste.
           Hier stand `ActiveGrows.FirstOrDefault()`. Steht neben dem
           Photoperioden-Grow in Bluetewoche 6 eine spaeter gesteckte
           Autoflower, lieferte die Liste womoeglich die Autoflower — und fuer
           die ist Licht in der Nacht kein Einbruch. Der Alarm fiel damit fuer
           das GANZE Zelt aus. Die Lampe leuchtet aber auf beide. */
        var betroffen = tent.ActiveGrows.Any(grow => LightIntrusionGuard.IsIntrusion(
            cycle, lokal, GrowStageResolver.Resolve(grow, DateTime.Today), grow.SeedType));
        if (!betroffen) return;

        _logger.LogWarning(
            "Lichteinbruch in der Dunkelphase: Zelt {TentId} um {Zeit}.", tent.Id, lokal);

        /* TROTZ Ruhezeit.
           Diese Meldung entsteht per Definition in der Dunkelphase, und die
           uebliche Ruhezeit 22-07 ueberdeckt neun der zwoelf Dunkelstunden
           eines 12/12-Zelts. Der Kommentar oben sagt es selbst: ein Push jetzt
           kann die Nacht noch retten, ein Blick ins Protokoll uebermorgen
           nicht. */
        await _notifications.SendAsync(
            NotificationCategory.Risk,
            "Licht in der Dunkelphase",
            LightIntrusionGuard.Message(tent.Name, cycle!, lokal),
            cancellationToken,
            trotzRuhezeit: true,
            seite: NotificationService.LiveSeite(tent.Id));
    }

}
