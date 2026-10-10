namespace GrowDiary.Web.Services;

/// <summary>
/// Der Verlauf des Demo-Grows — <b>eine</b> Quelle für Kurven und Messungen.
///
/// <para><b>Der Anlass.</b> „Die messdaten sind zu statisch, da ist kein
/// verlauf zu sehen." Zwei Gründe, beide echt:</para>
/// <list type="number">
///   <item>Die Sensor-Historie reichte <b>24 Stunden</b> zurück
///   (<c>DemoData.HistoryHours</c>), die Zelt-Historie bietet aber 7, 14 und 30
///   Tage an und steht auf 14. Im Diagramm war das ein Strich am rechten
///   Rand.</item>
///   <item>Die Werte kamen aus einer Sinuskurve um einen festen Mittelwert.
///   Über mehrere Tage sieht so etwas jeden Tag gleich aus — ein Band, keine
///   Entwicklung.</item>
/// </list>
///
/// <para><b>Warum eine gemeinsame Quelle.</b> Es gäbe zwei Wege, das zu
/// beheben: die Kurve für die Sensoren und die Kurve für die Messungen. Zwei
/// Wege heißt zwei Wahrheiten — und dann zeigt das Diagramm einen
/// EC-Sägezahn, während das Protokoll daneben etwas anderes behauptet. Beide
/// lesen deshalb hier.</para>
///
/// <para><b>Was der Verlauf erzählt.</b> Sechs Wochen Blüte mit vier
/// Geschichten:</para>
/// <list type="bullet">
///   <item><b>EC im Sägezahn.</b> Frisch angemischt bei 1,52, dann täglich
///   rund +0,03, weil die Pflanze mehr Wasser zieht als Salz. Am sechsten Tag
///   knapp über dem Planziel (1,5–1,7) — genau dann ist der Wasserwechsel
///   fällig. Das ist der Grund, warum es den Ablauf gibt. (Das Blütezelt 2
///   steht tiefer, siehe <see cref="Lage"/>.)</item>
///   <item><b>pH gegen die Dosierung.</b> Steigt täglich um rund 0,1, wird
///   alle drei Tage heruntergezogen. Bleibt im Band (5,85–6,09 gegen das Planziel
///   5,8–6,2) — der pH darf im RDWC wandern —, aber man sieht, wer ihn hält.</item>
///   <item><b>Wassertemperatur an der Nachtabsenkung.</b> Jede Blütewoche rund
///   0,35 °C tiefer (die Rampe aus beta.32), dazu der Tag-Nacht-Gang.</item>
///   <item><b>Ein Kühlerausfall</b> von Tag −18 bis −14: das Wasser klettert
///   über den Arbeitsbereich, der gelöste Sauerstoff fällt mit — warmes Wasser
///   hält weniger. Danach Erholung. Erst dadurch hat die Diagnose etwas zu
///   finden und der Verlauf eine Pointe.</item>
/// </list>
///
/// <para><b>Kein Zufall.</b> Jeder Wert folgt aus seinem Zeitpunkt. Ein
/// Bestand, der sich bei jedem Anlegen ändert, macht jede Prüfung, die auf
/// einen Wert zeigt, mal grün und mal rot.</para>
/// </summary>
public static class Demoverlauf
{
    /// <summary>Wie weit der Verlauf zurückreicht.</summary>
    /// <remarks>
    /// 42 Tage — mehr als die 30, die die Zelt-Historie höchstens anzeigt.
    /// </remarks>
    public const int TageRueckwaerts = 42;

    /// <summary>Alle wie viele Tage wird das Becken komplett getauscht?</summary>
    /// <remarks>Sieben — so heißt auch der Ablauf: <c>weekly-water-change</c>.</remarks>
    public const int WasserwechselAlleTage = 7;

    /// <summary>Alle wie viele Tage wird pH nachgestellt und HOCl gegeben?</summary>
    /// <remarks>Zwei bis drei laut SOP-RDWC-CAN-N1 §2.2; hier drei.</remarks>
    public const int DosierAlleTage = 3;

