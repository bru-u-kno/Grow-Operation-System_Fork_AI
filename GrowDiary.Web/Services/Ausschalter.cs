using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Schaltet eine Entität aus — mit Nachkontrolle und Wiederholung.
/// </summary>
/// <remarks>
/// <para><b>Warum nicht einfach <c>turn_off</c>.</b> Jeder Aufruf an Home
/// Assistant hat ein Zeitlimit von vier Sekunden. Läuft es ab, ist offen, ob der
/// Befehl angekommen ist — <see cref="HomeAssistantService"/> meldet dann
/// <c>false</c>, obwohl der Schalter womöglich längst umgelegt ist. Für das
/// EINschalten heisst das: „nicht geschaltet" ist keine Aussage. Für das
/// AUSschalten heisst es: ein einzelner Versuch ist kein Beleg.</para>
///
/// <para>Deshalb wird nach jedem Versuch nachgelesen, ob die Entität „aus"
/// meldet, und sonst wiederholt — auch wenn schon das Senden scheiterte.
/// <c>turn_off</c> ist idempotent, ein Versuch zu viel kostet nichts.</para>
///
/// <para>Anlass (29.09.2026): Pumpe und CO₂-Probe kehrten nach einem
/// gescheiterten Einschalten zurück, ohne je „aus" zu senden. Kam das „an"
/// trotzdem durch, lief die Pumpe bis zur Abschaltung in Home Assistant und
/// das Ventil blieb offen.</para>
/// </remarks>
public sealed class Ausschalter
{
    /// <summary>Wie oft „aus" höchstens gesendet wird.</summary>
    public const int Versuche = 3;

    /// <summary>Wartezeit zwischen Senden und Nachlesen.</summary>
    /// <remarks>
    /// Zwei Sekunden wie <see cref="AcSchreiber.Pause"/> — die AC-Infinity-Wolke
    /// meldet den neuen Zustand träge zurück, ein Steckdosen-Schalter sofort.
    /// </remarks>
    public static readonly TimeSpan Nachlesepause = TimeSpan.FromSeconds(2);

    private readonly IAcFunk _funk;
    private readonly ILogger<Ausschalter> _logger;

    public Ausschalter(IAcFunk funk, ILogger<Ausschalter> logger)
    {
        _funk = funk;
        _logger = logger;
    }

    /// <summary>
    /// Schaltet aus und liefert <c>true</c> nur, wenn die Entität hinterher
    /// „aus" meldet.
    /// </summary>
    /// <remarks>
    /// Läuft bewusst ohne Abbruch-Token: wer ausschaltet, weil etwas
    /// schiefging, darf nicht vom selben Abbruch gestoppt werden.
    /// </remarks>
    /// <param name="warten">Wie bei <see cref="AcSchreiber.SchreibenAsync"/> — im Test sofort.</param>
    public async Task<bool> AusschaltenAsync(
        HomeAssistantSettings einstellungen,
        string entityId,
        Func<TimeSpan, CancellationToken, Task>? warten = null)
    {
        warten ??= (dauer, token) => Task.Delay(dauer, token);
        var (domain, dienst, daten) = AusBefehl(entityId);

        for (var versuch = 1; versuch <= Versuche; versuch++)
        {
            await _funk.SchickenAsync(einstellungen, domain, dienst, entityId, daten, CancellationToken.None);
            await warten(Nachlesepause, CancellationToken.None);

            var ist = await _funk.ZustandAsync(einstellungen, entityId, CancellationToken.None);
            if (IstAus(ist?.State))
            {
                if (versuch > 1)
                {
                    _logger.LogInformation("{Entity} ist aus (nach {Versuch} Versuchen).", entityId, versuch);
                }
                return true;
            }

            _logger.LogWarning(
                "{Entity} meldet nach dem Ausschalten „{Ist}\" (Versuch {Versuch} von {Versuche}).",
                entityId, ist?.State ?? "nichts", versuch, Versuche);
        }

        _logger.LogError("{Entity} liess sich NICHT bestätigt ausschalten — in Home Assistant prüfen.", entityId);
        return false;
    }

    /// <summary>Der Dienst, der diese Entität ausschaltet.</summary>
    /// <remarks>
    /// Ein AC-Infinity-Port ist oft ein <c>select</c> mit den Optionen
    /// „On"/„Off" — der kennt kein <c>turn_off</c>.
    /// </remarks>
    public static (string Domain, string Dienst, IReadOnlyDictionary<string, object> Daten) AusBefehl(string entityId)
    {
        var domain = entityId.Split('.', 2)[0];
        return domain == "select"
            ? ("select", "select_option", new Dictionary<string, object> { ["option"] = "Off" })
            : (domain, "turn_off", new Dictionary<string, object>());
    }

    /// <summary>„off" in jeder Schreibweise. <c>unavailable</c> ist NICHT aus.</summary>
    public static bool IstAus(string? zustand)
        => string.Equals(zustand, "off", StringComparison.OrdinalIgnoreCase);
}
