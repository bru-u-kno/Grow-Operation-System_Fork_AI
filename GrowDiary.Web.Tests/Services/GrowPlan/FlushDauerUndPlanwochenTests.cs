using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services.GrowPlan;

/// <summary>
/// Flush eine oder zwei Wochen — und der Plan führt die Blütewochen bis dorthin.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (16.10.2026).</b> „Blütewoche 8 von 8" bei einer Sorte mit 9–10 Wochen:
/// Das SKX-Programm bringt acht Blütewochen mit, der Flush war fest auf 14 Tage vor der
/// Ernte gerechnet, und weitere Wochen entstanden erst am Tag, an dem die Blüte sie erreichte.
/// Der Nutzer wollte den Flush selbst wählen und die Werte neuer Wochen vorher prüfen können.</para>
///
/// <para><b>Die Regel.</b> Finish = Ernte laut Sorte (Maximum) minus Flush-Tage (7 oder 14,
/// Vorgabe 14). Die Blütewochen des Plans reichen bis dorthin; fehlende werden sofort aus der
/// letzten Woche kopiert und als „ungeprüft" vermerkt, bis der Nutzer sie bestätigt.</para>
/// </remarks>
public sealed class FlushDauerUndPlanwochenTests : IDisposable
{
    private static readonly DateTime Heute = new(2026, 10, 16);
    private const int Basis = 930_000;

    private readonly string _wurzel;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly GrowPlanRepository _repo;
    private readonly GrowPlanService _dienst;
    private readonly List<int> _benutzt = [];

    public FlushDauerUndPlanwochenTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "FlushDauer_" + Guid.NewGuid().ToString("N"));
        var pfade = new AppPaths(_wurzel);
        KopiereWissen(_wurzel);
        _wissen = new KnowledgeBaseLoader(pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        _wissen.Initialize();
        _repo = new GrowPlanRepository(pfade);
        _dienst = new GrowPlanService(_repo, _wissen, new TargetValueService(_wissen), NullLogger<GrowPlanService>.Instance,
            new EigeneProgramme(pfade, _wissen, NullLogger<EigeneProgramme>.Instance));
    }

    public void Dispose()
    {
        foreach (var id in _benutzt) GrowPlanRegister.Entfernen(id);
        try { Directory.Delete(_wurzel, recursive: true); } catch { }
    }

    /// <summary>Flip am 23.08.2026, Sorte 9–10 Wochen — wie der Mimosa-Fall.</summary>
    private GrowRun Mimosa(int nummer, int flushTage, int? maxWochen = 10, string programm = "skx-canna-aqua")
    {
        _benutzt.Add(Basis + nummer);
        var flip = new DateTime(2026, 8, 23);
        var start = flip.AddDays(-28); // vier Vegiwochen = die des Programms; sonst zählen angehängte Vegiwochen mit
        return new GrowRun
        {
            Id = Basis + nummer,
            Name = $"Mimosa {nummer}",
            FeedProgramId = programm,
            HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running,
            StartMaterial = StartMaterial.Seed,
            EntryPoint = GrowEntryPoint.Germination,
            StartDate = start,
            GerminatedAt = start,
            VegStartedAt = start,
            FlipDate = flip,
            BreederFlowerWeeksMin = 9,
            BreederFlowerWeeksMax = maxWochen,
            BreederWochenEigen = true,
            FlushTage = flushTage,
        };
    }

    private GrowPlanInhalt Arbeit(int growId) => _repo.Laden(growId, GrowPlanStaende.Arbeit)!.Inhalt;

    // ---- Finish-Beginn --------------------------------------------------------------------

    [Theory]
    [InlineData(14, "2026-10-18")] // zwei Wochen vor der Ernte am 01.11.
    [InlineData(7, "2026-10-25")]  // eine Woche vor der Ernte
    public void FinishBeginn_FolgtDerGewaehltenFlushDauer(int flushTage, string erwartet)
    {
        var stand = Phasenanker.Fuer(Mimosa(1, flushTage), Heute);
        Assert.Equal(DateTime.Parse(erwartet), stand.FinishAb);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(21)]
    [InlineData(-7)]
    public void UngueltigeFlushDauer_GiltAlsVorgabeZweiWochen(int unsinn)
    {
        Assert.Equal(14, GrowRun.FlushTageGueltig(unsinn));
        Assert.Equal(new DateTime(2026, 10, 18), Phasenanker.Fuer(Mimosa(2, unsinn), Heute).FinishAb);
    }

    [Fact]
    public void BestaetigtesFinish_GehtDerFlushDauerVor()
    {
        var grow = Mimosa(3, 14);
        grow.FinishStartedAt = new DateTime(2026, 10, 20);
        Assert.Equal(new DateTime(2026, 10, 20), Phasenanker.Fuer(grow, Heute).FinishAb);
    }

    // ---- Blütewochen im Plan --------------------------------------------------------------

    [Fact]
    public void ZweiWochenFlush_PlanBleibtBeiAchtBluetewochen()
    {
        var grow = Mimosa(4, 14);
        _dienst.Anlegen(grow);

        Assert.Equal(0, _dienst.WochenNachziehen(grow, Heute));
        Assert.Equal(8, Arbeit(grow.Id).Chart.Columns.Count(c => c.Stage == "Flower" && c.Week is not null));
        Assert.Empty(Arbeit(grow.Id).Ungeprueft);
    }

    [Fact]
    public void EineWocheFlush_LegtBluetewoche9SofortAn_UngeprueftUndMitDenWertenVonWoche8()
    {
        var grow = Mimosa(5, 7);
        _dienst.Anlegen(grow);
        var w8 = Arbeit(grow.Id).Chart.Columns.Single(c => c.Id == "flower-w8");

        // Heute ist Blütetag 55 (Woche 8) — Woche 9 beginnt erst am 18.10., der Plan hat sie schon.
        Assert.Equal(1, _dienst.WochenNachziehen(grow, Heute));

        var arbeit = Arbeit(grow.Id);
        var w9 = Assert.Single(arbeit.Chart.Columns, c => c.Stage == "Flower" && c.Week == 9);
        Assert.Equal("Blütewoche 9", w9.Label);
        Assert.True(arbeit.IstVerlaengert(w9.Id));
        Assert.Equal("flower-w8", arbeit.Verlaengert[w9.Id]);
        Assert.True(arbeit.IstUngeprueft(w9.Id));
        foreach (var feld in Wochenwertfelder.Alle) Assert.Equal(feld.Lesen(w8), feld.Lesen(w9));
        // Noch läuft Woche 8 — die laufende Woche ändert sich durch das Anlegen nicht.
        Assert.Equal("flower-w8", MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms, Heute)?.Spalte.Id);
    }

    [Fact]
    public void ZweiterLauf_UndWechselZurueckAufZweiWochen_LoeschenNichts()
    {
        var grow = Mimosa(6, 7);
        _dienst.Anlegen(grow);
        _dienst.WochenNachziehen(grow, Heute);
        var vorher = Arbeit(grow.Id).Chart.Columns.Count;

        Assert.Equal(0, _dienst.WochenNachziehen(grow, Heute));

        grow.FlushTage = 14; // Entscheidung zurückgenommen: die angelegte Woche bleibt stehen
        Assert.Equal(0, _dienst.WochenNachziehen(grow, Heute));
        Assert.Equal(vorher, Arbeit(grow.Id).Chart.Columns.Count);
        Assert.True(Arbeit(grow.Id).IstUngeprueft("flower-w9"));
    }

    [Fact]
    public void LaengereSorte_BekommtAlleFehlendenWochen_GeplantStattErstAmTag()
    {
        // Sorte 11–12 Wochen, zwei Wochen Flush: Finish nach 10 Wochen → Woche 9 und 10 fehlen im Plan.
        var grow = Mimosa(7, 14, maxWochen: 12);
        _dienst.Anlegen(grow);

        Assert.Equal(2, _dienst.WochenNachziehen(grow, Heute));
        var arbeit = Arbeit(grow.Id);
        Assert.Equal([9, 10], arbeit.Chart.Columns.Where(c => c.Stage == "Flower" && c.Week > 8).Select(c => c.Week!.Value).ToArray());
        Assert.True(arbeit.IstUngeprueft("flower-w9"));
        Assert.True(arbeit.IstUngeprueft("flower-w10"));
    }

    [Fact]
    public void WocheGeprueft_EntferntDieMarkierung_NurEinmalUndNurDieseWoche()
    {
        var grow = Mimosa(8, 14, maxWochen: 12);
        _dienst.Anlegen(grow);
        _dienst.WochenNachziehen(grow, Heute);

        Assert.True(_dienst.WocheGeprueft(grow.Id, "flower-w9"));
        var arbeit = Arbeit(grow.Id);
        Assert.False(arbeit.IstUngeprueft("flower-w9"));
        Assert.True(arbeit.IstUngeprueft("flower-w10"));
        Assert.False(_dienst.WocheGeprueft(grow.Id, "flower-w9"));   // zweites Mal: nichts mehr zu tun
        Assert.False(_dienst.WocheGeprueft(grow.Id, "flower-w3"));   // keine angehängte Woche
        // Die Werte der Woche selbst bleiben unberührt, und das Plan-Register kennt den Stand.
        Assert.False(GrowPlanRegister.Inhalt(grow.Id)!.IstUngeprueft("flower-w9"));
    }

    [Fact]
    public void AltePlaeneOhneVermerk_LoesenKeineErinnerungAus()
    {
        // Ein Plan, der vor dieser Einführung Wochen angehängt bekam, kennt die Liste nicht:
        // Das Feld fehlt im gespeicherten JSON → leer → keine Aufgabe je alter Woche.
        var inhalt = System.Text.Json.JsonSerializer.Deserialize<GrowPlanInhalt>(
            "{\"programmId\":\"x\",\"programmName\":\"x\",\"verlaengert\":{\"veg-w5\":\"veg-w4\"}}", new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })!;
        Assert.True(inhalt.IstVerlaengert("veg-w5"));
        Assert.Empty(inhalt.Ungeprueft);
    }

    [Fact]
    public void VegiwochenWerdenNichtAlsUngeprueftVermerkt()
    {
        // Nur Blütewochen lösen die Erinnerung aus; eine angehängte Vegiwoche ist Alltag.
        var grow = Mimosa(9, 14);
        grow.StartDate = grow.StartDate.AddDays(-20);
        grow.GerminatedAt = grow.StartDate;
        grow.VegStartedAt = grow.StartDate;
        _dienst.Anlegen(grow);
        _dienst.WochenNachziehen(grow, Heute);

        var arbeit = Arbeit(grow.Id);
        Assert.Contains(arbeit.Chart.Columns, c => c.Stage == "Veg" && c.Week > 4);
        Assert.Empty(arbeit.Ungeprueft);
    }

    [Fact]
    public void EinTippfehlerBeiDenZuechterwochen_LegtKeinenRiesenplanAn()
    {
        // 90 statt 9 Wochen: gedeckelt bei zwanzig, denn angelegte Wochen werden nie entfernt.
        var grow = Mimosa(12, 14, maxWochen: 90);
        _dienst.Anlegen(grow);

        Assert.Equal(Planwochen.MaxGeplanteBluetewochen, Planwochen.GeplanteBluetewochen(Phasenanker.Fuer(grow, Heute)));
        _dienst.WochenNachziehen(grow, Heute);
        Assert.Equal(20, Arbeit(grow.Id).Chart.Columns.Where(c => c.Stage == "Flower" && c.Week is not null).Max(c => c.Week!.Value));
    }

    [Fact]
    public void Programmwechsel_BehaeltGeprueftWennDieAenderungenMitgehen_SonstWiederZuPruefen()
    {
        // Athena führt neun Blütewochen; Sorte 11–12 Wochen, zwei Wochen Flush → zehn → Woche 10 wird angehängt.
        var grow = Mimosa(13, 14, maxWochen: 12, programm: "athena");
        _dienst.Anlegen(grow);
        _dienst.WochenNachziehen(grow, Heute);
        Assert.True(Arbeit(grow.Id).IstUngeprueft("flower-w10"));
        Assert.True(_dienst.WocheGeprueft(grow.Id, "flower-w10"));

        // Wechsel auf SKX (acht Blütewochen) MIT Änderungen: Woche 10 war geprüft und bleibt es;
        // Woche 9 kam im alten Plan aus dem Programm und ist jetzt neu angehängt → zu prüfen.
        _dienst.ProgrammWechseln(grow, "skx-canna-aqua", aenderungenBehalten: true, heute: Heute);
        var behalten = Arbeit(grow.Id);
        Assert.True(behalten.IstUngeprueft("flower-w9"));
        Assert.False(behalten.IstUngeprueft("flower-w10"));

        // Zurück auf Athena (mit Änderungen): Woche 10 war geprüft und bleibt es.
        _dienst.ProgrammWechseln(grow, "athena", aenderungenBehalten: true, heute: Heute);
        Assert.False(Arbeit(grow.Id).IstUngeprueft("flower-w10"));

        // Dann OHNE Änderungen auf SKX: die Werte sind neu kopiert und wieder zu prüfen.
        _dienst.ProgrammWechseln(grow, "skx-canna-aqua", aenderungenBehalten: false, heute: Heute);
        var verworfen = Arbeit(grow.Id);
        Assert.True(verworfen.IstUngeprueft("flower-w9"));
        Assert.True(verworfen.IstUngeprueft("flower-w10"));
    }

    // ---- Planwochen rein ------------------------------------------------------------------

    [Theory]
    [InlineData(14, 8)]
    [InlineData(7, 9)]
    public void GeplanteBluetewochen_SindErnteMinusFlush(int flushTage, int erwartet)
    {
        Assert.Equal(erwartet, Planwochen.GeplanteBluetewochen(Phasenanker.Fuer(Mimosa(10, flushTage), Heute)));
    }

    [Fact]
    public void GeplanteBluetewochen_OhneFlipOderWochen_SindNull()
    {
        var ohneWochen = Mimosa(11, 14);
        ohneWochen.BreederFlowerWeeksMin = null;
        ohneWochen.BreederFlowerWeeksMax = null;
        // Ohne Züchterwochen kommt kein Finish zustande — nichts zu planen, nichts zu behaupten.
        Assert.Equal(0, Planwochen.GeplanteBluetewochen(Phasenanker.Fuer(ohneWochen, Heute)));
    }

    private static void KopiereWissen(string wurzel)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) dir = Path.GetDirectoryName(dir);
        var quelle = Path.Combine(dir ?? throw new InvalidOperationException("Projektwurzel nicht gefunden."),
            "GrowDiary.Web", "wwwroot", "knowledge-defaults");
        var ziel = Path.Combine(wurzel, "wwwroot", "knowledge-defaults");
        foreach (var datei in Directory.EnumerateFiles(quelle, "*.json", SearchOption.AllDirectories))
        {
            var pfad = Path.Combine(ziel, Path.GetRelativePath(quelle, datei));
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.Copy(datei, pfad);
        }
    }
}
