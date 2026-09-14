using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;

namespace GrowDiary.Web.Services;

/// <summary>
/// Ein vollständiger Datenbestand zum Durchspielen — Zelt bis Aushärte-Glas.
///
/// <para><b>Der Anlass.</b> <see cref="DemoData"/> fälscht nur die
/// Home-Assistant-Seite: Sensoren, Verlauf, Dosierungen, Kamera. Grows, Messungen,
/// Ernten und alles andere kommen aus der Datenbank, und die legt niemand an. Auf
/// einem frischen Rechner steht deshalb überall „Noch keine …", und die
/// automatische Prüfung der Oberfläche prüft nichts: von 34 Fällen aus vier
/// E2E-Dateien übersprangen sich <b>31</b>, weil die Daten fehlten, gegen die sie
/// hätten prüfen sollen. Gefunden hat das der Tester — „es fehlen wieder mock
/// daten" —, nicht die Sammlung.</para>
///
/// <para><b>Was er anlegt.</b> Ein Zelt mit RDWC-Aufbau, einen laufenden Grow in
/// der Blüte mit sechs Wochen Messungen (Hand und Automatik gemischt), Fotos,
/// Journal, fällige Aufgaben, ein kalibriertes Messgerät mit Historie, eine
/// Alarmregel, ein offenes Risiko-Ereignis, ein Aushärte-Glas mit Ablesungen —
/// und zwei abgeschlossene Läufe mit Ernte, damit Archiv und Kostenrechnung
/// etwas zu zeigen haben.</para>
///
/// <para><b>Warum er fremde Daten nicht anfassen kann.</b> Er läuft nur, wenn in
/// der Datenbank <b>überhaupt kein Grow</b> steht. Wer schon einen hat, hat
/// eigene Daten — die sind besser als jede Demo, und sie zu ergänzen wäre ein
/// Eingriff, um den niemand gebeten hat.</para>
///
/// <para><b>Alles trägt „Testdaten" im Namen.</b> Wer den Bestand später neben
/// eigenen Läufen sieht, muss die Frage „ist das echt?" nicht stellen.</para>
/// </summary>
public static class Demobestand
{
    /// <summary>
    /// Ortszeit auf die Mittagsstunde legen.
    /// </summary>
    /// <remarks>
    /// Die Datumsfelder eines Grows gehen zwei verschiedene Wege in die
    /// Datenbank: <c>GerminatedAt</c>, <c>VegStartedAt</c> und
    /// <c>FinishStartedAt</c> durch <c>ToStorageUtc</c> (also mit
    /// <c>ToUniversalTime</c>), <c>StartDate</c>, <c>FlipDate</c> und
    /// <c>EndDate</c> dagegen unverschoben. Ein lokales Mitternachtsdatum
    /// rutscht in den UTC-Spalten dadurch einen Tag zurück. Mittags gesetzt
    /// passiert das in keiner europäischen Zeitzone.
    /// </remarks>
    private static DateTime Tag(int vorTagen)
        => DateTime.Today.AddDays(-vorTagen).AddHours(12);

    /// <summary>Läuft der Bestand? Nur in eine Datenbank ganz ohne Grows.</summary>
    public static bool IstNoetig(GrowRepository grows) => grows.GetAllGrows().Count == 0;

    /// <summary>
    /// Legt den ganzen Bestand an. Ruft nur, wer <see cref="IstNoetig"/> gefragt hat.
    /// </summary>
    /// <returns>Was angelegt wurde, für die Zeile im Protokoll.</returns>
    public static string Anlegen(IServiceProvider dienste)
    {
        var grows = dienste.GetRequiredService<GrowRepository>();
        var hydro = dienste.GetRequiredService<HydroSetupRepository>();
        var messungen = dienste.GetRequiredService<MeasurementRepository>();
        var ernten = dienste.GetRequiredService<HarvestRepository>();
        var journal = dienste.GetRequiredService<JournalRepository>();
        var aufgaben = dienste.GetRequiredService<TaskRepository>();
        var hardware = dienste.GetRequiredService<HardwareRepository>();
        var alarme = dienste.GetRequiredService<AlertRuleRepository>();
        var aushaerten = dienste.GetRequiredService<CuringRepository>();
        var setups = dienste.GetRequiredService<SetupRepository>();
        var dosierung = dienste.GetRequiredService<DosingRepository>();
        var addback = dienste.GetRequiredService<AddbackRepository>();

        var zelt = ZeltAnlegen(grows);
        var aufbau = AufbauAnlegen(hydro, zelt.Id);
        ZweiterAufbauAnlegen(hydro, zelt.Id);
        var (hauptSorte, zweiteSorte) = SorteAnlegen(setups);

        var laufend = LaufenderGrowAnlegen(grows, zelt.Id, aufbau.Id, hauptSorte.Id);
        PflanzenAnlegen(setups, laufend.Id, hauptSorte.Id, zweiteSorte.Id);
        var anzahl = MessungenAnlegen(messungen, laufend);
        WasserwechselAnlegen(addback, laufend);
        JournalAnlegen(journal, laufend.Id);
        AufgabenAnlegen(aufgaben, laufend.Id);
        var geraet = GeraetMitHistorieAnlegen(hardware, zelt.Id, laufend.Id);
        var pumpe = WeitereGeraeteAnlegen(hardware, zelt.Id, laufend.Id);
        AlarmregelAnlegen(alarme, zelt.Id);
        LichtplanAnlegen(grows, zelt.Id);
        SensorenZuordnen(grows, zelt.Id);
        RisikoAnlegen(hardware, zelt.Id, laufend.Id, pumpe.Id);
        GlasAnlegen(aushaerten, laufend.Id);
        PumpenAnlegen(dosierung, zelt.Id);
        LaufendenAblaufAnlegen(dienste, laufend.Id);

        /* Der erste Lauf bleibt ABSICHTLICH ohne Verknuepfung in die
           Bibliothek: "Northern Lights" steht dort nicht, und genau dieser
           Fall — ein Lauf, dessen Sorte nur als Text existiert — muss in der
           App noch anzeigbar sein. Der zweite ist verknuepft, damit die
           Sortenstatistik ueberhaupt eine Zahl hat. */
        AbgeschlossenenGrowAnlegen(grows, ernten, zelt.Id, aufbau.Id,
            "Northern Lights (Testdaten)", "Northern Lights", "Sensi Seeds",
            vorTagen: 190, dauerTage: 82, nass: 412, trocken: 96);
        AbgeschlossenenGrowAnlegen(grows, ernten, zelt.Id, aufbau.Id,
            "Gorilla Glue (Testdaten)", "Gorilla Glue #4", "GG Strains",
            vorTagen: 95, dauerTage: 88, nass: 468, trocken: 108,
            sorteId: zweiteSorte.Id);

        // Der Versuchsaufbau „Zelt (AC-Test)" bekommt ein Geraet, damit die
        // Seite im Testbestand etwas zeigt statt nur „noch nichts eingetragen".
        // Die Entitaet liefert DemoData.EntityState — ueber dieselbe
        // Schnittstelle, die auch im Betrieb benutzt wird.
        AcTest.Speichern(dienste.GetRequiredService<AppSettingsRepository>(), zelt.Id,
        [
            new AcGeraet("LED Top (Testdaten)", DemoData.LichtLeistung, null,
                DemoData.LichtEinZeit, DemoData.LichtAusZeit),
        ]);
        return $"1 Zelt, 1 RDWC-Aufbau, 3 Grows (1 laufend, 2 im Archiv), {anzahl} Messungen";
    }

