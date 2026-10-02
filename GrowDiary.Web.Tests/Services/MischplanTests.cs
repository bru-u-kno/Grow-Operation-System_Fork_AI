using System.Text.Json;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Der Mischplan: aus „nach Plan zugeben" werden konkrete Milliliter.
/// </summary>
/// <remarks>
/// <para>Der Kern der Begleitung — und der Teil, bei dem eine falsche Zahl
/// direkt im Reservoir landet. Deshalb prüfen die Tests zweierlei: dass die
/// ausgelieferten Chart-Daten wirklich die Werte aus dem Athena-PDF tragen
/// (Stichproben gegen die Quelle), und dass die Spaltenwahl zur Lage des
/// Grows passt.</para>
///
/// <para>Der Chart-Test liest die VORGABEN unter wwwroot/knowledge-defaults —
/// nicht die Laufzeit-Kopie. Genau diese Verwechslung hat die
/// Blended-Änderung aus beta.27 gekostet: sie lag nur im gitignorierten
/// App_Data und wurde nie ausgeliefert.</para>
/// </remarks>
public sealed class MischplanTests
{
    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "GrowDiary.slnx")))
        {
            dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("Projektwurzel nicht gefunden.");
        }
        return dir;
    }

    private static FeedChartDefinition GeliefertesChart()
    {
        var pfad = Path.Combine(FindProjectRoot(),
            "GrowDiary.Web", "wwwroot", "knowledge-defaults", "nutrient-programs", "athena-blended.json");
        var programm = JsonSerializer.Deserialize<NutrientProgramDefinition>(File.ReadAllText(pfad))!;
        Assert.NotNull(programm.FeedChart);
        return programm.FeedChart!;
    }

    [Fact]
    public void TheShippedChartCarriesTheNumbersFromTheAthenaPdf()
    {
        var chart = GeliefertesChart();

        Assert.Equal(16, chart.Columns.Count);
        Assert.Contains("÷ 3,785", chart.Note);

        // Stichproben woertlich gegen das PDF (mL/gal): Veg Grow A 11 → 2,91 ml/L;
        // Bluete W4 PK 6 → 1,59; CaMg-Spanne 3–5 → 0,79–1,32.
        var vegW1 = chart.Columns.Single(c => c.Id == "veg-w1");
        Assert.Equal(2.91, vegW1.Items.Single(i => i.Component == "Grow A").MinMlPerLiter);
        Assert.Equal(2.1, vegW1.EcTarget);

        var flowerW4 = chart.Columns.Single(c => c.Id == "flower-w4");
        Assert.Equal(1.59, flowerW4.Items.Single(i => i.Component == "PK").MinMlPerLiter);
        Assert.Equal(2.6, flowerW4.EcTarget);

        var camg = vegW1.Items.Single(i => i.Component == "CaMg");
        Assert.Equal(0.79, camg.MinMlPerLiter);
        Assert.Equal(1.32, camg.MaxMlPerLiter);

        // CaMg endet laut Chart nach Bluete-Woche 7.
        Assert.DoesNotContain(chart.Columns.Single(c => c.Id == "flower-w8").Items, i => i.Component == "CaMg");
    }

    [Fact]
    public void TheColumnFollowsTheGrowsWeek()
    {
        var chart = GeliefertesChart();

        // Veg-Woche 2: Start vor 10 Tagen, kein Flip.
        var veg = new GrowRun { StartDate = DateTime.Today.AddDays(-10), VegStartedAt = DateTime.Today.AddDays(-10) };
        Assert.Equal("veg-w2", MischplanService.SpalteFuer(chart, veg)!.Id);

        // Bluete-Woche 3: Flip vor 15 Tagen.
        var flower = new GrowRun { StartDate = DateTime.Today.AddDays(-60), FlipDate = DateTime.Today.AddDays(-15) };
        Assert.Equal("flower-w3", MischplanService.SpalteFuer(chart, flower)!.Id);
    }

    [Fact]
    public void WeeksBeyondTheChartKeepTheLastColumnInsteadOfGoingSilent()
    {
        var chart = GeliefertesChart();

        // Veg-Woche 7 — das Chart kennt nur 4. Woche 4 gilt weiter; sonst
        // stuende der Nutzer mitten in einer langen Veg ploetzlich ohne Plan.
        var langeVeg = new GrowRun { StartDate = DateTime.Today.AddDays(-45), VegStartedAt = DateTime.Today.AddDays(-45) };
        Assert.Equal("veg-w4", MischplanService.SpalteFuer(chart, langeVeg)!.Id);
    }

    [Fact]
    public void TheChartTargetsOnlyApplyWhenTheGrowAsksForThem()
    {
        var chart = GeliefertesChart();
        var grow = new GrowRun
        {
            StartDate = DateTime.Today.AddDays(-60),
            FlipDate = DateTime.Today.AddDays(-15),
            FeedProgramId = "athena",
        };
        var programme = new[] { new NutrientProgramDefinition { Id = "athena", Name = "Athena Blended", FeedChart = chart } };

        // Standard: das Chart mischt, entscheidet aber nicht ueber die Sollwerte.
        Assert.Null(MischplanService.ZielSpalteFuerGrow(grow, programme));

        grow.UseFeedChartTargets = true;
        var ziel = MischplanService.ZielSpalteFuerGrow(grow, programme);
        Assert.NotNull(ziel);
        Assert.Equal("flower-w3", ziel!.Value.Spalte.Id);
        Assert.Contains("Athena Blended", ziel.Value.Herkunft);
    }

    [Fact]
    public void TheChartMovesTheEcBandButKeepsItsWidth()
    {
        // Das Chart nennt EINE Zahl. Ein Ziel ohne Breite waere unbrauchbar —
        // jede Messung laege daneben. Also wandert das Band, es schrumpft nicht.
        var basis = new HydroTargetValues(
            PhMin: 5.5, PhMax: 6.5, EcMin: 1.8, EcMax: 2.2, OrpMin: 250, OrpMax: 350,
            WaterTempDayC: 20, WaterTempNightC: 19, VpdMin: 1.0, VpdMax: 1.4,
            PpfdMin: 600, PpfdMax: 900, Co2Min: 400, Co2Max: 800);

        var spalte = new FeedChartColumn { Id = "x", Label = "Test", EcTarget = 2.6, PhMin = 6.0, PhMax = 6.4 };
        var mitChart = MischplanService.MitFeedchart(basis, spalte);

        Assert.Equal(2.4, mitChart.EcMin, 3);
        Assert.Equal(2.8, mitChart.EcMax, 3);
        Assert.Equal(0.4, mitChart.EcMax - mitChart.EcMin, 3); // Breite unveraendert
        Assert.Equal(6.0, mitChart.PhMin);
        Assert.Equal(6.4, mitChart.PhMax);

        // Wovon das Chart nichts weiss, bleibt unangetastet.
        Assert.Equal(basis.OrpMin, mitChart.OrpMin);
        Assert.Equal(basis.WaterTempDayC, mitChart.WaterTempDayC);
        Assert.Equal(basis.PpfdMax, mitChart.PpfdMax);
    }

    [Fact]
    public void AnAutoflowerCountsFlowerWeeksFromFlowerStartNotFromSeed()
    {
        var chart = GeliefertesChart();

        // 35 Tage seit Keimung, „Blüte beginnt" an Tag 28 bestätigt — die
        // Pflanze steht in Bluetewoche 2. Ein Rechner, der die GESAMTwoche 6
        // nimmt, griffe im Chart zwei bis vier Spalten zu weit rechts.
        var auto = new GrowRun
        {
            SeedType = SeedType.Autoflower,
            StartDate = DateTime.Today.AddDays(-35),
            GerminatedAt = DateTime.Today.AddDays(-35),
            VegStartedAt = DateTime.Today.AddDays(-21),
            FlipDate = DateTime.Today.AddDays(-7),
        };

        Assert.Equal("flower-w2", MischplanService.SpalteFuer(chart, auto)!.Id);

        // Ohne Bestätigung keine Blüte — die Vegi-Spalte bleibt, bis jemand drückt.
        auto.FlipDate = null;
        Assert.StartsWith("veg-", MischplanService.SpalteFuer(chart, auto)!.Id);
    }
}
