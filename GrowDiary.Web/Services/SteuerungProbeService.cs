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
/// </remarks>
public sealed class SteuerungProbeService
{
    private static readonly TimeSpan Offenzeit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Nachlauf = TimeSpan.FromSeconds(3);

    private readonly HomeAssistantService _ha;
    private readonly ILogger<SteuerungProbeService> _log;

    public SteuerungProbeService(HomeAssistantService ha, ILogger<SteuerungProbeService> log)
    {
        _ha = ha;
        _log = log;
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

        var co2Vorher = co2Entity is null ? null : (await _ha.GetEntityStateAsync(settings, co2Entity, ct))?.State;
        var vorher = zustandEntity is null ? null : (await _ha.GetEntityStateAsync(settings, zustandEntity, ct))?.State;

        var geschaltet = await SchaltenAsync(settings, schalter, an: true, ct);
        if (!geschaltet)
        {
            return new Ergebnis(false, false, true, co2Vorher, null,
                "Der Schaltbefehl kam nicht durch. Stimmt die zugeordnete Entität?");
        }

        string? waehrend = null;
        try
        {
            await Task.Delay(Offenzeit, ct);
            if (zustandEntity is not null)
            {
                waehrend = (await _ha.GetEntityStateAsync(settings, zustandEntity, ct))?.State;
            }
        }
        finally
        {
            // Muss laufen, egal was oben schiefging. Ein offen gebliebenes
            // Ventil ist die eine Sache, die hier nicht passieren darf.
            await SchaltenAsync(settings, schalter, an: false, CancellationToken.None);
        }

        await Task.Delay(Nachlauf, ct);
        var nachher = zustandEntity is null ? null : (await _ha.GetEntityStateAsync(settings, zustandEntity, ct))?.State;
        var co2Nachher = co2Entity is null ? null : (await _ha.GetEntityStateAsync(settings, co2Entity, ct))?.State;

        var sprang = Offen(waehrend) && !Offen(vorher);
        var wiederZu = !Offen(nachher);

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

    private async Task<bool> SchaltenAsync(
        HomeAssistantSettings settings, string entityId, bool an, CancellationToken ct)
    {
        var domain = entityId.Split('.', 2)[0];
        return domain switch
        {
            "select" => await _ha.CallEntityServiceAsync(
                settings, "select", "select_option", entityId, ct,
                new Dictionary<string, object> { ["option"] = an ? "On" : "Off" }),
            _ => await _ha.CallEntityServiceAsync(
                settings, domain, an ? "turn_on" : "turn_off", entityId, ct),
        };
    }
}
