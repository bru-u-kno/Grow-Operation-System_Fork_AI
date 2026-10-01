using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.45): Prüft, welche Bauteile einer Steuerung in Home Assistant
/// vorhanden sind — und was ausfällt, wenn eines fehlt.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Die Steuerungsseite zeigte bei einer fremden
/// Installation lauter „nicht verfügbar", ohne zu sagen, woran es liegt. Die
/// Helfer und Automationen dahinter sind bei Bru über Tage von Hand entstanden;
/// bei jedem anderen entstehen sie gar nicht.</para>
/// <para><b>Was dieser Dienst tut und was nicht.</b> Er stellt fest und erklärt.
/// Er legt nichts an — das ist die nächste Etappe und braucht eine bewusste
/// Zustimmung, weil am Ende eine Automation ein Gasventil schaltet.</para>
/// <para><b>Fehlend heißt nicht kaputt.</b> Ein Bauteil, das an einer nicht
/// belegten Rolle hängt, wird gar nicht erst erwartet: Wer keinen Abluft-Regler
/// hat, braucht die vier T6-Helfer nicht. Es steht dann unter „entfällt", nicht
/// unter „fehlt" — sonst zeigt die Seite für immer rote Punkte für Dinge, die
/// nie kommen werden.</para>
/// </remarks>
public sealed class SteuerungBestandService
{
    private readonly HomeAssistantService _ha;
    private readonly ILogger<SteuerungBestandService> _log;

    public SteuerungBestandService(HomeAssistantService ha, ILogger<SteuerungBestandService> log)
    {
        _ha = ha;
        _log = log;
    }

    /// <summary>Wie es um ein einzelnes Bauteil steht.</summary>
    public enum Stand
    {
        /// <summary>Vorhanden und liefert einen Wert.</summary>
        Da,
        /// <summary>Vorhanden, aber ohne Wert — meist ein Geräteaussetzer.</summary>
        Stumm,
        /// <summary>Nicht vorhanden, wird aber gebraucht.</summary>
        Fehlt,
        /// <summary>Nicht vorhanden und auch nicht nötig, weil die Rolle frei ist.</summary>
        Entfaellt,
        /// <summary>
        /// Fork AI (01.10.2026): Eine vom Fork angelegte Automation in einer
        /// älteren Fassung als die mitgelieferte Vorlage. Sie läuft — aber ohne
        /// die Reparaturen der neueren Fassung.
        /// </summary>
        Veraltet,
    }

    public sealed record BauteilStand(
        string EntityId,
        string Name,
        string Art,
        string Zweck,
        Stand Stand,
        bool Pflicht,
        string? OhneDas);

    public sealed record Bestandsaufnahme(
        bool HaErreichbar,
        bool Eingerichtet,
        int Da,
        int Fehlt,
        int Entfaellt,
        IReadOnlyList<string> FehlendeRollen,
        IReadOnlyList<string> AusgefalleneFunktionen,
        IReadOnlyList<BauteilStand> Bauteile,
        int Veraltet = 0);