    private static Tent ZeltAnlegen(GrowRepository grows)
    {
        // Die Objekt-Variante, nicht CreateTent(string): die legt ein nacktes
        // Zelt mit TentType.MultiPurpose an. Der Rueckgabewert traegt die Id —
        // das uebergebene Objekt hat danach weiterhin Id 0.
        return grows.CreateTent(new Tent
        {
            Name = "Blütezelt (Testdaten)",
            // Production, weil das Setup weiter unten SetupType.Production ist:
            // SetupTentCompatibilityPolicy laesst zu einem Production-Zelt nur
            // ein Production-Setup zu.
            TentType = TentType.Production,
            Status = TentStatus.Active,
            WidthCm = 120,
            DepthCm = 120,
            TentHeightCm = 200,
            LightType = "LED",
            LightWatt = 480,
            Notes = "Testdaten — dieses Zelt gibt es nicht.",
            // Die Kuehler-Steuerung ist im Testbestand AN. Sonst waere die
            // Karte auf der Live-Seite unsichtbar und niemand — ich
            // eingeschlossen — saehe je, was sie zeigt.
            ChillerControlEnabled = true,
            ChillerSwitchEntityId = DemoData.KuehlerSteckdose,
            // LeafTempOffsetC bleibt auf seinem Standard (2,0). Auf 0 gesetzt
            // rechnete die App Luft- statt Blatt-VPD.
        });
    }

    private static GrowSystem AufbauAnlegen(HydroSetupRepository hydro, int zeltId)
    {
        // CreateHydroSetup, nicht CreateSystem: nur die erste normalisiert UND
        // prueft. Fuer RDWC gelten vier Zusatzpruefungen, jede wirft.
        return hydro.CreateHydroSetup(new GrowSystem
        {
            TentId = zeltId,
            Name = "RDWC 4er (Testdaten)",
            // Hier ein STRING, nicht das Enum — auf GrowRun heisst dasselbe
            // Feld genauso und ist dort ein Enum. Eine der Fallen des Hauses.
            HydroStyle = Models.HydroStyle.RDWC.ToString(),
            PotCount = 4,
            PotSizeLiters = 27,
            ReservoirLiters = 100,
            LayoutType = HydroSetupLayoutType.Grid2x2,
            ReservoirPosition = ReservoirPosition.External,
            Status = HydroSetupStatus.Active,
            HasCirculationPump = true,
            HasAirPump = true,
            AirPumpLitersPerHour = 3600,
            AirStoneCount = 4,
            HasChiller = true,
            DisplayOrder = 1,
        });
    }

    /// <summary>Ein zweites, kleineres System — damit ein Wechsel prüfbar ist.</summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026).</b> Der Prüfer fand einen Fehler, den
    /// er nicht nachstellen konnte: wechselt jemand im Grow-Formular von einem
    /// 4-Topf- auf ein 2-Topf-System, blieb die Belegung der vier Töpfe stehen
    /// („4 von 2 belegt"). Er kam nicht dran, weil der Testbestand nur <b>ein</b>
    /// System kannte.</para>
    ///
    /// <para>Dieselbe Klasse Lücke wie „ein Gerät, obwohl der Nutzer sieben
    /// hat": eine Menge von eins lässt jeden Fehler unsichtbar, der erst beim
    /// Wechseln auftritt. Zwei Systeme sind das Minimum, an dem sich „das eine
    /// gegen das andere" überhaupt zeigen kann.</para>
    /// </remarks>
    private static GrowSystem ZweiterAufbauAnlegen(HydroSetupRepository hydro, int zeltId)
        => hydro.CreateHydroSetup(new GrowSystem
        {
            TentId = zeltId,
            Name = "DWC 2er Mutterecke (Testdaten)",
            HydroStyle = Models.HydroStyle.DWC.ToString(),
            PotCount = 2,
            PotSizeLiters = 19,
            ReservoirLiters = 38,
            /* DIESELBEN Werte, die die Normalisierung daraus macht.
               Der Bestand schrieb zuerst `Row` und `External`; DWC wird von
               `HydroSetupRepository.NormalizeHydroSetup` aber immer auf
               `SingleBucket`/`None` gezwungen. Auf /hydro/2 stand dadurch
               „Tank kein Tank" und zwei Zeilen weiter „Tank 38 L" — der
               Bestand widersprach sich selbst. Gefunden vom Pruefer. */
            LayoutType = HydroSetupLayoutType.SingleBucket,
            ReservoirPosition = ReservoirPosition.None,
            Status = HydroSetupStatus.Active,
            HasCirculationPump = false,
            HasAirPump = true,
            AirPumpLitersPerHour = 1800,
            AirStoneCount = 2,
            HasChiller = false,
            DisplayOrder = 2,
        });

    /// <summary>Zwei Sorten — damit der Mischgrow im Bestand existiert.</summary>
    /// <remarks>
    /// Mit nur einer Sorte war der ganze Mehrsorten-Weg (Sorte je Pflanze,
    /// Verteilungszeile, „gemischt"-Kachel) im Testbestand unsichtbar — und
    /// genau diesen Weg hat ein Nutzer nicht gefunden. Der Testbestand ist
    /// die Miniatur, an der so etwas auffallen muss.
    /// </remarks>
    private static (Strain Haupt, Strain Zweite) SorteAnlegen(SetupRepository setups)
    {
        var haupt = setups.CreateStrain(new Strain
        {
            Name = "White Widow (Testdaten)",
            Breeder = "Royal Queen Seeds",
            Dominance = StrainDominance.Hybrid,
            FlowerWeeksMin = 8,
            FlowerWeeksMax = 9,
            SeedKind = Models.SeedKind.Feminized,
            ThcPercent = 19,
            CbdPercent = 0.6,
            Notes = "Testdaten.",
        });

        var zweite = setups.CreateStrain(new Strain
        {
            Name = "Gorilla Glue (Testdaten)",
            Breeder = "GG Strains",
            Dominance = StrainDominance.Hybrid,
            /* Laenger als die White Widow — und zwar aus zwei Gruenden.
               Erstens stimmt es: GG #4 wird ueblich mit 9 bis 10 Wochen
               angegeben, White Widow mit 8 bis 9. Zweitens ist der Testbestand
               Produktionscode: mit zweimal 8-9 konnte die Warnung „die Sorten
               brauchen unterschiedlich lange" gegen ihn NIE ausloesen — sie
               waere ungeprueft ausgeliefert worden. Gefunden vom Pruefer. */
            FlowerWeeksMin = 9,
            FlowerWeeksMax = 11,
            SeedKind = Models.SeedKind.Feminized,
            ThcPercent = 24,
            CbdPercent = 0.1,
            Notes = "Testdaten.",
        });

        return (haupt, zweite);
    }

