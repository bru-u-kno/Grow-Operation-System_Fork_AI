using GrowDiary.Web.Models;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Tests.Services.GrowPlan;

/// <summary>
/// Fork AI (forkai.132): einheitliche Wochennamen im Plan des Grows — die
/// Herstellertabelle behält ihre Begriffe (Bru, 21.09.2026).
/// </summary>
public sealed class WochennamenTests
{
    private static string Name(string stage, int? week, string label)
        => GrowPlanBauer.Wochenname(new FeedChartColumn { Id = "x", Stage = stage, Week = week, Label = label });

    [Theory]
    [InlineData("Flower", 5, "Flores · Woche 5", "Blütewoche 5")]
    [InlineData("Veg", 2, "Vega · Woche 2", "Vegiwoche 2")]
    [InlineData("Clone", null, "Root · Bewurzelung", "Bewurzelung")]
    [InlineData("Finish", null, "Flush", "Flush")]
    [InlineData("Veg", 1, "Veg · Woche 1", "Vegiwoche 1")]
    [InlineData("Flower", 9, "Blüte · Woche 9", "Blütewoche 9")]
    public void SkxUndAthenaHeissenGleich(string stage, int? week, string label, string erwartet)
        => Assert.Equal(erwartet, Name(stage, week, label));

    [Theory]
    [InlineData("Klon · Vorweichen")]
    [InlineData("Klon · Anfüttern")]
    public void MehrereSchritteEinerPhaseBleibenUnterscheidbar(string label)
        => Assert.Equal(label, Name("Clone", null, label));

    [Fact]
    public void AngleichenMeldetNurEchteAenderungen()
    {
        var inhalt = new GrowPlanInhalt();
        inhalt.Chart.Columns.Add(new FeedChartColumn { Id = "flower-w5", Stage = "Flower", Week = 5, Label = "Flores · Woche 5" });

        Assert.True(GrowPlanBauer.WochennamenAngleichen(inhalt));
        Assert.Equal("Blütewoche 5", inhalt.Chart.Columns[0].Label);
        Assert.False(GrowPlanBauer.WochennamenAngleichen(inhalt));
    }

    [Fact]
    public void DasProgrammSelbstBleibtUnveraendert()
    {
        // Der Plan ist eine Kopie — die Herstellertabelle (Wissen) behält „Flores".
        var programm = new NutrientProgramDefinition
        {
            Id = "skx", Name = "SKX",
            FeedChart = new FeedChartDefinition { Columns = [new FeedChartColumn { Id = "flower-w5", Stage = "Flower", Week = 5, Label = "Flores · Woche 5" }] },
        };
        var inhalt = GrowPlanBauer.AusProgramm(programm, _ => null, 4, 8);

        Assert.Equal("Blütewoche 5", inhalt.Chart.Columns[0].Label);
        Assert.Equal("Flores · Woche 5", programm.FeedChart.Columns[0].Label);
    }
}