    /// <summary>Der Kühler war von Tag −18 bis −14 aus.</summary>
    public const int StoerungVon = 18;

    /// <summary>Bis hierher — danach ist er repariert.</summary>
    public const int StoerungBis = 14;

    /// <summary>Das Licht geht um 08:00 an.</summary>
    /// <remarks>
    /// <para><b>12/12, weil der Testbestand in der Blüte steht.</b> Bis zum
    /// 24.08.2026 lief hier 18/6 — bei einem Grow, dessen Flip 35 Tage zurück
    /// liegt. Das ist genau der Widerspruch, den Grow OS selbst als Fehler
    /// meldet: <i>„Der Grow ist in der Blüte, das Licht läuft aber 18/6. Das
    /// verhindert die Blüte."</i> (<see cref="LightCycleLearner.Mismatch"/>).
    /// Aufgefallen ist es nie, weil der Testbestand keine Lichtflanken hat und
    /// der Lerner deshalb nie etwas zu vergleichen bekam.</para>
    ///
    /// <para>Ein Testbestand, der die eigenen Regeln der App bricht, verdeckt
    /// Fehler — beim Kühler ist genau das schon einmal passiert.</para>
    /// </remarks>
    public const int LichtAn = 8;

    /// <summary>Und um 20:00 wieder aus.</summary>
    /// <remarks>
    /// Dieselbe Zahl steht im Lichtplan des Testzelts und in den
    /// <c>time.</c>-Entitäten für den AC-Test. Alle drei lesen von hier.
    /// </remarks>
    public const int LichtAus = 20;

    /// <summary>Wie viele Tage liegt dieser Zeitpunkt zurück?</summary>
    private static int TageZurueck(DateTime ortszeit) => (DateTime.Today - ortszeit.Date).Days;

    /// <summary>Wie alt ist die Reihe an diesem Tag? 0 am Anfang, 42 heute.</summary>
    private static int Alter(DateTime ortszeit) => Math.Max(0, TageRueckwaerts - TageZurueck(ortszeit));

    /// <summary>War der Kühler an diesem Tag aus?</summary>
    public static bool Stoerung(DateTime ortszeit)
    {
        var zurueck = TageZurueck(ortszeit);
        return zurueck <= StoerungVon && zurueck >= StoerungBis;
    }

    /// <summary>Ist zu dieser Stunde Licht?</summary>
    /// <remarks>
    /// Trägt auch den Fall mit, dass die Aus-Zeit vor der Ein-Zeit liegt (die
    /// „umgekehrte" Beleuchtung über Mitternacht). Der Testbestand braucht das
    /// heute nicht — aber eine Fensterprüfung, die nur die eine Richtung kann,
    /// ist eine Falle für den, der die Zahlen oben ändert.
    /// </remarks>
    public static bool LichtBrennt(DateTime ortszeit)
        => LichtAus > LichtAn
            ? ortszeit.Hour >= LichtAn && ortszeit.Hour < LichtAus
            : ortszeit.Hour >= LichtAn || ortszeit.Hour < LichtAus;

    /// <summary>Die Ein-Zeit als HH:MM — für Lichtplan und Zeit-Entitäten.</summary>
    public static string LichtAnUhr => $"{LichtAn:00}:00";

    /// <summary>Die Aus-Zeit als HH:MM.</summary>
    public static string LichtAusUhr => $"{LichtAus % 24:00}:00";

