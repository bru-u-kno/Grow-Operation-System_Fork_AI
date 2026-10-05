using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Erfundene, aber plausible Messwerte für den Entwicklungsrechner.
/// </summary>
/// <remarks>
/// Auf dem Entwicklungsrechner gibt es kein Zelt, keine Sonden und kein Home
/// Assistant. Ohne Werte lässt sich dort nichts prüfen: keine Ampelfarben,
/// keine Kurven, keine Alarme, keine Dosier-Vorschläge. Dieser Modus liefert
/// sie — bewegt, damit man Trends sieht, und deterministisch aus der Uhrzeit,
/// damit zwei Abrufe kurz hintereinander zusammenpassen.
///
/// **Nur über die Umgebungsvariable <c>GROW_OS_DEMO</c>.** Bewusst kein
/// Schalter in der Oberfläche: erfundene Messwerte, die im Betrieb angezeigt
/// werden, wären nicht bloß falsch, sondern gefährlich — an ihnen hängen
/// Alarme und die Dosierung. Was man nicht anklicken kann, klickt man auch
/// nicht versehentlich an.
/// </remarks>
public static class DemoData
{
    /// <summary>Aus der Umgebung gelesen, einmal beim Start.</summary>
    public static bool IsEnabled { get; } =
        (Environment.GetEnvironmentVariable("GROW_OS_DEMO") ?? string.Empty).Trim() is "1" or "true" or "TRUE";

    /// <summary>Der Zeitraum, den <see cref="SeedHistory"/> rückwirkend füllt.</summary>
    /// <remarks>
    /// So weit wie der Verlauf reicht — 42 Tage. Vorher waren es <b>24
    /// Stunden</b>, während die Zelt-Historie 7, 14 und 30 Tage anbietet und
    /// auf 14 steht: im Diagramm war das ein Strich am rechten Rand, und
    /// genau das meinte „da ist kein verlauf zu sehen".
    /// </remarks>
    public const int HistoryHours = Demoverlauf.TageRueckwaerts * 24;

    /// <summary>Wie eine Demo-Entität heißt — überall sichtbar, nie zu verwechseln.</summary>
    public const string EntityPrefix = "demo";

    /// <summary>Die Entitäts-Kennung zu einem Metrik-Schlüssel.</summary>
    /// <remarks>
    /// <para><b>Es gab hier zwei Schreibweisen.</b> <see cref="StatesFor"/>
    /// bildete <c>demo.reservoir_ph</c>, <see cref="Entities"/> dagegen
    /// <c>sensor.demo_reservoir_ph</c> — dieselbe Messgröße, zwei Namen. Wer im
    /// Testbetrieb einen Sensor aus der Auswahlliste zuordnete, bekam deshalb
    /// nie einen Wert angezeigt: die Zuordnung zeigte auf eine Kennung, unter
    /// der kein Zustand lag.</para>
    ///
    /// <para>Es ist dieselbe Verwechslung, die den Kühler-Regler lahmgelegt
    /// hat — Metrik-Schlüssel gegen Entitäts-Kennung.</para>
    /// </remarks>
    public static string Kennung(string metricKey)
        => $"sensor.{EntityPrefix}_{metricKey.Replace('-', '_')}";

    /// <summary>Die Steckdose, an der im Testbestand der Kühler hängt.</summary>
    /// <remarks>
    /// Steht hier und nicht in <see cref="Demobestand"/>: der Bestand trägt sie
    /// ins Zelt ein, <see cref="StatesFor"/> liefert ihren Zustand. Zwei
    /// abgetippte Kennungen wären nach der ersten Umbenennung stumm
    /// auseinandergelaufen — der Kühler stünde dann dauerhaft auf „Zustand
    /// unbekannt", ohne dass irgendwo etwas rot wird.
    /// </remarks>
    public const string KuehlerSteckdose = "switch.demo_kuehler";

    /// <summary>Das Dimmfeld des Lichts im Testbestand — 0 bis 10.</summary>
    /// <remarks>
    /// Fuer den Versuchsaufbau „Zelt (AC-Test)". Ohne diese Entitaet zeigt die
    /// Seite im Testbetrieb nur „antwortet nicht" — richtig, aber nichts, woran
    /// man den Aufbau ansehen kann.
    ///
    /// Der Wert wird hier NICHT gehalten: der Testbetrieb schickt nichts an ein
    /// Geraet, also bleibt die Stufe, wie sie hier steht. Wer sie klickt, sieht
    /// die Meldung und den unveraenderten Stand — das ist ehrlicher als eine
    /// vorgetaeuschte Aenderung.
    /// </remarks>
    public const string LichtLeistung = "number.demo_licht_leistung";

