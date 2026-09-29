using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.73): Schaltet das Dosier-Ventil kurz auf und sieht nach, ob
/// es wirklich reagiert.
/// </summary>
/// <remarks>
/// <para><b>Warum das sein muss.</b> In dieser Anlage hat es Tage gedauert
/// herauszufinden, dass der Port „an" meldete und trotzdem kein Gas ankam — der
/// Arbeitsdruck stand zu niedrig und das Nadelventil war zu. Zwei Sekunden beim
/// Einrichten ersparen dem Nächsten diese Suche.</para>
/// <para><b>Was sie prüft und was nicht.</b> Sie prüft, dass der Schaltbefehl
/// ankommt und der Zustand umschlägt. Ob Gas strömt, weiß sie nicht — dafür
/// müsste der CO₂-Wert steigen, und zwei Sekunden reichen dafür nicht. Sie
/// meldet den CO₂-Wert vorher und nachher trotzdem mit, weil ein deutlicher
/// Sprung ein gutes Zeichen ist und sein Ausbleiben nichts beweist.</para>
/// <para><b>Sicherheit.</b> Das Ventil wird in jedem Fall wieder geschlossen,
/// auch wenn das Nachsehen dazwischen scheitert — deshalb steht das Schließen in
/// einem <c>finally</c>. Zwei Sekunden CO₂ sind in jedem Zelt harmlos; ein
/// offen gebliebenes Ventil ist es nicht.</para>
/// <para>Das gilt auch, wenn schon das Öffnen scheiterte: die AC-Infinity-Wolke
/// antwortet oft langsamer als das Zeitlimit von vier Sekunden, das „an" kann
/// also angekommen sein, obwohl der Aufruf als gescheitert zurückkam. Vorher
/// kehrte die Probe dann ohne Schließen zurück. Geschlossen wird mit
/// Nachkontrolle und Wiederholung (<see cref="Ausschalter"/>) — die Wolke
/// verwirft Aufträge auch still.</para>
/// </remarks>
public sealed class SteuerungProbeService
{
    private static readonly TimeSpan Offenzeit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Nachlauf = TimeSpan.FromSeconds(3);

    private readonly IAcFunk _funk;
    private readonly Ausschalter _ausschalter;
    private readonly ILogger<SteuerungProbeService> _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _warten;

    public SteuerungProbeService(
        IAcFunk funk,
        Ausschalter ausschalter,
        ILogger<SteuerungProbeService> log,
        Func<TimeSpan, CancellationToken, Task>? warten = null)
    {
        _funk = funk;
        _ausschalter = ausschalter;
        _log = log;
        _warten = warten ?? ((dauer, token) => Task.Delay(dauer, token));
    }

    public sealed record Ergebnis(
        bool Geschaltet,
        bool ZustandSprang,
        bool WiederZu,
        string? Co2Vorher,
        string? Co2Nachher,
        string Urteil);

    /// <summary>Das Ventil zwei Sekunden öffnen und nachsehen.</summary>
    public async Task<Ergebnis> ProbierenAsync(
        IReadOnlyDictionary<string, string> zuordnung,
        HomeAssistantSettings settings,
        CancellationToken ct = default)
    {
        if (!zuordnung.TryGetValue("port_schalter", out var schalter) || string.IsNullOrWhiteSpace(schalter))
        {
            return new Ergebnis(false, false, false, null, null,
                "Es ist keine Entität zugeordnet, die das Ventil schaltet.");
        }

        zuordnung.TryGetValue("port_zustand", out var zustandEntity);
        zuordnung.TryGetValue("co2_sensor", out var co2Entity);

        var co2Vorher = co2Entity is null ? null : (await _funk.ZustandAsync(settings, co2Entity, ct))?.State;
        var vorher = zustandEntity is null ? null : (await _funk.ZustandAsync(settings, zustandEntity, ct))?.State;

        var geschaltet = false;
        var zu = false;
        string? waehrend = null;
        try
        {
            geschaltet = await SchaltenAsync(settings, schalter, CancellationToken.None);
            if (geschaltet)
            {
                await _warten(Offenzeit, ct);
                if (zustandEntity is not null)
                {
                    waehrend = (await _funk.ZustandAsync(settings, zustandEntity, ct))?.State;
                }
            }
        }
        finally
        {
            // Muss laufen, egal was oben schiefging — auch nach einem
            // gescheiterten Oeffnen. Ein offen gebliebenes Ventil ist die eine
            // Sache, die hier nicht passieren darf.
            zu = await _ausschalter.AusschaltenAsync(settings, schalter, _warten);
        }

        if (!geschaltet)
        {
            return new Ergebnis(false, false, zu, co2Vorher, null, zu
                ? "Der Schaltbefehl kam nicht durch. Stimmt die zugeordnete Entität?"
                : "Der Schaltbefehl kam nicht bestätigt durch, und das Ventil meldet sich nicht als geschlossen. "
                + "Bitte sofort von Hand nachsehen und schließen.");
        }

        await _warten(Nachlauf, ct);
        var nachher = zustandEntity is null ? null : (await _funk.ZustandAsync(settings, zustandEntity, ct))?.State;
        var co2Nachher = co2Entity is null ? null : (await _funk.ZustandAsync(settings, co2Entity, ct))?.State;

        var sprang = Offen(waehrend) && !Offen(vorher);
        // Zu heisst: der Schalter hat „aus" bestaetigt UND der Port meldet
        // nicht mehr „an".
        var wiederZu = zu && !Offen(nachher);

        _log.LogInformation(
            "Probeschaltung: geschaltet, Zustand vorher {Vorher}, waehrend {Waehrend}, nachher {Nachher}.",
            vorher, waehrend, nachher);

        return new Ergebnis(true, sprang, wiederZu, co2Vorher, co2Nachher,
            Urteilen(zustandEntity is not null, sprang, wiederZu));
    }

    private static string Urteilen(bool zustandBekannt, bool sprang, bool wiederZu)
    {
        if (!wiederZu)
        {
            return "Das Ventil meldet sich noch als offen. Bitte von Hand nachsehen und schließen.";
        }

        if (!zustandBekannt)
        {
            return "Geschaltet und wieder geschlossen. Ob der Port wirklich reagiert hat, "
                 + "lässt sich ohne zugeordnete Zustands-Entität nicht sagen.";
        }

        return sprang
            ? "Geschaltet, der Port hat reagiert und ist wieder zu."
            : "Der Befehl ging durch, aber der Port meldete keinen Wechsel. Das kann an der trägen "
            + "Rückmeldung des Controllers liegen — oder daran, dass er nicht wirklich schaltet.";
    }

    private static bool Offen(string? zustand)
        => zustand is not null
           && (zustand.Equals("on", StringComparison.OrdinalIgnoreCase)
               || zustand.Equals("On", StringComparison.Ordinal));

    /// <summary>Öffnet das Ventil. Geschlossen wird über <see cref="Ausschalter"/>.</summary>
    private Task<bool> SchaltenAsync(HomeAssistantSettings settings, string entityId, CancellationToken ct)
    {
        var domain = entityId.Split('.', 2)[0];
        return domain == "select"
            ? _funk.SchickenAsync(settings, "select", "select_option", entityId,
                new Dictionary<string, object> { ["option"] = "On" }, ct)
            : _funk.SchickenAsync(settings, domain, "turn_on", entityId,
                new Dictionary<string, object>(), ct);
    }
}