    /// <summary>Der Tag-Nacht-Gang: +1 in der Mitte der Lichtphase, −1 in der Mitte der Nacht.</summary>
    /// <remarks>
    /// <para><b>Er hängt am Licht, nicht an der Sonne.</b> Vorher war die Kurve
    /// fest auf 6 Uhr (kalt) und 18 Uhr (warm) verdrahtet — draussen richtig, im
    /// Zelt falsch: dort heizt die Lampe, und die kälteste Stunde liegt mitten
    /// in der Dunkelphase. Bei 12/12 ab 08:00 ist der Höchstwert um 14:00 und
    /// der Tiefstwert um 02:00.</para>
    ///
    /// <para>Damit folgen Luft- und Wassertemperatur automatisch mit, wenn
    /// jemand <see cref="LichtAn"/> ändert.</para>
    /// </remarks>
    private static double Tagesgang(DateTime ortszeit)
    {
        var mitte = (LichtAn + (LichtAus > LichtAn ? LichtAus : LichtAus + 24)) / 2.0;
        return Math.Sin(2 * Math.PI * (ortszeit.TimeOfDay.TotalHours - mitte + 6) / 24);
    }

    /// <summary>Tage seit dem letzten Wasserwechsel (0 bis 6) — nach Kalendertag.</summary>
    /// <remarks>Für die Frage „ist dieser TAG ein Wechseltag?". Die Kurve fragt <see cref="ImWasserzyklus"/>.</remarks>
    public static int SeitWasserwechsel(DateTime ortszeit) => Alter(ortszeit) % WasserwechselAlleTage;

    /// <summary>Tage seit der letzten Dosierung (0 bis 2) — nach Kalendertag.</summary>
    public static int SeitDosierung(DateTime ortszeit) => Alter(ortszeit) % DosierAlleTage;

    /// <summary>Um diese Stunde wird gewechselt und pH nachgestellt — Ortszeit.</summary>
    /// <remarks>
    /// <para><b>Vorher sprang die Kurve um Mitternacht</b>, eingetragen war der
    /// Wechsel aber um 07:00 (Changeout und Messung mit Haken). Sieben Stunden
    /// Abstand zwischen Sensor und Eintrag: das Grow-Tagebuch (A-006) meldete
    /// jeden Wechsel als „dazu ist nichts eingetragen" — der Bestand widersprach
    /// der eigenen Regel der App. Jetzt springt die Kurve genau dann, wenn der
    /// Eintrag sagt, dass gewechselt wurde.</para>
    /// </remarks>
    public const int WechselStunde = 7;

    /// <summary>Wo der Wasser-Sägezahn zu diesem Zeitpunkt steht: 0 ab dem Wechsel um 07:00.</summary>
    public static int ImWasserzyklus(DateTime ortszeit) => Alter(ortszeit.AddHours(-WechselStunde)) % WasserwechselAlleTage;

    /// <summary>Wo der pH-Sägezahn steht: 0 ab dem Nachstellen um 07:00.</summary>
    public static int ImDosierzyklus(DateTime ortszeit) => Alter(ortszeit.AddHours(-WechselStunde)) % DosierAlleTage;

    /// <summary>Um so viel senkt das Nachfüllen ohne Eintrag den EC.</summary>
    public const double NachfuellenEcSenkung = 0.12;

    /// <summary>
    /// Ein Nachfüllen, zu dem nichts eingetragen ist — der Fall, den das
    /// Grow-Tagebuch als „Auffällig" zeigen soll (Brus 03.10.2026, EC 1,74 → 1,61).
    /// </summary>
    /// <remarks>
    /// <para>Immer um 16:55 Ortszeit am jüngsten zurückliegenden Tag (1 bis 6
    /// Tage zurück), an dem der Wasserzyklus bei Tag 4 oder später steht. So
    /// liegt es <b>immer</b> in den sieben Tagen Rohwerte — gleich, an welchem
    /// Tag der Bestand angelegt wird —, und der EC bleibt im Planziel 1,5–1,7:
    /// ab Tag 4 steht er bei mindestens 1,64, nach dem Nachfüllen bei 1,52.</para>
    /// <para>Nur in Zelt 1 (<see cref="Lage.Bluete"/>); bis zum nächsten Wechsel.</para>
    /// </remarks>
    public static DateTime? NachfuellenOhneEintrag()
    {
        for (var zurueck = 1; zurueck <= 6; zurueck++)
        {
            var zeitpunkt = DateTime.Today.AddDays(-zurueck).AddHours(16).AddMinutes(55);
            if (ImWasserzyklus(zeitpunkt) >= 4) return zeitpunkt;
        }

        return null;
    }

