using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Der Testbestand darf den eigenen Regeln der App nicht widersprechen.
/// </summary>
/// <remarks>
/// <para><b>Warum es diese Datei geben muss.</b> Bis zum 24.08.2026 hatte
/// <see cref="Demobestand"/> keinen einzigen Test — obwohl er die Grundlage
/// ist, gegen die alles andere geprüft wird: jede Oberflächen-Messung, jeder
/// E2E-Lauf, jeder Blick auf die laufende App. Ein Fehler <i>im Bestand</i>
/// verdeckt deshalb Fehler <i>in der App</i>, und genau das ist beim Kühler
/// schon passiert: die Testdaten trugen die Steckdose unter einer Kennung ein,
/// die der Regler im Betrieb nie gelesen hätte.</para>
///
/// <para><b>Was hier NICHT geprüft wird.</b> Nicht, ob die Zahlen hübsch sind.
/// Geprüft wird, ob der Bestand eine Geschichte erzählt, die Grow OS selbst für
/// möglich hält — mit denselben Prüfern, die auch auf echte Daten losgehen. Wo
/// die App eine Warnung ausgeben würde, ist der Bestand falsch, nicht die
/// Warnung.</para>
/// </remarks>
public sealed class DemobestandStimmigTests : IDisposable
{
    private readonly string _wurzel = Path.Combine(
        Path.GetTempPath(), "grow-os-demo-" + Guid.NewGuid().ToString("N"));

    private readonly ServiceProvider _dienste;
    private readonly GrowRepository _grows;

    public DemobestandStimmigTests()
    {
        Directory.CreateDirectory(_wurzel);
        var pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(pfade);

        // Zaehlung statt Liste: JEDE Ablage aus dem Betrieb wird eingetragen.
        // Eine handgeschriebene Liste haette hier bei jeder neuen Ablage einen
        // Testfehler erzeugt, der nichts mit dem Bestand zu tun hat — und beim
        // ersten Mal genau das getan.
        var sammlung = new ServiceCollection();
        sammlung.AddLogging();
        sammlung.AddSingleton(pfade);

        var ablagen = typeof(GrowRepository).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                        && t.Namespace == typeof(GrowRepository).Namespace
                        && t.Name.EndsWith("Repository", StringComparison.Ordinal))
            .ToList();

        Assert.True(ablagen.Count >= 10,
            $"Nur {ablagen.Count} Ablagen gefunden — die Reflexion sieht ihre Grundmenge nicht.");

        foreach (var typ in ablagen) sammlung.AddSingleton(typ);

        // Die Wissensbibliothek ist keine "Repository"-Ablage und faellt aus
        // der Reflexion — der Bestand braucht sie aber, seit er einen
        // laufenden Ablauf anlegt. Sie wird hier ausdruecklich eingetragen und
        // geladen, sonst stuende der Ablauf nur im Betrieb.
        sammlung.AddSingleton<KnowledgeBaseLoader>();
        _dienste = sammlung.BuildServiceProvider();
        _dienste.GetRequiredService<KnowledgeBaseLoader>().Initialize();

