using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Der einmal getroffene Entscheid über den Namensraum, gespeichert im Fork.</summary>
public sealed class KennungsRaumStand
{
    /// <summary><c>neu</c> oder <c>bestand</c>.</summary>
    public string Raum { get; set; } = "neu";

    /// <summary>Woran der Fork den Entscheid festgemacht hat — nur zum Nachlesen.</summary>
    public string Grund { get; set; } = "";
}

/// <summary>
/// Fork AI (forkai.217): Legt beim Start fest, in welchem Namensraum (<see cref="KennungsRaum"/>) die Objekte des Forks
/// in Home Assistant heißen.
/// </summary>
/// <remarks>
/// <para><b>Die Regel.</b> Hat der Fork für die Entfeuchter- oder die CO₂-Steuerung schon etwas gespeichert — Einstellungen oder
/// zugeordnete Geräte —, lief diese Installation mit den alten Kennungen: <see cref="KennungsRaum.Bestand"/>. Sonst
/// <see cref="KennungsRaum.Neu"/>. Entschieden wird <b>genau einmal</b> und dann im Fork gespeichert: Wer neu beginnt und
/// später Einstellungen speichert, soll beim nächsten Start nicht in den Bestand rutschen.</para>
/// <para><b>Warum nicht „gibt es einen Helfer namens …?".</b> Ein Helfer mit gleichem Namen kann dem Nutzer gehören.
/// Der Fork hält nur das für Bestand, was er selbst gespeichert hat. Das Wiedererkennen von Automationen läuft
/// unverändert über ihre Konfigurations-Kennung.</para>
/// </remarks>
public static class KennungsRaumWahl
{
    /// <summary>Unter diesem Namen liegt der Entscheid in den Steuerungs-Einstellungen.</summary>
    public const string Modul = "kennungsraum";

    private static readonly string[] Module = [EntfeuchterSteuerungService.Modul, EntfeuchterZusatzSteuerungService.Modul, Co2SteuerungService.Modul];

    /// <summary>Die reine Entscheidung — ohne Datenbank, damit sie sich einzeln prüfen lässt.</summary>
    /// <param name="gespeichert">Ein früherer Entscheid, falls vorhanden — er gilt immer.</param>
    /// <param name="hatEinstellungen">Hat der Fork Einstellungen einer der beiden Steuerungen gespeichert?</param>
    /// <param name="hatGeraete">Sind der Steuerung Geräte zugeordnet?</param>
    public static (KennungsRaum Raum, string Grund) Bestimmen(KennungsRaumStand? gespeichert, bool hatEinstellungen, bool hatGeraete)
    {
        if (gespeichert is not null)
        {
            return (string.Equals(gespeichert.Raum, "bestand", StringComparison.Ordinal) ? KennungsRaum.Bestand : KennungsRaum.Neu,
                gespeichert.Grund);
        }

        if (hatEinstellungen) return (KennungsRaum.Bestand, "Einstellungen der Entfeuchter-Steuerung waren schon gespeichert");
        if (hatGeraete) return (KennungsRaum.Bestand, "Geräte der Entfeuchter-Steuerung waren schon zugeordnet");
        return (KennungsRaum.Neu, "frische Installation");
    }

    /// <summary>Beim Start: entscheiden, speichern, festlegen.</summary>
    public static KennungsRaum BeimStart(SteuerungRepository repo, ILogger log)
    {
        var raum = Entscheiden(repo, log);
        Kennungen.Festlegen(raum);
        return raum;
    }

    /// <summary>Entscheiden und — beim ersten Mal — speichern. Ändert nichts am laufenden Prozess.</summary>
    public static KennungsRaum Entscheiden(SteuerungRepository repo, ILogger log)
    {
        var gespeichert = repo.GetEinstellungen<KennungsRaumStand>(Modul);
        var hatEinstellungen = Module.Any(m => repo.GetEinstellungen<object>(m) is not null);
        var hatGeraete = Module.Any(m => repo.GetGeraete(m).Count > 0);
        var (raum, grund) = Bestimmen(gespeichert, hatEinstellungen, hatGeraete);

        if (gespeichert is null)
        {
            repo.SetEinstellungen(Modul, new KennungsRaumStand
            {
                Raum = raum == KennungsRaum.Bestand ? "bestand" : "neu",
                Grund = grund,
            });
            log.LogInformation("Kennungsraum festgelegt: {Raum} ({Grund}).", raum, grund);
        }

        return raum;
    }
}