    /// <summary>Der aktive Modus des Lichts im Testbestand — wie AC Infinity ihn als <c>select</c> meldet.</summary>
    /// <remarks>
    /// Fork AI (02.10.2026): ohne ihn trug das Testgerät keinen Modus, und das
    /// Etikett „Modus …" auf „Zelt (AC-Test)" war im Testbestand nie zu sehen —
    /// auch nicht, dass dort roh „On" stand. „Schedule", weil der Bestand für
    /// dieses Licht einen 12/12-Lichtplan führt und die App beim Anwenden eines
    /// Lichtplans genau diesen Modus setzt (<c>AcModi.Zeitplan</c>).
    /// „On" hieße Dauerlicht — im Blütezelt ein Widerspruch (Prüfer, 02.10.2026).
    /// </remarks>
    public const string LichtModus = "select.demo_licht_modus";

    /// <summary>Die geplante Ein-Zeit des Lichts im Testbestand.</summary>
    /// <remarks>
    /// Fuer den Zeitplan im Versuchsaufbau „Zelt (AC-Test)". Der Wert kommt aus
    /// <see cref="Demoverlauf.LichtAnUhr"/> — derselben Zahl, aus der auch die
    /// Lichtkurven und der Lichtplan des Zelts kommen. Drei abgetippte Uhrzeiten
    /// waeren nach der ersten Aenderung auseinandergelaufen.
    /// </remarks>
    public const string LichtEinZeit = "time.demo_licht_ein";

    /// <inheritdoc cref="LichtEinZeit"/>
    public const string LichtAusZeit = "time.demo_licht_aus";

    /// <summary>Brennt die Demo-Lampe gerade? — für die Licht-Rolle „Lampe · Zustand".</summary>
    /// <remarks>
    /// Fork AI (03.10.2026, offene Punkte D3): ohne sie und
    /// <see cref="LichtPortOnline"/> ließen sich im Testbestand nur vier der sechs
    /// Licht-Rollen zuordnen — die beiden Messrollen verlangen ein
    /// <c>binary_sensor</c>, und den gab es hier nicht. Der Zustand folgt dem
    /// Lichtplan (<see cref="LightOn"/>), wie bei einer echten Lampe.
    /// </remarks>
    public const string LichtZustand = "binary_sensor.demo_licht_zustand";

    /// <summary>Ist der Controller-Port erreichbar? — Licht-Rolle „Lampe · Port online".</summary>
    public const string LichtPortOnline = "binary_sensor.demo_licht_status";

    /// <summary>Der gemeinsame kWh-Zähler im Testbestand — die Steckdosenleiste vor beiden Blütezelten.</summary>
    /// <remarks>
    /// <para><b>Der Anlass (02.10.2026).</b> Der Testbestand hatte keine
    /// Strom-Quelle und keinen Zählerstand. Die Kostenseite stand im Testbetrieb
    /// auf „Keine Strom-Quelle eingerichtet", und weder der Strom eines Grows noch
    /// die Teilung zwischen zwei gleichzeitigen Grows war je zu sehen.</para>
    /// <para>Der Stand ist eine reine Funktion der Zeit (<see cref="StromZaehlerKwh"/>):
    /// die Stände, die der Bestand ablegt, und der Wert, den der Worker später
    /// liest, kommen aus derselben Rechnung und laufen nie rückwärts.</para>
    /// </remarks>
    public const string StromZaehler = "sensor.demo_strom_energie";

    /// <summary>Die Leistung an derselben Steckdosenleiste, in W.</summary>
    public const string StromLeistung = "sensor.demo_strom_leistung";

    /// <summary>LED im Blütezelt des Testbestands, in W.</summary>
    public const int LedBluetezeltW = 480;

    /// <summary>LED im zweiten Blütezelt des Testbestands, in W.</summary>
    public const int LedZelt2W = 240;

    /// <summary>Was ohne Licht läuft — Umwälzpumpe, Luftpumpen, Lüfter, Kühler im Mittel.</summary>
    private const double StromGrundlastW = 160;

    /// <summary>Ab hier zählt der Testzähler; davor steht er auf <see cref="StromZaehlerStart"/>.</summary>
    private static readonly DateTime StromAnker = new(2025, 1, 1);

    private const double StromZaehlerStart = 1250;

    /// <summary>Die Leistung jetzt: Grundlast, und bei Licht beide LED.</summary>
    public static double StromLeistungW(DateTime nowUtc)
        => StromGrundlastW + (Demoverlauf.LichtBrennt(nowUtc.ToLocalTime()) ? LedBluetezeltW + LedZelt2W : 0);

    /// <summary>
    /// Der Zählerstand zu einem Zeitpunkt — das Integral von <see cref="StromLeistungW"/>
    /// seit <see cref="StromAnker"/>. Rein und mit fester Basis, damit derselbe
    /// Zeitpunkt nach einem Neustart oder über Mitternacht denselben Wert hat.
    /// </summary>
    public static double StromZaehlerKwh(DateTime nowUtc)
    {
        var ort = nowUtc.ToLocalTime();
        var ankerUtc = DateTime.SpecifyKind(StromAnker, DateTimeKind.Local).ToUniversalTime();
        var stunden = Math.Max(0, (nowUtc - ankerUtc).TotalHours);
        var tage = Math.Max(0, (ort.Date - StromAnker).Days);
        var lichtJeTag = Enumerable.Range(0, 24).Count(h => Demoverlauf.LichtBrennt(StromAnker.AddHours(h)));
        var lichtHeute = tage == 0 && ort < StromAnker
            ? 0
            : Enumerable.Range(0, 24).Sum(h => Demoverlauf.LichtBrennt(ort.Date.AddHours(h))
                ? Math.Clamp((ort - ort.Date.AddHours(h)).TotalHours, 0, 1)
                : 0);
        var lichtStunden = tage * lichtJeTag + lichtHeute;
        return StromZaehlerStart + (stunden * StromGrundlastW + lichtStunden * (LedBluetezeltW + LedZelt2W)) / 1000.0;
    }