    /// <summary>
    /// Die letzten Wasserwechsel — als eigene Einträge, nicht nur als Häkchen.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (31.08.2026).</b> Die Seite <c>/wasserwechsel</c>
    /// stand im Testbestand leer da: der Bestand markierte den Wechsel nur an
    /// der Messung (<c>SolutionChange</c>), die Tabelle <c>Changeouts</c> war
    /// nie befüllt. Gefunden von <c>demobestand-vollstaendig.spec.ts</c> —
    /// genau dafür gibt es sie.</para>
    ///
    /// <para><b>Warum das mehr ist als Kosmetik.</b> Ein Nutzer, der die App
    /// zum ersten Mal öffnet, sieht sonst eine leere Seite und hält die
    /// Funktion für unfertig. Und die Prüfungen darüber messen an einem
    /// Bestand, der einen der beiden Wege nie geht — ein Fehler <i>im</i>
    /// Bestand verdeckt Fehler <i>in</i> der App.</para>
    ///
    /// <para><b>Der Rhythmus ist derselbe</b> wie bei den Messungen
    /// (<see cref="Demoverlauf.WasserwechselAlleTage"/>), damit beide Belege
    /// auf denselben Tag fallen. Liefen sie auseinander, zeigte die App zwei
    /// verschiedene „letzte Wechsel" — und niemand wüsste, welcher stimmt.</para>
    /// </remarks>
    /// <summary>
    /// Wann ein gesäter Wasserwechsel stattgefunden hat — höchstens jetzt.
    /// </summary>
    /// <remarks>
    /// <para><b>Öffentlich, damit die Entscheidung prüfbar ist.</b> Die Prüfung
    /// über den fertigen Bestand (<c>DemobestandStimmigTests</c>) fängt einen
    /// Zukunfts-Zeitpunkt nur, wenn sie zufällig <i>vor</i> 07:00 an einem
    /// Wechseltag läuft — ab 07:00 wäre derselbe kaputte Code grün. Der Prüfer
    /// hat darauf hingewiesen; hier steht die Regel selbst, und die lässt sich
    /// zu jeder Uhrzeit prüfen.</para>
    ///
    /// <para>07:00 Ortszeit ist die Stunde, in der der Bestand auch die Messung
    /// mit dem Häkchen setzt — beide Belege sollen auf denselben Zeitpunkt
    /// fallen, sonst zeigte die App zwei verschiedene „letzte Wechsel".</para>
    /// </remarks>
    /// <param name="tag">Der Kalendertag des Wechsels, Ortszeit.</param>
    /// <param name="jetztUtc">Der aktuelle Zeitpunkt.</param>
    public static DateTime WechselZeitpunkt(DateTime tag, DateTime jetztUtc)
    {
        var geplant = tag.AddHours(7).ToUniversalTime();
        return geplant < jetztUtc ? geplant : jetztUtc;
    }

    private static void WasserwechselAnlegen(AddbackRepository addback, GrowRun grow)
    {
        var heute = DateTime.Today;

        // Die letzten vier planmaessigen Wechsel — genug fuer einen Verlauf,
        // wenig genug, dass die Liste lesbar bleibt.
        for (var nummer = 1; nummer <= 4; nummer += 1)
        {
            var wann = heute.AddDays(-(Demoverlauf.SeitWasserwechsel(heute)
                + Demoverlauf.WasserwechselAlleTage * (nummer - 1)));
            if (wann < grow.StartDate) break;

            // Der juengste ist ein Komplettwechsel, die aelteren Teilwechsel —
            // so hat die Anzeige beide Arten zu unterscheiden.
            var komplett = nummer == 1;

            addback.CreateChangeout(new ChangeoutEntry
            {
                GrowId = grow.Id,
                HydroSetupId = grow.SystemId,
                Kind = komplett ? ChangeoutKind.Full : ChangeoutKind.Partial,
                // 07:00 Ortszeit — dieselbe Stunde, in der der Bestand die
                // Messung mit dem Haekchen setzt.
                PerformedAtUtc = WechselZeitpunkt(wann, DateTime.UtcNow),
                PercentChanged = komplett ? 100 : 50,
                VolumeChangedLiters = komplett ? 100 : 50,
                /* Am Tag des Wechsels selbst ist der Verlauf schon wieder auf
                   Anfang — Demoverlauf.Ec rechnet ueber SeitWasserwechsel, und
                   das ist an diesem Tag 0. Gemessen wird deshalb am ABEND
                   davor, wenn der EC am hoechsten steht. Sonst stuende hier
                   "1,02 → 1,02": ein Wechsel, der nichts bewirkt hat. */
                EcBefore = Math.Round(Demoverlauf.Ec(wann.AddDays(-1).AddHours(20)), 2),
                EcAfter = 1.02,
                PhBefore = 6.2,
                PhAfter = 5.8,
                WaterUsed = grow.WaterSource,
                Notes = komplett
                    ? "Testdaten: kompletter Reset, Becken geschrubbt."
                    : "Testdaten: planmaessiger Teilwechsel.",
            });
        }
    }

    /// <summary>Die vier Pflanzen des laufenden Grows — je Topf eine, gemischt.</summary>
    /// <remarks>
    /// <para>Drei White Widow, eine Gorilla Glue, Töpfe 1–4: der Fall aus der
    /// Nutzer-Meldung „im RDWC je Topf eine eigene Sorte". <c>SiteIndex</c>
    /// zählt so, wie die Draufsicht ihre Sites zeichnet (1..n, zeilenweise).</para>
    /// </remarks>
    private static void PflanzenAnlegen(SetupRepository setups, int growId, int hauptSorteId, int zweiteSorteId)
    {
        var toepfe = new (int Topf, int SorteId)[]
        {
            (1, hauptSorteId),
            (2, hauptSorteId),
            (3, zweiteSorteId),
            (4, hauptSorteId),
        };

        foreach (var (topf, sorteId) in toepfe)
        {
            setups.CreatePlant(new PlantInstance
            {
                GrowId = growId,
                StrainId = sorteId,
                SiteIndex = topf,
                Label = $"Pflanze {topf}",
                PlantRole = PlantRole.Production,
                PlantStatus = PlantStatus.Active,
                StartedAt = Tag(73),
                Notes = "Testdaten.",
            });
        }
    }

    private static GrowRun LaufenderGrowAnlegen(GrowRepository grows, int zeltId, int aufbauId, int sorteId)
    {
        // Der Flip liegt 35 Tage zurueck. Das ist Absicht und liegt zwischen
        // zwei Grenzen des GrowStageResolver: unter 10 Tagen kaeme
        // GrowStage.Transition heraus, ab Tag 49 (9 Wochen minus 14 Tage
        // Finish-Vorlauf) GrowStage.Finish. Dazwischen liegt die Bluete.
        var grow = new GrowRun
        {
            TentId = zeltId,
            SystemId = aufbauId,
            Name = "White Widow (Testdaten)",
            Strain = "White Widow",
            /* Die VERKNUEPFUNG in die Bibliothek, nicht nur der Text.
               Bis zum 31.08.2026 fehlte sie: der laufende Grow hiess "White
               Widow", die Bibliothek fuehrte "White Widow (Testdaten)", und
               weil die Statistik ueber StrainId zaehlt, hatte KEINE Sorte im
               Testbestand einen Lauf oder einen Ø-Ertrag. Das MCP-Werkzeug
               "sorte" antwortete "hat dafuer aber keinen Eintrag in der
               Sortenliste". Ein Fehler IM Bestand verdeckt Fehler IN der App —
               siehe CLAUDE.md, "Der Testbestand ist Produktionscode". */
            StrainId = sorteId,
            Breeder = "Royal Queen Seeds",
            Status = GrowStatus.Running,
            MediumType = MediumType.Hydro,
            HydroStyle = Models.HydroStyle.RDWC,
            IrrigationType = IrrigationType.ActiveHydro,
            Environment = GrowEnvironment.Indoor,
            WaterSource = WaterSource.Tap,
            SeedType = Models.SeedType.Feminized,
            StartMaterial = StartMaterial.Seed,
            EntryPoint = GrowEntryPoint.Germination,
            PlantCount = 4,
            StartDate = Tag(73),
            GerminatedAt = Tag(73),
            VegStartedAt = Tag(64),
            FlipDate = Tag(35),
            BreederFlowerWeeksMin = 8,
            BreederFlowerWeeksMax = 9,
            Light = "LED 480 W",
            ReservoirSize = "100 L",
            Notes = "Testdaten: laufender Lauf in der Blüte.",
            // Ohne die Rampe gibt es keinen Sollwert, und ohne Sollwert
            // schaltet der Kuehler-Regler bewusst gar nichts — die Karte
            // saegte dann immer denselben Satz.
            NightRampEnabled = true,
        };

        grow.Id = grows.CreateGrow(grow);
        return grow;
    }

