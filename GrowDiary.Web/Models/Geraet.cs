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
    /// <summary>Zugeordnet von Hand (Inventar oder eigene Tabelle) statt nur geraten.</summary>
    public bool Bestaetigt { get; init; }
}

/// <summary>Eine Entität des Geräts samt allem, wofür sie im Fork benutzt wird.</summary>
public sealed record GeraetEntitaet(string EntityId, IReadOnlyList<GeraetVerwendung> Verwendungen);

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
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