        _grows = _dienste.GetRequiredService<GrowRepository>();
        Assert.True(Demobestand.IstNoetig(_grows), "Eine frische Datenbank sollte leer sein.");
        Demobestand.Anlegen(_dienste);
    }

    public void Dispose()
    {
        _dienste.Dispose();
        try { Directory.Delete(_wurzel, recursive: true); } catch (IOException) { }
    }

    /// <summary>Der Haupt-Grow: der älteste laufende im ersten Zelt (White Widow).</summary>
    /// <remarks>
    /// Seit 02.10.2026 laufen ZWEI Grows gleichzeitig — der zweite im zweiten
    /// Blütezelt, damit die Kostenseite den Fall „mehrere Grows" überhaupt zeigt.
    /// Die Prüfungen hier gelten dem ersten, an dem Messungen, Pflanzen und
    /// Wasserwechsel hängen.
    /// </remarks>
    private GrowRun LaufenderGrow()
    {
        var laufend = Laufende();
        var erstesZelt = _grows.GetTents()[0];
        return laufend.Where(g => g.TentId == erstesZelt.Id).OrderBy(g => g.StartDate).First();
    }

    private List<GrowRun> Laufende()
    {
        var laufend = _grows.GetAllGrows().Where(g => g.Status == GrowStatus.Running).ToList();
        Assert.Equal(2, laufend.Count);
        return laufend;
    }

    /// <summary>Der Bestand legt überhaupt etwas an.</summary>
    /// <remarks>
    /// Der Mengenwächter für alles Folgende: liefe <see cref="Demobestand.Anlegen"/>
    /// still ins Leere, wären alle anderen Prüfungen hier grün, ohne etwas
    /// gesehen zu haben.
    /// </remarks>
    [Fact]
    public void Der_Bestand_legt_wirklich_etwas_an()
    {
        Assert.NotEmpty(_grows.GetTents());
        Assert.True(_grows.GetAllGrows().Count >= 3, "Ein laufender Grow und zwei im Archiv.");
        Assert.False(Demobestand.IstNoetig(_grows));
    }

    /// <summary>Der Lichtzyklus des Bestands passt zur Phase des Grows.</summary>
    /// <remarks>
    /// <para><b>Der Fund, aus dem diese Datei entstand.</b> Der Bestand fuhr
    /// 18/6 bei einem Grow, dessen Flip 35 Tage zurücklag. Genau dazu sagt
    /// <see cref="LightCycleLearner.Mismatch"/>: <i>„Der Grow ist in der Blüte,
    /// das Licht läuft aber 18/6. Das verhindert die Blüte."</i> Aufgefallen ist
    /// es niemandem, weil der Bestand keine Lichtflanken anlegt und der Lerner
    /// deshalb nie etwas zu vergleichen bekam — die Prüfung der App lief über
    /// ihre eigenen Testdaten nie.</para>
    /// </remarks>
    [Fact]
    public void Der_Lichtzyklus_passt_zur_Phase_des_Grows()
    {
        var grow = LaufenderGrow();
        var phase = GrowStageResolver.Resolve(grow, DateTime.Today);

        var tag = DateTime.Today.AddDays(-1);
        var anStunden = Enumerable.Range(0, 24)
            .Count(h => Demoverlauf.LichtBrennt(tag.AddHours(h).AddMinutes(30)));

        // Mengenwaechter: 0 oder 24 Stunden waeren keine Aussage ueber einen Zyklus.
        Assert.InRange(anStunden, 1, 23);

        var zyklus = new LearnedCycle(
            anStunden,
            new TimeOnly(Demoverlauf.LichtAn, 0),
            new TimeOnly(Demoverlauf.LichtAus % 24, 0),
            Days: 7);

        var beanstandung = LightCycleLearner.Mismatch(zyklus, phase, grow.SeedType);
        Assert.True(beanstandung is null,
            $"Der Testbestand widerspricht der eigenen Regel der App: {beanstandung}");
    }

    /// <summary>Lichtplan, Lichtkurve und Zeit-Entitäten nennen dieselbe Uhrzeit.</summary>
    /// <remarks>
    /// Drei Stellen, an denen dieselbe Uhrzeit steht — Lichtplan des Zelts,
    /// Kurvengenerator, <c>time.</c>-Entitäten für den AC-Test. Laufen sie
    /// auseinander, schlägt der Vorschlag im Versuchsaufbau eine Zeit vor, die
    /// nichts mit dem Licht zu tun hat, das der Bestand fährt.
    /// </remarks>
    [Fact]
    public void Lichtplan_und_Lichtkurve_nennen_dieselbe_Uhrzeit()
    {
        var zelt = _grows.GetTents()[0];
        var plan = _grows.GetActiveLightScheduleForTent(zelt.Id);

        Assert.NotNull(plan);
        Assert.Equal(Demoverlauf.LichtAnUhr, plan!.LightsOnTime);
        Assert.Equal(Demoverlauf.LichtAusUhr, plan.LightsOffTime);

        var ein = DemoData.EntityState(DemoData.LichtEinZeit, DateTime.UtcNow);
        var aus = DemoData.EntityState(DemoData.LichtAusZeit, DateTime.UtcNow);
        Assert.NotNull(ein);
        Assert.NotNull(aus);
        Assert.Equal(Demoverlauf.LichtAnUhr, AcTest.AlsHhMm(ein!.State));
        Assert.Equal(Demoverlauf.LichtAusUhr, AcTest.AlsHhMm(aus!.State));
    }

    /// <summary>Jede Entität, die der Bestand einträgt, antwortet auch.</summary>
    /// <remarks>
    /// <para><b>Der Kühler-Fehler in allgemeiner Form.</b> Der Bestand trug eine
    /// Steckdose ein, die im Betrieb unter einer anderen Kennung gesucht wurde —
    /// die Oberfläche zeigte trotzdem einen Zustand, weil die Testdaten ihn
    /// zusätzlich unter der Metrik-Kennung lieferten. Eine eingetragene Kennung,
    /// die auf dem <b>Betriebsweg</b> nichts zurückgibt, ist eine Kulisse.</para>
    ///
    /// <para>Geprüft wird über <see cref="DemoData.EntityState"/> — denselben
    /// Weg, den <c>GetEntityStateAsync</c> im Testbetrieb nimmt.</para>
    /// </remarks>
    [Fact]
    public void Jede_eingetragene_Entitaet_antwortet_auf_dem_Betriebsweg()
    {
        var einstellungen = _dienste.GetRequiredService<AppSettingsRepository>();
        var zelt = _grows.GetTents()[0];
        var geraete = AcTest.Lesen(einstellungen, zelt.Id);

        // Mengenwaechter: ohne Geraete prueft die Schleife nichts.
        Assert.NotEmpty(geraete);

        // Die Kennungen kommen aus dem, was WIRKLICH gespeichert ist — Zelt und
        // Geraete-Eintraege. Der erste Anlauf hat `DemoData.KuehlerSteckdose`
        // abgetippt: damit prueft der Test seine eigene Annahme statt den
        // Bestand. Ein Pruefer hat die Zeile im Bestand auf
        // "switch.demo_wasserkuehler" gesetzt — plausibel, aber falsch — und
        // alle 1381 Tests blieben gruen. Genau der Fehler, gegen den diese
        // Datei geschrieben ist.
        var ausGeraeten = geraete
            .SelectMany(g => new[] { g.LeistungEntityId, g.ModusEntityId, g.EinZeitEntityId, g.AusZeitEntityId });

        var ausZelt = new[] { zelt.ChillerSwitchEntityId, zelt.WaterTargetEntityId };

        var ausHardware = _dienste.GetRequiredService<HardwareRepository>()
            .GetHardwareItemsByTent(zelt.Id)
            .Select(h => h.HaEntityId);

        var kennungen = ausGeraeten.Concat(ausZelt).Concat(ausHardware)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(kennungen.Count >= 4, "Zu wenige Kennungen — die Zaehlung sieht ihre Grundmenge nicht.");

        var stumm = kennungen
            .Where(k => DemoData.EntityState(k, DateTime.UtcNow) is null)
            .ToList();

        Assert.True(stumm.Count == 0,
            "Der Bestand traegt Kennungen ein, die auf dem Betriebsweg nichts melden: "
            + string.Join(", ", stumm));
    }

    /// <summary>Jede Messgröße, für die es einen Wert gibt, ist auch zugeordnet.</summary>
    /// <remarks>
    /// <para><b>Der Fall, den niemand gesehen hat.</b> Auf der
    /// Home-Assistant-Seite stand im Testbetrieb <i>„Entities gemappt: 0 von
    /// 17"</i>, während die Live-Seite dreizehn Werte zeigte — der Testbestand
    /// lieferte alles ohne Zuordnung. Damit lief jede Prüfung am zugeordneten
    /// Weg vorbei: die Sensorliste am Zelt, das Alter eines Werts, die
    /// Warnungen über fehlende Zuordnungen.</para>
    ///
    /// <para>Die Zählung geht über die Aufzählung, nicht über eine Liste: was
    /// der Bestand liefert, muss zugeordnet sein.</para>
    /// </remarks>
    [Fact]
    public void Jede_gelieferte_Messgroesse_ist_dem_Zelt_zugeordnet()
    {
        var zelt = _grows.GetTents()[0];
        var zugeordnet = _grows.GetTentSensors(zelt.Id)
            .Select(s => TentSensorMetricKeyMap.Resolve(s.MetricType))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var geliefert = DemoData.StatesFor(DateTime.UtcNow).Keys
            .Where(k => Enum.GetValues<SensorMetricType>()
                .Any(a => string.Equals(TentSensorMetricKeyMap.Resolve(a), k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Mengenwaechter: ohne gelieferte Groessen prueft die Schleife nichts.
        Assert.True(geliefert.Count >= 8,
            $"Nur {geliefert.Count} Messgroessen im Testbestand — die Zaehlung sieht ihre Grundmenge nicht.");

        var fehlend = geliefert.Where(k => !zugeordnet.Contains(k)).ToList();
        Assert.True(fehlend.Count == 0,
            "Der Testbestand liefert Werte, die keinem Sensor am Zelt zugeordnet sind: "
            + string.Join(", ", fehlend));
    }

    /// <summary>Der Bestand legt zu jedem Zelt einen Lichtplan an.</summary>
    /// <remarks>
    /// Ohne ihn laufen Alarme (Tag/Nacht), Stromkosten und der Zeitplan-Vorschlag
    /// im Testbetrieb auf einer Ersatzannahme — also genau dort nicht, wo man
    /// sie ansehen kann.
    /// </remarks>
    [Fact]
    public void Jedes_Zelt_hat_einen_Lichtplan()
    {
        var zelte = _grows.GetTents();
        Assert.NotEmpty(zelte);

        var ohne = zelte
            .Where(z => _grows.GetActiveLightScheduleForTent(z.Id) is null)
            .Select(z => z.Name)
            .ToList();

        Assert.True(ohne.Count == 0, "Ohne Lichtplan: " + string.Join(", ", ohne));
    }

    /// <summary>Der Bestand erzählt den Mehrsorten-Fall — je Topf eine Pflanze.</summary>
    /// <remarks>
    /// <para><b>Der Anlass.</b> Ein Nutzer fährt im RDWC je Topf eine eigene
    /// Sorte und hat den Weg dafür nicht gefunden — auch deshalb, weil der
    /// Testbestand keine einzige Pflanze anlegte: die Karte „Pflanzen &amp;
    /// Sorten" zeigte in der Demo nur ihren Leerzustand, und kein Screenshot,
    /// kein E2E-Lauf und kein Blick auf die laufende App konnte den
    /// Mehrsorten-Weg je sehen.</para>
    /// </remarks>
    [Fact]
    public void Der_Bestand_erzaehlt_den_Mehrsorten_Fall()
    {
        var setups = _dienste.GetRequiredService<SetupRepository>();
        var grow = LaufenderGrow();
        var pflanzen = setups.GetPlantsByGrow(grow.Id);

        // Mengenwaechter: ohne Pflanzen prueft alles Weitere nichts.
        Assert.True(pflanzen.Count >= 3,
            $"Nur {pflanzen.Count} Pflanzen im Bestand — der Mehrsorten-Fall braucht mehrere.");

        // So viele Pflanzen, wie der Grow behauptet — sonst widerspricht sich
        // der Bestand selbst (PlantCount 4, aber 2 erfasst).
        Assert.Equal(grow.PlantCount, pflanzen.Count);

        // MEHRERE Sorten, jede Pflanze mit einer: das ist der gemeldete Fall.
        var sorten = pflanzen.Select(p => p.StrainId).Distinct().ToList();
        Assert.True(sorten.Count >= 2,
            "Alle Pflanzen tragen dieselbe Sorte — der Mehrsorten-Fall ist unsichtbar.");
        Assert.DoesNotContain(null, sorten);

        // Jede Pflanze in ihrem eigenen Topf, Nummern ab 1 im Bereich des
        // Systems — die Zaehlung der Draufsicht.
        var toepfe = pflanzen.Select(p => p.SiteIndex).ToList();
        Assert.DoesNotContain(null, toepfe);
        Assert.Equal(toepfe.Count, toepfe.Distinct().Count());
        Assert.All(toepfe, t => Assert.InRange(t!.Value, 1, grow.PlantCount ?? int.MaxValue));
    }

    /// <summary>
    /// Kein Wasserwechsel im Bestand liegt in der Zukunft.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026).</b> Der Bestand legte den jüngsten
    /// Wechsel auf „07:00 Ortszeit am Wechseltag". Startet die App an einem
    /// solchen Tag <b>vor</b> 07:00, liegt dieser Zeitpunkt in der Zukunft — und
    /// genau das verbietet das Formular (<c>max</c> am Datumsfeld). Der Bestand
    /// erzeugte damit Daten, die die App selbst ablehnen würde.</para>
    ///
    /// <para><b>Warum es niemand sah.</b> Der Stand klemmt mit
    /// <c>Math.Max(0, …)</c> auf „0 Tage / frisch" — der Fehler wird unsichtbar
    /// gerechnet. Gefunden vom Prüfer, der auf die Uhr geschaut hat.</para>
    ///
    /// <para>„Der Testbestand ist Produktionscode": wo Grow OS eine Warnung
    /// ausgeben würde, ist der Bestand falsch, nicht die Warnung.</para>
    /// </remarks>
    [Fact]
    public void KeinWasserwechselLiegtInDerZukunft()
    {
        var grow = LaufenderGrow();
        var wechsel = _grows.GetChangeoutsForGrow(grow.Id);

        // Mengenwaechter: ohne Eintraege liefe die Pruefung null Mal durch.
        Assert.True(wechsel.Count >= 3,
            $"Nur {wechsel.Count} Wasserwechsel im Bestand — die Seite /wasserwechsel "
            + "stuende damit fast leer da, und diese Pruefung liefe fast leer mit.");

        var jetzt = DateTime.UtcNow;
        var zukunft = wechsel.Where(w => w.PerformedAtUtc > jetzt).ToList();

        Assert.True(zukunft.Count == 0,
            "Diese Wasserwechsel im Testbestand liegen in der ZUKUNFT: "
            + string.Join(" | ", zukunft.Select(w => $"{w.PerformedAtUtc:O} (jetzt: {jetzt:O})"))
            + ". Das Formular laesst das nicht zu — der Bestand darf keine Daten erzeugen, "
            + "die die App selbst ablehnen wuerde.");

        // Und sie gehoeren zu diesem Grow und seinem System, nicht irgendwohin.
        Assert.All(wechsel, w => Assert.Equal(grow.Id, w.GrowId));
        Assert.All(wechsel, w => Assert.Equal(grow.SystemId, w.HydroSetupId));

        // Ein Wechsel, bei dem EC vorher und nachher gleich sind, behauptet,
        // er habe nichts bewirkt. Die erste Fassung des Bestands tat genau das.
        var wirkungslos = wechsel
            .Where(w => w.EcBefore is { } vor && w.EcAfter is { } nach && Math.Abs(vor - nach) < 0.05)
            .ToList();
        Assert.True(wirkungslos.Count == 0,
            $"{wirkungslos.Count} Wasserwechsel im Bestand aendern den EC nicht — "
            + "ein Wechsel, der nichts bewirkt, ist keine brauchbare Testlage.");
    }

    /// <summary>
    /// Die Sorten im Bestand haben unterschiedliche Blütezeiten.
    /// </summary>
    /// <remarks>
    /// Beide Demo-Sorten trugen 8–9 Wochen. Damit konnte die Warnung „die
    /// Sorten in diesem Becken brauchen unterschiedlich lange" gegen den
    /// Bestand <b>nie</b> auslösen — sie wäre ungeprüft ausgeliefert worden.
    /// Ein Bestand, an dem eine Funktion nicht sichtbar wird, verdeckt sie.
    /// Gefunden vom Prüfer.
    /// </remarks>
    [Fact]
    public void DieSortenHabenVerschiedeneBluetezeiten()
    {
        var setups = _dienste.GetRequiredService<SetupRepository>();
        var sorten = setups.GetStrains();

        Assert.True(sorten.Count >= 2, $"Nur {sorten.Count} Sorten im Bestand.");

        var wochen = sorten
            .Select(s => s.FlowerWeeksMax ?? s.FlowerWeeksMin)
            .Where(w => w is > 0)
            .Select(w => w!.Value)
            .ToList();

        Assert.True(wochen.Count >= 2, "Weniger als zwei Sorten mit Bluetewochen.");
        Assert.True(wochen.Max() - wochen.Min() >= 2,
            $"Alle Sorten liegen zwischen {wochen.Min()} und {wochen.Max()} Bluetewochen. "
            + "Die Warnung ueber unterschiedliche Bluetezeiten (Schwelle: 2 Wochen) kann "
            + "gegen diesen Bestand nie auslosen — sie waere ungeprueft.");
    }

    /// <summary>
    /// Die Regel selbst: ein gesäter Wechsel liegt nie in der Zukunft.
    /// </summary>
    /// <remarks>
    /// <para>Die Prüfung über den fertigen Bestand darüber fängt den Fehler nur
    /// in einem Zeitfenster — läuft sie nach 07:00 an einem Wechseltag, wäre
    /// derselbe kaputte Code grün. Der Prüfer hat darauf hingewiesen: „der
    /// Bissnachweis gilt, aber die Prüfung fängt den Fehler nur in einem
    /// Zeitfenster."</para>
    ///
    /// <para><b>Und die erste Fassung dieser Prüfung war im Tor rot.</b> Sie
    /// schrieb die erwarteten Zeitpunkte in UTC aus — gerechnet mit „Ortszeit
    /// ist UTC+2", weil mein Rechner in MESZ steht. Der Runner läuft in UTC,
    /// dort kam 07:00 statt 05:00 heraus. Dieselbe Klasse wie „mein Rechner ist
    /// nicht die Anlage" in CLAUDE.md, nur für die Uhr statt für die Schrift.
    /// Geprüft werden deshalb die <b>Eigenschaften</b> der Regel, nicht
    /// ausgerechnete Zeitpunkte — die gelten in jeder Zeitzone.</para>
    /// </remarks>
    [Theory]
    // Der Wechseltag liegt lange zurueck: der geplante Zeitpunkt ist laengst
    // vorbei und gilt unveraendert.
    [InlineData(-7, 0)]
    // Heute, und 07:00 Ortszeit ist schon vorbei: gilt ebenfalls.
    [InlineData(0, 12)]
    // Heute, aber es ist erst kurz nach Mitternacht: 07:00 laege in der
    // Zukunft, also gilt jetzt.
    [InlineData(0, -6)]
    public void WechselZeitpunkt_LiegtNieInDerZukunft(int tageZurueck, int stundenVersatz)
    {
        var tag = DateTime.Today.AddDays(tageZurueck);
        // „Jetzt" wird relativ zum geplanten Zeitpunkt gesetzt, nicht als feste
        // Uhrzeit — sonst haengt der Fall an der Zeitzone des Rechners.
        var geplant = tag.AddHours(7).ToUniversalTime();
        var jetzt = geplant.AddHours(stundenVersatz);

        var ergebnis = Demobestand.WechselZeitpunkt(tag, jetzt);

        Assert.True(ergebnis <= jetzt,
            $"Der Zeitpunkt {ergebnis:O} liegt nach {jetzt:O} — also in der Zukunft.");

        if (geplant < jetzt)
        {
            Assert.Equal(geplant, ergebnis);
        }
        else
        {
            Assert.Equal(jetzt, ergebnis);
        }
    }

    /// <summary>
    /// Der Bestand hat einen Mutter- und einen Quarantäne-Bereich — mit Pflanzen,
    /// im passenden Zelt, und die App nimmt beide so an, wie sie sind.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.10.2026).</b> <c>GET /api/setups</c> lieferte im
    /// Testbestand <c>[]</c>; die Bereichs-Karte der Zeltseite rendert dann nie.
    /// Ihre rohen Wörter wurden repariert, ohne dass jemand sie ansehen konnte.</para>
    ///
    /// <para><b>Gegen die Regeln der App geprüft, nicht gegen eine Liste:</b>
    /// die Zelt-Verträglichkeit über <see cref="SetupTentCompatibilityPolicy"/>,
    /// die Feldwerte über <see cref="SetupsApiController.Update"/> — wer dort
    /// abgelehnt würde, ist im Bestand falsch. Das Licht über
    /// <see cref="LightCycleLearner.Mismatch"/>: eine Mutter, die unter 12/12
    /// steht, geht in die Blüte.</para>
    /// </remarks>
    [Fact]
    public void Der_Bestand_hat_Mutter_und_Quarantaene_Bereich_im_passenden_Zelt()
    {
        var bereiche = _grows.GetSetups();
        var muetter = bereiche.Where(b => b.SetupType == SetupType.Mother).ToList();
        var quarantaenen = bereiche.Where(b => b.SetupType == SetupType.Quarantine).ToList();

        // Mengenwaechter: ohne Bereiche prueft alles Weitere nichts — und die
        // Karte auf der Zeltseite stuende wieder nur im Leerzustand da.
        Assert.True(muetter.Count >= 1, "Kein Mutter-Bereich im Bestand — die Bereichs-Karte rendert nie.");
        Assert.True(quarantaenen.Count >= 1, "Kein Quarantäne-Bereich im Bestand — die Bereichs-Karte rendert nie.");

        var steuerung = new SetupsApiController(_grows);
        foreach (var bereich in muetter.Concat(quarantaenen))
        {
            var zelt = _grows.GetTent(bereich.TentId);
            Assert.NotNull(zelt);
            Assert.True(SetupTentCompatibilityPolicy.IsCompatible(zelt!.TentType, bereich.SetupType),
                $"„{bereich.Name}“ ({bereich.SetupType}) steht in „{zelt.Name}“ ({zelt.TentType}) — das lässt die App nicht zu.");
            Assert.Equal(SetupStatus.Active, bereich.Status);

            // Dieselben Werte, unverändert zurückgeschickt: die App muss sie annehmen.
            var antwort = steuerung.Update(bereich.Id, new UpdateSetupRequest
            {
                Name = bereich.Name,
                Status = bereich.Status,
                Notes = bereich.Notes,
                CloneCounterTotal = bereich.CloneCounterTotal,
                LastCloneCutAt = bereich.LastCloneCutAt,
                MotherHealthStatus = bereich.MotherHealthStatus,
                QuarantineStartedAt = bereich.QuarantineStartedAt,
                QuarantinePlannedEndAt = bereich.QuarantinePlannedEndAt,
                QuarantineResult = bereich.QuarantineResult,
            });
            Assert.True(antwort.Result is OkObjectResult,
                $"Die App lehnt „{bereich.Name}“ ab, so wie der Bestand ihn angelegt hat: {antwort.Result}");

            var pflanzen = _grows.GetPlantsBySetup(bereich.Id);
            Assert.True(pflanzen.Count >= 1, $"„{bereich.Name}“ hat keine Pflanzen.");
            Assert.All(pflanzen, p => Assert.Equal(PlantStatus.Active, p.PlantStatus));

            // Das Licht passt zu Pflanzen, die vegetativ bleiben sollen.
            var plan = _grows.GetActiveLightScheduleForTent(zelt.Id);
            Assert.NotNull(plan);
            var an = TimeOnly.Parse(plan!.LightsOnTime, CultureInfo.InvariantCulture);
            var aus = TimeOnly.Parse(plan.LightsOffTime, CultureInfo.InvariantCulture);
            var stunden = (aus - an).TotalHours;
            var zyklus = new LearnedCycle(stunden, an, aus, Days: 7);
            var phase = bereich.SetupType == SetupType.Mother ? GrowStage.Veg : GrowStage.Clone;
            var beanstandung = LightCycleLearner.Mismatch(zyklus, phase, SeedType.Feminized);
            Assert.True(beanstandung is null,
                $"Licht in „{zelt.Name}“ widerspricht der eigenen Regel der App: {beanstandung}");
        }

        // Mutter: Pflanzen mit der Rolle Mutter, und der Stecklingszaehler
        // stimmt mit den Stecklingen ueberein, die wirklich dastehen.
        var alle = _grows.GetPlants();
        foreach (var bereich in muetter)
        {
            var mutterpflanzen = _grows.GetPlantsBySetup(bereich.Id);
            Assert.All(mutterpflanzen, p => Assert.Equal(PlantRole.Mother, p.PlantRole));
            var ids = mutterpflanzen.Select(p => p.Id).ToHashSet();
            var stecklinge = alle.Where(p => p.ParentPlantId is { } eltern && ids.Contains(eltern)).ToList();

            Assert.True(stecklinge.Count >= 1, $"„{bereich.Name}“ hat keinen Steckling geschnitten — „Stecklinge“ und „Schnitt“ stünden auf „–“.");
            Assert.Equal(stecklinge.Count, bereich.CloneCounterTotal);
            Assert.NotNull(bereich.LastCloneCutAt);
            Assert.True(bereich.LastCloneCutAt <= DateTime.Now, "Der letzte Schnitt liegt in der Zukunft.");
            Assert.False(string.IsNullOrWhiteSpace(bereich.MotherHealthStatus));
        }

        // Quarantaene: laeuft gerade. Ein offenes Ergebnis nach dem geplanten
        // Ende waere ein vergessener Bereich, keine laufende Pruefung.
        foreach (var bereich in quarantaenen)
        {
            Assert.NotNull(bereich.QuarantineStartedAt);
            Assert.NotNull(bereich.QuarantinePlannedEndAt);
            Assert.True(bereich.QuarantineStartedAt <= DateTime.Now, "Die Quarantäne beginnt in der Zukunft.");
            Assert.False(string.IsNullOrWhiteSpace(bereich.QuarantineResult));
            if (string.Equals(bereich.QuarantineResult, "Pending", StringComparison.Ordinal))
            {
                Assert.True(bereich.QuarantinePlannedEndAt > DateTime.Now,
                    "Ergebnis offen, aber das geplante Ende ist vorbei — so sähe ein vergessener Bereich aus.");
            }

            // Der Fall „ohne Sorte“ gehört auf die Karte: ein Zugang von außen.
            Assert.Contains(_grows.GetPlantsBySetup(bereich.Id), p => p.StrainId is null);
        }
    }

    /// <summary>
    /// Ein Artikel zeigt einen gemessenen Füllstand samt Prognose.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.10.2026).</b> Der Bestand hatte keinen
    /// Verbrauchsartikel; der Füllstand-Balken der Kostenseite („Noch … %",
    /// „aus dem gebuchten Verbrauch") rendert ohne gebuchten Verbrauch nie.</para>
    ///
    /// <para>Gerechnet wird mit <see cref="KostenSeiteService.Berechnen"/> —
    /// derselben Rechnung, die <c>GET /api/kosten</c> ausliefert. Eine eigene
    /// Nachrechnung hier prüfte nur sich selbst.</para>
    /// </remarks>
    /// <summary>
    /// Zwei Grows laufen gleichzeitig am selben Zähler — und die Kostenseite
    /// teilt ihn, statt alles dem älteren zu geben.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (02.10.2026).</b> Der Bestand hatte einen laufenden
    /// Grow und keine Strom-Quelle. Dass ein zweiter laufender Grow nie Strom
    /// bekam, war gegen ihn unsichtbar.</para>
    /// <para>Die Kennungen kommen aus der gespeicherten Einstellung, nicht aus
    /// <see cref="DemoData"/> abgetippt — sonst prüfte der Test seine eigene
    /// Annahme.</para>
    /// </remarks>
    [Fact]
    public void Zwei_laufende_Grows_teilen_sich_den_Zaehler()
    {
        var einstellungen = _dienste.GetRequiredService<AppSettingsRepository>();
        var kosten = _dienste.GetRequiredService<KostenRepository>();
        var quelle = KostenSeiteService.StromQuelleLesen(einstellungen);
        var jetzt = DateTime.UtcNow;

        // Die eingetragenen Entitäten antworten auf dem Betriebsweg.
        Assert.False(string.IsNullOrWhiteSpace(quelle.ZaehlerEntityId), "Keine Strom-Quelle im Bestand.");
        var zaehler = DemoData.EntityState(quelle.ZaehlerEntityId!, jetzt);
        Assert.True(zaehler?.NumericValue is not null, $"{quelle.ZaehlerEntityId} meldet keinen Zahlenwert.");
        Assert.True(quelle.LeistungEntityId is null || DemoData.EntityState(quelle.LeistungEntityId, jetzt)?.NumericValue is not null,
            $"{quelle.LeistungEntityId} meldet keinen Zahlenwert.");

        var staende = kosten.GetZaehlerstaende();
        Assert.True(staende.Count >= 100, $"Nur {staende.Count} Zählerstände — die Kostenseite hätte kaum etwas zu rechnen.");

        // Der Zähler läuft nie rückwärts — auch nicht zwischen dem letzten
        // abgelegten Stand und dem, was der Worker gleich liest. Ein Rücksprung
        // zählte als Zählerwechsel mit vollem Stand.
        var reihe = staende.OrderBy(s => s.ZeitpunktUtc).Select(s => s.Kwh).Append(zaehler!.NumericValue!.Value).ToList();
        Assert.All(reihe.Zip(reihe.Skip(1)), p => Assert.True(p.Second >= p.First, $"Rücksprung {p.First} → {p.Second} kWh."));

        var alle = _grows.GetAllGrows();
        var laufend = Laufende();
        foreach (var grow in laufend)
        {
            var strom = KostenSeiteService.StromBerechnen(grow, alle, quelle, null, null, staende, jetzt);
            var andere = laufend.Single(g => g.Id != grow.Id);

            Assert.True(strom.KwhSeitStart is > 0, $"{grow.Name}: kein Strom — {strom.Hinweis}");
            Assert.True(strom.GeteiltTage >= 30, $"{grow.Name}: nur {strom.GeteiltTage} geteilte Tage.");
            // Geteilt NUR mit dem anderen laufenden — ein abgeschlossener Lauf,
            // der noch in die Laufzeit ragt, wäre ein Fehler im Bestand.
            var mit = Assert.Single(strom.GeteiltMit!);
            Assert.Equal(andere.Id, mit.GrowId);
            Assert.Contains(andere.Name, strom.TeilungHinweis);
        }

        // Kein Lauf im Archiv überlappt einen anderen im selben Zelt.
        var abgeschlossen = alle.Where(g => g.EndDate is not null).ToList();
        Assert.True(abgeschlossen.Count >= 2, "Mengenwächter: zwei Läufe im Archiv erwartet.");
        foreach (var lauf in abgeschlossen)
        {
            var ueberlappt = alle.Where(g => g.Id != lauf.Id && g.TentId == lauf.TentId
                && g.StartDate.Date <= lauf.EndDate!.Value.Date
                && (g.EndDate ?? DateTime.Today).Date >= lauf.StartDate.Date).Select(g => g.Name).ToList();
            Assert.True(ueberlappt.Count == 0, $"„{lauf.Name}“ läuft gleichzeitig mit {string.Join(", ", ueberlappt)} im selben Zelt.");
        }
    }

    [Fact]
    public void Ein_Artikel_hat_gemessenen_Fuellstand_mit_Prognose()
    {
        var kosten = _dienste.GetRequiredService<KostenRepository>();
        var hardware = _dienste.GetRequiredService<HardwareRepository>();
        var jetzt = DateTime.UtcNow;

        var seite = KostenSeiteService.Berechnen(
            LaufenderGrow(), _grows.GetAllGrows(), new StromQuelle(), null, null,
            kosten.GetZaehlerstaende(), kosten.GetArtikel(), kosten.GetNachfuellungen(),
            kosten.GetAnschaffungen(), jetzt, hardware.GetHardwareItems(), kosten.GetVerbraeuche());

        // Mengenwaechter: ohne Artikel liefe die Pruefung null Mal.
        Assert.True(seite.Artikel.Count >= 1, "Kein Verbrauchsartikel im Bestand.");

        var gemessen = seite.Artikel
            .Where(a => a.Aktuell is { FuellstandQuelle: "gemessen", FuellstandProzent: not null, PrognoseLeerAmUtc: not null })
            .ToList();
        Assert.True(gemessen.Count >= 1,
            "Kein Artikel mit gemessenem Füllstand und Prognose — der Balken auf der Kostenseite rendert nie. "
            + "Artikel: " + string.Join(" | ", seite.Artikel.Select(a =>
                $"{a.Name}: Quelle={a.Aktuell?.FuellstandQuelle}, Füllstand={a.Aktuell?.FuellstandProzent}, Prognose={a.Aktuell?.PrognoseLeerAmUtc:O}")));

        foreach (var artikel in gemessen)
        {
            var aktuell = artikel.Aktuell!;
            // Weder fast voll noch fast leer: beides waere als Anzeige-Lage wertlos,
            // und „fast leer" loeste im Betrieb sofort ein Nachkaufen aus.
            Assert.InRange(aktuell.FuellstandProzent!.Value, 15, 85);
            Assert.True(aktuell.PrognoseLeerAmUtc > jetzt, $"{artikel.Name}: die Prognose liegt in der Vergangenheit.");
            // Eine Prognose, die weiter reicht als ein Jahr, ist Hochrechnung aus
            // zu wenig — genau das, wovor die Seite selbst warnt.
            Assert.True(aktuell.PrognoseLeerAmUtc < jetzt.AddDays(365),
                $"{artikel.Name}: leer erst am {aktuell.PrognoseLeerAmUtc:d} — keine Prognose, die jemand ernst nimmt.");
            Assert.True(artikel.MittlereLaufzeitTage is > 0,
                $"{artikel.Name}: keine abgeschlossene Füllung — „Ø … Tage“ stünde leer.");
        }
    }

    /// <summary>
    /// Fork AI (02.10.2026): im Bestand läuft eine Phase länger als ihr Programm —
    /// sonst wäre die angehängte Woche nirgends zu sehen.
    /// </summary>
    /// <remarks>
    /// <para>Rein gerechnet, ohne den Plan-Dienst: dessen Register ist prozessweit,
    /// und die kleinen Ids des Bestands würden parallel laufende Tests mit einem
    /// fremden Plan versorgen. Der Plan entsteht wie in der Testdaten-App aus
    /// <see cref="Demobestand.Programm"/>.</para>
    /// <para>Verlangt werden beide Fälle, die die Oberfläche zeigen muss: ein Lauf,
    /// dessen LAUFENDE Woche verlängert ist (Wochenzeile „läuft · verlängert"), und
    /// einer mit mindestens zwei verlängerten Wochen — der Rundweg im Plan prüft die
    /// erste und die letzte (<c>e2e/verlaengerte-woche.spec.ts</c>).</para>
    /// </remarks>
    [Fact]
    public void Ein_Lauf_dauert_laenger_als_sein_Programm()
    {
        // Die Wissensbibliothek dieses Tests ist leer (kein Wissensordner); das
        // Programm kommt deshalb aus der ausgelieferten Datei, die auch die App lädt.
        var programm = MitgeliefertesProgramm(Demobestand.Programm);
        var ziele = new TargetValueService(_dienste.GetRequiredService<KnowledgeBaseLoader>());
        var laufend = Laufende();
        Assert.True(laufend.Count >= 2, "Mengenwächter: zwei laufende Grows erwartet.");

        var laufendeWocheVerlaengert = new List<string>();
        var mehrereVerlaengert = new List<string>();
        foreach (var grow in laufend)
        {
            var inhalt = GrowDiary.Web.Services.GrowPlan.GrowPlanBauer.AusProgramm(
                programm, stufe => ziele.GetTargets(TargetValueService.ProfileIdFor(grow.HydroStyle), stufe),
                GrowDiary.Web.Services.GrowPlan.GrowPlanService.VegiWochen(grow), GrowDiary.Web.Services.GrowPlan.GrowPlanService.Bluetewochen(grow));
            var neu = GrowDiary.Web.Services.GrowPlan.Planwochen.Anhaengen(inhalt, Phasenanker.Fuer(grow, DateTime.Today));

            var jetzt = MischplanService.SpalteFuer(inhalt.Chart, grow, DateTime.Today);
            if (jetzt is not null && inhalt.IstVerlaengert(jetzt.Id)) laufendeWocheVerlaengert.Add($"{grow.Name}: {jetzt.Label}");
            if (neu.Count >= 2) mehrereVerlaengert.Add($"{grow.Name}: {string.Join(", ", neu.Select(n => n.Neu.Label))}");
        }

        Assert.True(laufendeWocheVerlaengert.Count >= 1,
            "Kein laufender Grow steht in einer verlängerten Woche — die Markierung „verlängert“ an der laufenden Woche "
            + "wäre im Testbestand nie zu sehen.");
        Assert.True(mehrereVerlaengert.Count >= 1,
            "Kein laufender Grow hat zwei verlängerte Wochen — der Rundweg an der ersten und letzten fiele zusammen.");
    }

    private static GrowDiary.Web.Services.Knowledge.Schema.NutrientProgramDefinition MitgeliefertesProgramm(string id)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) dir = Path.GetDirectoryName(dir);
        var ordner = Path.Combine(dir ?? throw new InvalidOperationException("Projektwurzel nicht gefunden."),
            "GrowDiary.Web", "wwwroot", "knowledge-defaults", "nutrient-programs");
        var programme = Directory.EnumerateFiles(ordner, "*.json")
            .Select(datei => System.Text.Json.JsonSerializer.Deserialize<GrowDiary.Web.Services.Knowledge.Schema.NutrientProgramDefinition>(File.ReadAllText(datei)))
            .ToList();
        Assert.True(programme.Count >= 2, $"Nur {programme.Count} Programme gefunden — die Prüfung sähe ihre Grundmenge nicht.");
        return programme.Single(p => p?.Id == id)!;
    }
}