    /// <summary>Liegt dieser Zeitpunkt zwischen dem Nachfüllen ohne Eintrag und dem nächsten Wechsel?</summary>
    private static bool Nachgefuellt(DateTime ortszeit, Lage? lage)
    {
        if ((lage ?? Lage.Bluete) != Lage.Bluete || NachfuellenOhneEintrag() is not { } ab || ortszeit < ab) return false;
        var naechsterWechsel = ab.Date.AddDays(WasserwechselAlleTage - ImWasserzyklus(ab)).AddHours(WechselStunde);
        return ortszeit < naechsterWechsel;
    }

    /// <summary>Die wievielte Blütewoche, als Bruch.</summary>
    private static double Bluetewoche(DateTime ortszeit) => Alter(ortszeit) / 7.0;

    /// <summary>
    /// Wo ein Zelt im Grow steht — EC und Luftfeuchte folgen dem Plan seiner Woche.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (offene Punkte 03.10.2026, D1).</b> Beide Blütezelte
    /// lasen dieselbe Kurve — und die stammte aus einem älteren Profil
    /// (EC-Blüteziel 1,00–1,20, Luftfeuchte um 54 %). Seit die Grows nach dem
    /// SKX-Plan laufen, verlangt Zelt 1 EC 1,5–1,7 und höchstens 50 %, das
    /// Blütezelt 2 (Blütewoche 9) EC 0,87–1,13 und höchstens 40 %. Die
    /// Live-Kacheln zeigten bei beiden „daneben" — nach der Regel in CLAUDE.md
    /// war der Bestand falsch, nicht die Kachel. Mit einer Kurve für zwei Zelte
    /// lässt sich das nicht lösen; deshalb die Lage je Zelt.</para>
    /// <para>Alles andere — pH, Wassertemperatur, Kühlerausfall, Licht — bleibt
    /// eine gemeinsame Geschichte.</para>
    /// </remarks>
    /// <param name="EcFrisch">EC direkt nach dem Wasserwechsel, mS/cm.</param>
    /// <param name="FeuchteMitte">Mittlere Luftfeuchte in %.</param>
    public sealed record Lage(double EcFrisch, double FeuchteMitte)
    {
        /// <summary>Zelt 1, mitten in der Blüte: EC 1,52 → 1,71 über die Woche, Luftfeuchte 44–48 %.</summary>
        public static readonly Lage Bluete = new(1.52, 46);

        /// <summary>Das Blütezelt 2 kurz vor der Ernte: EC 0,92 → 1,11, Luftfeuchte 35–39 %.</summary>
        public static readonly Lage Spaetbluete = new(0.92, 37);
    }

    /* ------------------------------------------------------------------ */
    /* Die vier Geschichten                                                */
    /* ------------------------------------------------------------------ */

    /// <summary>EC in mS/cm — Sägezahn über die Woche.</summary>
    /// <remarks>
    /// Täglich rund +0,03, weil die Pflanze mehr Wasser zieht als Salz. In der
    /// <see cref="Lage.Bluete"/> liegt der sechste Tag knapp über dem Planziel
    /// 1,5–1,7 — genau dann ist der Wasserwechsel fällig.
    /// </remarks>
    public static double Ec(DateTime ortszeit, Lage? lage = null)
        => (lage ?? Lage.Bluete).EcFrisch + ImWasserzyklus(ortszeit) * 0.03 + (1 + Tagesgang(ortszeit)) * 0.006
           - (Nachgefuellt(ortszeit, lage) ? NachfuellenEcSenkung : 0);

