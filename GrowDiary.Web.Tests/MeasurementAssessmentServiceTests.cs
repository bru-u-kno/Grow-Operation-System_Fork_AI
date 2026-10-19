using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Der Beurteiler des Messprotokolls.
///
/// <para>Die Tests halten vor allem die drei Entscheidungen fest, an denen ein
/// naiver Beurteiler in die Irre läuft — und die ihn zu einer Anzeige machen
/// würden, die bei jeder Messung schreit und der Diagnoseseite widerspricht.</para>
/// </summary>
public sealed class MeasurementAssessmentServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly MeasurementAssessmentService _svc;

    public MeasurementAssessmentServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "AssessTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var projectRoot = FindProjectRoot();
        CopyDefaults(Path.Combine(projectRoot, "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _tempRoot);

        var paths = new AppPaths(_tempRoot);
        var loader = new KnowledgeBaseLoader(paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();

        _svc = new MeasurementAssessmentService(new TargetValueService(loader));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private static GrowRun Grow() => new()
    {
        Id = 1,
        Name = "Test",
        StartDate = DateTime.Today.AddDays(-60),
        HydroStyle = HydroStyle.RDWC,
        IrrigationType = IrrigationType.ActiveHydro,
    };

    private static Measurement Messung(double? ph = null, double? ec = null, double? wasser = null,
        double? luft = null, double? feuchte = null, DateTime? wann = null, GrowStage stufe = GrowStage.Veg)
        => new()
        {
            Id = 1,
            GrowId = 1,
            TakenAt = wann ?? DateTime.Now.AddHours(-2),
            Stage = stufe,
            Source = ValueOrigin.Manual,
            ReservoirPh = ph,
            ReservoirEc = ec,
            ReservoirWaterTempC = wasser,
            AirTemperatureC = luft,
            HumidityPercent = feuchte,
        };

    [Fact]
    public void PH_wird_gegen_die_Komfortzone_geprueft_nicht_gegen_das_Anmischziel()
    {
        // Der Phasenwert ist das Anmischziel — im RDWC darf der pH innerhalb der
        // Komfortzone 5,8–6,2 wandern, ab Blütewoche 4 sogar absichtlich. Gegen
        // den Phasenwert geprüft wären im echten Bestand ALLE acht Messungen rot
        // (5,95 bis 6,06), und das Protokoll widerspräche der Diagnoseseite.
        var bericht = _svc.Assess(Grow(), new[] { Messung(ph: 6.05) });
        var ph = bericht.Measurements[0].Metrics.Single(m => m.Metric == "ph");

        Assert.Equal(AssessmentVerdict.InTarget, ph.Verdict);
        Assert.Equal(5.8, ph.TargetMin);
        Assert.Equal(6.2, ph.TargetMax);
        Assert.Contains("Komfortzone", ph.Note);
    }

    [Fact]
    public void PH_ausserhalb_der_Komfortzone_faellt_auf()
    {
        var bericht = _svc.Assess(Grow(), new[] { Messung(ph: 6.6) });
        Assert.Equal(AssessmentVerdict.Above, bericht.Measurements[0].Metrics.Single(m => m.Metric == "ph").Verdict);
    }

    [Fact]
    public void Wassertemperatur_wird_gegen_den_Arbeitsbereich_geprueft_nicht_gegen_ein_Band_der_Breite_null()
    {
        // Gemessen am echten Profil: in der Veg-Phase sind Tag- und Nachtwert
        // beide 20 °C. Gegen dieses Band geprüft wären 19,7 UND 20,3 rot — bei
        // einem Wert, den niemand auf ein Zehntel genau hält.
        var bericht = _svc.Assess(Grow(), new[] { Messung(wasser: 20.3) });
        var wasser = bericht.Measurements[0].Metrics.Single(m => m.Metric == "water-temp");

        Assert.Equal(AssessmentVerdict.InTarget, wasser.Verdict);
        Assert.Equal(17, wasser.TargetMin);
        Assert.Equal(22, wasser.TargetMax);
    }

    [Fact]
    public void Zeitstempel_aus_der_Zukunft_fliegt_aus_der_Bilanz_bleibt_aber_sichtbar()
    {
        // Der echte Bestand enthält eine Zeile mit dem Datum 2099. Sie hat die
        // Diagnose sechs Wochen lang bestimmt. Sie darf die Bilanz nicht
        // verfälschen — aber verschwinden darf sie auch nicht, sonst sucht
        // jemand eine Messung, die er eingetragen hat.
        var bericht = _svc.Assess(Grow(), new[]
        {
            Messung(ph: 6.0),
            Messung(ph: 6.0, wann: new DateTime(2099, 1, 1)),
        });

        Assert.Equal(2, bericht.MeasurementCount);
        Assert.Equal(1, bericht.ExcludedCount);
        Assert.Equal(1, bericht.CheckedValueCount);
        var zukunft = bericht.Measurements.Single(m => m.Excluded);
        Assert.Contains("außerhalb des Laufs", zukunft.ExcludedReason);
        Assert.Empty(zukunft.Metrics);
    }

    [Fact]
    public void Geloester_Sauerstoff_bekommt_kein_Urteil_sondern_einen_Grund()
    {
        // Es gibt kein Profilfeld dafür, nur eine SOP-Schwelle. Eine leere Zelle
        // sähe aus wie „in Ordnung" — deshalb steht der Grund da.
        var m = Messung();
        m.DissolvedOxygenMgL = 7.2;
        var bericht = _svc.Assess(Grow(), new[] { m });
        var sauerstoff = bericht.Measurements[0].Metrics.Single(x => x.Metric == "do");

        Assert.Equal(AssessmentVerdict.NoTarget, sauerstoff.Verdict);
        Assert.Contains("6,5", sauerstoff.Note);
        Assert.Equal(0, bericht.CheckedValueCount);
    }

    [Fact]
    public void VPD_wird_aus_dem_Luft_Feuchte_Paar_derselben_Zeile_gerechnet()
    {
        var bericht = _svc.Assess(Grow(), new[] { Messung(luft: 25.0, feuchte: 60.0) });
        var vpd = bericht.Measurements[0].Metrics.Single(m => m.Metric == "vpd");

        Assert.True(vpd.Value > 0);
        Assert.Contains("nicht belegbar", vpd.Note);
    }

    [Fact]
    public void Fehlende_Werte_erzeugen_gar_kein_Urteil()
    {
        // Nicht gemessen ist nicht dasselbe wie in Ordnung.
        var bericht = _svc.Assess(Grow(), new[] { Messung() });
        Assert.Empty(bericht.Measurements[0].Metrics);
        Assert.Equal(0, bericht.CheckedValueCount);
    }

    [Fact]
    public void Die_gerechnete_Phase_steht_neben_der_gespeicherten()
    {
        // Im echten Bestand läuft die gespeicherte Phase rückwärts, und die zehn
        // jüngsten Messungen sagen Veg, während der Grow in der Blüte ist.
        // Beurteilt wird gegen die GESPEICHERTE Phase; die gerechnete steht
        // daneben, damit man den Widerspruch sieht statt ihn stillschweigend zu
        // überschreiben.
        var grow = Grow();
        grow.FlipDate = DateTime.Today.AddDays(-30);
        var bericht = _svc.Assess(grow, new[] { Messung(ph: 6.0, stufe: GrowStage.Veg) });

        Assert.Equal(GrowStage.Veg, bericht.Measurements[0].StoredStage);
        Assert.Equal(GrowStage.Flower, bericht.Measurements[0].ComputedStage);
    }

    [Fact]
    public void Physikalisch_unmoegliche_Werte_zaehlen_nicht_als_Abweichung()
    {
        // Gefunden zwei Stunden nach dem Bau dieses Dienstes: er zaehlte
        // EC 99999 und Wassertemperatur 5000 °C als ganz normale Abweichungen
        // in seine Bilanz. Beides Testeintraege, die vor der Sperre
        // hereingekommen waren. Damit stand ueber dem Protokoll eine Zahl, die
        // schlechter aussah, als der Grow lief.
        var bericht = _svc.Assess(Grow(), new[] { Messung(ec: 99999, wasser: 5000) });
        var werte = bericht.Measurements[0].Metrics;

        // Ein EIGENES Urteil, nicht NoTarget. Das war der zweite Fehler an
        // derselben Stelle: „kein Zielband für diese Phase" und „den Wert kann
        // es nicht geben" liefen unter demselben Urteil, und die Anzeige malt
        // NoTarget bewusst unauffällig. Ergebnis war ein Protokoll, in dem
        // EC 99.999 aussah wie jede andere Zeile — der Nutzer hat es gefunden,
        // nicht dieser Test.
        Assert.All(werte, w => Assert.Equal(AssessmentVerdict.Impossible, w.Verdict));
        Assert.All(werte, w => Assert.Contains("Physikalisch nicht möglich", w.Note));
        Assert.Equal(0, bericht.CheckedValueCount);
        Assert.Equal(0, bericht.OffTargetCount);

        // Und sie werden gezaehlt: sonst koennte die Anzeige nicht darauf
        // hinweisen, ohne selbst nachzurechnen — zweite Wahrheit je Zahl.
        Assert.Equal(werte.Count, bericht.ImpossibleCount);
        Assert.True(bericht.ImpossibleCount > 0);
    }

    [Fact]
    public void Ein_moeglicher_Wert_ohne_Zielband_bleibt_unauffaellig()
    {
        // Gegenprobe zum Test darueber. Wuerde alles ohne Zielband als
        // „unmoeglich" gelten, waere die Warnung wertlos: CO₂ und PPFD haben
        // absichtlich keins.
        var bericht = _svc.Assess(Grow(), new[] { Messung(ec: 1.5, wasser: 20) });
        var werte = bericht.Measurements[0].Metrics;

        Assert.DoesNotContain(werte, w => w.Verdict == AssessmentVerdict.Impossible);
        Assert.Equal(0, bericht.ImpossibleCount);
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }

    /// <summary>
    /// Die Wissensbasis in den Testordner legen.
    /// </summary>
    /// <remarks>
    /// Das Ziel ist <c>wwwroot/knowledge-defaults</c>, genau wie in den anderen
    /// Testklassen. Ein erster Anlauf legte die Dateien nach <c>knowledge/</c>
    /// — der Loader fand nichts, alle Sollwerte waren null, und der pH-Test war
    /// trotzdem gruen: pH faellt bei fehlenden Zielen auf die Komfortzone
    /// zurueck, und genau die hatte der Test geprueft. Aufgefallen ist es nur,
    /// weil der VPD-Test keinen solchen Rueckfall hat.
    /// </remarks>
    private static void CopyDefaults(string quelle, string tempRoot)
    {
        var ziel = Path.Combine(tempRoot, "wwwroot", "knowledge-defaults");
        foreach (var datei in Directory.EnumerateFiles(quelle, "*.json", SearchOption.AllDirectories))
        {
            var relativ = Path.GetRelativePath(quelle, datei);
            var nach = Path.Combine(ziel, relativ);
            Directory.CreateDirectory(Path.GetDirectoryName(nach)!);
            File.Copy(datei, nach);
        }
    }
}
