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
/// <param name="Name">
/// Der Name in Home Assistant — <b>kein</b> Titel für die Oberfläche.
/// <para>
/// Home Assistant leitet die Objektkennung aus dem Namen ab: klein, alles
/// Nicht-Alphanumerische zu Unterstrichen. Der Name muss deshalb genau zu
/// <see cref="Bauteil.EntityId"/> passen, sonst entsteht ein Helfer unter einer
/// Kennung, die der Katalog nie findet — und beim nächsten Lauf wird er ein
/// zweites Mal angelegt. Zwei Namen sehen deshalb ungeschickt aus:
/// „CO2 Impulsdauer" heißt so, weil die Entität aus einer Zeit stammt, als es
/// noch keine Obergrenze gab, und „CO2 Canopy Obergrenze" trägt ein Wort, das
/// auf der Oberfläche nichts zu suchen hat. Gelesen wird ohnehin
/// <see cref="Bauteil.Zweck"/>.
/// </para>
/// </param>
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
/// <param name="Einheit">Einheit bei <see cref="BauteilArt.Zahl"/> und den Rechenwerten.</param>
/// <param name="Vorlage">
/// Die Rechenvorschrift eines Rechenwerts, mit Platzhaltern für die Geräte.
/// <para>
/// Ein Platzhalter steht für den Rollen-Schlüssel in doppelten eckigen Klammern
/// und wird beim Anlegen durch die zugeordnete Entität ersetzt. Er passt
/// absichtlich an beiden Stellen: <c>states('[[canopy]]')</c> ergibt den Wert,
/// <c>states.[[canopy]]</c> das Objekt mit <c>last_changed</c> — weil eine
/// Entitäts-Id mit ihrem Punkt genau das ist, was nach <c>states.</c> gehört.
/// </para>
/// <para>
/// Die Vorschriften bilden den Stand ab, der in Brus Anlage läuft, samt
/// Hysterese und dem Halten des letzten Zustands bei Geräteaussetzern. Ändert er
/// dort etwas, laufen Anlage und Vorlage auseinander — der Abgleich meldet das,
/// überschrieben wird nichts.
/// </para>
/// </param>
/// <param name="Zustandsklasse">Die <c>state_class</c> eines Rechenwerts, etwa <c>measurement</c>.</param>
/// <param name="Verfuegbarkeit">
/// Wann ein Rechenwert überhaupt einen Wert hat — dieselben <c>[[rolle]]</c>-Platzhalter
/// wie in <see cref="Bauteil.Vorlage"/>.
/// <para>
/// Ohne das wird ein Rechenwert bei fehlendem Fühler nicht „nicht verfügbar", sondern
/// rechnet mit dem Vorgabewert weiter — bei der Zuluft hieße das: der Lüfter saugt
/// nach einer Differenz, die aus 0 °C und 0 % entstanden ist.
/// </para>
/// </param>
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
    string? Einheit = null,
    string? Vorlage = null,
    string? Zustandsklasse = null,
    string? Verfuegbarkeit = null)
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
    private const string Zuluft = "zuluft";
    private const string Chiller = "chiller";
    private const string Entfeuchter = "entfeuchter";

    // Rollen, an denen Bauteile hängen — Schreibweise wie in SteuerungGeraeteRollen.
    private static readonly string[] BrauchtAbluft = { "abluft_stufe" };
    private static readonly string[] BrauchtRh = { "rh" };
    private static readonly string[] BrauchtCanopy = { "canopy" };
    private static readonly string[] BrauchtStufe = { "port_stufe" };

    /// <summary>Alle Bauteile aller Steuerungen.</summary>
    public static IReadOnlyList<Bauteil> Alle { get; } = new Bauteil[]
    {
        // --- Ziel ---------------------------------------------------------
        new(Co2, "input_number.co2_zielwert", "CO2 Zielwert", BauteilArt.Zahl,
            "Ziel bei warmer Canopy-Temperatur.", Min: 400, Max: 2000, Schritt: 10, Einheit: "ppm"),
        new(Co2, "input_number.co2_ziel_mittel_25_bis_27_c", "CO2 Ziel mittel 25 bis 27 C", BauteilArt.Zahl,
            "Ziel im mittleren Temperaturbereich.", Pflicht: false, HaengtAn: BrauchtCanopy,
            OhneDas: "Ohne Canopy-Fühler gilt durchgehend ein Ziel.",
            Min: 400, Max: 2000, Schritt: 10, Einheit: "ppm"),
        new(Co2, "input_number.co2_ziel_kuehl_unter_25_c", "CO2 Ziel kuehl unter 25 C", BauteilArt.Zahl,
            "Ziel bei kühler Canopy-Temperatur.", Pflicht: false, HaengtAn: BrauchtCanopy,
            OhneDas: "Ohne Canopy-Fühler gilt durchgehend ein Ziel.",
            Min: 400, Max: 2000, Schritt: 10, Einheit: "ppm"),
        new(Co2, "input_number.co2_hysterese", "CO2 Hysterese", BauteilArt.Zahl,
            "Wie weit der Wert unter das Ziel fallen darf, bevor nachdosiert wird.",
            Min: 20, Max: 300, Schritt: 10, Einheit: "ppm"),

        // --- Dosierung ----------------------------------------------------
        new(Co2, "input_number.co2_impulsdauer", "CO2 Impulsdauer", BauteilArt.Zahl,
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
        new(Co2, "input_number.co2_canopy_obergrenze", "CO2 Canopy Obergrenze", BauteilArt.Zahl,
            "Über dieser Canopy-Temperatur wird nicht dosiert.",
            Pflicht: false, HaengtAn: BrauchtCanopy,
            OhneDas: "Ohne Canopy-Fühler entfällt die Temperaturgrenze.",
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
            "Bis zu dieser Canopy-Temperatur ist die tiefe Stufe erlaubt.",
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
            "Welches der drei Ziele gerade gilt, mit Hysterese an den Temperaturstufen.",
            Einheit: "ppm", Zustandsklasse: "measurement",
            Vorlage: "{% set t = states('[[canopy]]') | float(30) %}{% set kuehl = states('input_number.co2_ziel_kuehl_unter_25_c') | int(650) %}{% set mittel = states('input_number.co2_ziel_mittel_25_bis_27_c') | int(820) %}{% set warm = states('input_number.co2_zielwert') | int(920) %}{% set alt = states('sensor.co2_ziel_effektiv') | int(0) %}{% if t >= 27.0 %}{{ warm }}{% elif t >= 26.5 and alt == warm %}{{ warm }}{% elif t >= 25.0 %}{{ mittel }}{% elif t >= 24.5 and alt == mittel %}{{ mittel }}{% else %}{{ kuehl }}{% endif %}"),
        new(Co2, "binary_sensor.co2_bedarf", "CO2 Bedarf", BauteilArt.RechenSchalter,
            "An, solange nachdosiert werden soll. Hält seinen Zustand, wenn der Sensor schweigt.",
            Vorlage: "{% set co2_s = states.[[co2_sensor]] %}{% set weg = co2_s is none or co2_s.state in ['unknown','unavailable'] %}{% set ziel = states('sensor.co2_ziel_effektiv') | float(0) %}{% if weg or ziel <= 0 %}{% set seit = (as_timestamp(now()) - as_timestamp(co2_s.last_changed)) if co2_s is not none else 9999 %}{% if seit > 300 %}false{% else %}{{ is_state('binary_sensor.co2_bedarf', 'on') }}{% endif %}{% else %}{% set ist = co2_s.state | float(0) %}{% set h = states('input_number.co2_hysterese') | float(100) %}{% if ist <= 0 %}{{ is_state('binary_sensor.co2_bedarf', 'on') }}{% elif ist < ziel - h %}true{% elif ist >= ziel %}false{% else %}{{ is_state('binary_sensor.co2_bedarf', 'on') }}{% endif %}{% endif %}"),
        new(Co2, "binary_sensor.co2_klima_ok", "CO2 Klima OK", BauteilArt.RechenSchalter,
            "Ob das Klima die Dosierung erlaubt.", Pflicht: false, HaengtAn: BrauchtRh,
            OhneDas: "Ohne Feuchtefühler ist die Freigabe immer erteilt.",
            Vorlage: "{% set t_s = states.[[canopy]] %}{% set rh_s = states.[[rh]] %}{% set weg = t_s is none or rh_s is none or t_s.state in ['unknown','unavailable'] or rh_s.state in ['unknown','unavailable'] %}{% if weg %}{% set seit = [ (as_timestamp(now()) - as_timestamp(t_s.last_changed)) if t_s is not none else 9999, (as_timestamp(now()) - as_timestamp(rh_s.last_changed)) if rh_s is not none else 9999 ] | max %}{% if seit > 300 %}false{% else %}{{ is_state('binary_sensor.co2_klima_ok', 'on') }}{% endif %}{% else %}{% set rh = rh_s.state | float(100) %}{% set t = t_s.state | float(100) %}{% set rh_ob = states('input_number.co2_rh_obergrenze') | float(65) %}{% set t_ob = states('input_number.co2_canopy_obergrenze') | float(29) %}{% set h = states('input_number.co2_klima_hysterese') | float(3) %}{% if rh > rh_ob or t > t_ob %}false{% elif rh <= rh_ob - h and t <= t_ob - 0.2 %}true{% else %}{{ is_state('binary_sensor.co2_klima_ok', 'on') }}{% endif %}{% endif %}"),
        new(Co2, "sensor.co2_impuls_bedarf", "CO2 Impuls Bedarf", BauteilArt.RechenSensor,
            "Wie lang der nächste Impuls sein muss — aus fehlenden ppm, Volumen und Durchfluss.",
            Einheit: "s", Zustandsklasse: "measurement",
            Vorlage: "{% set ziel = states('sensor.co2_ziel_effektiv') | float(0) %}{% set ist = states('[[co2_sensor]]') | float(0) %}{% set gps = states('input_number.co2_gramm_pro_sekunde') | float(0.26) %}{% set vol = states('input_number.co2_zeltvolumen') | float(5.76) %}{% set mn = states('input_number.co2_impulsdauer') | float(5) %}{% set mx = states('input_number.co2_impulsdauer_max') | float(20) %}{% set gramm = ([ziel - ist, 0] | max) * vol / 557 %}{{ ([ [gramm / gps, mn] | max, mx ] | min) | round(0) | int }}"),

        // --- Automationen -------------------------------------------------
        new(Co2, "automation.co2_dosierung_rdwc_port_5", "CO2 Dosierung", BauteilArt.Automation,
            "Die eigentliche Regelung: Impuls, Prüfen, Warten."),
        new(Co2, "automation.co2_wachter_rdwc_port_5", "CO2 Wächter", BauteilArt.Automation,
            "Schließt das Ventil zwangsweise, wenn es zu lange offen steht. Wird immer angelegt."),
        new(Co2, "automation.co2_abluft_drosselung_t6_rdwc_port_1", "CO2 Abluft-Drosselung", BauteilArt.Automation,
            "Senkt die Abluft während des Dosierens.", Pflicht: false, HaengtAn: BrauchtAbluft,
            OhneDas: "Ohne Abluft-Regler entfällt die Drosselung."),

        // ====================================================================
        // Zuluft Keller — Außenluft ansaugen, solange sie trockener ist als die
        // Kellerluft. Gerechnet wird mit ABSOLUTER Feuchte: 88 % bei 12 °C tragen
        // weniger Wasser als 57 % bei 22 °C, die Prozente allein führen in die Irre.
        // ====================================================================

        // --- Regel ----------------------------------------------------------
        new(Zuluft, "input_number.zuluft_mindest_differenz", "Zuluft Mindest-Differenz", BauteilArt.Zahl,
            "Ab wie viel Unterschied das Ansaugen lohnt.", Min: 0.2, Max: 10, Schritt: 0.1, Einheit: "g/m³"),
        new(Zuluft, "input_number.zuluft_aussentemperatur_min", "Zuluft Aussentemperatur min", BauteilArt.Zahl,
            "Frostschutz: darunter bleibt der Lüfter aus, egal wie trocken es draußen ist.",
            Min: -10, Max: 25, Schritt: 0.5, Einheit: "°C"),
        // Fork AI (forkai.128)
        new(Zuluft, "input_number.zuluft_zelttemperatur_min", "Zuluft Zelttemperatur min", BauteilArt.Zahl,
            "Fällt das Zelt darunter, pausiert die Zuluft; wieder an ab + 1 °C.",
            Min: 10, Max: 30, Schritt: 0.5, Einheit: "°C"),

        // --- Lüfter ---------------------------------------------------------
        new(Zuluft, "input_number.zuluft_stufe_min", "Zuluft Stufe min", BauteilArt.Zahl,
            "Stufe bei knapper Differenz.", Pflicht: false, HaengtAn: BrauchtStufe,
            OhneDas: "Ohne Stufenregler läuft der Lüfter nur ein und aus.",
            Min: 1, Max: 10, Schritt: 1),
        new(Zuluft, "input_number.zuluft_stufe_max", "Zuluft Stufe max", BauteilArt.Zahl,
            "Stufe bei großer Differenz.", Pflicht: false, HaengtAn: BrauchtStufe,
            OhneDas: "Ohne Stufenregler läuft der Lüfter nur ein und aus.",
            Min: 1, Max: 10, Schritt: 1),
        new(Zuluft, "input_number.zuluft_mindestlaufzeit", "Zuluft Mindestlaufzeit", BauteilArt.Zahl,
            "Wie lange der Lüfter mindestens läuft, bevor er wieder aus darf.",
            Min: 0, Max: 120, Schritt: 1, Einheit: "min"),
        new(Zuluft, "input_number.zuluft_mindestpause", "Zuluft Mindestpause", BauteilArt.Zahl,
            "Wie lange er mindestens aus bleibt.", Min: 0, Max: 120, Schritt: 1, Einheit: "min"),
        new(Zuluft, "input_datetime.zuluft_letzter_schaltvorgang", "Zuluft letzter Schaltvorgang", BauteilArt.Zeitpunkt,
            "Zeitstempel statt last_changed — die Port-Entitäten setzen bei Controller-Aussetzern kurz aus."),

        // --- Rechenwerte ----------------------------------------------------
        // Magnus-Formel. Beide Seiten stehen einzeln, weil die Differenz sonst
        // nicht nachvollziehbar wäre — und weil das Dashboard sie einzeln zeigt.
        new(Zuluft, "sensor.absolute_feuchte_draussen", "Absolute Feuchte Draussen", BauteilArt.RechenSensor,
            "Wie viel Wasser die Außenluft wirklich trägt.",
            Einheit: "g/m³", Zustandsklasse: "measurement",
            Vorlage: "{% set t = states('[[aussen_temp]]') | float %}{% set rh = states('[[aussen_rh]]') | float %}{{ (216.7 * (rh / 100 * 6.112 * e ** (17.62 * t / (243.12 + t))) / (t + 273.15)) | round(2) }}",
            Verfuegbarkeit: "{{ has_value('[[aussen_temp]]') and has_value('[[aussen_rh]]') }}"),
        new(Zuluft, "sensor.absolute_feuchte_keller", "Absolute Feuchte Keller", BauteilArt.RechenSensor,
            "Dasselbe für die Kellerluft.",
            Einheit: "g/m³", Zustandsklasse: "measurement",
            Vorlage: "{% set t = states('[[keller_temp]]') | float %}{% set rh = states('[[keller_rh]]') | float %}{{ (216.7 * (rh / 100 * 6.112 * e ** (17.62 * t / (243.12 + t))) / (t + 273.15)) | round(2) }}",
            Verfuegbarkeit: "{{ has_value('[[keller_temp]]') and has_value('[[keller_rh]]') }}"),
        new(Zuluft, "sensor.zuluft_differenz", "Zuluft Differenz", BauteilArt.RechenSensor,
            "Keller minus draußen. Positiv heißt: Ansaugen trocknet.",
            Einheit: "g/m³", Zustandsklasse: "measurement",
            Vorlage: "{{ (states('sensor.absolute_feuchte_keller') | float - states('sensor.absolute_feuchte_draussen') | float) | round(2) }}",
            Verfuegbarkeit: "{{ has_value('sensor.absolute_feuchte_keller') and has_value('sensor.absolute_feuchte_draussen') }}"),
        new(Zuluft, "sensor.zuluft_zielstufe", "Zuluft Zielstufe", BauteilArt.RechenSensor,
            "Stufe proportional zur Differenz, zwischen Stufe min und max.",
            Pflicht: false, HaengtAn: BrauchtStufe,
            OhneDas: "Ohne Stufenregler entfällt die proportionale Stufe.",
            Vorlage: "{% set d = states('sensor.zuluft_differenz') | float %}{% set s = states('input_number.zuluft_mindest_differenz') | float %}{% set lo = states('input_number.zuluft_stufe_min') | float %}{% set hi = states('input_number.zuluft_stufe_max') | float %}{% set f = [([(d - s) / 3.0, 0] | max), 1] | min %}{{ (lo + (hi - lo) * f) | round(0) | int }}",
            Verfuegbarkeit: "{{ has_value('sensor.zuluft_differenz') }}"),
        new(Zuluft, "binary_sensor.zuluft_bedarf", "Zuluft Bedarf", BauteilArt.RechenSchalter,
            "An, solange Ansaugen lohnt und das Zelt warm genug ist. Mit Hysterese, damit er an der Schwelle nicht flattert. Fehlt der Zeltwert kurz, zählt er nicht (float(99)).",
            Vorlage: "{% set d = states('sensor.zuluft_differenz') | float %}{% set s = states('input_number.zuluft_mindest_differenz') | float %}{% set t = states('[[aussen_temp]]') | float %}{% set tm = states('input_number.zuluft_aussentemperatur_min') | float %}{% set tz = states('[[zelt_temp]]') | float(99) %}{% set tzm = states('input_number.zuluft_zelttemperatur_min') | float(21) %}{% set prev = (this.state == 'on') if this is defined else false %}{% if d >= s and t >= tm and tz >= tzm + 1 %}on{% elif d < s - 0.5 or t < tm - 1 or tz < tzm %}off{% else %}{{ 'on' if prev else 'off' }}{% endif %}",
            Verfuegbarkeit: "{{ has_value('sensor.zuluft_differenz') and has_value('[[aussen_temp]]') }}"),

        // --- Automation -----------------------------------------------------
        new(Zuluft, "automation.zuluft_keller_regelung", "Zuluft Keller Regelung", BauteilArt.Automation,
            "Schaltet den Lüfter-Port und führt die Stufe nach."),

        // ====================================================================
        // Water Chiller — Wassertemperatur auf zwei Zielen halten, Tag und Nacht.
        // Der Kompressor ist das empfindliche Teil: Mindestlaufzeit und -pause
        // sind keine Kosmetik, und das Totband verhindert, dass er im
        // Messrauschen taktet.
        // ====================================================================

        // --- Ziel -----------------------------------------------------------
        new(Chiller, "input_number.chiller_zieltemperatur_tag", "Chiller Zieltemperatur Tag", BauteilArt.Zahl,
            "Ziel, solange die Lampe brennt.", Min: 4, Max: 30, Schritt: 0.5, Einheit: "°C"),
        new(Chiller, "input_number.chiller_zieltemperatur_nacht", "Chiller Zieltemperatur Nacht", BauteilArt.Zahl,
            "Ziel in der Dunkelphase.", Min: 4, Max: 30, Schritt: 0.5, Einheit: "°C"),

        // --- Schutz ---------------------------------------------------------
        new(Chiller, "input_number.chiller_mindestlaufzeit", "Chiller Mindestlaufzeit", BauteilArt.Zahl,
            "Wie lange der Kompressor mindestens läuft, bevor er wieder aus darf.",
            Min: 0, Max: 120, Schritt: 1, Einheit: "min"),
        new(Chiller, "input_number.chiller_mindestpause", "Chiller Mindestpause", BauteilArt.Zahl,
            "Wie lange er mindestens aus bleibt. Zu kurze Pausen kosten ihn das Leben.",
            Min: 0, Max: 120, Schritt: 1, Einheit: "min"),
        new(Chiller, "input_datetime.chiller_letzter_schaltvorgang", "Chiller letzter Schaltvorgang", BauteilArt.Zeitpunkt,
            "Zeitstempel statt last_changed — eine Funksteckdose fällt bei WLAN-Aussetzern kurz aus."),

        // --- Rechenwerte ----------------------------------------------------
        // Das aktive Ziel hängt am Licht, nicht an der Uhr. Fällt der
        // Lichtzustand kurz aus, bleibt der letzte Wert stehen: ein Blip darf
        // das Ziel nicht um Grade springen lassen.
        new(Chiller, "sensor.chiller_zieltemperatur_aktiv", "Chiller Zieltemperatur aktiv", BauteilArt.RechenSensor,
            "Tag- oder Nachtziel, je nach Lichtzustand.",
            Einheit: "°C", Zustandsklasse: "measurement",
            Vorlage: "{% set licht = states('[[licht_zustand]]') %}{% if licht == 'on' %}{{ states('input_number.chiller_zieltemperatur_tag') | float }}{% elif licht == 'off' %}{{ states('input_number.chiller_zieltemperatur_nacht') | float }}{% else %}{{ this.state | float(states('input_number.chiller_zieltemperatur_tag') | float) }}{% endif %}",
            Verfuegbarkeit: "{{ has_value('input_number.chiller_zieltemperatur_tag') and has_value('input_number.chiller_zieltemperatur_nacht') }}"),
        new(Chiller, "binary_sensor.chiller_kuhlbedarf", "Chiller Kuhlbedarf", BauteilArt.RechenSchalter,
            "An, sobald das Wasser über dem Ziel steht. Mit Totband, damit der Kompressor nicht taktet.",
            Vorlage: "{% set t = states('[[wasser_temp]]') | float %}{% set z = states('sensor.chiller_zieltemperatur_aktiv') | float %}{% set prev = (this.state == 'on') if this is defined else false %}{% if t >= z + 0.3 %}on{% elif t <= z - 0.3 %}off{% else %}{{ 'on' if prev else 'off' }}{% endif %}",
            Verfuegbarkeit: "{{ has_value('[[wasser_temp]]') and has_value('sensor.chiller_zieltemperatur_aktiv') }}"),

        // --- Automationen ---------------------------------------------------
        new(Chiller, "automation.water_chiller_regelung", "Water Chiller Regelung", BauteilArt.Automation,
            "Schaltet die Steckdose nach Kühlbedarf, gegen Mindestlaufzeit und -pause."),
        new(Chiller, "automation.water_chiller_wachter", "Water Chiller Wachter", BauteilArt.Automation,
            "Schaltet ab, wenn der Wasserfühler ausfällt, und warnt bei zu warmem Wasser.",
            Pflicht: false,
            OhneDas: "Ohne Wächter läuft der Kühler weiter, wenn der Fühler stumm wird."),


        // ====================================================================
        // Entfeuchter — Fork AI (forkai.129). Geräteverhalten; die Pflanzenziele
        // (VPD-Band, Feuchte max., Blatt-Offset) gehören dem Plan und stehen
        // nicht hier. Die Rechenwerte (EIN/AUS/Temp max. aktiv) und die
        // Automation haben noch keine Vorlage für Neuanlagen — offen in F-023.
        // ====================================================================
        new(Entfeuchter, "input_boolean.trotec_vpd_regelung", "Trotec VPD Regelung", BauteilArt.Schalter,
            "An: Schwellen wandern mit Temperatur und VPD-Band. Aus: feste Schwellen."),
        new(Entfeuchter, "input_number.trotec_hysterese", "Trotec Hysterese", BauteilArt.Zahl,
            "Wie weit die Feuchte unter EIN fallen muss, bevor er ausgeht.", Min: 1, Max: 10, Schritt: 0.5, Einheit: "%"),
        new(Entfeuchter, "input_number.trotec_mindestlaufzeit", "Trotec Mindestlaufzeit", BauteilArt.Zahl,
            "Vorher schaltet ihn erreichte Feuchte nicht ab.", Min: 0, Max: 60, Schritt: 1, Einheit: "min"),
        new(Entfeuchter, "input_number.trotec_einschaltverzoegerung", "Trotec Einschaltverzoegerung", BauteilArt.Zahl,
            "So lange muss die Feuchte über EIN liegen, bevor er anspringt.", Min: 0, Max: 60, Schritt: 1, Einheit: "min"),
        new(Entfeuchter, "input_number.trotec_wartezeit_aussenluft", "Trotec Wartezeit Aussenluft", BauteilArt.Zahl,
            "Wartezeit, solange die Zuluft trocknet.", Min: 0, Max: 120, Schritt: 1, Einheit: "min"),
        new(Entfeuchter, "input_boolean.trotec_tagbetrieb_erlauben", "Trotec Tagbetrieb erlauben", BauteilArt.Schalter,
            "Aus: nur in der Dunkelphase."),
        new(Entfeuchter, "input_number.trotec_temp_max_tag", "Trotec Temp max Tag", BauteilArt.Zahl,
            "Darüber geht er tagsüber aus.", Min: 15, Max: 35, Schritt: 0.5, Einheit: "°C"),
        new(Entfeuchter, "input_number.trotec_temp_max", "Trotec Temp max", BauteilArt.Zahl,
            "Darüber geht er nachts aus.", Min: 15, Max: 35, Schritt: 0.5, Einheit: "°C"),
        new(Entfeuchter, "input_number.trotec_feuchte_ein_tag", "Trotec Feuchte EIN Tag", BauteilArt.Zahl,
            "Rückfallebene ohne VPD-Regelung.", Min: 30, Max: 90, Schritt: 1, Einheit: "%"),
        new(Entfeuchter, "input_number.trotec_feuchte_aus_tag", "Trotec Feuchte AUS Tag", BauteilArt.Zahl,
            "Rückfallebene ohne VPD-Regelung.", Min: 30, Max: 90, Schritt: 1, Einheit: "%"),
        new(Entfeuchter, "input_number.trotec_feuchte_ein", "Trotec Feuchte EIN", BauteilArt.Zahl,
            "Rückfallebene ohne VPD-Regelung (Nacht).", Min: 30, Max: 90, Schritt: 1, Einheit: "%"),
        new(Entfeuchter, "input_number.trotec_feuchte_aus", "Trotec Feuchte AUS", BauteilArt.Zahl,
            "Rückfallebene ohne VPD-Regelung (Nacht).", Min: 30, Max: 90, Schritt: 1, Einheit: "%"),
        new(Entfeuchter, "automation.rdwc_trotec_nachtregelung_port_7_dehumi", "RDWC Trotec Regelung", BauteilArt.Automation,
            "Schaltet den Entfeuchter nach Feuchte, Temperatur und Außenluft."),
    };

    /// <summary>
    /// Die Platzhalter einer Vorlage durch die zugeordneten Entitäten ersetzen.
    /// </summary>
    /// <param name="vorlage">Die Rechenvorschrift mit <c>[[rolle]]</c>-Platzhaltern.</param>
    /// <param name="zuordnung">Rollen-Schlüssel auf Entitäts-Id.</param>
    /// <returns>
    /// Die fertige Vorschrift — oder null, wenn eine gebrauchte Rolle frei ist.
    /// </returns>
    /// <remarks>
    /// Null statt einer Vorschrift mit stehengebliebenem Platzhalter: Ein
    /// Rechenwert, der <c>states('[[canopy]]')</c> auswertet, wird nicht
    /// ungültig — er liefert stumm den Ausweichwert und die Regelung rechnet
    /// mit einer erfundenen Temperatur weiter. Lieber gar nicht anlegen.
    /// </remarks>
    public static string? VorlageFuellen(string vorlage, IReadOnlyDictionary<string, string> zuordnung)
    {
        var fertig = vorlage;
        foreach (var (rolle, entity) in zuordnung)
        {
            if (!string.IsNullOrWhiteSpace(entity))
            {
                fertig = fertig.Replace($"[[{rolle}]]", entity, StringComparison.Ordinal);
            }
        }

        return fertig.Contains("[[", StringComparison.Ordinal) ? null : fertig;
    }

    /// <summary>Welche Rollen eine Vorlage braucht.</summary>
    public static IReadOnlyList<string> PlatzhalterIn(string vorlage)
    {
        var gefunden = new List<string>();
        var rest = vorlage.AsSpan();
        while (true)
        {
            var auf = rest.IndexOf("[[", StringComparison.Ordinal);
            if (auf < 0) break;
            var zu = rest[auf..].IndexOf("]]", StringComparison.Ordinal);
            if (zu < 0) break;

            var name = rest.Slice(auf + 2, zu - 2).ToString();
            if (!gefunden.Contains(name, StringComparer.Ordinal)) gefunden.Add(name);
            rest = rest[(auf + zu + 2)..];
        }

        return gefunden;
    }

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