    /// <summary>
    /// Sechs Wochen Messungen, Hand und Automatik gemischt.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum gemischt.</b> Die Automatik kann nur elf Felder füllen
    /// (<c>AutoMeasurementField</c>); Drain, Addback und Höhe bleiben
    /// Handarbeit. Ein Bestand, in dem beides vorkommt, ist deshalb nicht nur
    /// hübscher, sondern die einzige Lage, in der die Herkunfts-Anzeige
    /// („Hand" gegen „Automatik") überhaupt etwas zu unterscheiden hat.</para>
    ///
    /// <para><b>TakenAt ist Ortszeit</b>, nicht UTC — die Spalte heißt nicht
    /// „…Utc". Wer hier UTC hineinschreibt, datiert in Deutschland jede Messung
    /// ein bis zwei Stunden zurück und schiebt sie über Mitternacht auf den
    /// falschen Tag.</para>
    ///
    /// <para><b>In die Bilanz zählen genau fünf Größen:</b> pH, EC, ORP,
    /// Wassertemperatur und VPD — letzteres nur, wenn Lufttemperatur UND
    /// Feuchte in <i>derselben</i> Zeile stehen. Deshalb stehen sie hier immer
    /// zusammen.</para>
    /// </remarks>
    /// <summary>
    /// Sechs Wochen Messungen — aus <see cref="Demoverlauf"/>, nicht aus
    /// einer eigenen Kurve.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum von dort.</b> Die Diagramme auf der Live- und der
    /// Zeltseite kommen aus den gefälschten Sensoren, dieses Protokoll aus
    /// den Messungen. Zwei Kurven für dieselbe Sache heißt: das Diagramm
    /// zeigt einen EC-Sägezahn, und die Zeile daneben behauptet etwas
    /// anderes. Beide lesen deshalb dieselbe Quelle.</para>
    ///
    /// <para><b>Was hier eigen bleibt.</b> Nur die Auswahl: WANN gemessen
    /// wird und WELCHE Felder dabei anfallen. Die Automatik kann elf Felder
    /// füllen und misst zweimal am Tag; von Hand kommen ORP, Sauerstoff,
    /// Füllstand und Höhe dazu, dafür nur einmal die Woche. Ein Bestand, in
    /// dem beides vorkommt, ist die einzige Lage, in der die
    /// Herkunfts-Anzeige („Hand" gegen „Automatik") etwas zu unterscheiden
    /// hat.</para>
    ///
    /// <para><b>TakenAt ist Ortszeit</b>, nicht UTC — die Spalte heißt nicht
    /// „…Utc". Wer hier UTC hineinschreibt, datiert in Deutschland jede
    /// Messung ein bis zwei Stunden zurück und schiebt sie über Mitternacht
    /// auf den falschen Tag.</para>
    /// </remarks>
    private static int MessungenAnlegen(MeasurementRepository messungen, GrowRun grow)
    {
        var anzahl = 0;

        for (var tag = Demoverlauf.TageRueckwaerts; tag >= 0; tag--)
        {
            var wann = DateTime.Today.AddDays(-tag);

            // Die Automatik misst zweimal am Tag: nach Licht an und vor Licht aus.
            foreach (var stunde in new[] { 7, 21 })
            {
                var zeitpunkt = wann.AddHours(stunde);

                // Nichts in der Zukunft anlegen. Wer den Bestand morgens
                // erzeugt, haette sonst eine Messung von „heute 21:00" — die
                // Beurteilung weist die als „unplausibler Zeitpunkt" aus und
                // laesst sie ganz aus der Bilanz. Gesehen beim LESEN der
                // Seite, nicht beim Messen.
                if (zeitpunkt > DateTime.Now) continue;

                messungen.CreateMeasurement(new Measurement
                {
                    GrowId = grow.Id,
                    TakenAt = zeitpunkt,
                    Stage = PhaseAm(grow, wann),
                    Source = ValueOrigin.HomeAssistant,

                    // Am Wechseltag traegt die Morgenmessung den Vermerk.
                    //
                    // OHNE IHN LOG DER BESTAND: die EC-Kurve saegt woechentlich
                    // zurueck und im Journal steht ein Wasserwechsel — aber
                    // SopDueService liest den Wechsel aus `SolutionChange` an
                    // einer Messung, und den setzte niemand. Auf der Seite
                    // „Was jetzt zu tun ist" stand deshalb dauerhaft
                    // „Woechentlicher Wasserwechsel: zuletzt vor 73 Tagen" —
                    // 73 ist der Start des Grows, also die Ersatzannahme fuer
                    // „gar kein Beleg". Gefunden beim LESEN der Seite.
                    SolutionChange = stunde == 7 && Demoverlauf.SeitWasserwechsel(wann) == 0,

                    ReservoirPh = Runden(Demoverlauf.Ph(zeitpunkt), 2),
                    ReservoirEc = Runden(Demoverlauf.Ec(zeitpunkt), 2),
                    ReservoirWaterTempC = Runden(Demoverlauf.WasserTempC(zeitpunkt), 1),
                    AirTemperatureC = Runden(Demoverlauf.LuftTempC(zeitpunkt), 1),
                    HumidityPercent = Runden(Demoverlauf.FeuchtePercent(zeitpunkt), 0),
                    ReservoirLevelLiters = Runden(Demoverlauf.FuellstandLiter(zeitpunkt), 0),
                });
                anzahl++;
            }

            // Von Hand am Vorabend des Wasserwechsels — mit den Feldern, die
            // die Automatik gar nicht kennt.
            var abends = wann.AddHours(19).AddMinutes(20);
            if (Demoverlauf.SeitWasserwechsel(wann) == Demoverlauf.WasserwechselAlleTage - 1
                && abends <= DateTime.Now)
            {
                messungen.CreateMeasurement(new Measurement
                {
                    GrowId = grow.Id,
                    TakenAt = abends,
                    Stage = PhaseAm(grow, wann),
                    Source = ValueOrigin.Manual,
                    ReservoirPh = Runden(Demoverlauf.Ph(abends), 2),
                    ReservoirEc = Runden(Demoverlauf.Ec(abends), 2),
                    ReservoirWaterTempC = Runden(Demoverlauf.WasserTempC(abends), 1),
                    AirTemperatureC = Runden(Demoverlauf.LuftTempC(abends), 1),
                    HumidityPercent = Runden(Demoverlauf.FeuchtePercent(abends), 0),
                    OrpMv = Runden(Demoverlauf.OrpMv(abends), 0),
                    DissolvedOxygenMgL = Runden(Demoverlauf.SauerstoffMgL(abends), 1),
                    ReservoirLevelLiters = Runden(Demoverlauf.FuellstandLiter(abends), 0),
                    HeightCm = Runden(46 + (Demoverlauf.TageRueckwaerts - tag) * 0.9, 0),
                    Notes = Demoverlauf.Stoerung(wann)
                        ? "Testdaten: Wasser zu warm, Kühler prüfen."
                        : "Testdaten: Kontrolle vor dem Wasserwechsel.",
                });
                anzahl++;
            }
        }

        // Eine frische Messung zum Schluss — sonst haengt „zuletzt gemessen"
        // je nach Uhrzeit bis zu einen Tag zurueck, und die App mahnt im
        // Demobestand sofort zum Nachmessen.
        var jetzt = DateTime.Now.AddMinutes(-90);
        messungen.CreateMeasurement(new Measurement
        {
            GrowId = grow.Id,
            TakenAt = jetzt,
            Stage = PhaseAm(grow, jetzt),
            Source = ValueOrigin.Manual,
            ReservoirPh = Runden(Demoverlauf.Ph(jetzt), 2),
            ReservoirEc = Runden(Demoverlauf.Ec(jetzt), 2),
            ReservoirWaterTempC = Runden(Demoverlauf.WasserTempC(jetzt), 1),
            AirTemperatureC = Runden(Demoverlauf.LuftTempC(jetzt), 1),
            HumidityPercent = Runden(Demoverlauf.FeuchtePercent(jetzt), 0),
            OrpMv = Runden(Demoverlauf.OrpMv(jetzt), 0),
            DissolvedOxygenMgL = Runden(Demoverlauf.SauerstoffMgL(jetzt), 1),
            ReservoirLevelLiters = Runden(Demoverlauf.FuellstandLiter(jetzt), 0),
            HeightCm = Runden(46 + Demoverlauf.TageRueckwaerts * 0.9, 0),
            Notes = "Testdaten: letzte Kontrolle.",
        });
        anzahl++;
        // EINE Zeile mit unmoeglichen Werten. Die gehoert in den Demobestand,
        // weil die App dafuer eine eigene Anzeige hat — Zeichen, Farbe, Zaehler
        // in der Bilanz —, und ohne so eine Zeile sieht sie niemand. Genau
        // dieser Fall ist beim Nutzer aufgeschlagen: 9000 Grad Luft standen im
        // Protokoll wie jede andere Zahl.
        //
        // Der Grund ist ein echter: ein Messgeraet, das die Verbindung verliert,
        // meldet oft nicht nichts, sondern Unsinn.
        var gestoert = DateTime.Today.AddDays(-11).AddHours(14);
        messungen.CreateMeasurement(new Measurement
        {
            GrowId = grow.Id,
            TakenAt = gestoert,
            Stage = PhaseAm(grow, gestoert),
            Source = ValueOrigin.HomeAssistant,
            ReservoirEc = 99999,
            ReservoirWaterTempC = 5000,
            AirTemperatureC = 9000,
            Co2Ppm = -500,
            Notes = "Testdaten: Sonde hatte einen Aussetzer — die Werte kann es nicht geben.",
        });
        anzahl++;

        return anzahl;
    }