    /// <summary>
    /// Ein Wert je Messgröße: Mittelwert, Schwankung, Periode in Stunden und
    /// eine langsame Drift pro Stunde.
    /// </summary>
    /// <remarks>
    /// pH und EC driften nach oben, Füllstand nach unten — so, wie es in einem
    /// laufenden Reservoir wirklich passiert. Das ist kein Schmuck: dadurch
    /// gibt es auf dem Entwicklungsrechner etwas zu korrigieren, und die
    /// Dosierung lässt sich gegen eine echte Abweichung prüfen.
    /// </remarks>
    private static readonly Dictionary<string, (double Base, double Amp, double Hours, double DriftPerHour, string? Unit, string Label)> Shape = new()
    {
        ["temperature"] = (24.4, 1.3, 24, 0, "°C", "Demo Lufttemperatur"),
        ["humidity"] = (58, 6, 24, 0, "%", "Demo Luftfeuchte"),
        ["co2"] = (780, 120, 12, 0, "ppm", "Demo CO₂"),
        ["ppfd"] = (720, 90, 24, 0, "µmol/m²/s", "Demo PPFD"),
        ["reservoir-ph"] = (5.85, 0.06, 6, 0.012, null, "Demo pH"),
        ["reservoir-ec"] = (1.52, 0.04, 8, 0.006, "mS/cm", "Demo EC"),
        ["reservoir-temp"] = (19.6, 0.7, 24, 0, "°C", "Demo Wassertemperatur"),
        // Kein erfundener Liter-Sensor: ein Becken misst entweder Liter ODER
        // Zentimeter. Solange hier beides stand, gewann der Liter-Wert — und
        // der ganze Weg „eTape kalibrieren, dann Liter sehen" war im
        // Vorfuehrmodus unsichtbar.
        // Ein cm-Pegel wie ein eTape — damit sich der Kalibrier-Assistent ohne
        // Hardware durchspielen laesst. Faellt langsam, wie ein trinkendes Becken.
        ["reservoir-level-cm"] = (31, 0.4, 24, -0.09, "cm", "Demo eTape"),
        ["orp"] = (352, 28, 10, 0, "mV", "Demo ORP"),
        ["dissolved-oxygen"] = (7.6, 0.5, 9, 0, "mg/L", "Demo Sauerstoff"),
        // Die Raumluft am Controller, außerhalb des Zelts — wie der eingebaute
        // Fühler eines AC Infinity 69 Pro. Mit eigenem VPD, weil der Controller
        // es selbst meldet (innen rechnet Grow OS es aus).
        ["outside-temperature"] = (20, 1, 24, 0, "°C", "Demo Controller Außen Temperatur"),
        ["outside-humidity"] = (60, 4, 24, 0, "%", "Demo Controller Außen Luftfeuchte"),
        ["outside-vpd"] = (0.94, 0.15, 24, 0, "kPa", "Demo Controller Außen VPD"),
    };

    public static IReadOnlyCollection<string> MetricKeys => Shape.Keys;

    /// <summary>
    /// Der Wert einer Messgröße zu einem Zeitpunkt. Rein — dieselbe Zeit
    /// ergibt denselben Wert, auch nach einem Neustart.
    /// </summary>
    /// <remarks>
    /// <para>Rechnet nicht selbst, sondern fragt <see cref="Demoverlauf"/>.
    /// Vorher lag hier eine eigene Sinuskurve um einen festen Mittelwert —
    /// und die Messungen im <see cref="Demobestand"/> hatten ihre eigene.
    /// Zwei Kurven für dieselbe Sache heißt: das Diagramm zeigt einen
    /// EC-Sägezahn und das Protokoll daneben behauptet etwas anderes.</para>
    /// <para><b>UTC hinein, Ortszeit hinaus.</b> Die Sensor-Tabelle rechnet in
    /// UTC (Spalte <c>CapturedAtUtc</c>), der Verlauf denkt in Kalendertagen
    /// und Tageszeiten — „nachts kühler" heißt nachts <i>hier</i>. Deshalb
    /// die Umrechnung an genau dieser Stelle.</para>
    /// </remarks>
    public static double? ValueFor(string metricKey, DateTime whenUtc, Demoverlauf.Lage? lage = null)
        => Demoverlauf.Wert(metricKey, whenUtc.ToLocalTime(), lage);

