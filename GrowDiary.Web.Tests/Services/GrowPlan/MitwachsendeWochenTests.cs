using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services.GrowPlan;

/// <summary>
/// Fork AI (02.10.2026): läuft eine Phase länger als der Plan, bekommt der Plan
/// des Grows eigene Wochen — statt die letzte Spalte stumm zu halten.
/// </summary>
/// <remarks>
/// Entscheidung des Nutzers: „erstreckt sich die Blüte über zehn Wochen, zeigt
/// das Schema auch zehn Wochen. Genauso in der Bewurzelungsphase und in der
/// vegetativen Phase." Die neue Woche übernimmt die Werte der letzten Woche der
/// Phase, ist als verlängert markiert, bearbeitbar und steht im Änderungsbuch.
/// Geprüft an den mitgelieferten Programmen: Athena (neun Blütewochen, zwei
/// Klon-Schritte) und SKX (vier Vegi-, acht Blütewochen, eine Bewurzelung).
/// </remarks>
public sealed class MitwachsendeWochenTests : IDisposable
{
    private static readonly DateTime Heute = new(2026, 10, 2);

    private readonly string _wurzel;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly GrowPlanRepository _repo;
    private readonly GrowPlanService _dienst;
    private readonly TargetValueService _ziele;

    // Hohe Ids: das Register ist prozessweit und wird von den Integrationstests mitbenutzt.
    private const int Basis = 910_000;
    private readonly List<int> _benutzt = [];

    public MitwachsendeWochenTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Mitwachsend_" + Guid.NewGuid().ToString("N"));
        var pfade = new AppPaths(_wurzel);
        KopiereWissen(_wurzel);
        _wissen = new KnowledgeBaseLoader(pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        _wissen.Initialize();
        _repo = new GrowPlanRepository(pfade);
        _ziele = new TargetValueService(_wissen);
        _dienst = new GrowPlanService(_repo, _wissen, _ziele, NullLogger<GrowPlanService>.Instance,
            new EigeneProgramme(pfade, _wissen, NullLogger<EigeneProgramme>.Instance));
    }

    public void Dispose()
    {
        foreach (var id in _benutzt) GrowPlanRegister.Entfernen(id);
        try { Directory.Delete(_wurzel, recursive: true); } catch { }
    }

    /// <summary>Ein Samen-Grow ohne Anzucht-Überhang: Vegi ab Start, Blüte ab <paramref name="flipVorTagen"/>.</summary>
    private GrowRun Bluehend(int nummer, string programm, int flipVorTagen, int vegiTage = 28)
    {
        _benutzt.Add(Basis + nummer);
        var start = Heute.AddDays(-(flipVorTagen + vegiTage));
        return new GrowRun
        {
            Id = Basis + nummer,
            Name = $"Test {nummer}",
            FeedProgramId = programm,
            HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running,
            StartMaterial = StartMaterial.Seed,
            EntryPoint = GrowEntryPoint.Germination,
            StartDate = start,
            GerminatedAt = start,
            VegStartedAt = start,
            FlipDate = Heute.AddDays(-flipVorTagen),
        };
    }

    private GrowPlanInhalt Arbeit(int growId) => _repo.Laden(growId, GrowPlanStaende.Arbeit)!.Inhalt;

    private static FeedChartColumn Spalte(GrowPlanInhalt inhalt, string id) => inhalt.Chart.Columns.Single(c => c.Id == id);