    /// <summary>pH — Sägezahn über drei Tage, steigt bei Licht schneller.</summary>
    /// <remarks>
    /// <para><b>Boden 5,85, nicht mehr 5,78 (04.10.2026).</b> Der Tiefstwert — frisch
    /// dosiert, mitten in der Nacht — lag unter dem Planziel 5,8–6,2. Weil der
    /// Live-Wert immer auf heute steht (frisch dosiert), war die Kachel in der halben
    /// Nacht „daneben", und <c>e2e/demobestand-im-ziel.spec.ts</c> wurde je nach
    /// Uhrzeit rot (CI-Lauf 37179321959 um 07:16 mit 5,79). Jetzt 5,85 bis 6,09 über
    /// den ganzen Dosierzyklus; gehalten von <c>DemowerteImZielTests</c>.</para>
    /// </remarks>
    public static double Ph(DateTime ortszeit)
        => 5.85 + ImDosierzyklus(ortszeit) * 0.1 + (1 + Tagesgang(ortszeit)) * 0.02;

    /// <summary>Wassertemperatur in °C — Nachtabsenkung, plus Kühlerausfall.</summary>
    public static double WasserTempC(DateTime ortszeit)
        => 20.6 - Bluetewoche(ortszeit) * 0.35
           + (Stoerung(ortszeit) ? 4.4 : 0)
           + Tagesgang(ortszeit) * 0.75;

    /// <summary>
    /// Läuft der Kühler gerade? — nur für den Testbestand.
    /// </summary>
    /// <remarks>
    /// <b>Aus derselben Kurve wie die Wassertemperatur.</b> Ein zweiter,
    /// unabhängig gewürfelter Zustand widerspräche sich sofort: die Karte
    /// zeigt Temperatur und Kühlerzustand nebeneinander, und ein stehender
    /// Kühler bei 23 °C wäre im Testbestand kein Fund, sondern ein Fehler in
    /// den Testdaten. Die Schwelle liegt bewusst mitten im Band, damit über
    /// den Tag beide Zustände vorkommen.
    ///
    /// Während der Störung (dem simulierten Kühlerausfall) steht er — das ist
    /// ja gerade die Störung.
    /// </remarks>
    public static bool KuehlerLaeuft(DateTime ortszeit)
        => !Stoerung(ortszeit) && WasserTempC(ortszeit) > 19.4;

    /// <summary>Gelöster Sauerstoff in mg/L — fällt mit der Wärme.</summary>
    /// <remarks>
    /// Warmes Wasser hält weniger Sauerstoff. Deshalb faellt er waehrend des
    /// Kuehlerausfalls mit — dieselbe Ursache, zwei sichtbare Kurven.
    /// </remarks>
    public static double SauerstoffMgL(DateTime ortszeit)
        => Stoerung(ortszeit) ? 5.8 : 7.6 - Bluetewoche(ortszeit) * 0.05 - Tagesgang(ortszeit) * 0.15;

    /// <summary>Lufttemperatur in °C.</summary>
    public static double LuftTempC(DateTime ortszeit) => 24.75 + Tagesgang(ortszeit) * 0.75;

    /// <summary>Luftfeuchte in % — sinkt, wenn es waermer wird.</summary>
    /// <remarks>
    /// Unter dem Höchstwert des Plans der jeweiligen Woche (Zelt 1: 50 %,
    /// Blütezelt 2: 40 %), siehe <see cref="Lage"/>.
    /// </remarks>
    public static double FeuchtePercent(DateTime ortszeit, Lage? lage = null)
        => (lage ?? Lage.Bluete).FeuchteMitte - Tagesgang(ortszeit) * 1.5;

    /// <summary>ORP in mV — faellt zwischen den HOCl-Gaben ab.</summary>
    /// <remarks>
    /// 18 mV je Tag, nicht mehr 19 (A-006): seit die Gabe um 07:00 kommt statt
    /// um Mitternacht, steht der dritte Tag morgens bis 07:00 noch an — bei 19
    /// waren das 399 mV gegen das Planziel 400–450, und die Kachel war jeden
    /// Morgen „daneben" (DemowerteImZielTests).
    /// </remarks>
    public static double OrpMv(DateTime ortszeit) => 437 - ImDosierzyklus(ortszeit) * 18;

