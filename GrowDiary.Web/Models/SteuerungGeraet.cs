namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (forkai.21): Welche Geräte eine Steuerung braucht — als Rolle, nicht
/// als Entity-ID.
/// </summary>
/// <remarks>
/// <para><b>Warum.</b> Die CO₂-Begasung hatte ihre Geräte als Konstanten im
/// Dienst stehen. Wer den Abluftventilator tauscht oder den CO₂-Fühler an einen
/// anderen Port hängt, musste in den Code. Eine Rolle beschreibt dagegen, was
/// gebraucht wird („der Fühler, der ppm liefert"); welche Entität das erfüllt,
/// steht in der Datenbank und lässt sich in der Oberfläche ändern.</para>
///
/// <para><b>Was hier NICHT steht.</b> Die Sollwert-Helfer der Regelung
/// (<c>input_number.co2_*</c>), die abgeleiteten Sensoren
/// (<c>binary_sensor.co2_bedarf</c> …) und die Automation selbst. Die legt das
/// Konzept der Steuerung an, sie sind kein Gerät des Nutzers und bleiben als
/// Konstanten im Dienst — ein Auswahlfeld dafür wäre ein Knopf ohne Sinn.</para>
/// </remarks>
public sealed record GeraeteRolle(
    string Modul,
    string Schluessel,
    string Label,
    string Gruppe,
    string Vorgabe,
    IReadOnlyList<string> Domains,
    bool Pflicht = true,
    string? Einheit = null,
    string? Hinweis = null);

/// <summary>Eine gespeicherte Zuordnung: welche Entität hinter einer Rolle steht.</summary>
public sealed class SteuerungGeraet
{
    public string Modul { get; set; } = string.Empty;
    /// <summary>Rollen-Schlüssel, oder bei <see cref="SteuerungGeraeteRollen.EigenesModul"/> der freie Name.</summary>
    public string Rolle { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Die Rollen aller Steuerungen an einer Stelle.</summary>
public static class SteuerungGeraeteRollen
{
    /// <summary>Sammelplatz für frei benannte Geräte, die keiner Steuerung gehören.</summary>
    public const string EigenesModul = "eigene";

    /// <summary>
    /// Verweis auf ein eigenes Gerät statt auf eine Entität: <c>@Zuluft Zelt</c>.
    /// So hängt ein Gerätetausch an einer Zeile, auch wenn drei Regelungen es nutzen.
    /// </summary>
    public const char VerweisZeichen = '@';

    public const string GruppeMessen = "messen";
    public const string GruppeSchalten = "schalten";
    public const string GruppeUmfeld = "umfeld";

    public static IReadOnlyList<GeraeteRolle> Alle { get; } = new GeraeteRolle[]
    {
        new("co2", "co2_sensor", "CO₂-Sensor", GruppeMessen,
            "sensor.big_co2_light_sensor_co2", new[] { "sensor" }, Einheit: "ppm"),
        new("co2", "canopy", "Canopy-Temperatur", GruppeMessen,
            "sensor.big_probe_sensor_sonden_temperatur", new[] { "sensor" }, Einheit: "°C"),
        new("co2", "rh", "Luftfeuchte", GruppeMessen,
            "sensor.big_probe_sensor_sonden_luftfeuchtigkeit", new[] { "sensor" }, Einheit: "%"),
        new("co2", "vpd", "VPD", GruppeMessen,
            "sensor.big_probe_sensor_sonden_vpd", new[] { "sensor" }, Pflicht: false, Einheit: "kPa"),
        new("co2", "port_zustand", "Dosier-Steckdose · Zustand", GruppeSchalten,
            "binary_sensor.big_port_5_zustand", new[] { "binary_sensor", "switch" },
            Hinweis: "Läuft das Ventil wirklich — nicht nur „Port online“."),
        new("co2", "abluft_stufe", "Abluft T6 · Stufe", GruppeSchalten,
            "number.rdwc_venti_einschaltleistung", new[] { "number" }, Pflicht: false),
        new("co2", "licht", "Licht-Status", GruppeUmfeld,
            "binary_sensor.klein_abluft_zustand", new[] { "binary_sensor", "switch", "light" },
            Hinweis: "Begast wird nur bei Licht."),
    };

    public static IReadOnlyList<GeraeteRolle> FuerModul(string modul)
        => Alle.Where(rolle => string.Equals(rolle.Modul, modul, StringComparison.OrdinalIgnoreCase)).ToList();

    public static GeraeteRolle? Finden(string modul, string schluessel)
        => Alle.FirstOrDefault(rolle
            => string.Equals(rolle.Modul, modul, StringComparison.OrdinalIgnoreCase)
            && string.Equals(rolle.Schluessel, schluessel, StringComparison.OrdinalIgnoreCase));

    /// <summary>Module, die überhaupt Rollen haben — speist die Chip-Leiste der Geräteseite.</summary>
    public static IReadOnlyList<string> Module { get; } = Alle
        .Select(rolle => rolle.Modul)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