    /// <summary>Wo dieses Zelt im Grow steht — für EC und Luftfeuchte.</summary>
    /// <remarks>
    /// <para>Das Blütezelt 2 trägt den Grow in Blütewoche 9, Zelt 1 den in der
    /// Blüte davor; ihr Plan verlangt verschiedene EC und Luftfeuchte
    /// (<see cref="Demoverlauf.Lage"/>, offene Punkte 03.10.2026, D1).</para>
    /// <para><b>Über den Namen, nicht die Id:</b> die Id vergibt die Datenbank,
    /// den Namen der Bestand (<see cref="Demobestand.ZweitesBluetezeltName"/>).
    /// Nur der Weg je Zelt (<see cref="HomeAssistantService.GetStatesAsync"/>,
    /// die gesäte Historie) unterscheidet; wer eine Entität einzeln abfragt,
    /// bekommt die Werte von Zelt 1 — beide Zelte teilen sich die Kennungen.</para>
    /// </remarks>
    public static Demoverlauf.Lage LageFuer(Tent zelt)
        => string.Equals(zelt.Name, Demobestand.ZweitesBluetezeltName, StringComparison.Ordinal)
            ? Demoverlauf.Lage.Spaetbluete
            : Demoverlauf.Lage.Bluete;
    /// <summary>
    /// Die Tageswerte der zurückliegenden Wochen.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum die zusätzlich nötig sind.</b> Die App bewahrt
    /// <b>sieben Tage</b> Rohablesungen auf; alles Ältere räumt
    /// <c>HomeAssistantSnapshotWorker.CleanupOldReadingsAsync</c> weg und
    /// rollt es vorher zu Tageswerten zusammen. Die Zelt-Historie bietet
    /// aber 7, 14 und 30 Tage an.</para>
    ///
    /// <para>Wer also nur Rohablesungen säte, bekam im 14-Tage-Diagramm
    /// genau einen Punkt — gemessen, nachdem 11 520 gesäte Zeilen auf 312
    /// zusammengeschmolzen waren. Das war der zweite Grund für „da ist kein
    /// verlauf zu sehen", und er lässt sich nicht durch mehr Rohdaten
    /// beheben: die werden ja gerade gelöscht.</para>
    ///
    /// <para>Min, Max und die Bänder kommen aus demselben Verlauf, im
    /// Stundenraster über den Tag gerechnet — sonst behauptete das
    /// Tagesband etwas anderes als die Kurve daneben.</para>
    /// </remarks>
    public static IEnumerable<TentSensorDailyStat> SeedDailyStats(int tentId, DateTime heute, Demoverlauf.Lage? lage = null)
    {
        // Der jüngste Tag bleibt den Rohablesungen überlassen: für ihn ist
        // die Aggregation noch nicht gelaufen, und zwei Quellen für denselben
        // Tag ergäben einen Sprung im Diagramm.
        for (var tag = Demoverlauf.TageRueckwaerts; tag >= 1; tag--)
        {
            var datum = heute.AddDays(-tag);

            foreach (var key in Demoverlauf.Schluessel)
            {
                var werte = new List<double>();
                for (var stunde = 0; stunde < 24; stunde++)
                {
                    var wert = Demoverlauf.Wert(key, datum.AddHours(stunde), lage);
                    if (wert is { } vorhanden) werte.Add(vorhanden);
                }

                if (werte.Count == 0) continue;
                werte.Sort();

                yield return new TentSensorDailyStat
                {
                    TentId = tentId,
                    MetricKey = key,
                    Date = DateOnly.FromDateTime(datum),
                    Min = werte[0],
                    Max = werte[^1],
                    Median = werte[werte.Count / 2],
                    P5 = werte[(int)(werte.Count * 0.05)],
                    P95 = werte[(int)(werte.Count * 0.95)],
                    Avg = Math.Round(werte.Average(), 2),
                    Count = werte.Count,
                    Unit = Demoverlauf.Einheit(key),
                };
            }
        }
    }

    /// <summary>Brennt das Licht? Eine Wahrheit, dieselbe wie fuer die Kurven.</summary>
    /// <remarks>
    /// <para><b>Hier standen zwei Wahrheiten.</b> Diese Methode rechnete mit der
    /// <i>UTC</i>-Stunde, <see cref="Demoverlauf.LichtBrennt"/> mit der
    /// <i>Ortszeit</i> — in Deutschland zwei Stunden Unterschied. Zweimal am Tag
    /// meldete der Testbestand damit „Licht an" bei PPFD 0 und umgekehrt.
    /// Aufgefallen ist es am 24.08.2026 um kurz nach eins nachts, weil die
    /// Live-Seite genau das anzeigte.</para>
    ///
    /// <para>Deshalb steht die Stunde jetzt nur noch an einer Stelle. Wer sie
    /// dort ändert, ändert Kurven und Schaltzustand gemeinsam.</para>
    /// </remarks>
    public static bool LightOn(DateTime whenUtc) => Demoverlauf.LichtBrennt(whenUtc.ToLocalTime());