    /// <summary>Fuellstand in Litern — faellt ueber die Woche, springt beim Wechsel zurueck.</summary>
    /// <remarks>
    /// Das Nachfüllen ohne Eintrag (<see cref="NachfuellenOhneEintrag"/>) hebt ihn um
    /// <see cref="NachfuellenLiter"/> — rund 8 %, passend zur EC-Senkung um 0,12 bei
    /// 1,64–1,71 (7 %). Ohne das sänke der EC durch Wasser, das nie ins Becken kam
    /// (Prüfer 05.10.2026).
    /// </remarks>
    public static double FuellstandLiter(DateTime ortszeit, Lage? lage = null)
        => 96 - ImWasserzyklus(ortszeit) * 2.4 - (1 + Tagesgang(ortszeit)) * 0.55
           + (Nachgefuellt(ortszeit, lage) ? NachfuellenLiter : 0);

    /// <summary>So viel Wasser kam beim Nachfüllen ohne Eintrag dazu.</summary>
    public const double NachfuellenLiter = 7;

    /// <summary>Derselbe Pegel als Zentimeter — was ein eTape misst.</summary>
    /// <remarks>
    /// Kein zweiter erfundener Sensor: ein Becken misst entweder Liter ODER
    /// Zentimeter. Der cm-Wert ist die Umrechnung ueber die Grundflaeche des
    /// 100-Liter-Beckens, damit sich der Kalibrier-Assistent durchspielen
    /// laesst.
    /// </remarks>
    public static double FuellstandCm(DateTime ortszeit, Lage? lage = null) => FuellstandLiter(ortszeit, lage) / 3.1;

    /// <summary>PPFD — null, solange das Licht aus ist.</summary>
    public static double Ppfd(DateTime ortszeit)
        => LichtBrennt(ortszeit) ? 720 + Tagesgang(ortszeit) * 60 : 0;

    /// <summary>CO₂ in ppm — faellt bei Licht, weil die Pflanze zehrt.</summary>
    public static double Co2Ppm(DateTime ortszeit)
        => LichtBrennt(ortszeit) ? 760 - Tagesgang(ortszeit) * 90 : 900;

    /// <summary>Der Gang der Raumluft: +1 um 16:00, −1 um 04:00 — die Sonne, nicht die Lampe.</summary>
    /// <remarks>
    /// Außerhalb des Zelts heizt die LED nicht; der Raum folgt dem Tag draußen
    /// und der Heizung, gedämpft. Deshalb eine eigene Kurve und nicht
    /// <see cref="Tagesgang"/> — sonst liefe die Raumluft im Takt der Lampe mit.
    /// </remarks>
    private static double Raumgang(DateTime ortszeit)
        => Math.Sin(2 * Math.PI * (ortszeit.TimeOfDay.TotalHours - 10) / 24);

    /// <summary>Lufttemperatur außerhalb des Zelts in °C — ein beheizter Raum, 19 bis 21 °C.</summary>
    public static double AussenTempC(DateTime ortszeit) => 20 + Raumgang(ortszeit) * 1.0;

    /// <summary>Luftfeuchte außerhalb des Zelts in % — 56 bis 64 %, sinkt, wenn der Raum wärmer wird.</summary>
    public static double AussenFeuchtePercent(DateTime ortszeit) => 60 - Raumgang(ortszeit) * 4;

    /// <summary>VPD der Raumluft in kPa — aus Temperatur und Feuchte, ohne Blatt-Versatz.</summary>
    /// <remarks>
    /// Mit derselben Formel wie die Kacheln (<see cref="VpdCalculator"/>) und
    /// aus den gerundeten Werten, die auch als Sensor erscheinen — sonst passten
    /// die drei Außenkarten im Verlauf nicht zueinander.
    /// </remarks>
    public static double AussenVpdKpa(DateTime ortszeit)
        => VpdCalculator.Calculate(
            Math.Round(AussenTempC(ortszeit), 1), Math.Round(AussenFeuchtePercent(ortszeit), 0)) ?? 0;

