namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (forkai.45): Was eine Steuerung in Home Assistant an eigenen Objekten
/// braucht — Helfer, Rechen-Sensoren, Automationen.
/// </summary>
/// <remarks>
/// <para><b>Wozu das hier steht.</b> Die Rollen in <see cref="SteuerungGeraeteRollen"/>
/// beschreiben <i>fremde</i> Geräte: den CO₂-Sensor, die Steckdose, den Lüfter.
/// Die gehören dem Nutzer, der Fork ordnet sie nur zu. Dieser Katalog beschreibt
/// das Gegenstück — die Objekte, die <i>zur Steuerung selbst</i> gehören und in
/// einer frischen Installation schlicht nicht existieren: ein Helfer für die
/// Hysterese, ein Sensor, der die Impulslänge rechnet, die Automation, die
/// dosiert.</para>
/// <para><b>Warum deklarativ.</b> Bei Bru sind diese Objekte über Tage von Hand
/// entstanden. Bei jedem anderen entstehen sie gar nicht, und die Steuerungsseite
/// zeigt lauter „nicht verfügbar" ohne zu sagen, woran es liegt. Eine Liste, die
/// Prüfen und Anlegen aus derselben Quelle speist, kann beides nicht
/// auseinanderlaufen lassen.</para>
/// <para><b>Was hier absichtlich fehlt.</b> Die Werte. Ein Bauteil sagt, dass es
/// den Helfer <c>co2_hysterese</c> geben muss und in welchen Grenzen er sich
/// bewegt — nicht, dass dort 50 steht. Die Sollwerte gehören dem Nutzer und
/// stehen in den Einstellungen der Steuerung.</para>
/// </remarks>
public enum BauteilArt
{
    /// <summary>Ein Zahlenwert, den der Nutzer verstellt (input_number).</summary>
    Zahl,
    /// <summary>Ein Schalter (input_boolean).</summary>
    Schalter,
    /// <summary>Ein Zeitstempel (input_datetime).</summary>
    Zeitpunkt,
    /// <summary>Ein Zähler (counter).</summary>
    Zaehler,
    /// <summary>Ein rechnender Sensor (Template-Helfer, sensor).</summary>
    RechenSensor,
    /// <summary>Ein rechnender Ja/Nein-Sensor (Template-Helfer, binary_sensor).</summary>
    RechenSchalter,
    /// <summary>Eine Automation.</summary>
    Automation,
}

/// <summary>Ein einzelnes Objekt, das die Steuerung in Home Assistant braucht.</summary>
/// <param name="Modul">Zu welcher Steuerung es gehört — <c>co2</c>, später andere.</param>
/// <param name="EntityId">Die volle Entitäts-Id, wie sie nach dem Anlegen heißt.</param>
/// <param name="Name">Der Anzeigename in Home Assistant.</param>
/// <param name="Art">Was für ein Objekt es ist.</param>
/// <param name="Zweck">Ein Satz für den Nutzer — warum es das gibt.</param>
/// <param name="Pflicht">
/// False, wenn die Steuerung ohne dieses Objekt läuft und nur eine Funktion
/// verliert. Die Seite sagt dann, welche.
/// </param>
/// <param name="HaengtAn">
/// Rollen-Schlüssel, ohne die dieses Bauteil sinnlos ist. Wer keinen Lüfter hat,
/// braucht die Abluft-Helfer nicht — sie werden dann gar nicht erst angelegt.
/// </param>
/// <param name="OhneDas">Was ausfällt, wenn das Bauteil fehlt (nur bei <c>Pflicht: false</c>).</param>
/// <param name="Min">Untergrenze bei <see cref="BauteilArt.Zahl"/>.</param>
/// <param name="Max">Obergrenze bei <see cref="BauteilArt.Zahl"/>.</param>
/// <param name="Schritt">Schrittweite bei <see cref="BauteilArt.Zahl"/>.</param>
/// <param name="Einheit">Einheit bei <see cref="BauteilArt.Zahl"/>.</param>
public sealed record Bauteil(
    string Modul,
    string EntityId,
    string Name,
    BauteilArt Art,
    string Zweck,
    bool Pflicht = true,
    IReadOnlyList<string>? HaengtAn = null,
    string? OhneDas = null,
    double? Min = null,
    double? Max = null,
    double? Schritt = null,
    string? Einheit = null)
{
    /// <summary>Die Domäne der Entität — <c>input_number</c>, <c>sensor</c>, …</summary>
    public string Domaene => EntityId.Split('.', 2)[0];

    /// <summary>Der Teil hinter dem Punkt, den Home Assistant aus dem Namen ableitet.</summary>
    public string Objektkennung => EntityId.Split('.', 2).Length > 1 ? EntityId.Split('.', 2)[1] : EntityId;
}