    [Fact]
    public void Bluetewoche10EntstehtGenauEinmalMitDenWertenVonWoche9()
    {
        // Athena führt neun Blütewochen; Tag 64 nach dem Flip ist Blütewoche 10.
        var grow = Bluehend(1, "athena", flipVorTagen: 63);
        _dienst.Anlegen(grow);
        var w9 = Spalte(Arbeit(grow.Id), "flower-w9");

        Assert.Equal(1, _dienst.WochenNachziehen(grow, Heute));

        var arbeit = Arbeit(grow.Id);
        var w10 = Assert.Single(arbeit.Chart.Columns, c => c.Stage == "Flower" && c.Week == 10);
        Assert.Equal("flower-w10", w10.Id);
        Assert.Equal("Blütewoche 10", w10.Label);
        Assert.True(arbeit.IstVerlaengert(w10.Id));
        Assert.Equal("flower-w9", arbeit.Verlaengert[w10.Id]);
        foreach (var feld in Wochenwertfelder.Alle)
        {
            Assert.Equal(feld.Lesen(w9), feld.Lesen(w10));
        }
        Assert.Equal(w9.Items.Select(i => (i.Component, i.MinMlPerLiter)), w10.Items.Select(i => (i.Component, i.MinMlPerLiter)));
        // Direkt hinter Woche 9, vor dem Flush.
        Assert.Equal(arbeit.Chart.Columns.FindIndex(c => c.Id == "flower-w9") + 1, arbeit.Chart.Columns.IndexOf(w10));

        var eintrag = Assert.Single(_repo.Buch(grow.Id), e => e.Art == GrowPlanArten.Verlaengert);
        Assert.Equal("flower-w10", eintrag.SpalteId);
        Assert.Equal("flower-w9", eintrag.Alt);

        // Der Mischplan wählt sie — keine gehaltene Woche 9 mehr.
        Assert.Equal("flower-w10", MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms, Heute)?.Spalte.Id);
    }

    [Fact]
    public void EinZweiterLaufAendertNichts()
    {
        var grow = Bluehend(2, "athena", flipVorTagen: 63);
        _dienst.Anlegen(grow);
        _dienst.WochenNachziehen(grow, Heute);
        var vorher = Arbeit(grow.Id).Chart.Columns.Count;
        var buch = _repo.Buch(grow.Id).Count;

        Assert.Equal(0, _dienst.WochenNachziehen(grow, Heute));
        Assert.Equal(0, _dienst.AlleNachziehen([grow], Heute));
        Assert.Equal(vorher, Arbeit(grow.Id).Chart.Columns.Count);
        Assert.Equal(buch, _repo.Buch(grow.Id).Count);
    }

    [Fact]
    public void Woche11KommtEineWocheSpaeterUndUebernimmtWoche10()
    {
        var grow = Bluehend(3, "athena", flipVorTagen: 63);
        _dienst.Anlegen(grow);
        _dienst.WochenNachziehen(grow, Heute);
        // Woche 10 wurde im Plan geändert — Woche 11 übernimmt das.
        _dienst.WerteSetzen(grow.Id, [("flower-w10", "ecTarget", 1.6)]);

        Assert.Equal(0, _dienst.WochenNachziehen(grow, Heute.AddDays(6)));
        Assert.Equal(1, _dienst.WochenNachziehen(grow, Heute.AddDays(7)));

        var arbeit = Arbeit(grow.Id);
        var w11 = Spalte(arbeit, "flower-w11");
        Assert.Equal("Blütewoche 11", w11.Label);
        Assert.Equal(1.6, w11.EcTarget);
        Assert.Equal("flower-w10", arbeit.Verlaengert["flower-w11"]);
        Assert.Equal("flower-w9", arbeit.Programmwoche("flower-w11"));
        Assert.Equal("flower-w11", MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms, Heute.AddDays(7))?.Spalte.Id);
    }

    [Fact]
    public void DerStartstandBleibtUndZurueckAufPlanNimmtDieLetzteProgrammwoche()
    {
        var grow = Bluehend(4, "athena", flipVorTagen: 63);
        _dienst.Anlegen(grow);
        var startVorher = _repo.Laden(grow.Id, GrowPlanStaende.Start)!.Inhalt.Chart.Columns.Count;
        var feld = Wochenwertfelder.Finden("ecTarget")!;
        var startW9 = _dienst.Startwert(grow.Id, "flower-w9", feld);
        _dienst.WerteSetzen(grow.Id, [("flower-w9", "ecTarget", 1.95)]);

        _dienst.WochenNachziehen(grow, Heute);

        Assert.Equal(startVorher, _repo.Laden(grow.Id, GrowPlanStaende.Start)!.Inhalt.Chart.Columns.Count);
        var arbeit = Arbeit(grow.Id);
        // Die Änderung an Woche 9 geht mit — und ist in Woche 10 eine eigene.
        Assert.Equal(1.95, Spalte(arbeit, "flower-w10").EcTarget);
        Assert.Equal(GrowPlanHerkunft.Eigen, arbeit.HerkunftVon("flower-w10", "ecTarget"));
        Assert.Equal(startW9, _dienst.Startwert(grow.Id, "flower-w10", feld));

        // „zurück" (Wert null) setzt den Startwert der Programmwoche, nicht nichts.
        _dienst.WerteSetzen(grow.Id, [("flower-w10", "ecTarget", null)]);
        arbeit = Arbeit(grow.Id);
        Assert.Equal(startW9, Spalte(arbeit, "flower-w10").EcTarget);
        Assert.NotEqual(GrowPlanHerkunft.Eigen, arbeit.HerkunftVon("flower-w10", "ecTarget"));
    }

    [Fact]
    public void EineVerlaengerteWocheOhneAenderungIstKeineEigeneAenderung()
    {
        var grow = Bluehend(5, "athena", flipVorTagen: 63);
        _dienst.Anlegen(grow);
        _dienst.WochenNachziehen(grow, Heute);

        // Die Dosierung der neuen Woche gleicht der ihrer Programmwoche.
        Assert.Equal(0, _dienst.EigeneAenderungen(grow.Id));
    }

    [Fact]
    public void EinEingefrorenerPlanBleibtUnveraendert()
    {
        var grow = Bluehend(6, "athena", flipVorTagen: 63);
        _dienst.Anlegen(grow);
        grow.Status = GrowStatus.Completed;
        Assert.Equal(GrowPlanArten.Eingefroren, _dienst.Abgleichen(grow));
        var spalten = Arbeit(grow.Id).Chart.Columns.Count;

        Assert.Equal(0, _dienst.WochenNachziehen(grow, Heute));
        Assert.Equal(spalten, Arbeit(grow.Id).Chart.Columns.Count);
        Assert.Equal(spalten, _repo.Laden(grow.Id, GrowPlanStaende.Ende)!.Inhalt.Chart.Columns.Count);

        // Auch wenn nur der Endstand die Sperre trägt (Status inzwischen anders gelesen).
        grow.Status = GrowStatus.Running;
        Assert.Equal(0, _dienst.WochenNachziehen(grow, Heute));
        Assert.Equal(spalten, Arbeit(grow.Id).Chart.Columns.Count);
    }

    [Fact]
    public void DieAnzuchtSpalteIstWoche1UndWirdAbWoche2Fortgeschrieben()
    {
        // Samen, Tag 16 der Anzucht, Vegi nicht bestätigt: Anzucht 3 (beim Samen
        // heißt die Phase „Anzucht", GrowPlanBauer.Anzuchtname).
        _benutzt.Add(Basis + 7);
        var grow = new GrowRun
        {
            Id = Basis + 7, Name = "Anzucht", FeedProgramId = "skx-canna-aqua", HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running, StartMaterial = StartMaterial.Seed, EntryPoint = GrowEntryPoint.Germination,
            StartDate = Heute.AddDays(-15), GerminatedAt = Heute.AddDays(-15),
        };
        _dienst.Anlegen(grow);
        var bewurzelung = Spalte(Arbeit(grow.Id), "root");

        Assert.Equal(2, _dienst.WochenNachziehen(grow, Heute));

        var arbeit = Arbeit(grow.Id);
        var anzucht = arbeit.Chart.Columns.Take(3).ToList();
        Assert.Equal(["root", "clone-w2", "clone-w3"], anzucht.Select(c => c.Id));
        Assert.Equal(["Anzucht", "Anzucht 2", "Anzucht 3"], anzucht.Select(c => c.Label));
        Assert.All(anzucht.Skip(1), c => Assert.Equal("Clone", c.Stage));
        Assert.Equal(bewurzelung.EcTarget, anzucht[2].EcTarget);
        Assert.Equal("root", arbeit.Programmwoche("clone-w3"));

        // Woche für Woche: die Anzucht-Spalte in Woche 1, danach die angehängten.
        Assert.Equal("root", MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms, Heute.AddDays(-10))?.Spalte.Id);
        Assert.Equal("clone-w2", MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms, Heute.AddDays(-5))?.Spalte.Id);
        Assert.Equal("clone-w3", MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms, Heute)?.Spalte.Id);
    }

    /// <summary>
    /// Fork AI (02.10.2026): gespeicherte Pläne heißen nach dem Start so, wie ein
    /// neuer Plan hieße — Samen „Anzucht"/„Anzucht 2", Steckling
    /// „Bewurzelung"/„Bewurzelung 2". Der eingefrorene Endstand bleibt, wie er war
    /// (Begründung an <see cref="GrowPlanService.FehlendeFelderNachtragen"/>).
    /// </summary>
    [Theory]
    [InlineData(StartMaterial.Seed, "Bewurzelung", "Bewurzelung 2", "Anzucht", "Anzucht 2")]
    [InlineData(StartMaterial.Seed, "Bewurzelung", "Anzuchtwoche 2", "Anzucht", "Anzucht 2")]
    [InlineData(StartMaterial.Clone, "Bewurzelung", "Anzuchtwoche 2", "Bewurzelung", "Bewurzelung 2")]
    public void GespeichertePlaeneHeissenNachDemStartWieIhrStartmaterial(
        StartMaterial material, string altSpalte, string altWoche, string neuSpalte, string neuWoche)
    {
        _benutzt.Add(Basis + 20);
        var grow = new GrowRun
        {
            Id = Basis + 20, Name = "Angleichen", FeedProgramId = "skx-canna-aqua", HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running, StartMaterial = material, EntryPoint = GrowEntryPoint.Germination,
            StartDate = Heute.AddDays(-9), GerminatedAt = material == StartMaterial.Seed ? Heute.AddDays(-9) : null,
        };
        _dienst.Anlegen(grow);
        Assert.Equal(1, _dienst.WochenNachziehen(grow, Heute));

        // So lag der Plan aus einer früheren Version in der Datenbank.
        foreach (var name in new[] { GrowPlanStaende.Start, GrowPlanStaende.Arbeit })
        {
            var stand = _repo.Laden(grow.Id, name)!;
            Spalte(stand.Inhalt, "root").Label = altSpalte;
            if (stand.Inhalt.Chart.Columns.FirstOrDefault(c => c.Id == "clone-w2") is { } woche) woche.Label = altWoche;
            _repo.Nachtragen(stand);
        }
        var alt = _repo.Laden(grow.Id, GrowPlanStaende.Arbeit)!;
        _repo.Speichern([alt with { Stand = GrowPlanStaende.Ende }], []);

        var nachtrag = _dienst.FehlendeFelderNachtragen([grow]);

        // Start und Arbeit beim Samen; beim Steckling stimmte die Spalte im Startstand schon.
        Assert.Equal(material == StartMaterial.Seed ? 2 : 1, nachtrag.Wochennamen);
        Assert.Equal(nachtrag.Wochennamen, nachtrag.Planstaende);
        Assert.Equal(0, nachtrag.EcBand);
        Assert.Equal([neuSpalte, neuWoche], Arbeit(grow.Id).Chart.Columns.Take(2).Select(c => c.Label));
        Assert.Equal(neuSpalte, Spalte(_repo.Laden(grow.Id, GrowPlanStaende.Start)!.Inhalt, "root").Label);
        // Alle Leser sehen den neuen Namen sofort, nicht erst nach dem nächsten Laden.
        Assert.Equal([neuSpalte, neuWoche], GrowPlanRegister.Inhalt(grow.Id)!.Chart.Columns.Take(2).Select(c => c.Label));
        // Der Endstand ist eingefroren — auch beim Namen.
        Assert.Equal([altSpalte, altWoche], _repo.Laden(grow.Id, GrowPlanStaende.Ende)!.Inhalt.Chart.Columns.Take(2).Select(c => c.Label));

        Assert.Equal(0, _dienst.FehlendeFelderNachtragen([grow]).Planstaende);
    }

    [Fact]
    public void BeiAthenaWaechstDasAnfuetternUndNichtDasVorweichen()
    {
        _benutzt.Add(Basis + 8);
        var grow = new GrowRun
        {
            Id = Basis + 8, Name = "Klon", FeedProgramId = "athena", HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running, StartMaterial = StartMaterial.Clone, EntryPoint = GrowEntryPoint.Germination,
            StartDate = Heute.AddDays(-9),
        };
        _dienst.Anlegen(grow);

        Assert.Equal(1, _dienst.WochenNachziehen(grow, Heute));

        var arbeit = Arbeit(grow.Id);
        Assert.Equal(["clone-presoak", "clone-feed", "clone-w2"], arbeit.Chart.Columns.Take(3).Select(c => c.Id));
        Assert.Equal("clone-feed", arbeit.Verlaengert["clone-w2"]);
        // Steckling: die angehängte Woche heißt „Bewurzelung 2"; die benannten
        // Klon-Schritte davor behalten ihre Namen.
        Assert.Equal(["Klon · Vorweichen", "Klon · Anfüttern", "Bewurzelung 2"], arbeit.Chart.Columns.Take(3).Select(c => c.Label));
    }

    [Fact]
    public void SpuelenWaechstNicht()
    {
        // Finish läuft drei Wochen: der Flush bleibt eine Spalte.
        var grow = Bluehend(9, "skx-canna-aqua", flipVorTagen: 70);
        grow.FinishStartedAt = Heute.AddDays(-21);
        _dienst.Anlegen(grow);

        _dienst.WochenNachziehen(grow, Heute);

        Assert.Single(Arbeit(grow.Id).Chart.Columns, c => c.Stage == "Finish");
        // Die Blüte lief 49 Tage = 7 Wochen: SKX führt acht, nichts angehängt.
        Assert.DoesNotContain(Arbeit(grow.Id).Chart.Columns, c => c.Stage == "Flower" && c.Week > 8);
    }

    [Fact]
    public void EineAbgeschlosseneVegiBekommtIhreWochenNachtraeglich()
    {
        // 43 Tage Vegi, SKX führt vier Vegi-Wochen: 5, 6 und 7 kommen dazu.
        var grow = Bluehend(10, "skx-canna-aqua", flipVorTagen: 20, vegiTage: 43);
        _dienst.Anlegen(grow);

        Assert.Equal(3, _dienst.WochenNachziehen(grow, Heute));

        var arbeit = Arbeit(grow.Id);
        Assert.Equal(["veg-w1", "veg-w2", "veg-w3", "veg-w4", "veg-w5", "veg-w6", "veg-w7", "flower-w1"],
            arbeit.Chart.Columns.Skip(1).Take(8).Select(c => c.Id));

        // Die Auswertung führt sie mit eigenen Zeiträumen — Woche 4 wird nicht mehr bis zum Flip gestreckt.
        var vegi = grow.VegStartedAt!.Value.Date;
        var flip = grow.FlipDate!.Value.Date;
        var zeit = PlanAuswertung.Zeitraeume(grow, arbeit.Chart.Columns, Heute);
        Assert.Equal((vegi.AddDays(21), vegi.AddDays(28)), zeit["veg-w4"]);
        Assert.Equal((vegi.AddDays(28), vegi.AddDays(35)), zeit["veg-w5"]);
        Assert.Equal((vegi.AddDays(42), flip), zeit["veg-w7"]);   // endet mit dem Flip
        Assert.Equal((flip, flip.AddDays(7)), zeit["flower-w1"]);

        var auswertung = PlanAuswertung.Bauen(grow, _repo.Laden(grow.Id, GrowPlanStaende.Start)!.Inhalt, arbeit, [], Heute);
        var w6 = auswertung.Single(w => w.Id == "veg-w6");
        Assert.True(w6.Verlaengert);
        Assert.False(auswertung.Single(w => w.Id == "veg-w4").Verlaengert);
        // Geplant war für sie, was die letzte Programmwoche vorsah.
        Assert.Equal(auswertung.Single(w => w.Id == "veg-w4").Start["ecTarget"], w6.Start["ecTarget"]);
    }

    [Fact]
    public void EinEingefrorenerPlanOhneWochenBehaeltDieGehalteneWocheInDerAuswertung()
    {
        // Wie oben, aber nie nachgezogen (Plan aus der Zeit davor): die letzte
        // Vegi-Woche reicht bis zum Flip, damit ihre Messungen nicht ins Leere fallen.
        var grow = Bluehend(11, "skx-canna-aqua", flipVorTagen: 20, vegiTage: 43);
        var plan = _dienst.Anlegen(grow)!;
        var vegi = grow.VegStartedAt!.Value.Date;

        var zeit = PlanAuswertung.Zeitraeume(grow, plan.Inhalt.Chart.Columns, Heute);

        Assert.Equal((vegi.AddDays(21), grow.FlipDate!.Value.Date), zeit["veg-w4"]);
    }

    [Fact]
    public void EineVegiKuerzerAlsGeplantLegtKeineWocheUeberDieBluete()
    {
        // Nach 17 Tagen geflippt: Vegi-Woche 3 endet am Flip, Woche 4 hat keinen Zeitraum.
        var grow = Bluehend(12, "skx-canna-aqua", flipVorTagen: 20, vegiTage: 17);
        var plan = _dienst.Anlegen(grow)!;
        var vegi = grow.VegStartedAt!.Value.Date;

        var zeit = PlanAuswertung.Zeitraeume(grow, plan.Inhalt.Chart.Columns, Heute);

        Assert.Equal((vegi.AddDays(14), grow.FlipDate!.Value.Date), zeit["veg-w3"]);
        Assert.Equal((null, null), zeit["veg-w4"]);
    }

    [Fact]
    public void KachelGrenzwerteUebergabeUndAuswertungZeigenDieselbeWoche()
    {
        var grow = Bluehend(13, "athena", flipVorTagen: 63);
        _dienst.Anlegen(grow);
        _dienst.WochenNachziehen(grow, Heute);
        // Woche 10 unterscheidbar machen — sonst sähe eine gehaltene Woche 9 gleich aus.
        _dienst.WerteSetzen(grow.Id, [("flower-w10", "ecTarget", 2.4), ("flower-w10", "ecMin", 2.2), ("flower-w10", "ecMax", 2.6)]);

        // Die eine Auswahl, über die alle Leser gehen (Kacheln, Alarme, Grenzwerte,
        // Übergabe an HA, Wochenzeile, Mischplan).
        var spalte = MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms, Heute)!.Value.Spalte;
        Assert.Equal("flower-w10", spalte.Id);
        Assert.Equal(10, Phasenanker.Fuer(grow, Heute).WocheIn("Flower"));

        // Kachel und Grenzwerte: das Zielband.
        var band = Zielband.FuerGrow(_ziele, _wissen, grow, GrowStage.Flower, null, null, Heute)!;
        Assert.Equal((2.2, 2.6), (band.EcMin, band.EcMax));

        // Übergabe an HA: die Werte der Spalte, die der Takt liest.
        Assert.Equal(WochenplanSyncService.Werte(spalte).ToList(),
            WochenplanSyncService.Werte(Spalte(Arbeit(grow.Id), "flower-w10")).ToList());

        // Auswertung: heute liegt in genau dieser Woche, in keiner anderen.
        var zeit = PlanAuswertung.Zeitraeume(grow, Arbeit(grow.Id).Chart.Columns, Heute);
        var heuteIn = zeit.Where(z => z.Value.Von <= Heute && Heute < z.Value.Bis).Select(z => z.Key).ToList();
        Assert.Equal(["flower-w10"], heuteIn);
    }

    [Fact]
    public void EinProgrammwechselNimmtDieVerlaengerteWocheUndIhreAenderungMit()
    {
        var grow = Bluehend(14, "athena", flipVorTagen: 63);
        _dienst.Anlegen(grow);
        _dienst.WochenNachziehen(grow, Heute);
        _dienst.WerteSetzen(grow.Id, [("flower-w10", "ecTarget", 2.1)]);

        _dienst.ProgrammWechseln(grow, "skx-canna-aqua", aenderungenBehalten: true, heute: Heute);

        var arbeit = Arbeit(grow.Id);
        // SKX führt acht Blütewochen: 9 und 10 werden angehängt, die Änderung an 10 bleibt.
        Assert.True(arbeit.IstVerlaengert("flower-w9"));
        Assert.Equal(2.1, Spalte(arbeit, "flower-w10").EcTarget);
        Assert.Equal("flower-w8", arbeit.Programmwoche("flower-w10"));
        // Eine Woche vor dem Wechsel, zwei mit ihm — jede steht im Buch.
        Assert.Equal(3, _repo.Buch(grow.Id).Count(e => e.Art == GrowPlanArten.Verlaengert));
    }

    [Fact]
    public void EineAbgeschlossenePhaseZaehltBisZuIhremLetztenTag()
    {
        // 28 Tage Vegi sind vier Wochen — bis 02.10.2026 zählte der Flip-Tag mit (fünf).
        var grow = Bluehend(15, "skx-canna-aqua", flipVorTagen: 20, vegiTage: 28);
        Assert.Equal(4, Phasenanker.Fuer(grow, Heute).WocheIn("Veg"));

        // Der Erntetag gehört zum Lauf, der Tag danach nicht.
        grow.EndDate = grow.FlipDate!.Value.AddDays(13);
        Assert.Equal(2, Phasenanker.Fuer(grow, Heute).WocheIn("Flower"));
    }

    private static void KopiereWissen(string wurzel)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) dir = Path.GetDirectoryName(dir);
        var quelle = Path.Combine(dir ?? throw new InvalidOperationException("Projektwurzel nicht gefunden."),
            "GrowDiary.Web", "wwwroot", "knowledge-defaults");
        var ziel = Path.Combine(wurzel, "wwwroot", "knowledge-defaults");
        var anzahl = 0;
        foreach (var datei in Directory.EnumerateFiles(quelle, "*.json", SearchOption.AllDirectories))
        {
            var pfad = Path.Combine(ziel, Path.GetRelativePath(quelle, datei));
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.Copy(datei, pfad);
            anzahl++;
        }
        Assert.True(anzahl >= 4, $"Nur {anzahl} Wissensdateien gefunden — der Test sähe seine Programme nicht.");
    }
}