    /// <summary>
    /// Welche Phase galt an diesem Tag?
    /// </summary>
    /// <remarks>
    /// <c>Measurement.Stage</c> wird <b>gelesen</b>, nicht gerechnet: die
    /// Beurteilung prüft gegen die Sollwerte dieser Phase. Wer sechs Wochen
    /// stumpf <c>Veg</c> schreibt, lässt Blütewochen gegen Veg-Ziele prüfen —
    /// und die Seite zeigt daneben die gerechnete Phase, sodass der Widerspruch
    /// im Bestand steht.
    /// </remarks>
    private static GrowStage PhaseAm(GrowRun grow, DateTime wann)
    {
        if (grow.FlipDate is not { } flip) return GrowStage.Veg;
        var seitFlip = (wann.Date - flip.Date).Days;
        if (seitFlip < 0) return GrowStage.Veg;
        return seitFlip < 10 ? GrowStage.Transition : GrowStage.Flower;
    }



    private static double Runden(double wert, int stellen) => Math.Round(wert, stellen);

    private static void JournalAnlegen(JournalRepository journal, int growId)
    {
        var eintraege = new (int VorTagen, JournalEntryType Typ, string Titel, string Text)[]
        {
            (64, JournalEntryType.GerminationConfirmed, "Sämling vorbei",
                "Testdaten: echte gezackte Blätter da, ab hier Wachstum."),
            (49, JournalEntryType.Training, "LST angelegt",
                "Testdaten: vier Haupttriebe waagerecht gezogen."),
            (35, JournalEntryType.Action, "Auf 12/12 geflippt",
                "Testdaten: Licht umgestellt, EC auf 1,1 angehoben."),
            (21, JournalEntryType.ReservoirChange, "Wasserwechsel",
                "Testdaten: komplett getauscht, Becken gespült."),
            (9, JournalEntryType.Observation, "Blattränder Woche 5",
                "Testdaten: leichte Spitzenverbrennung an zwei Pflanzen, EC leicht zurückgenommen."),
        };

        foreach (var (vorTagen, typ, titel, text) in eintraege)
        {
            journal.Create(new JournalEntry
            {
                GrowId = growId,
                Title = titel,
                Body = text,
                EntryType = typ,
                Source = ValueOrigin.Manual,
                // Echtes UTC: die Spalte heisst OccurredAtUtc.
                OccurredAtUtc = DateTime.UtcNow.AddDays(-vorTagen),
            });
        }
    }

    private static void AufgabenAnlegen(TaskRepository aufgaben, int growId)
    {
        // Eine ueberfaellige, eine heute faellige, eine kuenftige — sonst zeigt
        // die Aufgabenseite nur einen Zustand.
        var liste = new (string Titel, double StundenBisFaellig, TaskPriority Rang, string? Notiz)[]
        {
            ("pH-Sonde nachkalibrieren", -52, TaskPriority.Critical,
                "Testdaten: driftet seit dem Wasserwechsel."),
            ("EC messen und Addback rechnen", 5, TaskPriority.High, null),
            ("Luftfilter prüfen", 76, TaskPriority.Normal,
                "Testdaten: Vorfilter absaugen, Aktivkohle bleibt."),
        };

        foreach (var (titel, stunden, rang, notiz) in liste)
        {
            aufgaben.Create(new GrowTask
            {
                GrowId = growId,
                Title = titel,
                // Echtes UTC — ein DateTime mit unbestimmter Art gaelte als
                // Ortszeit und verschoebe sich beim Schreiben.
                DueAtUtc = DateTime.UtcNow.AddHours(stunden),
                Priority = rang,
                Status = GrowTaskStatus.Open,
                Notes = notiz,
            });
        }
    }

