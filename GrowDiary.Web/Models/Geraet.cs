namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (forkai.22): Ein Gerät — die Einheit, in der der Nutzer denkt.
/// </summary>
/// <remarks>
/// <para><b>Warum nicht die Entität.</b> Der Fork pflegte Entitäten an fünf
/// Stellen: Messgrößen des Zelts, Zelt-Technik, Inventar, Dosierpumpen,
/// Steuerungs-Rollen, Stromzähler. Eine Zeile war immer genau eine Entität. Die
/// Anlage sieht anders aus: der AC-Infinity-Controller trägt vier Ports, der
/// Bluelab Guardian drei Messwerte, und die CO₂-Flasche hat gar keine Entität,
/// steht aber im Inventar. Das Gerät ist die Klammer, die es zusammenhält.</para>
///
/// <para><b>Diese Etappe liest nur.</b> Die Quellen bleiben, wo sie sind; hier
/// entsteht die gemeinsame Sicht darauf plus zwei Tabellen, in denen eine
/// spätere Etappe Korrekturen des Nutzers ablegt.</para>
/// </remarks>
public sealed record Geraet(
    string Schluessel,
    string Name,
    int? TentId,
    int? HardwareItemId,
    IReadOnlyList<GeraetEntitaet> Entitaeten)
{
    /// <summary>Zugeordnet von Hand oder aus dem HA-Geräteregister statt nur geraten.</summary>
    public bool Bestaetigt { get; init; }

    /// <summary>
    /// Das Gerät, an dem dieses hängt — der Controller, in dessen Port es steckt.
    /// Null bei einem Gerät, das für sich steht.
    /// </summary>
    public string? ElternSchluessel { get; init; }

    /// <summary>Die Steckstelle am Eltern-Gerät: „Port 5", „Fühler 2".</summary>
    public string? Anschluss { get; init; }

    /// <summary>Ein Controller, der selbst keine Steckstelle belegt, aber Kinder trägt.</summary>
    public bool IstController { get; init; }

    /// <summary>Hersteller und Modell aus dem HA-Geräteregister, soweit bekannt.</summary>
    public string? Modell { get; init; }

    /// <summary>
    /// Eine selbst angelegte Rubrik — kein Gerät, sondern ein Fach. „Kameras",
    /// „Klima". Technisch dasselbe wie ein Eltern-Gerät, nur ohne Entitäten und
    /// ohne Entsprechung in Home Assistant; deshalb zählt sie nicht als Gerät.
    /// </summary>
    public bool IstRubrik { get; init; }

    /// <summary>Der Nutzer hat gesagt, woran dieses Gerät hängt (oder dass es an nichts hängt).</summary>
    public bool ElternVomNutzer { get; init; }

    /// <summary>Der Nutzer hat dem Gerät einen eigenen Namen gegeben.</summary>
    public bool NameVomNutzer { get; init; }

    /// <summary>Woran es ohne die Korrektur hinge — für „zurück wohin?" in der Oberfläche.</summary>
    public string? AbgeleiteterEltern { get; init; }
}

/// <summary>Eine Entität des Geräts samt allem, wofür sie im Fork benutzt wird.</summary>
/// <param name="Verschoben">Von Hand diesem Gerät zugeschlagen, nicht von Home Assistant.</param>
/// <param name="HerkunftName">Das Gerät, zu dem Home Assistant sie zählt — nur bei <paramref name="Verschoben"/>.</param>
public sealed record GeraetEntitaet(
    string EntityId,
    IReadOnlyList<GeraetVerwendung> Verwendungen,
    bool Verschoben = false,
    string? HerkunftName = null);

/// <summary>
/// Wofür eine Entität benutzt wird — „Messgröße EC", „Steuerung CO₂ · Licht-Status".
/// <paramref name="Quelle"/> nennt die Stelle, an der es heute gepflegt wird, damit
/// die Oberfläche später dorthin führen kann.
/// </summary>
public sealed record GeraetVerwendung(string Zweck, string Quelle);

/// <summary>Die Herkunft einer Verwendung — zugleich der Weg zur heutigen Pflegestelle.</summary>
public static class GeraetQuellen
{
    public const string Messgroesse = "messgroesse";
    public const string ZeltTechnik = "zelt";
    public const string Inventar = "inventar";
    public const string Dosierung = "dosierung";
    public const string Steuerung = "steuerung";
    public const string Strom = "strom";
}

/// <summary>
/// Der Schlüssel, unter dem Entitäten ohne ausdrückliche Zuordnung zu einem Gerät
/// zusammenfinden.
/// </summary>
/// <remarks>
/// <para><b>Die Regel.</b> Die ersten beiden Abschnitte des Objektnamens:
/// <c>sensor.bluelab_guardian_ph</c> und <c>sensor.bluelab_guardian_temperature</c>
/// landen beide unter <c>bluelab_guardian</c>, die vier Ports des Controllers unter
/// <c>klein_abluft</c>. Das ist geraten, nicht gewusst — deshalb heißt so ein Gerät
/// in der Oberfläche „vermutet", und eine Korrektur des Nutzers sticht es immer.</para>
///
/// <para>Kürzere Namen bleiben, wie sie sind: <c>switch.pumpe</c> wird
/// <c>pumpe</c> und steht für sich allein. Das ist ehrlicher, als zwei Geräte
/// zusammenzuwerfen, die nur zufällig ähnlich heißen.</para>
/// </remarks>
public static class GeraeteSchluessel
{
    /// <summary>Namensraum der selbst angelegten Rubriken.</summary>
    public const string RubrikPraefix = "rubrik:";

