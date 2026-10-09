using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (A-016, Etappe 2): Welche Steuerungen der Nutzer hat.
/// </summary>
/// <remarks>
/// <para><b>Wozu.</b> Nicht jeder hat einen Entfeuchter, einen Abluft-Regler mit Stufen oder
/// eine Gasflasche. Die Übersicht zeigte bisher immer alle Steuerungen, und der Fork legte
/// Bausteine für Geräte an, die es gar nicht gibt. Jetzt sagt der Nutzer, was er hat; nur das
/// erscheint in der Übersicht, alles andere steht als „nicht eingerichtet" daneben.</para>
/// <para><b>Zwei Zustände.</b> <i>Gespeichert</i>: der Nutzer hat die Auswahl gesetzt, sie gilt
/// wörtlich. <i>Nie gespeichert</i> (Bestandsanlagen): eine Steuerung gilt als gewählt, sobald ihr
/// mindestens eine Geräte-Rolle zugeordnet ist — wer vor diesem Stand eingerichtet hat, sieht
/// seine Steuerungen unverändert. Ausgewertet werden nur <i>gespeicherte</i> Zuordnungen, nicht
/// die Rückfall-Vorgaben der Rollen (<see cref="SteuerungGeraeteService.Rueckfall"/>) — die machten
/// auf einer frischen Anlage jede Steuerung zur gewählten.</para>
/// <para><b>Was die Auswahl nicht tut.</b> Sie löscht nichts und schaltet nichts ab: Abwählen
/// blendet eine Steuerung aus. Automationen in Home Assistant laufen weiter.</para>
/// </remarks>
public sealed class SteuerungAuswahlService
{
    /// <summary>Ablageort der Auswahl (JSON-Liste der Kennungen).</summary>
    public const string Schluessel = "fork-ai:steuerung:auswahl";

    /// <summary>Eine Steuerung, wie der Nutzer sie auswählt.</summary>
    /// <param name="Kennung">Kennung der Auswahl (nicht immer die eines Moduls: „entfeuchter" umfasst auch den Zusatz).</param>
    /// <param name="Module">Die Katalog-Module (<see cref="SteuerungBauteile"/>, <see cref="SteuerungGeraeteRollen"/>), die dazugehören.</param>
    /// <param name="Uebersicht">Die Kennungen, unter denen sie in der Übersicht erscheint.</param>
    public sealed record Art(string Kennung, string Titel, string Beschreibung, IReadOnlyList<string> Module, IReadOnlyList<string> Uebersicht);

    public static IReadOnlyList<Art> Alle { get; } = new Art[]
    {
        new("co2", "CO₂-Begasung",
            "Gasflasche mit Ventil und CO₂-Fühler. Hat die Abluft Stufen, wird sie beim Begasen gedrosselt.",
            ["co2"], ["co2", "abluft"]),
        new("entfeuchter", "Entfeuchter",
            "Ein schaltbarer Entfeuchter, geregelt nach Feuchte, Temperatur und Plan; auf Wunsch mit einem zweiten Gerät.",
            ["entfeuchter", EntfeuchterZusatzSteuerungService.Modul], ["entfeuchter", EntfeuchterZusatzSteuerungService.Modul]),
        new("zuluft", "Zuluft",
            "Außenluft ansaugen, solange sie trockener ist als die Luft im Raum.",
            ["zuluft"], ["zuluft"]),
        new("chiller", "Wasserkühler",
            "Kühlt das Nährwasser auf ein Tag- und ein Nachtziel — mit Steckdose oder mit eigenem Sollwert.",
            ["chiller"], ["chiller"]),
        new("licht", "Lampe mit Zeitplan",
            "Schaltet die Lampe und führt ihren Zeitplan; Ein- und Aus-Zeit kommen aus dem Plan.",
            ["licht"], ["licht"]),
    };

    private readonly AppSettingsRepository _einstellungen;
    private readonly SteuerungRepository _steuerung;

    public SteuerungAuswahlService(AppSettingsRepository einstellungen, SteuerungRepository steuerung)
    {
        _einstellungen = einstellungen;
        _steuerung = steuerung;
    }

    public static Art? Finden(string? kennung)
        => Alle.FirstOrDefault(a => string.Equals(a.Kennung, kennung, StringComparison.Ordinal));

    /// <summary>Hat der Nutzer die Auswahl je gesetzt?</summary>
    public bool Gespeichert() => Gespeicherte() is not null;

    /// <summary>Die gewählten Steuerungen (Kennungen der Auswahl), in der Reihenfolge von <see cref="Alle"/>.</summary>
    public IReadOnlyList<string> Gewaehlt()
    {
        var gespeichert = Gespeicherte();
        if (gespeichert is not null)
        {
            return Alle.Where(a => gespeichert.Contains(a.Kennung)).Select(a => a.Kennung).ToList();
        }

        var zugeordnet = _steuerung.GetGeraete()
            .Where(g => !string.IsNullOrWhiteSpace(g.EntityId))
            .Select(g => g.Modul)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Alle.Where(a => a.Module.Any(zugeordnet.Contains)).Select(a => a.Kennung).ToList();
    }

    /// <summary>
    /// Die Auswahl speichern. Unbekannte Kennungen werden abgelehnt, nicht stillschweigend verworfen.
    /// </summary>
    /// <returns>Die unbekannten Kennungen; leer, wenn gespeichert wurde.</returns>
    public IReadOnlyList<string> Speichern(IEnumerable<string> kennungen)
    {
        var liste = kennungen.Select(k => k?.Trim() ?? string.Empty).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var unbekannt = liste.Where(k => Finden(k) is null).ToList();
        if (unbekannt.Count > 0) return unbekannt;

        _einstellungen.SetValue(Schluessel, JsonSerializer.Serialize(liste));
        return [];
    }

    /// <summary>Ob die Übersichts-Kennung (etwa „abluft") zu einer gewählten Steuerung gehört.</summary>
    public static bool Sichtbar(string uebersichtKennung, IReadOnlyCollection<string> gewaehlt)
        => Alle.Any(a => gewaehlt.Contains(a.Kennung) && a.Uebersicht.Contains(uebersichtKennung, StringComparer.Ordinal));

    /// <summary>Wie viele Pflicht-Rollen der Steuerung zugeordnet sind (nur gespeicherte Zuordnungen).</summary>
    public (int Zugeordnet, int Gesamt) Pflichtrollen(Art art)
    {
        var gespeichert = _steuerung.GetGeraete()
            .Where(g => !string.IsNullOrWhiteSpace(g.EntityId))
            .Select(g => $"{g.Modul}/{g.Rolle}".ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var pflicht = art.Module.SelectMany(SteuerungGeraeteRollen.FuerModul).Where(r => r.Pflicht).ToList();
        return (pflicht.Count(r => gespeichert.Contains($"{r.Modul}/{r.Schluessel}".ToLowerInvariant())), pflicht.Count);
    }

    private HashSet<string>? Gespeicherte()
    {
        var roh = _einstellungen.GetValue(Schluessel);
        if (string.IsNullOrWhiteSpace(roh)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(roh)?.ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return null; // kaputter Eintrag: als nie gespeichert behandeln, statt alles auszublenden
        }
    }
}
