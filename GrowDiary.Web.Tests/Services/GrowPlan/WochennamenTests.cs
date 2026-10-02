using GrowDiary.Web.Models;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Tests.Services.GrowPlan;

/// <summary>
/// Fork AI (forkai.132): einheitliche Wochennamen im Plan des Grows — die
/// Herstellertabelle behält ihre Begriffe (Bru, 21.09.2026).
/// </summary>
/// <remarks>
/// Seit 02.10.2026 heißt die Anzucht nach dem Startmaterial (Entscheidung des
/// Nutzers): Steckling „Bewurzelung", „Bewurzelung 2" …; Samen „Anzucht",
/// „Anzucht 2" … (<see cref="GrowPlanBauer.Anzuchtname"/>).
/// </remarks>
public sealed class WochennamenTests
{
    private static string Name(string stage, int? week, string label, StartMaterial material = StartMaterial.Seed)
        => GrowPlanBauer.Wochenname(new FeedChartColumn { Id = "x", Stage = stage, Week = week, Label = label }, material);

    [Theory]
    [InlineData("Flower", 5, "Flores · Woche 5", "Blütewoche 5")]
    [InlineData("Veg", 2, "Vega · Woche 2", "Vegiwoche 2")]
    [InlineData("Finish", null, "Flush", "Flush")]
    [InlineData("Veg", 1, "Veg · Woche 1", "Vegiwoche 1")]
    [InlineData("Flower", 9, "Blüte · Woche 9", "Blütewoche 9")]
    public void SkxUndAthenaHeissenGleich(string stage, int? week, string label, string erwartet)
    {
        // Außerhalb der Anzucht spielt das Startmaterial keine Rolle.
        Assert.Equal(erwartet, Name(stage, week, label, StartMaterial.Seed));
        Assert.Equal(erwartet, Name(stage, week, label, StartMaterial.Clone));
    }

    [Theory]
    [InlineData(StartMaterial.Clone, "Bewurzelung")]
    [InlineData(StartMaterial.Seed, "Anzucht")]
    public void DieAnzuchtSpalteHeisstNachDemStartmaterial(StartMaterial material, string erwartet)
    {
        // Der Herstellername, das eigene Raster, beide Namen aus dem Plan, leer.
        foreach (var label in new[] { "Root · Bewurzelung", "Bewurzelung", "Anzucht", "" })
        {
            Assert.Equal(erwartet, Name("Clone", null, label, material));
        }
    }

    [Theory]
    [InlineData(StartMaterial.Clone, 2, "Bewurzelung 2")]
    [InlineData(StartMaterial.Clone, 5, "Bewurzelung 5")]
    [InlineData(StartMaterial.Seed, 2, "Anzucht 2")]
    [InlineData(StartMaterial.Seed, 5, "Anzucht 5")]
    public void AngehaengteAnzuchtWochenHeissenNachDemStartmaterial(StartMaterial material, int woche, string erwartet)
    {
        Assert.Equal(erwartet, Name("Clone", woche, "Bewurzelung 2", material));
        Assert.Equal(erwartet, Name("Seedling", woche, "Anzuchtwoche 2", material));
    }

    [Fact]
    public void NurDerStecklingBewurzeltSich()
    {
        // Grundmenge: jeder Wert des Enums. Alles außer Clone heißt „Anzucht" —
        // wie im Phasenanker; ein unbekannter Wert ist keine Behauptung „Steckling".
        var werte = Enum.GetValues<StartMaterial>();
        Assert.True(werte.Length >= 2, "Mengenwächter: StartMaterial hat weniger als zwei Werte.");
        foreach (var material in werte)
        {
            Assert.Equal(material == StartMaterial.Clone ? "Bewurzelung" : "Anzucht", GrowPlanBauer.Anzuchtname(material));
        }
        Assert.Equal("Anzucht", GrowPlanBauer.Anzuchtname((StartMaterial)99));
    }

    [Theory]
    [InlineData("Klon · Vorweichen")]
    [InlineData("Klon · Anfüttern")]
    public void MehrereSchritteEinerPhaseBleibenUnterscheidbar(string label)
    {
        Assert.Equal(label, Name("Clone", null, label, StartMaterial.Clone));
        Assert.Equal(label, Name("Clone", null, label, StartMaterial.Seed));
    }

    [Fact]
    public void AngleichenMeldetNurEchteAenderungen()
    {
        var inhalt = new GrowPlanInhalt();
        inhalt.Chart.Columns.Add(new FeedChartColumn { Id = "flower-w5", Stage = "Flower", Week = 5, Label = "Flores · Woche 5" });

        Assert.True(GrowPlanBauer.WochennamenAngleichen(inhalt, StartMaterial.Seed));
        Assert.Equal("Blütewoche 5", inhalt.Chart.Columns[0].Label);
        Assert.False(GrowPlanBauer.WochennamenAngleichen(inhalt, StartMaterial.Seed));
    }

    private static GrowPlanInhalt GespeicherterPlan(params string[] anzucht)
    {
        var inhalt = new GrowPlanInhalt();
        inhalt.Chart.Columns.Add(new FeedChartColumn { Id = "root", Stage = "Clone", Label = anzucht[0] });
        for (var i = 1; i < anzucht.Length; i++)
        {
            inhalt.Chart.Columns.Add(new FeedChartColumn { Id = $"clone-w{i + 1}", Stage = "Clone", Week = i + 1, Label = anzucht[i] });
        }
        inhalt.Chart.Columns.Add(new FeedChartColumn { Id = "veg-w1", Stage = "Veg", Week = 1, Label = "Vegiwoche 1" });
        return inhalt;
    }