    /// <summary>Alle Messwerte eines Zelts, so wie Home Assistant sie liefern würde.</summary>
    /// <remarks>
    /// Bewusst ALLE bekannten Messgrößen, nicht nur zugeordnete Sensoren: auf
    /// einem frischen Entwicklungsrechner ist nichts zugeordnet, und dann wäre
    /// der Bildschirm wieder leer — genau das, was dieser Modus beheben soll.
    /// </remarks>
    public static Dictionary<string, HomeAssistantState> StatesFor(DateTime nowUtc, Demoverlauf.Lage? lage = null)
    {
        var states = new Dictionary<string, HomeAssistantState>();
        foreach (var (key, shape) in Shape)
        {
            var wert = ValueFor(key, nowUtc, lage);
            if (wert is null) continue;
            states[key] = new HomeAssistantState
            {
                EntityId = Kennung(key),
                State = wert.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FriendlyName = shape.Label,
                UnitOfMeasurement = shape.Unit,
                NumericValue = wert,
                LastChanged = nowUtc,
            LastUpdated = nowUtc,
            };
        }

        // Die Kuehler-Steckdose unter ihrer ENTITAETS-Kennung: der Regler sucht
        // sie genau so, weil dort steht, ob Strom anliegt — der Chiller-Sensor
        // sagt nur, ob das Geraet laeuft.
        // Erst das Schaltbrett fragen: hat der Regler in dieser Sitzung schon
        // geschaltet, ist DAS der Zustand der Steckdose. Sonst die Kurve.
        var gestellteSteckdose = Demoschaltbrett.Lesen(KuehlerSteckdose);
        var kuehlerAn = gestellteSteckdose is not null
            ? gestellteSteckdose.State == "on"
            : Demoverlauf.KuehlerLaeuft(nowUtc.ToLocalTime());
        states[KuehlerSteckdose] = new HomeAssistantState
        {
            EntityId = KuehlerSteckdose,
            State = kuehlerAn ? "on" : "off",
            FriendlyName = "Demo Kühler-Steckdose",
            LastChanged = nowUtc,
            LastUpdated = nowUtc,
        };

        var lichtKey = TentSensorMetricKeyMap.Resolve(SensorMetricType.LightStatus);
        states[lichtKey] = new HomeAssistantState
        {
            EntityId = Kennung(lichtKey),
            State = LightOn(nowUtc) ? "on" : "off",
            FriendlyName = "Demo Licht",
            LastChanged = nowUtc,
            LastUpdated = nowUtc,
        };

        return states;
    }

