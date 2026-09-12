using System.Globalization;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.58): Legt die Helfer an, die eine Steuerung braucht.
/// </summary>
/// <remarks>
/// <para><b>Nur die einfachen Arten.</b> Zahlen, Schalter, Zeitstempel und
/// Zähler entstehen über einen einzigen Befehl am WebSocket. Rechen-Sensoren
/// brauchen einen mehrstufigen Einrichtungsdialog und Automationen einen ganz
/// anderen Weg — beides kommt getrennt, weil es getrennt schiefgehen kann.</para>
/// <para><b>Drei Regeln, die nicht verhandelbar sind.</b> Es wird nur angelegt,
/// was fehlt. Vorhandenes wird nie überschrieben — in Brus Anlage stecken Werte,
/// die über Tage entstanden sind, und ein Helfer mit dem richtigen Namen aber
/// zurückgesetztem Inhalt wäre schlimmer als gar keiner. Und was an einer nicht
/// belegten Rolle hängt, entsteht gar nicht erst: Wer keinen Abluft-Regler hat,
/// bekommt keine T6-Helfer.</para>
/// <para><b>Startwerte.</b> Der Katalog kennt Grenzen und Einheit, nicht den
/// Wert. Home Assistant setzt eine neue Zahl auf ihre Untergrenze; die
/// Steuerung schreibt beim nächsten Speichern ihre eigenen Sollwerte hinüber.
/// Ein Helfer ist also kurz auf einem unsinnigen Wert — deshalb gehört das
/// Anlegen vor die erste Automation, nicht danach.</para>
/// </remarks>
public sealed class SteuerungHelferService
{
    private readonly HomeAssistantService _ha;
    private readonly ILogger<SteuerungHelferService> _log;

    public SteuerungHelferService(HomeAssistantService ha, ILogger<SteuerungHelferService> log)
    {
        _ha = ha;
        _log = log;
    }

    /// <summary>Was mit einem einzelnen Helfer geschehen ist.</summary>
    public sealed record Ergebnis(string EntityId, string Name, bool Angelegt, string? Fehler);

    public sealed record Bilanz(
        bool Erreichbar,
        int Angelegt,
        int Uebersprungen,
        int Fehlgeschlagen,
        IReadOnlyList<Ergebnis> Einzeln);

    /// <summary>Die Arten, die dieser Dienst kann.</summary>
    private static readonly IReadOnlySet<BauteilArt> Machbar = new HashSet<BauteilArt>
    {
        BauteilArt.Zahl, BauteilArt.Schalter, BauteilArt.Zeitpunkt, BauteilArt.Zaehler,
    };

    /// <summary>Die fehlenden Helfer einer Steuerung anlegen.</summary>
    public async Task<Bilanz> AnlegenAsync(
        string modul,
        IReadOnlyCollection<string> belegteRollen,
        HomeAssistantSettings settings,
        CancellationToken ct = default)
    {
        var vorhanden = (await _ha.GetEntitiesAsync(settings, ct))
            .Select(e => e.EntityId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (vorhanden.Count == 0)
        {
            // Ohne Antwort wissen wir nicht, was es gibt. Jetzt anzulegen hiesse,
            // blind 20 Helfer zu erzeugen, die vielleicht schon da sind.
            return new Bilanz(false, 0, 0, 0, Array.Empty<Ergebnis>());
        }

        var offen = SteuerungBauteile.Anwendbar(modul, belegteRollen)
            .Where(b => Machbar.Contains(b.Art))
            .Where(b => !vorhanden.Contains(b.EntityId))
            .ToList();

        var uebersprungen = SteuerungBauteile.Anwendbar(modul, belegteRollen)
            .Count(b => Machbar.Contains(b.Art) && vorhanden.Contains(b.EntityId));

        if (offen.Count == 0)
        {
            return new Bilanz(true, 0, uebersprungen, 0, Array.Empty<Ergebnis>());
        }

        await using var socket = await HomeAssistantSocket.OeffnenAsync(settings, ct);
        if (socket is null)
        {
            _log.LogWarning("Helfer anlegen: Home Assistant nimmt die WebSocket-Anmeldung nicht an.");
            return new Bilanz(false, 0, uebersprungen, 0, Array.Empty<Ergebnis>());
        }

        var einzeln = new List<Ergebnis>();
        foreach (var b in offen)
        {
            var antwort = await socket.BefehlAsync(Befehl(b), Felder(b), ct);
            einzeln.Add(new Ergebnis(b.EntityId, b.Name, antwort.Erfolg, antwort.Fehler));

            if (antwort.Erfolg)
            {
                _log.LogInformation("Helfer angelegt: {EntityId}", b.EntityId);
            }
            else
            {
                _log.LogWarning("Helfer {EntityId} nicht angelegt: {Fehler}", b.EntityId, antwort.Fehler);
            }
        }

        return new Bilanz(
            true,
            einzeln.Count(e => e.Angelegt),
            uebersprungen,
            einzeln.Count(e => !e.Angelegt),
            einzeln);
    }

    /// <summary>Der WebSocket-Befehl zur Art.</summary>
    public static string Befehl(Bauteil b) => b.Art switch
    {
        BauteilArt.Zahl => "input_number/create",
        BauteilArt.Schalter => "input_boolean/create",
        BauteilArt.Zeitpunkt => "input_datetime/create",
        BauteilArt.Zaehler => "counter/create",
        _ => throw new ArgumentOutOfRangeException(nameof(b), b.Art, "Diese Art legt SteuerungHelferService nicht an."),
    };

    /// <summary>
    /// Die Felder des Befehls.
    /// </summary>
    /// <remarks>
    /// Home Assistant leitet die Objektkennung aus dem Namen ab. Damit am Ende
    /// genau die Entität entsteht, die der Katalog erwartet, muss der Name zur
    /// Kennung passen — deshalb steht im Katalog kein hübscher Titel, sondern
    /// der Name, aus dem die richtige Id fällt.
    /// </remarks>
    public static IReadOnlyDictionary<string, object?> Felder(Bauteil b)
    {
        var felder = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = b.Name };

        switch (b.Art)
        {
            case BauteilArt.Zahl:
                felder["min"] = b.Min ?? 0;
                felder["max"] = b.Max ?? 100;
                felder["step"] = b.Schritt ?? 1;
                felder["mode"] = "box";
                if (!string.IsNullOrWhiteSpace(b.Einheit)) felder["unit_of_measurement"] = b.Einheit;
                break;

            case BauteilArt.Zeitpunkt:
                felder["has_date"] = true;
                felder["has_time"] = true;
                break;

            case BauteilArt.Zaehler:
                felder["initial"] = 0;
                felder["step"] = 1;
                felder["minimum"] = 0;
                break;

            case BauteilArt.Schalter:
                break;
        }

        return felder;
    }

    /// <summary>Ob diese Art hier angelegt werden kann.</summary>
    public static bool Kann(BauteilArt art) => Machbar.Contains(art);

    /// <summary>Eine Zahl so schreiben, wie Home Assistant sie liest.</summary>
    public static string Zahl(double wert) => wert.ToString(CultureInfo.InvariantCulture);
}