    /* ------------------------------------------------------------------ */
    /* Die Bruecke zu den Sensor-Schluesseln                                */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// Der Wert einer Messgröße zu einem Zeitpunkt — für die gefälschten
    /// Home-Assistant-Sensoren.
    /// </summary>
    /// <param name="metricKey">
    /// Ein Schlüssel aus <see cref="TentSensorMetricKeyMap"/>. Unbekannte
    /// geben <c>null</c>: sie sollen keinen erfundenen Wert bekommen.
    /// </param>
    /// <param name="ortszeit">
    /// <b>Ortszeit</b>, nicht UTC. Der Verlauf ist in Kalendertagen und
    /// Tageszeiten gedacht — „nachts kühler" heißt nachts <i>hier</i>.
    /// </param>
    /// <param name="lage">Wo das Zelt im Grow steht; ohne Angabe <see cref="Lage.Bluete"/>.</param>
    public static double? Wert(string metricKey, DateTime ortszeit, Lage? lage = null) => metricKey switch
    {
        "temperature" => Math.Round(LuftTempC(ortszeit), 1),
        "humidity" => Math.Round(FeuchtePercent(ortszeit, lage), 0),
        "co2" => Math.Round(Co2Ppm(ortszeit), 0),
        "ppfd" => Math.Round(Ppfd(ortszeit), 0),
        "reservoir-ph" => Math.Round(Ph(ortszeit), 2),
        "reservoir-ec" => Math.Round(Ec(ortszeit, lage), 2),
        "reservoir-temp" => Math.Round(WasserTempC(ortszeit), 1),
        "reservoir-level-cm" => Math.Round(FuellstandCm(ortszeit, lage), 1),
        "orp" => Math.Round(OrpMv(ortszeit), 0),
        "dissolved-oxygen" => Math.Round(SauerstoffMgL(ortszeit), 1),
        "outside-temperature" => Math.Round(AussenTempC(ortszeit), 1),
        "outside-humidity" => Math.Round(AussenFeuchtePercent(ortszeit), 0),
        "outside-vpd" => Math.Round(AussenVpdKpa(ortszeit), 2),
        "power" => Math.Round(DemoData.StromLeistungW(ortszeit.ToUniversalTime()), 0),
        _ => null,
    };

    /// <summary>Alle Schlüssel, für die es einen Verlauf gibt.</summary>
    /// <remarks>
    /// Bewusst als Liste und nicht über Reflexion: sie muss zu
    /// <see cref="Wert"/> passen, und ein Enum-Wert ohne Fall dort soll
    /// auffallen statt still null zu liefern.
    /// </remarks>
    public static readonly string[] Schluessel =
    [
        "temperature", "humidity", "co2", "ppfd",
        "reservoir-ph", "reservoir-ec", "reservoir-temp", "reservoir-level-cm",
        "orp", "dissolved-oxygen",
        "outside-temperature", "outside-humidity", "outside-vpd",
        "power",
    ];

    /// <summary>Die Einheit zu einem Schlüssel — leer, wo es keine gibt (pH).</summary>
    public static string? Einheit(string metricKey) => metricKey switch
    {
        "temperature" or "reservoir-temp" or "outside-temperature" => "°C",
        "humidity" or "outside-humidity" => "%",
        "outside-vpd" => "kPa",
        "co2" => "ppm",
        "ppfd" => "µmol/m²/s",
        "power" => "W",
        "reservoir-ec" => "mS/cm",
        "reservoir-level-cm" => "cm",
        "orp" => "mV",
        "dissolved-oxygen" => "mg/L",
        _ => null,
    };
}