/// <summary>Der Katalog aller Steuerungen an einer Stelle.</summary>
public static class SteuerungBauteile
{
    private const string Co2 = "co2";

    // Rollen, an denen Bauteile hängen — Schreibweise wie in SteuerungGeraeteRollen.
    private static readonly string[] BrauchtAbluft = { "abluft_stufe" };
    private static readonly string[] BrauchtRh = { "rh" };
    private static readonly string[] BrauchtCanopy = { "canopy" };

    /// <summary>Alle Bauteile aller Steuerungen.</summary>
    public static IReadOnlyList<Bauteil> Alle { get; } = new Bauteil[]
    {
        // --- Ziel ---------------------------------------------------------
        new(Co2, "input_number.co2_zielwert", "CO2 Zielwert", BauteilArt.Zahl,
            "Ziel bei warmer Blatttemperatur.", Min: 400, Max: 2000, Schritt: 10, Einheit: "ppm"),
        new(Co2, "input_number.co2_ziel_mittel_25_bis_27_c", "CO2 Ziel mittel 25 bis 27 C", BauteilArt.Zahl,
            "Ziel im mittleren Temperaturbereich.", Pflicht: false, HaengtAn: BrauchtCanopy,
            OhneDas: "Ohne Blattfühler gilt durchgehend ein Ziel.",
            Min: 400, Max: 2000, Schritt: 10, Einheit: "ppm"),
        new(Co2, "input_number.co2_ziel_kuehl_unter_25_c", "CO2 Ziel kuehl unter 25 C", BauteilArt.Zahl,
            "Ziel bei kühler Blatttemperatur.", Pflicht: false, HaengtAn: BrauchtCanopy,
            OhneDas: "Ohne Blattfühler gilt durchgehend ein Ziel.",
            Min: 400, Max: 2000, Schritt: 10, Einheit: "ppm"),
        new(Co2, "input_number.co2_hysterese", "CO2 Hysterese", BauteilArt.Zahl,
            "Wie weit der Wert unter das Ziel fallen darf, bevor nachdosiert wird.",
            Min: 20, Max: 300, Schritt: 10, Einheit: "ppm"),

        // --- Dosierung ----------------------------------------------------
        new(Co2, "input_number.co2_impulsdauer", "CO2 Impulsdauer min", BauteilArt.Zahl,
            "Kürzester Impuls.", Min: 1, Max: 60, Schritt: 1, Einheit: "s"),
        new(Co2, "input_number.co2_impulsdauer_max", "CO2 Impulsdauer max", BauteilArt.Zahl,
            "Längster Impuls. Muss unter der Wächter-Schwelle bleiben.",
            Min: 5, Max: 45, Schritt: 1, Einheit: "s"),
        new(Co2, "input_number.co2_wartezeit", "CO2 Wartezeit", BauteilArt.Zahl,
            "Pause nach einem Impuls — der Sensor hinkt der Wirklichkeit nach.",
            Min: 10, Max: 600, Schritt: 5, Einheit: "s"),
        new(Co2, "input_number.co2_max_impulse_je_zyklus", "CO2 max Impulse je Zyklus", BauteilArt.Zahl,
            "Notbremse gegen eine Schleife, die nicht endet.", Min: 1, Max: 60, Schritt: 1),
        new(Co2, "input_number.co2_zeltvolumen", "CO2 Zeltvolumen", BauteilArt.Zahl,
            "Aus dem Volumen wird die nötige Gasmenge gerechnet.",
            Min: 1, Max: 20, Schritt: 0.01, Einheit: "m³"),
        new(Co2, "input_number.co2_gramm_pro_sekunde", "CO2 Gramm pro Sekunde", BauteilArt.Zahl,
            "Durchfluss des Ventils. Wird aus dem ppm-Anstieg nachgeführt.",
            Min: 0.005, Max: 2, Schritt: 0.001, Einheit: "g/s"),
        new(Co2, "input_number.co2_g_s_letzte_messung", "CO2 g/s letzte Messung", BauteilArt.Zahl,
            "Der Rohwert der letzten Messung, ungeglättet.",
            Min: 0, Max: 2, Schritt: 0.001, Einheit: "g/s"),
        new(Co2, "input_boolean.co2_autokalibrierung", "CO2 Autokalibrierung", BauteilArt.Schalter,
            "Führt den Durchfluss selbst nach."),
        new(Co2, "input_number.co2_flasche_rest", "CO2 Flasche Rest", BauteilArt.Zahl,
            "Rechnerischer Flascheninhalt. Springt er nach oben, gilt das als Wechsel.",
            Min: 0, Max: 12, Schritt: 0.0001, Einheit: "kg"),
        new(Co2, "counter.co2_impulse_heute", "CO2 Impulse heute", BauteilArt.Zaehler,
            "Zählt die Impulse der laufenden Lichtphase."),
        new(Co2, "input_datetime.co2_letzter_impuls", "CO2 letzter Impuls", BauteilArt.Zeitpunkt,
            "Zeitstempel statt last_changed — überlebt einen Neustart."),

        // --- Klima --------------------------------------------------------
        new(Co2, "input_number.co2_rh_obergrenze", "CO2 RH Obergrenze", BauteilArt.Zahl,
            "Über dieser Feuchte wird nicht dosiert.", Pflicht: false, HaengtAn: BrauchtRh,
            OhneDas: "Ohne Feuchtefühler hat das Klima keinen Vorrang.",
            Min: 40, Max: 80, Schritt: 1, Einheit: "%"),
        new(Co2, "input_number.co2_klima_hysterese", "CO2 Klima Hysterese", BauteilArt.Zahl,
            "Abstand unter der Obergrenze, ab dem wieder freigegeben wird.",
            Pflicht: false, HaengtAn: BrauchtRh,
            OhneDas: "Ohne Feuchtefühler hat das Klima keinen Vorrang.",
            Min: 0, Max: 10, Schritt: 0.5, Einheit: "%"),
        new(Co2, "input_number.co2_canopy_obergrenze", "CO2 Blatttemperatur Obergrenze", BauteilArt.Zahl,
            "Über dieser Blatttemperatur wird nicht dosiert.",
            Pflicht: false, HaengtAn: BrauchtCanopy,
            OhneDas: "Ohne Blattfühler entfällt die Temperaturgrenze.",
            Min: 22, Max: 34, Schritt: 0.5, Einheit: "°C"),

        // --- Abluft -------------------------------------------------------
        new(Co2, "input_boolean.co2_abluft_drosseln", "CO2 Abluft drosseln", BauteilArt.Schalter,
            "Drosselt die Abluft während des Dosierens.",
            Pflicht: false, HaengtAn: BrauchtAbluft,
            OhneDas: "Ohne Abluft-Regler bleibt das Plateau niedriger — es wird weiter abgesaugt."),
        new(Co2, "input_number.co2_t6_stufe_normal", "CO2 T6 Stufe normal", BauteilArt.Zahl,
            "Stufe außerhalb der Dosierung.", Pflicht: false, HaengtAn: BrauchtAbluft,
            OhneDas: "Ohne Abluft-Regler entfällt die Drosselung.", Min: 1, Max: 10, Schritt: 1),
        new(Co2, "input_number.co2_t6_stufe_dosierung", "CO2 T6 Stufe Dosierung", BauteilArt.Zahl,
            "Stufe während der Dosierung.", Pflicht: false, HaengtAn: BrauchtAbluft,
            OhneDas: "Ohne Abluft-Regler entfällt die Drosselung.", Min: 1, Max: 10, Schritt: 1),
        new(Co2, "input_number.co2_t6_stufe_tief", "CO2 T6 Stufe tief", BauteilArt.Zahl,
            "Noch tiefere Stufe, wenn Temperatur und Feuchte es hergeben.",
            Pflicht: false, HaengtAn: BrauchtAbluft,
            OhneDas: "Ohne Abluft-Regler entfällt die Drosselung.", Min: 1, Max: 10, Schritt: 1),
        new(Co2, "input_number.co2_t6_tief_max_temp", "CO2 T6 tief max Temp", BauteilArt.Zahl,
            "Bis zu dieser Blatttemperatur ist die tiefe Stufe erlaubt.",
            Pflicht: false, HaengtAn: BrauchtAbluft,
            OhneDas: "Ohne Abluft-Regler entfällt die Drosselung.", Min: 24, Max: 32, Schritt: 0.1, Einheit: "°C"),

        // --- Zeiten -------------------------------------------------------
        new(Co2, "input_number.co2_start_nach_licht_an", "CO2 Start nach Licht an", BauteilArt.Zahl,
            "Die Photosynthese braucht 15 bis 30 Minuten, bis sie voll läuft.",
            Min: 0, Max: 120, Schritt: 1, Einheit: "min"),
        new(Co2, "input_number.co2_ende_vor_licht_aus", "CO2 Ende vor Licht aus", BauteilArt.Zahl,
            "Gerechnet gegen die geplante Aus-Zeit des Lichts.",
            Min: 0, Max: 240, Schritt: 5, Einheit: "min"),

        // --- Rechen-Sensoren ----------------------------------------------
        new(Co2, "sensor.co2_ziel_effektiv", "CO2 Ziel effektiv", BauteilArt.RechenSensor,
            "Welches der drei Ziele gerade gilt, mit Hysterese an den Temperaturstufen."),
        new(Co2, "binary_sensor.co2_bedarf", "CO2 Bedarf", BauteilArt.RechenSchalter,
            "An, solange nachdosiert werden soll. Hält seinen Zustand, wenn der Sensor schweigt."),
        new(Co2, "binary_sensor.co2_klima_ok", "CO2 Klima OK", BauteilArt.RechenSchalter,
            "Ob das Klima die Dosierung erlaubt.", Pflicht: false, HaengtAn: BrauchtRh,
            OhneDas: "Ohne Feuchtefühler ist die Freigabe immer erteilt."),
        new(Co2, "sensor.co2_impuls_bedarf", "CO2 Impuls Bedarf", BauteilArt.RechenSensor,
            "Wie lang der nächste Impuls sein muss — aus fehlenden ppm, Volumen und Durchfluss."),

        // --- Automationen -------------------------------------------------
        new(Co2, "automation.co2_dosierung_rdwc_port_5", "CO2 Dosierung", BauteilArt.Automation,
            "Die eigentliche Regelung: Impuls, Prüfen, Warten."),
        new(Co2, "automation.co2_wachter_rdwc_port_5", "CO2 Wächter", BauteilArt.Automation,
            "Schließt das Ventil zwangsweise, wenn es zu lange offen steht. Wird immer angelegt."),
        new(Co2, "automation.co2_abluft_drosselung_t6_rdwc_port_1", "CO2 Abluft-Drosselung", BauteilArt.Automation,
            "Senkt die Abluft während des Dosierens.", Pflicht: false, HaengtAn: BrauchtAbluft,
            OhneDas: "Ohne Abluft-Regler entfällt die Drosselung."),
    };

    /// <summary>Die Bauteile einer Steuerung.</summary>
    public static IReadOnlyList<Bauteil> FuerModul(string modul)
        => Alle.Where(b => string.Equals(b.Modul, modul, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Die Bauteile, die bei den zugeordneten Rollen überhaupt Sinn ergeben.
    /// </summary>
    /// <remarks>
    /// Wer keinen Abluft-Regler hat, bekommt die vier T6-Helfer nicht — sie
    /// stünden sonst als „fehlt" in der Liste und wären doch nie zu gebrauchen.
    /// </remarks>
    public static IReadOnlyList<Bauteil> Anwendbar(string modul, IReadOnlyCollection<string> belegteRollen)
        => FuerModul(modul)
            .Where(b => b.HaengtAn is null || b.HaengtAn.All(belegteRollen.Contains))
            .ToList();
}