    /// <summary>Den Bestand für eine Steuerung aufnehmen.</summary>
    /// <param name="modul">Der Modul-Schlüssel, etwa <c>co2</c>.</param>
    /// <param name="belegteRollen">Rollen-Schlüssel, denen eine Entität zugeordnet ist.</param>
    public async Task<Bestandsaufnahme> AufnehmenAsync(
        string modul,
        IReadOnlyCollection<string> belegteRollen,
        HomeAssistantSettings settings,
        CancellationToken ct = default)
    {
        // GetEntitiesAsync statt GetStatesAsync: letzteres will ein Zelt und
        // liefert nur dessen Sensoren. Hier geht es um Helfer und Automationen,
        // die zu keinem Zelt gehoeren.
        var alle = await _ha.GetEntitiesAsync(settings, ct);
        var zustaende = alle.ToDictionary(e => e.EntityId, e => e.State ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var haDa = zustaende.Count > 0;

        var fehlendeRollen = SteuerungGeraeteRollen.FuerModul(modul)
            .Where(r => r.Pflicht && !belegteRollen.Contains(r.Schluessel))
            .Select(r => r.Label)
            .ToList();

        var anwendbar = SteuerungBauteile.Anwendbar(modul, belegteRollen);
        var anwendbareIds = anwendbar.Select(b => b.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var liste = new List<BauteilStand>();
        HttpClient? client = null;
        foreach (var b in SteuerungBauteile.FuerModul(modul))
        {
            // Fork AI (01.10.2026): Eine Automation steht handgebaut unter der
            // Katalog-Kennung, vom Fork angelegt unter der Kennung, die Home
            // Assistant aus dem Alias ableitet. Vorher galt nur die erste — die
            // angelegten standen für immer unter „fehlt".
            var entityId = b.EntityId;
            if (b.Art == BauteilArt.Automation && SteuerungBauteile.AutomationFinden(b, alle) is { Count: > 0 } gefunden)
            {
                entityId = gefunden[0];
            }

            Stand stand;
            if (!anwendbareIds.Contains(b.EntityId))
            {
                stand = Stand.Entfaellt;
            }
            else if (!haDa)
            {
                // Ohne Antwort von Home Assistant wissen wir nichts. Alles als
                // fehlend zu melden waere eine Luege mit Konsequenzen — der
                // Nutzer wuerde 32 Objekte neu anlegen, die es schon gibt.
                stand = Stand.Stumm;
            }
            else if (!zustaende.TryGetValue(entityId, out var zustand))
            {
                stand = Stand.Fehlt;
            }
            else
            {
                stand = zustand is "unavailable" or "unknown" or "" ? Stand.Stumm : Stand.Da;
            }

            // Eine vom Fork angelegte Automation in älterer Fassung: Nur so
            // bekommt eine bestehende Installation eine reparierte Vorlage
            // angeboten — angelegt wird sie über denselben Weg wie neu.
            if (stand is Stand.Da or Stand.Stumm && b.VorlagenDatei is { } vorlage && b.KonfigKennung is { } kennung
                && alle.Any(e => string.Equals(e.KonfigKennung, kennung, StringComparison.Ordinal))
                && SteuerungAutomationService.VorlagenFassung(modul, vorlage) is { } mitgeliefert)
            {
                client ??= _ha.CreateClient(settings);
                if (await SteuerungAutomationService.FassungInHaAsync(client, kennung, ct) is { } inHa && inHa < mitgeliefert)
                {
                    stand = Stand.Veraltet;
                }
            }

            liste.Add(new BauteilStand(
                entityId, b.Name, b.Art.ToString(), b.Zweck, stand, b.Pflicht, b.OhneDas));
        }
        client?.Dispose();

        var ausgefallen = liste
            .Where(s => s.Stand is Stand.Entfaellt or Stand.Fehlt && !string.IsNullOrWhiteSpace(s.OhneDas))
            .Select(s => s.OhneDas!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var fehlt = liste.Count(s => s.Stand == Stand.Fehlt);
        if (fehlt > 0)
        {
            _log.LogInformation(
                "Steuerung {Modul}: {Fehlt} von {Gesamt} Bauteilen fehlen in Home Assistant.",
                modul, fehlt, anwendbar.Count);
        }

        return new Bestandsaufnahme(
            HaErreichbar: haDa,
            Eingerichtet: haDa && fehlt == 0 && fehlendeRollen.Count == 0,
            Da: liste.Count(s => s.Stand == Stand.Da),
            Fehlt: fehlt,
            Entfaellt: liste.Count(s => s.Stand == Stand.Entfaellt),
            FehlendeRollen: fehlendeRollen,
            AusgefalleneFunktionen: ausgefallen,
            Bauteile: liste,
            Veraltet: liste.Count(s => s.Stand == Stand.Veraltet));
    }
}