    private static HardwareItem GeraetMitHistorieAnlegen(HardwareRepository hardware, int zeltId, int growId)
    {
        var geraet = hardware.CreateHardwareItem(new HardwareItem
        {
            Name = "Bluelab pH-Sonde (Testdaten)",
            Category = "Sensor",
            DeviceKind = HardwareDeviceKind.FixedSensor,

            // OHNE DIESE ZWEI ZEILEN legte der Testbestand ZWEI pH-Sonden an:
            // der Startlauf prueft „gibt es schon eine Sonde fuer ReservoirPh?",
            // und dieses Geraet hatte gar keine Messgroesse. Dazu meldete die
            // Seite „Sensoren & Wartung" fuer beide dauerhaft „kein Mapping" —
            // fuer Geraete, die der Bestand selbst angelegt hat.
            MetricType = SensorMetricType.ReservoirPh,
            HaEntityId = DemoData.EntitaetFuer(SensorMetricType.ReservoirPh),

            Status = HardwareItemStatus.Active,
            Criticality = HardwareItemCriticality.High,
            CalibrationIntervalDays = 14,
            TentId = zeltId,
            GrowId = growId,
            InstalledAtUtc = DateTime.UtcNow.AddDays(-120),
            Manufacturer = "Bluelab",
            Model = "Guardian Monitor",
            SerialNumber = "BL-2291",
        });

        // Drei durchgefuehrte Kalibrierungen. PerformedAtUtc IMMER selbst
        // setzen: sonst legt ApplyCalibrationDefaults alle drei auf heute, und
        // aus einer Historie wird ein Stapel.
        var verlauf = new (int VorTagen, decimal Vorher, CalibrationResult Ergebnis)[]
        {
            (42, 7.02m, CalibrationResult.Passed),
            (28, 7.09m, CalibrationResult.Passed),
            (14, 7.14m, CalibrationResult.AdjustmentNeeded),
        };

        foreach (var (vorTagen, vorher, ergebnis) in verlauf)
        {
            var wann = DateTime.UtcNow.AddDays(-vorTagen);
            hardware.CreateCalibrationEvent(new CalibrationEvent
            {
                HardwareItemId = geraet.Id,
                Title = "2-Punkt-Kalibrierung pH",
                CalibrationType = CalibrationEventType.Ph,
                Status = CalibrationEventStatus.Completed,
                Result = ergebnis,
                ReferenceSolution = "pH 7,00 / pH 4,00 Pufferlösung",
                ReferenceValue = 7.00m,
                BeforeValue = vorher,
                AfterValue = 7.00m,
                TemperatureC = 20.5m,
                PerformedAtUtc = wann,
                NextDueAtUtc = wann.AddDays(14),
            });
        }

        // Ein GEPLANTER Termin, faellig seit fuenf Tagen. Ohne ihn hat kein
        // Geraet ein `nextCare`, der Knopf „Kalibriert" erscheint nie, und die
        // Pflege-Maske ist im Testbestand nicht erreichbar — die Analyse hat
        // genau das als Schwaeche benannt: von allem genau eins, und was
        // niemand ausloesen kann, prueft auch niemand.
        hardware.CreateCalibrationEvent(new CalibrationEvent
        {
            HardwareItemId = geraet.Id,
            Title = "2-Punkt-Kalibrierung pH",
            CalibrationType = CalibrationEventType.Ph,
            Status = CalibrationEventStatus.Planned,
            ReferenceSolution = "pH 7,00 / pH 4,00 Pufferlösung",
            ReferenceValue = 7.00m,
            // DueAtUtc, nicht NextDueAtUtc: das eine sagt „ist faellig", das
            // andere „danach waere wieder faellig". Die Oberflaeche liest fuer
            // einen GEPLANTEN Termin das erste — mit dem zweiten stand das
            // Geraet auf „Pflege geplant 0" und der Knopf erschien nie.
            DueAtUtc = DateTime.UtcNow.AddDays(-5),
        });

        // NICHT CompleteCalibrationEvent zum Nachtragen benutzen: das legt zu
        // jedem Abschluss selbst einen neuen Termin an, und aus drei Nachtraegen
        // wuerden drei offene Erinnerungen.
        return geraet;
    }

    /// <summary>Jede Messgröße, für die es einen Wert gibt, dem Zelt zuordnen.</summary>
    /// <remarks>
    /// <para><b>Der groesste blinde Fleck im Testbestand.</b> Bis zum
    /// 24.08.2026 stand auf der Home-Assistant-Seite <i>„Entities gemappt: 0
    /// von 17"</i> — im Testbetrieb war <b>keine einzige</b> Messgröße
    /// zugeordnet. Die Live-Seite zeigte trotzdem dreizehn Werte, weil
    /// <see cref="DemoData.StatesFor"/> alles liefert, ob zugeordnet oder
    /// nicht. Der Weg über eine <i>zugeordnete</i> Messgröße — und alles, was
    /// daran hängt: die Sensorliste am Zelt, das Alter eines Werts, die
    /// Zuordnungs-Warnungen — wurde von keiner Prüfung je betreten.</para>
    ///
    /// <para><b>Zählung statt Liste.</b> Durchlaufen wird die Aufzählung, nicht
    /// eine abgetippte Auswahl. Was der Testbestand liefert, wird zugeordnet;
    /// was er nicht liefert, bleibt frei — und bleibt damit auch ein sichtbarer
    /// Fall „noch nicht eingerichtet", den es ebenfalls zu sehen geben muss.</para>
    /// </remarks>
    private static void SensorenZuordnen(GrowRepository grows, int zeltId)
    {
        var zustaende = DemoData.StatesFor(DateTime.UtcNow);

        foreach (var art in Enum.GetValues<SensorMetricType>())
        {
            var schluessel = TentSensorMetricKeyMap.Resolve(art);
            if (!zustaende.TryGetValue(schluessel, out var zustand)) continue;
            if (string.IsNullOrWhiteSpace(zustand.EntityId)) continue;

            grows.AddTentSensor(new TentSensor
            {
                TentId = zeltId,
                MetricType = art,
                HaEntityId = zustand.EntityId,
                DisplayLabel = zustand.FriendlyName,
                IsActive = true,
            });
        }
    }

