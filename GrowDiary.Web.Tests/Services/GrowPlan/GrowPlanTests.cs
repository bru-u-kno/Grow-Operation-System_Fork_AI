using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services.GrowPlan;

/// <summary>
/// Fork AI (Grow-Plan): der Plan eines Grows ist eine eigene, vollständige Kopie.
/// </summary>
/// <remarks>
/// Geprüft an den echten mitgelieferten Programmen — SKX (vollständig),
/// Athena (nur EC/pH) und Canna Aqua (gar keine Wochen).
/// </remarks>
public sealed class GrowPlanTests : IDisposable
{
    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly GrowPlanRepository _repo;
    private readonly GrowPlanService _dienst;
    private readonly EigeneProgramme _eigene;

    public GrowPlanTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "GrowPlan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        KopiereWissen();
        _wissen = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        _wissen.Initialize();
        _repo = new GrowPlanRepository(_pfade);
        _eigene = new EigeneProgramme(_pfade, _wissen, NullLogger<EigeneProgramme>.Instance);
        _dienst = new GrowPlanService(_repo, _wissen, new TargetValueService(_wissen), NullLogger<GrowPlanService>.Instance, _eigene);
    }

    // Hohe Ids: das Register ist prozessweit und wird von den Integrationstests mitbenutzt.
    private const int Basis = 900_000;
    private readonly List<int> _benutzt = [];

    public void Dispose()
    {
        foreach (var id in _benutzt) GrowPlanRegister.Entfernen(id);
        try { Directory.Delete(_wurzel, recursive: true); } catch { }
    }

    private GrowRun Grow(int nummer, string programm, int? vegiTage = null, int? bluetewochen = null)
    {
        _benutzt.Add(Basis + nummer);
        return GrowMitId(Basis + nummer, programm, vegiTage, bluetewochen);
    }

    private static GrowRun GrowMitId(int id, string programm, int? vegiTage, int? bluetewochen) => new()
    {
        Id = id,
        Name = $"Test {id}",
        FeedProgramId = programm,
        HydroStyle = HydroStyle.RDWC,
        PlannedVegDays = vegiTage,
        BreederFlowerWeeksMax = bluetewochen,
    };

    [Fact]
    public void SkxWirdVollstaendigKopiertUndOrpKommtAusDemProgramm()
    {
        var plan = _dienst.Anlegen(Grow(1, "skx-canna-aqua"))!;

        Assert.Equal(14, plan.Inhalt.Chart.Columns.Count);
        var w4 = plan.Inhalt.Chart.Columns.Single(c => c.Id == "flower-w4");
        Assert.Equal(400, w4.OrpMin);
        Assert.Equal(450, w4.OrpMax);
        Assert.Equal(GrowPlanHerkunft.Programm, plan.Inhalt.HerkunftVon("flower-w4", "orpMin"));
        Assert.Equal(GrowPlanHerkunft.Programm, plan.Inhalt.HerkunftVon("flower-w4", "rhMax"));

        // Nur die Bewurzelung nennt kein ORP — dort kommt der Standard.
        Assert.Equal(GrowPlanHerkunft.Standard, plan.Inhalt.HerkunftVon("root", "orpMin"));
        Assert.NotNull(plan.Inhalt.Chart.Columns.Single(c => c.Id == "root").OrpMin);
    }

    [Fact]
    public void DerPlanIstEineKopieUndAendertDasProgrammNicht()
    {
        var plan = _dienst.Anlegen(Grow(2, "skx-canna-aqua"))!;
        plan.Inhalt.Chart.Columns.Single(c => c.Id == "flower-w5").EcTarget = 9.9;

        var programm = _wissen.NutrientPrograms.Single(p => p.Id == "skx-canna-aqua");
        Assert.NotEqual(9.9, programm.FeedChart!.Columns.Single(c => c.Id == "flower-w5").EcTarget);

        // Das Lückenfüllen darf nicht ins geladene Programm schreiben: die
        // Bewurzelung nennt dort kein ORP und muss es auch danach nicht tun.
        Assert.Null(programm.FeedChart.Columns.Single(c => c.Id == "root").OrpMin);
    }

    [Fact]
    public void AthenaBekommtKlimaAusDemStandardUndLuftBleibtLeer()
    {
        var plan = _dienst.Anlegen(Grow(3, "athena"))!;
        var spalte = plan.Inhalt.Chart.Columns.First(c => c.Stage == "Flower");

        Assert.Equal(GrowPlanHerkunft.Programm, plan.Inhalt.HerkunftVon(spalte.Id, "ecTarget"));
        Assert.Equal(GrowPlanHerkunft.Standard, plan.Inhalt.HerkunftVon(spalte.Id, "vpdMin"));
        Assert.NotNull(spalte.VpdMin);
        Assert.Null(spalte.RhMax);
        Assert.Equal(GrowPlanHerkunft.Fehlt, plan.Inhalt.HerkunftVon(spalte.Id, "rhMax"));
        Assert.Equal(GrowPlanHerkunft.Fehlt, plan.Inhalt.HerkunftVon(spalte.Id, "airTempC"));
    }

    [Fact]
    public void ProgrammOhneWochenBekommtEinRasterAusVegiUndSorte()
    {
        var plan = _dienst.Anlegen(Grow(4, "canna-aqua", vegiTage: 36, bluetewochen: 10))!;
        var spalten = plan.Inhalt.Chart.Columns;

        // Bewurzelung + 6 Vegi-Wochen (36 Tage) + 10 Blütewochen + Flush
        Assert.Equal(1 + 6 + 10 + 1, spalten.Count);
        Assert.Equal("root", spalten[0].Id);
        Assert.Equal("flush", spalten[^1].Id);
        Assert.All(spalten, s => Assert.Empty(s.Items));
        Assert.All(spalten, s => Assert.NotNull(s.EcTarget));
        Assert.Equal(GrowPlanHerkunft.Standard, plan.Inhalt.HerkunftVon("flower-w3", "ecTarget"));
    }

    [Fact]
    public void OhneVegiDauerUndSorteGeltenDieStandardwochen()
    {
        var plan = _dienst.Anlegen(Grow(5, "canna-aqua"))!;
        Assert.Equal(1 + GrowPlanBauer.StandardVegiWochen + GrowPlanBauer.StandardBluetewochen + 1,
            plan.Inhalt.Chart.Columns.Count);
    }

    [Fact]
    public void StartUndArbeitsstandWerdenGespeichertUndImRegisterGefunden()
    {
        _dienst.Anlegen(Grow(6, "skx-canna-aqua"), vermerk: "nachträglich angelegt");

        Assert.NotNull(_repo.Laden(Basis + 6, GrowPlanStaende.Start));
        Assert.NotNull(_repo.Laden(Basis + 6, GrowPlanStaende.Arbeit));
        Assert.Null(_repo.Laden(Basis + 6, GrowPlanStaende.Ende));
        Assert.Equal("nachträglich angelegt", _repo.Laden(Basis + 6, GrowPlanStaende.Start)!.Vermerk);

        var buch = _repo.Buch(Basis + 6);
        Assert.Single(buch);
        Assert.Equal(GrowPlanArten.Angelegt, buch[0].Art);

        GrowPlanRegister.Entfernen(Basis + 6);
        Assert.Null(GrowPlanRegister.Programm(Basis + 6));
        _dienst.RegisterLaden();
        Assert.Equal(14, GrowPlanRegister.Programm(Basis + 6)!.FeedChart!.Columns.Count);
    }

    [Fact]
    public void ZweitesAnlegenAendertNichts()
    {
        Assert.NotNull(_dienst.Anlegen(Grow(7, "skx-canna-aqua")));
        Assert.Null(_dienst.Anlegen(Grow(7, "canna-aqua")));
        Assert.Equal("skx-canna-aqua", _repo.Laden(Basis + 7, GrowPlanStaende.Arbeit)!.Inhalt.ProgrammId);
        Assert.Single(_repo.Buch(Basis + 7));
    }

    [Fact]
    public void DerStartstandWirdNieUeberschrieben()
    {
        var plan = _dienst.Anlegen(Grow(8, "skx-canna-aqua"))!;
        var geaendert = GrowPlanBauer.Kopie(plan.Inhalt);
        geaendert.Chart.Columns[0].EcTarget = 7.7;

        _repo.Speichern(
            [plan with { Stand = GrowPlanStaende.Start, Inhalt = geaendert },
             plan with { Inhalt = geaendert }],
            []);

        Assert.NotEqual(7.7, _repo.Laden(Basis + 8, GrowPlanStaende.Start)!.Inhalt.Chart.Columns[0].EcTarget);
        Assert.Equal(7.7, _repo.Laden(Basis + 8, GrowPlanStaende.Arbeit)!.Inhalt.Chart.Columns[0].EcTarget);
    }

    [Fact]
    public void OhneProgrammGibtEsKeinenPlan()
    {
        Assert.Null(_dienst.Anlegen(Grow(9, "")));
        Assert.Null(_dienst.Anlegen(Grow(10, "gibt-es-nicht")));
    }

    [Fact]
    public void DerGrowLiestSeinenPlanUndNichtDieBibliothek()
    {
        var grow = Grow(11, "skx-canna-aqua");
        grow.UseFeedChartTargets = false;
        Assert.False(MischplanService.NutztWochenziele(grow));

        _dienst.Anlegen(grow);
        Assert.True(MischplanService.NutztWochenziele(grow));

        _dienst.WerteSetzen(grow.Id, [("flower-w5", "ecTarget", 1.3)], grund: "Test");

        var ausPlan = MischplanService.ProgrammFuerGrow(grow, _wissen.NutrientPrograms)!;
        Assert.Equal(1.3, ausPlan.FeedChart!.Columns.Single(c => c.Id == "flower-w5").EcTarget);

        var bibliothek = _wissen.NutrientPrograms.Single(p => p.Id == "skx-canna-aqua");
        Assert.Equal(1.5, bibliothek.FeedChart!.Columns.Single(c => c.Id == "flower-w5").EcTarget);
    }

    [Fact]
    public void WerteSetzenSchreibtBuchUndNullStelltDenStartwertHer()
    {
        var grow = Grow(12, "skx-canna-aqua");
        _dienst.Anlegen(grow);

        Assert.Equal(1, _dienst.WerteSetzen(grow.Id, [("flower-w5", "ecTarget", 1.4)], grund: "Spitzen hell"));
        var arbeit = _repo.Laden(grow.Id, GrowPlanStaende.Arbeit)!;
        Assert.Equal(1.4, arbeit.Inhalt.Chart.Columns.Single(c => c.Id == "flower-w5").EcTarget);
        Assert.Equal(GrowPlanHerkunft.Eigen, arbeit.Inhalt.HerkunftVon("flower-w5", "ecTarget"));

        var eintrag = _repo.Buch(grow.Id)[0];
        Assert.Equal(GrowPlanArten.Wert, eintrag.Art);
        Assert.Equal(("1.5", "1.4", "Spitzen hell"), (eintrag.Alt, eintrag.Neu, eintrag.Grund));

        // Gleicher Wert noch einmal: keine Änderung, kein Eintrag.
        Assert.Equal(0, _dienst.WerteSetzen(grow.Id, [("flower-w5", "ecTarget", 1.4)]));

        // Zurück auf den Startwert.
        Assert.Equal(1, _dienst.WerteSetzen(grow.Id, [("flower-w5", "ecTarget", null)]));
        arbeit = _repo.Laden(grow.Id, GrowPlanStaende.Arbeit)!;
        Assert.Equal(1.5, arbeit.Inhalt.Chart.Columns.Single(c => c.Id == "flower-w5").EcTarget);
        Assert.Equal(GrowPlanHerkunft.Programm, arbeit.Inhalt.HerkunftVon("flower-w5", "ecTarget"));
        Assert.Equal(3, _repo.Buch(grow.Id).Count);

        // Der Startstand bleibt unberührt.
        Assert.Equal(1.5, _repo.Laden(grow.Id, GrowPlanStaende.Start)!.Inhalt.Chart.Columns.Single(c => c.Id == "flower-w5").EcTarget);
    }

    [Fact]
    public void UebernahmeBeimStartUeberspringtAbgeschlosseneGrows()
    {
        var laeuft = Grow(13, "skx-canna-aqua");
        var fertig = Grow(14, "skx-canna-aqua");
        fertig.Status = GrowStatus.Completed;
        var ohne = Grow(15, "");

        Assert.Equal(1, _dienst.FehlendePlaeneAnlegen([laeuft, fertig, ohne]));
        Assert.Equal(GrowPlanService.Nachtraeglich, _repo.Laden(laeuft.Id, GrowPlanStaende.Start)!.Vermerk);
        Assert.Null(_repo.Laden(fertig.Id, GrowPlanStaende.Arbeit));

        // Zweiter Start: nichts Neues.
        Assert.Equal(0, _dienst.FehlendePlaeneAnlegen([laeuft, fertig, ohne]));
    }

    private static FeedChartColumnView Woche(GrowPlanStand stand, string id)
        => new(stand.Inhalt.Chart.Columns.Single(c => c.Id == id));

    private sealed record FeedChartColumnView(GrowDiary.Web.Services.Knowledge.Schema.FeedChartColumn C);

    [Fact]
    public void DasEcBandLiegtUmDasZielMitDerBreiteDesStandards()
    {
        var grow = Grow(20, "skx-canna-aqua");
        var plan = _dienst.Anlegen(grow)!;
        var w4 = Woche(plan, "flower-w4").C;
        var standard = new TargetValueService(_wissen).GetTargets(TargetValueService.ProfileIdFor(HydroStyle.RDWC), GrowStage.Flower)!;

        Assert.Equal(1.4, (w4.EcMin!.Value + w4.EcMax!.Value) / 2, 3);
        Assert.Equal(standard.EcMax - standard.EcMin, w4.EcMax.Value - w4.EcMin.Value, 3);
        Assert.Equal(GrowPlanHerkunft.Standard, plan.Inhalt.HerkunftVon("flower-w4", "ecMin"));
    }

    [Fact]
    public void WandertDasEcZielWandertDasBandMit()
    {
        var grow = Grow(21, "skx-canna-aqua");
        var vorher = Woche(_dienst.Anlegen(grow)!, "flower-w5").C;
        var breite = vorher.EcMax!.Value - vorher.EcMin!.Value;

        _dienst.WerteSetzen(grow.Id, [("flower-w5", "ecTarget", 1.3)]);
        var nachher = Woche(_repo.Laden(grow.Id, GrowPlanStaende.Arbeit)!, "flower-w5").C;

        Assert.Equal(1.3, (nachher.EcMin!.Value + nachher.EcMax!.Value) / 2, 3);
        Assert.Equal(breite, nachher.EcMax.Value - nachher.EcMin.Value, 3);
    }

    [Fact]
    public void DosierungErsetzenSchreibtJeUnterschiedEinenEintrag()
    {
        var grow = Grow(22, "skx-canna-aqua");
        var vorher = Woche(_dienst.Anlegen(grow)!, "flower-w5").C;
        var ohneBoost = vorher.Items.Where(i => i.Component != "Cannaboost")
            .Select(i => new PlanDosis(i.Component, i.MinMlPerLiter)).ToList();
        ohneBoost.Add(new PlanDosis("Pro-Silicate", 0.6));

        var ergebnis = _dienst.Speichern(grow.Id, new PlanSpeichernAnfrage(
            "flower-w5", [], ohneBoost, AuchInsProgramm: false, ProgrammName: null, Grund: "Organik raus"));

        Assert.Equal(2, ergebnis.Aenderungen);
        Assert.Null(ergebnis.ProgrammId);
        var items = Woche(_repo.Laden(grow.Id, GrowPlanStaende.Arbeit)!, "flower-w5").C.Items;
        Assert.DoesNotContain(items, i => i.Component == "Cannaboost");
        Assert.Contains(items, i => i.Component == "Pro-Silicate" && i.MinMlPerLiter == 0.6);

        var buch = _repo.Buch(grow.Id).Where(e => e.Art == GrowPlanArten.Dosierung).ToList();
        Assert.Contains(buch, e => e.Feld == "Cannaboost" && e.Neu == null && e.Grund == "Organik raus");
        Assert.Contains(buch, e => e.Feld == "Pro-Silicate" && e.Alt == null && e.Neu == "0.6");

        var bibliothek = _wissen.NutrientPrograms.Single(p => p.Id == "skx-canna-aqua");
        Assert.Contains(bibliothek.FeedChart!.Columns.Single(c => c.Id == "flower-w5").Items, i => i.Component == "Cannaboost");
    }

    [Fact]
    public void AuchInsProgrammLegtEinEigenesProgrammAnUndNutztEsDanachWeiter()
    {
        var grow = Grow(23, "skx-canna-aqua");
        _dienst.Anlegen(grow);

        var erstes = _dienst.Speichern(grow.Id, new PlanSpeichernAnfrage(
            "flower-w5", [("ecTarget", 1.45)], null, AuchInsProgramm: true, ProgrammName: "Mimosa RDWC", Grund: null));

        Assert.Equal("eigen-mimosa-rdwc", erstes.ProgrammId);
        var eigenes = _wissen.NutrientPrograms.Single(p => p.Id == "eigen-mimosa-rdwc");
        Assert.Equal("Mimosa RDWC", eigenes.Name);
        Assert.Equal(1.45, eigenes.FeedChart!.Columns.Single(c => c.Id == "flower-w5").EcTarget);
        // Nur diese Änderung — die anderen Wochen bleiben wie im Programm.
        Assert.Equal(1.4, eigenes.FeedChart.Columns.Single(c => c.Id == "flower-w4").EcTarget);
        Assert.Equal(1.5, _wissen.NutrientPrograms.Single(p => p.Id == "skx-canna-aqua")
            .FeedChart!.Columns.Single(c => c.Id == "flower-w5").EcTarget);
        Assert.Equal("eigen-mimosa-rdwc", _repo.Laden(grow.Id, GrowPlanStaende.Arbeit)!.Inhalt.EigenesProgrammId);
        Assert.StartsWith("programm:eigen-mimosa-rdwc", _repo.Buch(grow.Id)[0].Ziel);

        // Zweites Mal: dasselbe Programm, keine neue Datei.
        var zweites = _dienst.Speichern(grow.Id, new PlanSpeichernAnfrage(
            "flower-w6", [("ecTarget", 1.55)], null, AuchInsProgramm: true, ProgrammName: "anderer Name", Grund: null));
        Assert.Equal("eigen-mimosa-rdwc", zweites.ProgrammId);
        Assert.Single(_wissen.NutrientPrograms, p => EigeneProgramme.IstEigen(p.Id));
        Assert.Equal(1.55, _wissen.NutrientPrograms.Single(p => p.Id == "eigen-mimosa-rdwc")
            .FeedChart!.Columns.Single(c => c.Id == "flower-w6").EcTarget);

        // Ein neuer Grow kann das eigene Programm wählen.
        var naechster = Grow(24, "eigen-mimosa-rdwc");
        Assert.Equal("Mimosa RDWC", _dienst.Anlegen(naechster)!.Inhalt.ProgrammName);
    }

    [Fact]
    public void EinEingefrorenerPlanLaesstSichNichtSpeichern()
    {
        var grow = Grow(25, "skx-canna-aqua");
        var plan = _dienst.Anlegen(grow)!;
        _repo.Speichern([plan with { Stand = GrowPlanStaende.Ende }], []);

        Assert.Throws<InvalidOperationException>(() => _dienst.Speichern(grow.Id,
            new PlanSpeichernAnfrage("flower-w5", [("ecTarget", 1.2)], null, false, null, null)));
    }

    [Fact]
    public void AlteplaeneBekommenDasEcBandNachgetragen()
    {
        var grow = Grow(26, "skx-canna-aqua");
        var plan = _dienst.Anlegen(grow)!;
        foreach (var name in new[] { GrowPlanStaende.Start, GrowPlanStaende.Arbeit })
        {
            var stand = _repo.Laden(grow.Id, name)!;
            foreach (var spalte in stand.Inhalt.Chart.Columns) { spalte.EcMin = null; spalte.EcMax = null; }
            _repo.Nachtragen(stand);
        }

        Assert.Equal(2, _dienst.FehlendeFelderNachtragen([grow]));
        Assert.NotNull(Woche(_repo.Laden(grow.Id, GrowPlanStaende.Start)!, "flower-w4").C.EcMin);
        Assert.NotNull(Woche(_repo.Laden(grow.Id, GrowPlanStaende.Arbeit)!, "flower-w4").C.EcMax);
        Assert.Equal(0, _dienst.FehlendeFelderNachtragen([grow]));
        Assert.Single(_repo.Buch(grow.Id)); // Nachtrag ist keine Änderung am Ziel.
    }

    [Theory]
    [InlineData("SKX Canna Aqua (eigen)", "skx-canna-aqua-eigen")]
    [InlineData("Blüte ÄÖÜ ß 2026!", "bluete-aeoeue-ss-2026")]
    [InlineData("!!!", "programm")]
    public void SlugIstLesbar(string name, string erwartet)
        => Assert.Equal(erwartet, EigeneProgramme.Slug(name));

    private void KopiereWissen()
    {
        var quelle = Path.Combine(ProjektWurzel(), "GrowDiary.Web", "wwwroot", "knowledge-defaults");
        var ziel = Path.Combine(_wurzel, "wwwroot", "knowledge-defaults");
        foreach (var datei in Directory.EnumerateFiles(quelle, "*.json", SearchOption.AllDirectories))
        {
            var pfad = Path.Combine(ziel, Path.GetRelativePath(quelle, datei));
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.Copy(datei, pfad);
        }
    }

    private static string ProjektWurzel()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