    /// <summary>Der Zustand einer einzelnen Entität im Testbestand.</summary>
    /// <remarks>
    /// Bewusst NUR die Kühler-Steckdose: sie ist die einzige Entität, die der
    /// Testbestand unter ihrer eigenen Kennung schaltet. Alles andere geht
    /// über <see cref="StatesFor"/> und dessen Metrik-Kennungen. Wer hier
    /// grosszügig würde, baute dem Betrieb wieder eine Kulisse.
    /// </remarks>
    public static HomeAssistantState? EntityState(string entityId, DateTime nowUtc)
    {
        // Was jemand gestellt hat, gewinnt ueber die Kurve — sonst zeigt die
        // Oberflaeche den alten Wert und jede Nachkontrolle scheitert.
        var gestellt = Demoschaltbrett.Lesen(entityId);
        if (gestellt is not null) return gestellt;

        foreach (var (kennung, uhr, name) in new[]
                 {
                     (LichtEinZeit, Demoverlauf.LichtAnUhr, "Demo LED · Geplante Ein-Zeit"),
                     (LichtAusZeit, Demoverlauf.LichtAusUhr, "Demo LED · Geplante Aus-Zeit"),
                 })
        {
            if (!string.Equals(entityId, kennung, StringComparison.OrdinalIgnoreCase)) continue;

            return new HomeAssistantState
            {
                // Home Assistant meldet Uhrzeiten mit Sekunden.
                EntityId = kennung,
                State = uhr + ":00",
                FriendlyName = name,
                LastChanged = nowUtc,
                LastUpdated = nowUtc,
            };
        }

        foreach (var (kennung, wert, einheit, name) in new[]
                 {
                     (StromZaehler, Math.Round(StromZaehlerKwh(nowUtc), 3), "kWh", "Demo Steckdosenleiste · Energie"),
                     (StromLeistung, Math.Round(StromLeistungW(nowUtc), 0), "W", "Demo Steckdosenleiste · Leistung"),
                 })
        {
            if (!string.Equals(entityId, kennung, StringComparison.OrdinalIgnoreCase)) continue;

            return new HomeAssistantState
            {
                EntityId = kennung,
                State = wert.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NumericValue = wert,
                UnitOfMeasurement = einheit,
                FriendlyName = name,
                LastChanged = nowUtc,
                LastUpdated = nowUtc,
            };
        }

        foreach (var (kennung, brennt, name) in new[]
                 {
                     (LichtZustand, LightOn(nowUtc), "Demo LED · Zustand"),
                     (LichtPortOnline, true, "Demo LED · Port online"),
                 })
        {
            if (!string.Equals(entityId, kennung, StringComparison.OrdinalIgnoreCase)) continue;

            return new HomeAssistantState
            {
                EntityId = kennung,
                State = brennt ? "on" : "off",
                FriendlyName = name,
                LastChanged = nowUtc,
                LastUpdated = nowUtc,
            };
        }

        if (string.Equals(entityId, LichtModus, StringComparison.OrdinalIgnoreCase))
        {
            return new HomeAssistantState
            {
                EntityId = LichtModus,
                State = AcModi.Zeitplan,
                FriendlyName = "Demo LED · Aktiver Modus",
                LastChanged = nowUtc,
                LastUpdated = nowUtc,
            };
        }

        if (string.Equals(entityId, LichtLeistung, StringComparison.OrdinalIgnoreCase))
        {
            return new HomeAssistantState
            {
                EntityId = LichtLeistung,
                State = "7",
                NumericValue = 7,
                FriendlyName = "Demo LED · Einschaltleistung",
                LastChanged = nowUtc,
                LastUpdated = nowUtc,
            };
        }

        // Die Messgroessen unter IHRER Entitaets-Kennung.
        //
        // Ohne das stand auf „Sensoren & Wartung" bei einem zugeordneten Sensor
        // in der Spalte WERT ein Strich: das Woerterbuch aus StatesFor ist nach
        // METRIK-Schluesseln benannt, wer eine Entitaet sucht, findet dort
        // nichts. Genau dieselbe Verwechslung hat den Kuehler-Regler lahmgelegt
        // — nur andersherum.
        foreach (var (_, zustand) in StatesFor(nowUtc))
        {
            if (string.Equals(zustand.EntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                return zustand;
            }
        }

        if (!string.Equals(entityId, KuehlerSteckdose, StringComparison.OrdinalIgnoreCase)) return null;

        var an = Demoverlauf.KuehlerLaeuft(nowUtc.ToLocalTime());
        return new HomeAssistantState
        {
            EntityId = KuehlerSteckdose,
            State = an ? "on" : "off",
            FriendlyName = "Demo Kühler-Steckdose",
            LastChanged = nowUtc,
            LastUpdated = nowUtc,
        };
    }

    /// <summary>
    /// Die Entitätenliste für die Auswahlfelder — Messwerte plus vier
    /// schaltbare Steckdosen, damit sich Dosierpumpen zuordnen lassen.
    /// </summary>
    public static IReadOnlyList<HomeAssistantEntity> Entities(DateTime nowUtc)
    {
        // Aus DERSELBEN Quelle wie die Zustaende. Vorher lief die Schleife
        // ueber `Shape` und liess damit alles aus, was StatesFor zusaetzlich
        // liefert — den Lichtstatus zum Beispiel. Was die App als Wert kennt,
        // muss auch auswaehlbar sein; sonst gibt es Werte ohne Zuordnung und
        // Zuordnungen ohne Wert.
        var liste = new List<HomeAssistantEntity>();
        foreach (var (_, zustand) in StatesFor(nowUtc))
        {
            liste.Add(new HomeAssistantEntity
            {
                EntityId = zustand.EntityId,
                FriendlyName = zustand.FriendlyName,
                State = zustand.State,
                UnitOfMeasurement = zustand.UnitOfMeasurement,
                Domain = zustand.EntityId.Split('.', 2)[0],
            });
        }

        // Die benannten Geraete: ohne sie steht im Testbetrieb keine Steckdose
        // und kein Dimmfeld in der Auswahl — und dann laesst sich weder der
        // Kuehler noch der AC-Versuch ueberhaupt einrichten.
        foreach (var kennung in new[] { LichtLeistung, LichtModus, LichtEinZeit, LichtAusZeit, LichtZustand, LichtPortOnline, StromZaehler, StromLeistung })
        {
            var zustand = EntityState(kennung, nowUtc);
            if (zustand is null) continue;

            liste.Add(new HomeAssistantEntity
            {
                EntityId = kennung,
                FriendlyName = zustand.FriendlyName,
                State = zustand.State,
                UnitOfMeasurement = zustand.UnitOfMeasurement,
                Domain = kennung.Split('.', 2)[0],
            });
        }

        foreach (var (name, label) in new[]
                 {
                     ("ph_minus", "Demo Dosierpumpe pH Minus"),
                     ("ph_plus", "Demo Dosierpumpe pH Plus"),
                     ("nutrient_a", "Demo Dosierpumpe Nährstoff A"),
                     ("nutrient_b", "Demo Dosierpumpe Nährstoff B"),
                 })
        {
            liste.Add(new HomeAssistantEntity
            {
                EntityId = $"switch.{EntityPrefix}_{name}",
                FriendlyName = label,
                State = "off",
                Domain = "switch",
            });
        }

        liste.Add(new HomeAssistantEntity
        {
            EntityId = $"camera.{EntityPrefix}_zelt",
            FriendlyName = "Demo Kamera",
            State = "idle",
            Domain = "camera",
        });

        return liste;
    }

    /// <summary>
    /// Die letzten 24 Stunden je Messgröße, im Viertelstundentakt.
    /// </summary>
    /// <remarks>
    /// Aus demselben Generator wie die Live-Werte — die Kurve endet also genau
    /// dort, wo die Kachel steht. Ohne das wäre der Verlauf erst nach einem Tag
    /// Laufzeit zu sehen, und Kurven, Verlaufsseite und Trend-Wächter liessen
    /// sich auf dem Entwicklungsrechner gar nicht prüfen.
    /// </remarks>
    public static IEnumerable<TentSensorReading> SeedHistory(int tentId, DateTime nowUtc, Demoverlauf.Lage? lage = null)
    {
        // Zwei Auflösungen. Die letzten sieben Tage im Viertelstundentakt —
        // so lange behält die echte Anlage ihre Rohwerte, und das
        // Grow-Tagebuch (A-006) erkennt Sprünge nur, wo Werte höchstens 15
        // Minuten auseinanderliegen. Bis A-006 waren es zwei Tage: ob der
        // Bestand einen Wechsel zeigte, hing dann vom Wochentag ab. Davor
        // stündlich: 42 Tage im Viertelstundentakt wären rund 48 000 Zeilen
        // je Zelt, und im 30-Tage-Diagramm sieht man den Unterschied nicht.
        const int FeinBisStunden = 7 * 24;

        for (var minuten = HistoryHours * 60; minuten > 0; minuten -= 15)
        {
            var grob = minuten > FeinBisStunden * 60;
            if (grob && minuten % 60 != 0) continue;

            var zeitpunkt = nowUtc.AddMinutes(-minuten);
            foreach (var key in Demoverlauf.Schluessel)
            {
                var wert = ValueFor(key, zeitpunkt, lage);
                if (wert is null) continue;
                yield return new TentSensorReading
                {
                    TentId = tentId,
                    MetricKey = key,
                    Value = wert.Value,
                    Unit = Demoverlauf.Einheit(key),
                    CapturedAtUtc = zeitpunkt,
                };
            }
        }
    }

    /// <summary>
    /// Ein gezeichnetes Kamerabild — mit Uhrzeit, damit man sieht, dass es sich
    /// erneuert, und mit „DEMO" quer darüber, damit es nie für echt gehalten wird.
    /// </summary>
    public static byte[] CameraImage(string entityId, DateTime whenLocal)
    {
        var uhr = whenLocal.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 360" width="640" height="360">
              <rect width="640" height="360" fill="#0b1310"/>
              <g fill="none" stroke="#1f7d49" stroke-width="2" opacity="0.55">
                <circle cx="320" cy="150" r="54"/>
                <path d="M320 96 v108 M266 150 h108 M282 112 l76 76 M358 112 l-76 76"/>
              </g>
              <text x="320" y="252" text-anchor="middle" fill="#52e98c"
                    font-family="monospace" font-size="30" letter-spacing="8">DEMO KAMERA</text>
              <text x="320" y="286" text-anchor="middle" fill="#7d8f86"
                    font-family="monospace" font-size="16">{System.Security.SecurityElement.Escape(entityId)}</text>
              <text x="320" y="316" text-anchor="middle" fill="#7d8f86"
                    font-family="monospace" font-size="20">{uhr}</text>
            </svg>
            """;
        return System.Text.Encoding.UTF8.GetBytes(svg);
    }

    /// <summary>
    /// Die Verbindungseinstellungen, die im Testbetrieb gelten sollen.
    /// </summary>
    /// <remarks>
    /// Damit gilt Home Assistant überall als verbunden — sonst hielten
    /// Watchdog, Live und Dosierung den Rechner für unkonfiguriert und
    /// blockierten, bevor die erfundenen Werte überhaupt gefragt wären.
    /// Die Adresse ist bewusst keine echte: hier geht nie ein Aufruf raus.
    /// </remarks>
    public static HomeAssistantSettings Settings() => new()
    {
        Enabled = true,
        BaseUrl = "http://demo.invalid",
        AccessToken = "demo",
    };

    /// <summary>
    /// Ein paar zurückliegende Dosen mit Wirkung — damit die Pumpe etwas gelernt hat.
    /// </summary>
    /// <remarks>
    /// Ohne das lässt sich Stufe 2 auf dem Entwicklungsrechner gar nicht ansehen.
    /// Gelernt wird aus Dosen mit Wert davor und danach, und simulierte Dosen
    /// lehren bewusst nichts: im Testbetrieb ist nichts geflossen, jede Änderung
    /// danach hat eine andere Ursache. Diese hier sind deshalb als echt
    /// eingetragen — im Testdatenmodus ist ohnehin die ganze Datenbank erfunden,
    /// von den Messwerten an, und der Streifen „Testdaten" steht über jeder Seite.
    ///
    /// Die Wirkung ist bewusst nicht exakt gleich: −0,10 bis −0,12 pH je ml. Eine
    /// perfekt konstante Wirkung gibt es an keinem echten Becken, und ein
    /// Vorschlag, der aus makellosen Zahlen entsteht, prüft nichts.
    /// </remarks>
    public static IEnumerable<DoseEvent> SeedDoses(int pumpId, int tentId, DateTime nowUtc)
    {
        var muster = new[]
        {
            (Stunden: 52.0, Ml: 3.5, Vorher: 6.42, Wirkung: -0.11),
            (Stunden: 34.0, Ml: 2.0, Vorher: 6.28, Wirkung: -0.12),
            (Stunden: 22.0, Ml: 3.0, Vorher: 6.35, Wirkung: -0.10),
            (Stunden: 9.0,  Ml: 2.5, Vorher: 6.31, Wirkung: -0.11),
        };

        foreach (var (stunden, ml, vorher, wirkung) in muster)
        {
            yield return new DoseEvent
            {
                PumpId = pumpId,
                TentId = tentId,
                OccurredAtUtc = nowUtc.AddHours(-stunden),
                Trigger = DoseTrigger.Manual,
                Outcome = DoseOutcome.Done,
                RequestedMl = ml,
                DosedMl = ml,
                SecondsRun = Math.Round(ml / 45.0 * 60, 2),
                ValueBefore = vorher,
                ValueAfter = Math.Round(vorher + ml * wirkung, 3),
                Reason = "Testdaten: zurückliegende Dosis mit gemessener Wirkung.",
                Simulated = false,
            };
        }
    }

    /// <summary>Kennt der Testbestand diese Entität überhaupt?</summary>
    /// <remarks>
    /// <para><b>Sonst verdeckt der Testbetrieb genau den Fehler, um den es
    /// geht.</b> Das Schaltbrett nahm anfangs jede Kennung an. Ein Prüfer hat
    /// die Kühler-Steckdose im Bestand auf <c>switch.demo_wasserkuehler</c>
    /// gesetzt — eine Entität, die es nicht gibt —, und die Live-Karte meldete
    /// trotzdem „läuft": der Regler hatte eingeschaltet, das Brett hatte es
    /// vermerkt, und niemand hat gefragt, ob das Gerät existiert.</para>
    ///
    /// <para>In einer echten Anlage antwortet Home Assistant auf einen Dienst
    /// für eine unbekannte Entität nicht mit Erfolg. Der Testbetrieb tut es
    /// jetzt auch nicht mehr.</para>
    /// </remarks>
    public static bool KennstEntitaet(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return false;

        var jetzt = DateTime.UtcNow;
        foreach (var eintrag in Entities(jetzt))
        {
            if (string.Equals(eintrag.EntityId, entityId, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>Welche Entität liefert diese Messgröße im Testbestand?</summary>
    /// <remarks>
    /// Damit niemand eine Kennung abtippt. Die Kennungen entstehen in
    /// <see cref="StatesFor"/> aus dem Metrik-Schlüssel; wer sie an einer
    /// zweiten Stelle bildet, hat sie beim ersten Umbenennen verloren.
    /// </remarks>
    public static string? EntitaetFuer(SensorMetricType art)
        => StatesFor(DateTime.UtcNow)
            .TryGetValue(TentSensorMetricKeyMap.Resolve(art), out var zustand)
            ? zustand.EntityId
            : null;

    /// <summary>
    /// Eine kalibrierte pH-Sonde — sonst bleibt die Automatik im Testbetrieb gesperrt.
    /// </summary>
    /// <remarks>
    /// Die Automatik verlangt eine Sonde, die kalibriert und nicht überfällig
    /// ist. Das ist keine Formalie: eine driftende Sonde meldet 6,0, während 5,4
    /// im Becken steht, und dosiert wird dann überzeugt in die falsche Richtung.
    /// Auf dem Entwicklungsrechner gibt es keine Sonde, also auch keine
    /// Kalibrierung — und ohne die liesse sich Stufe 3 nirgends durchspielen.
    /// </remarks>
    public static (HardwareItem Probe, CalibrationEvent Calibration) SeedProbe(int tentId, DateTime nowUtc)
    {
        var probe = new HardwareItem
        {
            Name = "Demo pH-Sonde",
            Category = "Sonde",
            DeviceKind = HardwareDeviceKind.FixedSensor,
            MetricType = SensorMetricType.ReservoirPh,

            // Ohne Zuordnung meldet die App dauerhaft „kein Mapping" — fuer ein
            // Geraet, das sie selbst angelegt hat.
            HaEntityId = EntitaetFuer(SensorMetricType.ReservoirPh),
            TentId = tentId,
            Status = HardwareItemStatus.Active,
            CalibrationIntervalDays = 14,
            Notes = "Testdaten — diese Sonde gibt es nicht.",
        };

        var calibration = new CalibrationEvent
        {
            CalibrationType = CalibrationEventType.Ph,
            Status = CalibrationEventStatus.Completed,
            Result = CalibrationResult.Passed,
            Title = "Testdaten: pH-Kalibrierung",
            PerformedAtUtc = nowUtc.AddDays(-3),
            NextDueAtUtc = nowUtc.AddDays(11),
        };

        return (probe, calibration);
    }
}
