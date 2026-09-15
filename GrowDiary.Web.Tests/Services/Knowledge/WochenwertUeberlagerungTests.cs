using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services.Knowledge;

/// <summary>
/// Fork AI (F-004): eigene Wochenwerte liegen auf dem Plan, ohne ihn zu verlieren.
/// </summary>
/// <remarks>
/// Geprüft wird am echten SKX-Programm, weil genau dessen Spalten die Sollwerte
/// liefern. Was hier fällt, fällt beim Nutzer als falscher Chiller-Wert auf.
/// </remarks>
public sealed class WochenwertUeberlagerungTests : IDisposable
{
    private const string Programm = "skx-canna-aqua";
    private const string Woche = "flower-w4";

    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly WochenwertRepository _repo;
    private readonly WochenwertUeberlagerung _ueberlagerung;
    private readonly KnowledgeBaseLoader _wissen;

    public WochenwertUeberlagerungTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Wochenwerte_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        KopiereWissen();

        _repo = new WochenwertRepository(_pfade);
        _ueberlagerung = new WochenwertUeberlagerung(_repo, NullLogger<WochenwertUeberlagerung>.Instance);
        _wissen = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance)
        {
            NachDemLaden = _ueberlagerung.Anwenden,
        };
        _wissen.Initialize();
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { }
    }

    private FeedChartColumn Spalte()
        => _wissen.NutrientPrograms.Single(p => p.Id == Programm).FeedChart!.Columns.Single(c => c.Id == Woche);

    private static Wochenwertfelder.Feld Feld(string name) => Wochenwertfelder.Finden(name)!;

    [Fact]
    public void DieSpalteDieDerTestBenutztGibtEs()
    {
        // Ohne diese Spalte prüfen die übrigen Fälle nichts.
        Assert.NotNull(Spalte().WaterTempNightC);
    }

    [Fact]
    public void EigenerWertGiltUndDerPlanwertBleibtSichtbar()
    {
        var plan = Spalte().WaterTempNightC;

        _repo.Speichern(Programm, [(Woche, "waterTempNightC", 19.5)]);
        _ueberlagerung.Auffrischen();

        Assert.Equal(19.5, Spalte().WaterTempNightC);
        Assert.Equal(plan, _ueberlagerung.Planwert(Spalte(), Feld("waterTempNightC")));
        Assert.True(_ueberlagerung.IstGeaendert(Programm, Woche, "waterTempNightC"));
    }

    [Fact]
    public void ZurueckAufPlanStelltDenDateiwertWiederHer()
    {
        var plan = Spalte().EcTarget;

        _repo.Speichern(Programm, [(Woche, "ecTarget", 1.7)]);
        _ueberlagerung.Auffrischen();
        _repo.Speichern(Programm, [(Woche, "ecTarget", null)]);
        _ueberlagerung.Auffrischen();

        Assert.Equal(plan, Spalte().EcTarget);
        Assert.False(_ueberlagerung.IstGeaendert(Programm, Woche, "ecTarget"));
    }

    [Fact]
    public void NachDemNeuLadenGiltDerEigeneWertWeiter()
    {
        _repo.Speichern(Programm, [(Woche, "rhMax", 57)]);
        _ueberlagerung.Auffrischen();

        _wissen.Reload();

        Assert.Equal(57, Spalte().RhMax);
    }

    [Fact]
    public void ZweimalAendernMerktSichDenPlanNichtDenZwischenstand()
    {
        var plan = Spalte().Co2Min;

        _repo.Speichern(Programm, [(Woche, "co2Min", 900)]);
        _ueberlagerung.Auffrischen();
        _repo.Speichern(Programm, [(Woche, "co2Min", 1000)]);
        _ueberlagerung.Auffrischen();

        Assert.Equal(1000, Spalte().Co2Min);
        Assert.Equal(plan, _ueberlagerung.Planwert(Spalte(), Feld("co2Min")));
    }

    [Fact]
    public void AndereSpaltenBleibenUnberuehrt()
    {
        var nachbar = _wissen.NutrientPrograms.Single(p => p.Id == Programm)
            .FeedChart!.Columns.First(c => c.Id != Woche);
        var vorher = nachbar.WaterTempNightC;

        _repo.Speichern(Programm, [(Woche, "waterTempNightC", 19.5)]);
        _ueberlagerung.Auffrischen();

        Assert.Equal(vorher, nachbar.WaterTempNightC);
    }

    [Fact]
    public void JedesFeldSchreibtAufSichSelbst()
    {
        // Ein vertauschter Setter (phMin schreibt phMax) fiele sonst erst am
        // falschen Alarm auf.
        var spalte = new FeedChartColumn();
        foreach (var feld in Wochenwertfelder.Alle)
        {
            var probe = feld.Min + feld.Schritt;
            feld.Schreiben(spalte, probe);
            Assert.Equal(probe, feld.Lesen(spalte));
            feld.Schreiben(spalte, null);
        }
        Assert.Equal(Wochenwertfelder.Alle.Count, Wochenwertfelder.Alle.Select(f => f.Name).Distinct().Count());
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