    [Theory]
    // forkai.157 hat angehängte Anzucht-Wochen als „Anzuchtwoche N" gespeichert,
    // der Stand vom 02.10.2026 als „Bewurzelung N" — für Samen beides falsch.
    [InlineData("Bewurzelung", "Anzuchtwoche 2", "Anzuchtwoche 3")]
    [InlineData("Bewurzelung", "Bewurzelung 2", "Bewurzelung 3")]
    [InlineData("Root · Bewurzelung", "Bewurzelung 2", "Anzuchtwoche 3")]
    public void GespeicherteSamenPlaeneHeissenNachDemStartAnzucht(string spalte, string woche2, string woche3)
    {
        var inhalt = GespeicherterPlan(spalte, woche2, woche3);

        Assert.True(GrowPlanBauer.WochennamenAngleichen(inhalt, StartMaterial.Seed));
        Assert.Equal(["Anzucht", "Anzucht 2", "Anzucht 3", "Vegiwoche 1"], inhalt.Chart.Columns.Select(c => c.Label));
        Assert.False(GrowPlanBauer.WochennamenAngleichen(inhalt, StartMaterial.Seed));
    }

    [Fact]
    public void GespeicherteStecklingPlaeneHeissenNachDemStartBewurzelung()
    {
        var inhalt = GespeicherterPlan("Bewurzelung", "Anzuchtwoche 2");

        Assert.True(GrowPlanBauer.WochennamenAngleichen(inhalt, StartMaterial.Clone));
        Assert.Equal(["Bewurzelung", "Bewurzelung 2", "Vegiwoche 1"], inhalt.Chart.Columns.Select(c => c.Label));
        Assert.False(GrowPlanBauer.WochennamenAngleichen(inhalt, StartMaterial.Clone));
    }

    [Fact]
    public void EinGeaendertesStartmaterialBenenntInBeideRichtungenUm()
    {
        var inhalt = GespeicherterPlan("Bewurzelung", "Bewurzelung 2");

        Assert.True(GrowPlanBauer.WochennamenAngleichen(inhalt, StartMaterial.Seed));
        Assert.Equal(["Anzucht", "Anzucht 2", "Vegiwoche 1"], inhalt.Chart.Columns.Select(c => c.Label));
        Assert.True(GrowPlanBauer.WochennamenAngleichen(inhalt, StartMaterial.Clone));
        Assert.Equal(["Bewurzelung", "Bewurzelung 2", "Vegiwoche 1"], inhalt.Chart.Columns.Select(c => c.Label));
    }

    [Theory]
    [InlineData(StartMaterial.Clone, "Bewurzelung")]
    [InlineData(StartMaterial.Seed, "Anzucht")]
    public void EinNeuerPlanHeisstNachDemStartmaterial(StartMaterial material, string erwartet)
    {
        // SKX nennt die Spalte „Root · Bewurzelung", ein Programm ohne Wochen bekommt das Raster.
        var mitChart = new NutrientProgramDefinition
        {
            Id = "skx", Name = "SKX",
            FeedChart = new FeedChartDefinition { Columns = [new FeedChartColumn { Id = "root", Stage = "Clone", Label = "Root · Bewurzelung" }] },
        };
        var ohneChart = new NutrientProgramDefinition { Id = "vbx", Name = "VBX" };

        Assert.Equal(erwartet, GrowPlanBauer.AusProgramm(mitChart, _ => null, 4, 8, material).Chart.Columns[0].Label);
        Assert.Equal(erwartet, GrowPlanBauer.AusProgramm(ohneChart, _ => null, 4, 8, material).Chart.Columns[0].Label);
        Assert.Equal("Root · Bewurzelung", mitChart.FeedChart.Columns[0].Label);
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
        var inhalt = GrowPlanBauer.AusProgramm(programm, _ => null, 4, 8, StartMaterial.Seed);

        Assert.Equal("Blütewoche 5", inhalt.Chart.Columns[0].Label);
        Assert.Equal("Flores · Woche 5", programm.FeedChart.Columns[0].Label);
    }

    [Fact]
    public void DieStartmeldungNenntNurWasGeschah()
    {
        // Bis 02.10.2026: „N Planstände um das EC-Band ergänzt" — auch für Umbenennungen.
        var nurNamen = new PlanNachtrag(Planstaende: 3, EcBand: 0, NachtLuft: 0, Wochennamen: 3).Meldungen().ToList();
        Assert.Equal(["Grow-Plan: Wochennamen in 3 Planständen angeglichen."], nurNamen);
        Assert.DoesNotContain(nurNamen, m => m.Contains("EC-Band"));

        var alles = new PlanNachtrag(Planstaende: 2, EcBand: 1, NachtLuft: 2, Wochennamen: 1).Meldungen().ToList();
        Assert.Equal(
            ["Grow-Plan: 1 Planstand um das EC-Band ergänzt.", "Grow-Plan: 2 Planstände um „Luft Nacht“ ergänzt.", "Grow-Plan: Wochennamen in 1 Planstand angeglichen."],
            alles);
        Assert.Empty(new PlanNachtrag(0, 0, 0, 0).Meldungen());
    }
}