    /// <summary>Aus einem Rubriknamen einen stabilen Schlüssel: „Kameras" → „rubrik:kameras".</summary>
    public static string RubrikSchluessel(string name)
    {
        var sauber = new string(name.Trim().ToLowerInvariant()
            .Select(zeichen => char.IsLetterOrDigit(zeichen) ? zeichen : '-').ToArray())
            .Trim('-');
        while (sauber.Contains("--", StringComparison.Ordinal)) sauber = sauber.Replace("--", "-", StringComparison.Ordinal);
        return RubrikPraefix + (sauber.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : sauber);
    }

    public static string AusEntity(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return string.Empty;

        var punkt = entityId.IndexOf('.');
        var objekt = punkt >= 0 ? entityId[(punkt + 1)..] : entityId;
        var teile = objekt.Split('_', StringSplitOptions.RemoveEmptyEntries);

        return teile.Length <= 2
            ? objekt.Trim().ToLowerInvariant()
            : string.Join('_', teile.Take(2)).ToLowerInvariant();
    }

    /// <summary>Aus dem geratenen Schlüssel ein lesbarer Name: „bluelab_guardian" → „Bluelab Guardian".</summary>
    public static string AlsName(string schluessel)
    {
        if (string.IsNullOrWhiteSpace(schluessel)) return "Unbenannt";
        var worte = schluessel
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(wort => wort.Length == 1 ? wort.ToUpperInvariant() : char.ToUpperInvariant(wort[0]) + wort[1..]);
        return string.Join(' ', worte);
    }
}

/// <summary>Eine vom Nutzer gesetzte Gerätezeile (Etappe 3 schreibt sie; hier nur Datenform).</summary>
public sealed class GespeichertesGeraet
{
    public string Schluessel { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? TentId { get; set; }
    public int? HardwareItemId { get; set; }
    /// <summary>Korrektur des Nutzers: an welchem Gerät dieses hängt.</summary>
    public string? ElternSchluessel { get; set; }
    /// <summary>Korrektur des Nutzers: an welcher Steckstelle.</summary>
    public string? Anschluss { get; set; }
    /// <summary>Ein selbst angelegtes Fach statt eines echten Geräts.</summary>
    public bool IstRubrik { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}


/// <summary>
/// Fork AI (forkai.22): Was Home Assistant über die Herkunft einer Entität weiß.
/// </summary>
/// <remarks>
/// <para>Die Zustandsliste (<c>/api/states</c>) kennt das nicht — sie liefert nur
/// Name und Wert. <c>DeviceId</c> und <c>UniqueId</c> kommen aus dem Geräte- und
/// Entitätsregister. Fehlen sie (alte Anlage, Registry nicht erreichbar), fällt die
/// Ableitung auf die Namensvermutung zurück; nichts bricht, es wird nur gröber.</para>
///
/// <para><c>ViaDeviceId</c> ist Home Assistants eigene Auskunft darüber, an welchem
/// Gerät dieses hängt — bei AC Infinity zeigt jedes Port-Gerät auf seinen Controller.
/// Das ist die Wahrheit; die MAC aus der <c>unique_id</c> ist nur noch der Notnagel
/// für Integrationen, die kein <c>via_device</c> setzen.</para>
/// </remarks>
public sealed record HerkunftEintrag(
    string EntityId,
    string? DeviceId,
    string? DeviceName,
    string? UniqueId,
    string? ViaDeviceId = null,
    string? ViaDeviceName = null,
    string? DeviceModell = null,
    string? ViaDeviceModell = null);

/// <summary>
/// Die Klammer über den Geräten, die Home Assistant je Port einzeln anlegt.
/// </summary>
/// <remarks>
/// <para><b>Das Problem.</b> Die AC-Infinity-Integration macht aus EINEM Controller
/// bis zu sieben HA-Geräte: je Port eines, je Fühler eines. Auf der Geräte-Id allein
/// stünde der Controller siebenfach in der Liste, und niemand sähe, dass es dieselbe
/// Box ist.</para>
///
/// <para><b>Die Lösung.</b> Die <c>unique_id</c> trägt die MAC:
/// <c>ac_infinity_34CDB02C4C16_port_5_loadState</c>. Gleiche MAC heißt gleicher
/// Controller; <c>port_5</c> bzw. <c>sensor_2</c> sagt, an welcher Steckstelle das
/// Kind hängt. Was AM Port hängt — Ventil, Ventilator, Chiller — weiß Home Assistant
/// nicht; das trägt der Nutzer ein.</para>
/// </remarks>
public static partial class GeraeteHerkunft
{
    /// <summary>MAC und Steckstelle aus einer unique_id, soweit sie eine trägt.</summary>
    public static (string? Mac, string? Anschluss) Lesen(string? uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) return (null, null);

        var treffer = MacUndPort().Match(uniqueId);
        if (!treffer.Success) return (null, null);

        var art = treffer.Groups["art"].Value.Equals("port", StringComparison.OrdinalIgnoreCase) ? "Port" : "Fühler";
        return (treffer.Groups["mac"].Value.ToUpperInvariant(), $"{art} {treffer.Groups["nr"].Value}");
    }

    public static string ControllerSchluessel(string mac) => $"mac:{mac.ToLowerInvariant()}";

    public static string ControllerName(string mac)
        => $"Controller {mac[^4..]}";

    [System.Text.RegularExpressions.GeneratedRegex(
        @"_(?<mac>[0-9A-Fa-f]{12})_(?<art>port|sensor)_(?<nr>\d+)",
        System.Text.RegularExpressions.RegexOptions.ExplicitCapture)]
    private static partial System.Text.RegularExpressions.Regex MacUndPort();
}