    /// <summary>Die übrigen Geräte einer echten Anlage.</summary>
    /// <returns>Die Umwälzpumpe — an ihr hängt das Risiko im Bestand.</returns>
    /// <remarks>
    /// <para><b>Warum acht Geräte und nicht eins.</b> Der Testbestand hatte
    /// genau ein Gerät; der Tester hat sieben. Eine Prüfung, die im Bestand über
    /// eine Liste läuft, sieht mit einer Zeile fast nichts: kein Sortieren, kein
    /// Filtern, kein zweiter Klick, keine Zeile, die eine andere verdeckt. Am
    /// 24.08.2026 ist genau das aufgeflogen — die Prüfung <i>„Bearbeiten holt
    /// das Formular auch beim ZWEITEN Klick ins Bild"</i> konnte im Bestand
    /// nicht laufen, weil es kein zweites Gerät gab. Und derselbe Knopf war dem
    /// Tester schon zweimal aufgefallen.</para>
    ///
    /// <para><b>Und weil das Risiko sonst am falschen Gerät hing.</b> Der
    /// Bestand meldete <i>„Umwälzpumpe meldet an, zieht aber 0 W"</i> und
    /// verwies dabei auf die pH-Sonde: wer der Meldung folgte, landete beim
    /// falschen Gerät. Jetzt gibt es die Pumpe.</para>
    ///
    /// <para>Die Verschleiß-Vorlagen sind echte Kennungen aus
    /// <c>wwwroot/knowledge-defaults/wear</c> — abgetippte hätten stumm ins
    /// Leere gezeigt.</para>
    /// </remarks>
    private static HardwareItem WeitereGeraeteAnlegen(
        HardwareRepository hardware, int zeltId, int growId)
    {
        HardwareItem Anlegen(HardwareItem geraet)
        {
            geraet.TentId = zeltId;
            geraet.GrowId = growId;
            geraet.Status = HardwareItemStatus.Active;
            return hardware.CreateHardwareItem(geraet);
        }

        // --- Messgeräte, fest verbaut und zugeordnet -------------------------
        Anlegen(new HardwareItem
        {
            Name = "EC-Sonde (Testdaten)",
            Category = "Sensor",
            DeviceKind = HardwareDeviceKind.FixedSensor,
            MetricType = SensorMetricType.ReservoirEc,
            HaEntityId = DemoData.EntitaetFuer(SensorMetricType.ReservoirEc),
            Criticality = HardwareItemCriticality.High,
            CalibrationIntervalDays = 30,
            WearTemplateId = "ec-probe",
            Manufacturer = "Bluelab",
            Model = "Guardian Monitor",
            InstalledAtUtc = DateTime.UtcNow.AddDays(-120),
        });

        Anlegen(new HardwareItem
        {
            Name = "Wassertemperatur-Fühler (Testdaten)",
            Category = "Sensor",
            DeviceKind = HardwareDeviceKind.FixedSensor,
            MetricType = SensorMetricType.ReservoirWaterTemp,
            HaEntityId = DemoData.EntitaetFuer(SensorMetricType.ReservoirWaterTemp),
            Criticality = HardwareItemCriticality.High,
            InstalledAtUtc = DateTime.UtcNow.AddDays(-120),
        });

        Anlegen(new HardwareItem
        {
            Name = "Klimasensor Zelt (Testdaten)",
            Category = "Sensor",
            DeviceKind = HardwareDeviceKind.FixedSensor,
            MetricType = SensorMetricType.AirTemperature,
            HaEntityId = DemoData.EntitaetFuer(SensorMetricType.AirTemperature),
            Criticality = HardwareItemCriticality.Medium,
            InstalledAtUtc = DateTime.UtcNow.AddDays(-118),
        });

        // --- Technik ---------------------------------------------------------
        var umwaelzpumpe = Anlegen(new HardwareItem
        {
            Name = "Umwälzpumpe (Testdaten)",
            Category = "Pumpe",
            DeviceKind = HardwareDeviceKind.Equipment,
            Criticality = HardwareItemCriticality.Critical,
            Manufacturer = "Jebao",
            Model = "DCP-5000",
            InstalledAtUtc = DateTime.UtcNow.AddDays(-210),
            ExpectedLifespanDays = 900,
            InspectionIntervalDays = 60,
        });

        Anlegen(new HardwareItem
        {
            Name = "Luftpumpe + Steine (Testdaten)",
            Category = "Pumpe",
            DeviceKind = HardwareDeviceKind.Equipment,
            Criticality = HardwareItemCriticality.High,
            WearTemplateId = "air-stones-ceramic",
            InstalledAtUtc = DateTime.UtcNow.AddDays(-150),
            InspectionIntervalDays = 30,
        });

        Anlegen(new HardwareItem
        {
            Name = "Wasserkühler (Testdaten)",
            Category = "Klima",
            DeviceKind = HardwareDeviceKind.Equipment,
            Criticality = HardwareItemCriticality.High,
            Manufacturer = "Hailea",
            Model = "HC-130A",
            HaEntityId = DemoData.KuehlerSteckdose,
            InstalledAtUtc = DateTime.UtcNow.AddDays(-200),
            InspectionIntervalDays = 90,
        });

        Anlegen(new HardwareItem
        {
            Name = "Aktivkohlefilter (Testdaten)",
            Category = "Luft",
            DeviceKind = HardwareDeviceKind.Equipment,
            Criticality = HardwareItemCriticality.Medium,
            InstalledAtUtc = DateTime.UtcNow.AddDays(-160),
            ExpectedLifespanDays = 365,
            InspectionIntervalDays = 45,
        });

        // --- Ein Handmessgerät: es wird NICHT zugeordnet, und das ist richtig.
        // Ohne so ein Gerät bleibt der Fall „braucht kein Mapping" im Bestand
        // ungeprüft, und die Warnung dazu hätte niemand je gegengelesen.
        Anlegen(new HardwareItem
        {
            Name = "pH-Stift zum Nachmessen (Testdaten)",
            Category = "Sonde",
            DeviceKind = HardwareDeviceKind.HandheldMeter,
            Criticality = HardwareItemCriticality.Low,
            CalibrationIntervalDays = 60,
            Manufacturer = "Apera",
            Model = "PH60",
            InstalledAtUtc = DateTime.UtcNow.AddDays(-90),
        });

        return umwaelzpumpe;
    }

    /// <summary>Der Lichtplan des Testzelts.</summary>
    /// <remarks>
    /// <para><b>Ohne ihn fehlte dem Testbestand ein ganzes Kapitel.</b> Der
    /// Lichtplan speist die Tag/Nacht-Erkennung der Alarme, die Stromkosten je
    /// Grow und den Vorschlag im Versuchsaufbau „Zelt (AC-Test)". Alle drei
    /// liefen im Testbetrieb auf einer Ersatzannahme — also genau da nicht, wo
    /// man sie ansehen kann.</para>
    ///
    /// <para>Die Uhrzeiten kommen aus <see cref="Demoverlauf"/> und nicht von
    /// Hand: sonst zeigte der Plan 08:00 und die Kurve ginge um 06:00 an.</para>
    /// </remarks>
    private static void LichtplanAnlegen(GrowRepository grows, int zeltId)
    {
        grows.CreateLightSchedule(new LightSchedule
        {
            TentId = zeltId,
            Name = "Blüte 12/12 (Testdaten)",
            IsActive = true,
            LightsOnTime = Demoverlauf.LichtAnUhr,
            LightsOffTime = Demoverlauf.LichtAusUhr,
            Source = LightSource.Manual,
        });
    }

    private static void AlarmregelAnlegen(AlertRuleRepository alarme, int zeltId)
    {
        // ReplaceForTent LOESCHT erst alle Regeln des Zelts — hier unkritisch,
        // weil das Zelt gerade erst entstanden ist. Wer spaeter eine Regel
        // ergaenzt, muss vorher GetForTent lesen.
        alarme.ReplaceForTent(zeltId,
        [
            new TentAlertRule
            {
                TentId = zeltId,
                // Der kanonische Klartext-Schluessel, kein Enum-Name.
                MetricKey = "reservoir-ph",
                MinValue = 5.5,
                MaxValue = 6.3,
                NotifyService = string.Empty,
                Enabled = true,
                CooldownMinutes = 30,
            },
            new TentAlertRule
            {
                TentId = zeltId,
                MetricKey = "reservoir-temp",
                MinValue = 17,
                MaxValue = 22,
                NotifyService = string.Empty,
                Enabled = true,
                CooldownMinutes = 60,
            },
        ]);
    }

    private static void RisikoAnlegen(HardwareRepository hardware, int zeltId, int growId, int geraetId)
    {
        hardware.CreateRiskEvent(new RiskEvent
        {
            Title = "Umwälzpumpe meldet an, zieht aber 0 W (Testdaten)",
            EventType = RiskEventType.PumpOffline,
            Severity = RiskEventSeverity.Critical,
            Status = RiskEventStatus.Open,
            Source = RiskEventSource.Manual,
            Description = "Testdaten: Zustand „an“, Leistungsaufnahme seit 25 Minuten unter 2 W.",
            HardwareItemId = geraetId,
            TentId = zeltId,
            GrowId = growId,
            StartedAtUtc = DateTime.UtcNow.AddHours(-3),
            LastSeenAtUtc = DateTime.UtcNow,
            RawValue = "0.4",
            // DedupeKey bleibt leer: mit Schluessel wuerde ein zweiter Aufruf
            // das bestehende Ereignis nur fortschreiben statt eines anzulegen.
        });
    }

