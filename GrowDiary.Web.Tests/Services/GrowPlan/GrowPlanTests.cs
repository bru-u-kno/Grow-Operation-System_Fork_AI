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
[Collection("GrowPlanRegister")]
public sealed class GrowPlanTests : IDisposable
{
    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly GrowPlanRepository _repo;
    private readonly GrowPlanService _dienst;

    public GrowPlanTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "GrowPlan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        KopiereWissen();
        _wissen = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        _wissen.Initialize();
        _repo = new GrowPlanRepository(_pfade);
        _dienst = new GrowPlanService(_repo, _wissen, new TargetValueService(_wissen), NullLogger<GrowPlanService>.Instance);
        GrowPlanRegister.Leeren();
    }

    public void Dispose()
    {
        GrowPlanRegister.Leeren();
        try { Directory.Delete(_wurzel, recursive: true); } catch { }
    }

    private static GrowRun Grow(int id, string programm, int? vegiTage = null, int? bluetewochen = null) => new()
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

        Assert.NotNull(_repo.Laden(6, GrowPlanStaende.Start));
        Assert.NotNull(_repo.Laden(6, GrowPlanStaende.Arbeit));
        Assert.Null(_repo.Laden(6, GrowPlanStaende.Ende));
        Assert.Equal("nachträglich angelegt", _repo.Laden(6, GrowPlanStaende.Start)!.Vermerk);

        var buch = _repo.Buch(6);
        Assert.Single(buch);
        Assert.Equal(GrowPlanArten.Angelegt, buch[0].Art);

        GrowPlanRegister.Leeren();
        Assert.Null(GrowPlanRegister.Programm(6));
        _dienst.RegisterLaden();
        Assert.Equal(14, GrowPlanRegister.Programm(6)!.FeedChart!.Columns.Count);
    }

    [Fact]
    public void ZweitesAnlegenAendertNichts()
    {
        Assert.NotNull(_dienst.Anlegen(Grow(7, "skx-canna-aqua")));
        Assert.Null(_dienst.Anlegen(Grow(7, "canna-aqua")));
        Assert.Equal("skx-canna-aqua", _repo.Laden(7, GrowPlanStaende.Arbeit)!.Inhalt.ProgrammId);
        Assert.Single(_repo.Buch(7));
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

        Assert.NotEqual(7.7, _repo.Laden(8, GrowPlanStaende.Start)!.Inhalt.Chart.Columns[0].EcTarget);
        Assert.Equal(7.7, _repo.Laden(8, GrowPlanStaende.Arbeit)!.Inhalt.Chart.Columns[0].EcTarget);
    }

    [Fact]
    public void OhneProgrammGibtEsKeinenPlan()
    {
        Assert.Null(_dienst.Anlegen(Grow(9, "")));
        Assert.Null(_dienst.Anlegen(Grow(10, "gibt-es-nicht")));
    }

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
