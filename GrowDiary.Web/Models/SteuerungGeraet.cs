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
    IReadOnlyList<string> Domains,
    bool Pflicht = true,
    string? Einheit = null,
    string? Hinweis = null)
{
    /// <summary>
    /// Fork AI (forkai.138, F-034): Es gibt keine Werksvorgabe. Die früheren Vorgaben waren die
    /// Entitäten einer einzelnen Anlage — für jeden anderen Benutzer fremde Kennungen. Sie wurden
    /// einmalig als feste Zuordnung übernommen, wo es die Entität gab; seit A-016 (Etappe 5) stehen
    /// sie nicht mehr im Code. Die Eigenschaft bleibt, weil die Oberfläche das Feld kennt.
    /// </summary>
    public string Vorgabe => string.Empty;
}

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

    /// <summary>
    /// Fork AI (Chiller-Ansteuerung): Eine optionale Rolle mit Vorgabe, die der
    /// Nutzer bewusst leer lässt. Ohne diese Marke fiele eine geleerte Rolle auf
    /// die Vorgabe zurück — wer keine Steckdose hat, bekäme dann die Steckdose
    /// einer fremden Anlage zugeordnet.
    /// </summary>
    public const string BewusstLeer = "-";

    public const string GruppeMessen = "messen";
    public const string GruppeSchalten = "schalten";
    public const string GruppeUmfeld = "umfeld";

    public static IReadOnlyList<GeraeteRolle> Alle { get; } = new GeraeteRolle[]
    {
        new("co2", "co2_sensor", "CO₂-Sensor", GruppeMessen,
            new[] { "sensor" }, Einheit: "ppm"),
        // Fork AI: heisst bewusst nicht "Blatttemperatur". Vorbelegt ist der
        // Luftfuehler der Sonde, und das ist fuer die CO2-Schwellen auch die
        // richtige Groesse -- die Quelle nennt 26-30 Grad Lufttemperatur. Wer
        // einen IR-Sensor hat, mappt hier die echte Blatttemperatur; die
        // Beschriftung muss deshalb fuer beides stimmen.
        new("co2", "canopy", "Canopy-Temperatur", GruppeMessen,
            new[] { "sensor" }, Einheit: "°C",
            Hinweis: "Der Luftfühler im Zelt genügt. Ein IR-Sensor misst hier die echte Blatttemperatur — dann verschieben sich die Schwellen entsprechend."),
        new("co2", "rh", "Luftfeuchte", GruppeMessen,
            new[] { "sensor" }, Einheit: "%"),
        new("co2", "vpd", "VPD", GruppeMessen,
            new[] { "sensor" }, Pflicht: false, Einheit: "kPa"),
        new("co2", "port_zustand", "Dosier-Steckdose · Zustand", GruppeSchalten,
            new[] { "binary_sensor", "switch" },
            Hinweis: "Läuft das Ventil wirklich — nicht nur „Port online“."),
        // Fork AI (forkai.70): Zustand und Schalter sind zwei Rollen, weil es bei
        // AC-Infinity zwei Entitaeten sind - ein binary_sensor sagt, ob der Port
        // laeuft, ein select schaltet ihn. Ohne diese Trennung kann der Fork die
        // Dosier-Automation nicht anlegen: er wuesste, woran er den Zustand
        // abliest, aber nicht, was er umlegen soll.
        new("co2", "port_schalter", "Dosier-Steckdose · schalten", GruppeSchalten,
            new[] { "select", "switch", "input_boolean" },
            Hinweis: "Was das Ventil wirklich umlegt — bei AC Infinity der Modus-Auswahlpunkt."),
        // Fork AI (01.10.2026): Die Dosier-Vorlage prüfte vor dem Öffnen
        // port_zustand auf „on" — vor dem Öffnen ist das Ventil aber zu, die
        // Dosierung startete nie. Gemeint war „Port online", wie in der
        // handgebauten Fassung. Optional: ohne sie entfällt nur diese Prüfung.
        new("co2", "port_status", "Dosier-Steckdose · Port online", GruppeMessen,
            new[] { "binary_sensor" }, Pflicht: false,
            Hinweis: "Erreichbar oder nicht — nicht, ob das Ventil offen ist. Ohne sie dosiert die Vorlage auch, wenn der Port nicht antwortet."),
        new("co2", "abluft_stufe", "Abluft T6 · Stufe", GruppeSchalten,
            new[] { "number" }, Pflicht: false),
        new("co2", "licht", "Licht-Status", GruppeUmfeld,
            new[] { "binary_sensor", "switch", "light" },
            Hinweis: "Begast wird nur bei Licht."),

        // Licht: alle sechs Rollen zeigen auf denselben Controller-Port. Getrennt,
        // weil ein anderes Geraet sie anders aufteilt — manche Lampen haben keine
        // Stufe, manche keinen eigenen Zeitplan.
        new("licht", "licht_modus", "Betriebsart der Lampe", GruppeSchalten,
            new[] { "select" },
            Hinweis: "Aus / An / Zeitplan. Beim AC-Infinity-Port heißt das Off, On, Schedule."),
        new("licht", "licht_stufe", "Leistungsstufe", GruppeSchalten,
            new[] { "number" }, Pflicht: false,
            Hinweis: "Ohne sie fehlt der Stufenbalken; Aus, An und Zeitplan bleiben."),
        new("licht", "licht_ein_zeit", "Geplante Ein-Zeit", GruppeSchalten,
            new[] { "time" }, Pflicht: false,
            Hinweis: "Ohne die beiden Zeiten entfallen die Zeitpläne; Aus und An gehen weiter."),
        new("licht", "licht_aus_zeit", "Geplante Aus-Zeit", GruppeSchalten,
            new[] { "time" }, Pflicht: false),
        new("licht", "licht_zustand", "Lampe · Zustand", GruppeMessen,
            new[] { "binary_sensor", "switch", "light" },
            Hinweis: "Brennt die Lampe wirklich — nicht nur „Port online“."),
        new("licht", "licht_status", "Lampe · Port online", GruppeMessen,
            new[] { "binary_sensor" }, Pflicht: false),

        // Zuluft: zwei Fuehlerpaare und ein Luefter-Port. Temperatur und Feuchte
        // stehen getrennt, weil die absolute Feuchte aus beiden gerechnet wird —
        // ein kombinierter Fuehler waere die Ausnahme, nicht die Regel.
        new("zuluft", "aussen_temp", "Außenfühler · Temperatur", GruppeMessen,
            new[] { "sensor" }, Einheit: "°C",
            Hinweis: "Die Luft, die angesaugt wird."),
        new("zuluft", "aussen_rh", "Außenfühler · Feuchte", GruppeMessen,
            new[] { "sensor" }, Einheit: "%"),
        new("zuluft", "keller_temp", "Kellerfühler · Temperatur", GruppeMessen,
            new[] { "sensor" }, Einheit: "°C",
            Hinweis: "Die Luft, die verdrängt werden soll."),
        new("zuluft", "keller_rh", "Kellerfühler · Feuchte", GruppeMessen,
            new[] { "sensor" }, Einheit: "%"),
        new("zuluft", "port_schalter", "Zuluft-Lüfter · schalten", GruppeSchalten,
            new[] { "select", "switch", "input_boolean" },
            Hinweis: "Was den Lüfter wirklich umlegt — bei AC Infinity der Modus-Auswahlpunkt."),
        new("zuluft", "port_zustand", "Zuluft-Lüfter · Zustand", GruppeMessen,
            new[] { "binary_sensor", "switch" },
            Hinweis: "Läuft der Lüfter wirklich — nicht nur „Port online“."),
        new("zuluft", "port_stufe", "Zuluft-Lüfter · Stufe setzen", GruppeSchalten,
            new[] { "number" }, Pflicht: false,
            Hinweis: "Ohne sie läuft der Lüfter nur ein und aus."),
        new("zuluft", "port_ist_stufe", "Zuluft-Lüfter · laufende Stufe", GruppeMessen,
            new[] { "sensor", "number" }, Pflicht: false),
        new("zuluft", "port_status", "Zuluft-Lüfter · Port online", GruppeMessen,
            new[] { "binary_sensor" }, Pflicht: false),
        // Fork AI (forkai.128): Das Zelt zieht seine Luft aus dem Keller. Wird es
        // zu kalt, pausiert die Zuluft — Frostschutz draußen reicht dafür nicht.
        new("zuluft", "zelt_temp", "Zeltfühler · Temperatur", GruppeMessen,
            new[] { "sensor" }, Einheit: "°C",
            Hinweis: "Fällt das Zelt unter das Minimum, pausiert die Zuluft."),

        // Fork AI (forkai.141, F-037): Alarmgrenzen am Bluelab Guardian. Der Fork
        // überträgt seine Grenzwerte (pH, EC, Wassertemperatur) ans Gerät — nur
        // wenn Skript und Grenz-Entitäten zugeordnet sind. Alles optional: wer
        // kein Bluelab hat, lässt das Modul leer.
        new("bluelab", "skript", "Grenze setzen · Skript", GruppeSchalten,
            new[] { "script" }, Pflicht: false,
            Hinweis: "Skript mit den Feldern setting_key und value, das eine Alarmgrenze in der Edenic-Cloud setzt."),
        new("bluelab", "ph_low", "pH · unten", GruppeMessen,
            new[] { "number" }, Pflicht: false),
        new("bluelab", "ph_high", "pH · oben", GruppeMessen,
            new[] { "number" }, Pflicht: false),
        new("bluelab", "ec_low", "EC · unten", GruppeMessen,
            new[] { "number" }, Pflicht: false, Einheit: "mS/cm"),
        new("bluelab", "ec_high", "EC · oben", GruppeMessen,
            new[] { "number" }, Pflicht: false, Einheit: "mS/cm"),
        new("bluelab", "temp_low", "Wassertemperatur · unten", GruppeMessen,
            new[] { "number" }, Pflicht: false, Einheit: "°C"),
        new("bluelab", "temp_high", "Wassertemperatur · oben", GruppeMessen,
            new[] { "number" }, Pflicht: false, Einheit: "°C"),

        // Kühler: ein Fühler im Wasser, eine Steckdose und der Lichtzustand.
        // Die Lampe steht hier, weil Tag und Nacht am Licht hängen und nicht an
        // der Uhr — verschiebt sich die Lichtphase, verschiebt sich das Ziel mit.
        new("chiller", "wasser_temp", "Wasserfühler", GruppeMessen,
            new[] { "sensor" }, Einheit: "°C",
            Hinweis: "Die Temperatur, die geregelt wird."),
        // Fork AI (Chiller-Ansteuerung): Wie der Kühler seine Befehle bekommt,
        // ergibt sich aus diesen beiden Rollen — Steckdose allein: HA schaltet
        // mit Hysterese; Sollwert allein: der Kühler regelt selbst, HA schreibt
        // nur das Ziel; beide: Sollwert ins Gerät, die Steckdose ist Not-Aus.
        // Deshalb ist keine der beiden Pflicht.
        new("chiller", "steckdose", "Kühler · schalten", GruppeSchalten,
            new[] { "switch", "input_boolean" }, Pflicht: false,
            Hinweis: "Für Kühler ohne eigenen Sollwert-Eingang: die Steckdose, die den Kompressor umlegt. Mit Sollwert-Gerät nur noch Not-Aus."),
        new("chiller", "kuehler_sollwert", "Kühler · Sollwert", GruppeSchalten,
            new[] { "climate", "number" }, Pflicht: false, Einheit: "°C",
            Hinweis: "Für Kühler mit eigenem Thermostat (z. B. WLAN): Home Assistant schreibt das Tag- oder Nachtziel direkt ins Gerät."),
        new("chiller", "steckdose_zustand", "Kühler · Zustand", GruppeMessen,
            new[] { "binary_sensor", "switch" }, Pflicht: false,
            Hinweis: "Nur nötig, wenn Schalten und Rückmeldung getrennt sind — eine Funksteckdose ist beides."),
        new("chiller", "leistung", "Kühler · Leistung", GruppeMessen,
            new[] { "sensor" }, Pflicht: false, Einheit: "W",
            Hinweis: "Zeigt, ob der Kompressor wirklich zieht — und füttert die Kosten."),
        new("chiller", "licht_zustand", "Lampe · Zustand", GruppeUmfeld,
            new[] { "binary_sensor", "switch", "light" },
            Hinweis: "Entscheidet zwischen Tag- und Nachtziel."),

        // Fork AI (forkai.129): Entfeuchter — Zeltfühler, der Port und das Licht.
        // Das Licht entscheidet Tag/Nacht, wie beim Kühler: nicht die Uhr.
        new("entfeuchter", "zelt_rh", "Zeltfühler · Feuchte", GruppeMessen,
            new[] { "sensor" }, Einheit: "%",
            Hinweis: "Die Feuchte, nach der geschaltet wird."),
        new("entfeuchter", "zelt_temp", "Zeltfühler · Temperatur", GruppeMessen,
            new[] { "sensor" }, Einheit: "°C",
            Hinweis: "Darüber schaltet der Entfeuchter ab — er gibt selbst Wärme ab."),
        new("entfeuchter", "zelt_vpd", "Zeltfühler · VPD", GruppeMessen,
            new[] { "sensor" }, Pflicht: false, Einheit: "kPa"),
        new("entfeuchter", "port_schalter", "Entfeuchter · schalten", GruppeSchalten,
            new[] { "select", "switch", "input_boolean" },
            Hinweis: "Was das Gerät wirklich umlegt — bei AC Infinity der Modus-Auswahlpunkt."),
        new("entfeuchter", "port_zustand", "Entfeuchter · Zustand", GruppeMessen,
            new[] { "binary_sensor", "switch" },
            Hinweis: "Läuft er wirklich — nicht nur „Port online“."),
        new("entfeuchter", "port_status", "Entfeuchter · Port online", GruppeMessen,
            new[] { "binary_sensor" }, Pflicht: false),
        new("entfeuchter", "licht_zustand", "Lampe · Zustand", GruppeUmfeld,
            new[] { "binary_sensor", "switch", "light" },
            Hinweis: "Entscheidet zwischen Tag- und Nachtschwellen."),
        // Fork AI (A-016, 09.10.2026): Die Zuluft-Steuerung läuft neben dem Entfeuchter. Trocknet
        // die Außenluft ohnehin, wartet er länger, bevor er anspringt (trotec_wartezeit_aussenluft).
        // Beides optional: wer keine Zuluft-Steuerung hat, lässt die Rollen leer, und die
        // Regelung schaltet nach der Einschaltverzögerung allein.
        new("entfeuchter", "zuluft_bedarf", "Zuluft · Bedarf", GruppeUmfeld,
            new[] { "binary_sensor" }, Pflicht: false,
            Hinweis: "An, solange Außenluft die Zeltluft trockener macht. Ohne sie gilt immer die kurze Wartezeit."),
        new("entfeuchter", "zuluft_stufe_ist", "Zuluft-Lüfter · laufende Stufe", GruppeMessen,
            new[] { "sensor", "number" }, Pflicht: false,
            Hinweis: "Läuft die Zuluft wirklich? Ohne sie zählt allein der Bedarf."),

        // Fork AI (A-009, 06.10.2026): Zusatz-Entfeuchter — ein zweiter Trotec an
        // einer Shelly-Steckdose. Zelt-Fühler und Licht werden NICHT noch einmal
        // zugeordnet, sondern aus dem Modul „entfeuchter" mitbenutzt
        // (siehe MitbenutztVon).
        new(ZusatzModul, "zusatz_schalter", "Zusatz-Entfeuchter · schalten", GruppeSchalten,
            new[] { "switch", "input_boolean" },
            Hinweis: "Die Steckdose (Shelly), an der der zweite Entfeuchter hängt."),
        new(ZusatzModul, "zusatz_leistung", "Zusatz-Entfeuchter · Leistung", GruppeMessen,
            new[] { "sensor" }, Pflicht: false, Einheit: "W",
            Hinweis: "Zeigt, ob er wirklich zieht. Ohne sie entfällt die Meldung „zieht nichts“."),
        new(ZusatzModul, "zusatz_energie", "Zusatz-Entfeuchter · Energie", GruppeMessen,
            new[] { "sensor" }, Pflicht: false, Einheit: "kWh",
            Hinweis: "Zählerstand der Steckdose — daraus rechnet die Seite „Energie heute“."),
        new(ZusatzModul, "fuehrung_zustand", "Führungsgerät · Zustand", GruppeMessen,
            new[] { "select", "switch", "input_boolean", "binary_sensor" },
            Hinweis: "Das Gerät aus „Entfeuchter“ (bei AC Infinity der Modus-Auswahlpunkt On/Off). Der Zusatz schaltet nur zu oder ab, solange es läuft — beide gehen nie gleichzeitig aus."),
    };

    /// <summary>Die Kennung des Zusatz-Entfeuchters.</summary>
    public const string ZusatzModul = "entfeuchter-zusatz";

    /// <summary>
    /// Fork AI (A-009): Rollen, die ein Modul von einem anderen mitbenutzt — keine
    /// doppelte Zuordnung. Zelt-Fühler und Licht des Zusatz-Entfeuchters sind die
    /// des Entfeuchters.
    /// </summary>
    public static IReadOnlyList<(string Quelle, string Rolle)> MitbenutztVon(string modul)
        => string.Equals(modul, ZusatzModul, StringComparison.OrdinalIgnoreCase)
            ? [("entfeuchter", "zelt_rh"), ("entfeuchter", "zelt_temp"), ("entfeuchter", "zelt_vpd"), ("entfeuchter", "licht_zustand")]
            : [];

    /// <summary>Die eigenen Rollen eines Moduls und die mitbenutzten.</summary>
    public static IReadOnlyList<GeraeteRolle> FuerModulMitMitbenutzten(string modul)
        => FuerModul(modul)
            .Concat(MitbenutztVon(modul).Select(m => Finden(m.Quelle, m.Rolle)
                ?? throw new InvalidOperationException($"{m.Quelle}/{m.Rolle} gibt es nicht — Tippfehler in MitbenutztVon.")))
            .ToList();


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