    private static void GlasAnlegen(CuringRepository aushaerten, int growId)
    {
        var glas = aushaerten.CreateJar(new CuringJar
        {
            GrowId = growId,
            Label = "Glas 1 — obere Blüten (Testdaten)",
            FilledAtUtc = DateTime.UtcNow.AddDays(-9),
            WeightG = 84.5,
            HasHumidityPack = false,
            Notes = "Testdaten: dunkel im Schrank.",
        });

        // Vier Ablesungen, die sich dem Zielfenster 58–62 % nähern. Eine davon
        // OHNE BurpedMinutes: „nachgesehen, nicht gelüftet" ist ein eigener
        // Fall, und ohne ihn kaeme er im Bestand nie vor.
        var ablesungen = new (int VorTagen, double Feuchte, int? Gelueftet, string? Notiz)[]
        {
            (8, 68.0, 15, "Testdaten: riecht noch nach Heu."),
            (6, 64.5, 10, null),
            (4, 61.0, null, "Testdaten: nur nachgesehen."),
            (1, 60.0, 5, null),
        };

        foreach (var (vorTagen, feuchte, gelueftet, notiz) in ablesungen)
        {
            aushaerten.CreateReading(new CuringReading
            {
                JarId = glas,
                ReadAtUtc = DateTime.UtcNow.AddDays(-vorTagen),
                HumidityPercent = feuchte,
                BurpedMinutes = gelueftet,
                Source = CuringReadingSource.Manual,
                Note = notiz,
            });
        }
    }

    /// <summary>
    /// Zwei Dosierpumpen, an die vorhandenen Demo-Schalter gehängt.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum das hier fehlte.</b> <see cref="DemoData"/> legt die
    /// Home-Assistant-Schalter längst an (<c>switch.demo_ph_minus</c> und drei
    /// weitere) und trägt sogar eine Dosier-Historie nach — aber nur für
    /// Pumpen, die schon in der Datenbank stehen
    /// (<c>foreach (var pump in dosing.GetPumps())</c>). Auf einem frischen
    /// Rechner gibt es keine, die Schleife läuft null Mal, und
    /// <c>/dosierung</c> bleibt leer. Gemessen im strengen Lauf: die einzige
    /// von 25 Seiten, die durchfiel.</para>
    ///
    /// <para>Die Automatik bleibt bewusst <b>aus</b>: eine Demo, die von sich
    /// aus Säure dosiert, wäre eine Zumutung — und ohne kalibrierte Sonde
    /// sperrt sie sich ohnehin selbst.</para>
    /// </remarks>
    private static void PumpenAnlegen(DosingRepository dosierung, int zeltId)
    {
        var pumpen = new (string Name, DosingPurpose Zweck, string Schalter, string Mittel, double Konzentration)[]
        {
            ("pH Minus (Testdaten)", DosingPurpose.PhDown, "switch.demo_ph_minus", "Phosphorsäure 30 %", 30),
            ("Nährstoff A (Testdaten)", DosingPurpose.Nutrient, "switch.demo_nutrient_a", "Athena Pro Core", 100),
        };

        foreach (var (name, zweck, schalter, mittel, konzentration) in pumpen)
        {
            dosierung.InsertPump(new DosingPump
            {
                TentId = zeltId,
                Name = name,
                Purpose = zweck,
                HaEntityId = schalter,
                Agent = mittel,
                ConcentrationPercent = konzentration,
                MlPerMinute = 45,
                CostPerLiterEur = 24.90,
                CalibratedAtUtc = DateTime.UtcNow.AddDays(-12),
                TubeChangedAtUtc = DateTime.UtcNow.AddDays(-30),
                // Aus. Siehe oben.
                AutomationEnabled = false,
            });
        }
    }

    private static void AbgeschlossenenGrowAnlegen(
        GrowRepository grows, HarvestRepository ernten, int zeltId, int aufbauId,
        string name, string sorte, string zuechter,
        int vorTagen, int dauerTage, double nass, double trocken,
        int? sorteId = null)
    {
        // Das Verhaeltnis nass zu trocken liegt bei rund 23 % — der uebliche
        // Bereich fuer ordentlich getrocknetes Material. Bewusst keine
        // Bestleistung: eine Demo mit Traumwerten prueft die Anzeige nicht,
        // sie schmeichelt ihr.
        var geerntet = Tag(vorTagen);
        var gestartet = geerntet.AddDays(-dauerTage);

        var grow = new GrowRun
        {
            TentId = zeltId,
            SystemId = aufbauId,
            Name = name,
            Strain = sorte,
            /* Ohne Verknuepfung zaehlt die Sortenstatistik diesen Lauf nicht —
               genau deshalb stand im Testbestand bei JEDER Sorte "0 Laeufe,
               kein Ø-Ertrag". */
            StrainId = sorteId,
            Breeder = zuechter,
            // Nur Completed und Aborted landen im Archiv. Vergessen heisst:
            // der Lauf steht im Dashboard statt im Archiv.
            Status = GrowStatus.Completed,
            MediumType = MediumType.Hydro,
            HydroStyle = Models.HydroStyle.RDWC,
            IrrigationType = IrrigationType.ActiveHydro,
            Environment = GrowEnvironment.Indoor,
            SeedType = Models.SeedType.Feminized,
            StartMaterial = StartMaterial.Seed,
            EntryPoint = GrowEntryPoint.Germination,
            PlantCount = 4,
            StartDate = gestartet,
            GerminatedAt = gestartet,
            VegStartedAt = gestartet.AddDays(9),
            FlipDate = gestartet.AddDays(37),
            FinishStartedAt = geerntet.AddDays(-9),
            EndDate = geerntet,
            Light = "LED 480 W",
            ReservoirSize = "100 L",
            Notes = "Testdaten: abgeschlossener Lauf.",
        };

        grow.Id = grows.CreateGrow(grow);

        ernten.Create(new HarvestEntry
        {
            GrowId = grow.Id,
            // Reines Ortsdatum, kein UTC-Zeitpunkt.
            HarvestedAt = geerntet,
            WetWeightG = nass,
            DryWeightG = trocken,
            DryDays = 11,
            Rating = 4,
            YieldNotes = $"Testdaten: 4 Pflanzen, rund {Math.Round(trocken / 4)} g je Pflanze.",
            FlavorNotes = "Erdig, leicht süß.",
            EffectNotes = "Ruhig, körperbetont.",
            NugStructure = "Dicht.",
        });
    }
    /// <summary>Ein Ablauf, der gerade laeuft.</summary>
    /// <remarks>
    /// <para><b>Warum das noetig ist.</b> Der Bestand hatte keine einzige
    /// laufende Routine — <c>/sops</c> stand leer. Damit war der Knopf
    /// „Abbrechen" im Testbestand unsichtbar, und keine Oberflaechen-Pruefung
    /// konnte ihn je erreichen. Genau die Falle „der Testbestand ist eine
    /// Miniatur, das verdeckt Fehler" aus CLAUDE.md.</para>
    ///
    /// <para>Genommen wird der erste Ablauf der Bibliothek, nicht ein
    /// festverdrahteter Name: eine Kennung aus dem Kopf ist in diesem Projekt
    /// ueber 30-mal danebengegangen.</para>
    /// </remarks>
    private static void LaufendenAblaufAnlegen(IServiceProvider dienste, int growId)
    {
        var wissen = dienste.GetRequiredService<KnowledgeBaseLoader>();
        var ablauf = wissen.Sops.FirstOrDefault();
        if (ablauf is null) return;

        dienste.GetRequiredService<SopRepository>().StartSopInstance(
            growId, ablauf, SopStartSource.Manual, null, null, "Testdaten: laeuft gerade.");
    }

}
