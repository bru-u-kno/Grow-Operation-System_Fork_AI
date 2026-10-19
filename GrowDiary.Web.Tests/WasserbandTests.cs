using System.Text.RegularExpressions;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Der Arbeitsbereich der Wassertemperatur — eine Zahl, eine Stelle.
///
/// <para><b>Der Anlass.</b> Seit beta.52 fährt Cultivate Control die Nachtabsenkung
/// wirklich: je Blütewoche ein Grad tiefer bis zum Finish-Nachtwert des
/// Profils, im Standardprofil 16 °C. Der Arbeitsbereich begann bei 17 °C.
/// Ab Blütewoche 3 meldete die App damit ihre <b>eigene Regelung</b> als
/// Abweichung — in zwei Diensten unabhängig voneinander, weil beide die
/// Zahlen 17 und 22 fest verdrahtet trugen.</para>
/// </summary>
public sealed class WasserbandTests
{
    private static HydroTargetValues Ziele(double nachtC) => new(
        PhMin: 5.5, PhMax: 6.3,
        EcMin: 1.0, EcMax: 1.4,
        OrpMin: 400, OrpMax: 450,
        WaterTempDayC: 20, WaterTempNightC: nachtC,
        VpdMin: 1.0, VpdMax: 1.2,
        PpfdMin: 700, PpfdMax: 900,
        Co2Min: 400, Co2Max: 800);

    [Fact]
    public void Ohne_Profil_gilt_der_SOP_Wert()
    {
        Assert.Equal(17, Wasserband.UntergrenzeC(null));
        Assert.Contains("SOP-RDWC-CAN-N1", Wasserband.Begruendung(null));
    }

    [Fact]
    public void Der_Nachtwert_zieht_die_Untergrenze_mit_nach_unten()
    {
        // Genau der Fall der Absenkrampe: das Profil zielt nachts auf 16 °C.
        Assert.Equal(16, Wasserband.UntergrenzeC(Ziele(nachtC: 16)));
    }

    [Fact]
    public void Ein_hoeherer_Nachtwert_verengt_das_Band_NICHT()
    {
        // Das Band ist eine Grenze, kein Ziel. Ein Profil mit Nachtwert 19 darf
        // nicht dazu führen, dass 18 °C plötzlich beanstandet wird.
        Assert.Equal(17, Wasserband.UntergrenzeC(Ziele(nachtC: 19)));
    }

    [Fact]
    public void Die_Begruendung_nennt_beide_Zahlen_und_ihre_Herkunft()
    {
        var satz = Wasserband.Begruendung(Ziele(nachtC: 16));

        Assert.Contains("16", satz);
        Assert.Contains("17", satz);
        Assert.Contains("SOP-RDWC-CAN-N1", satz);
        // Projektregel: eine Zahl ohne Herkunft ist schlechter als keine.
        Assert.Contains("Absenkung", satz);
        // Und kurz genug fuer eine Tabellenzelle: der Satz steht im Messprotokoll
        // neben 92 anderen.
        Assert.True(satz.Split(' ').Length <= 26, $"Zu lang ({satz.Split(' ').Length} Woerter): {satz}");
    }

    [Fact]
    public void Der_Rampenboden_zieht_tiefer_als_der_Phasenwert()
    {
        // <b>Der Fall, für den das Ganze gebaut ist — und den die erste Fassung
        // verfehlt hat.</b> Die Messung steht in der Blüte (Nachtwert 18), die
        // Rampe fährt aber längst auf den Finish-Wert (16). Ohne den
        // Rampenboden bliebe die Untergrenze bei 17, und jede Nachtmessung
        // ab Blütewoche 3 wäre eine Abweichung.
        Assert.Equal(16, Wasserband.UntergrenzeC(Ziele(nachtC: 18), rampenBodenC: 16));

        // Und die Begründung sagt es auch.
        var satz = Wasserband.Begruendung(Ziele(nachtC: 18), rampenBodenC: 16);
        Assert.Contains("16", satz);
        Assert.Contains("Absenkung", satz);
    }

    [Fact]
    public void Ohne_laufende_Rampe_bleibt_es_beim_SOP_Wert()
    {
        // RampenBodenC gibt null, wenn die Absenkung fuer den Grow aus ist.
        Assert.Equal(17, Wasserband.UntergrenzeC(Ziele(nachtC: 18), rampenBodenC: null));
    }

    [Fact]
    public void RampenBodenC_ist_null_wenn_die_Absenkung_aus_ist()
    {
        var grow = new GrowRun
        {
            Id = 1,
            Name = "Ohne Rampe",
            StartDate = DateTime.Today.AddDays(-60),
            FlipDate = DateTime.Today.AddDays(-30),
            NightRampEnabled = false,
        };

        Assert.Null(Wasserband.RampenBodenC(grow, Ziele(nachtC: 18), Ziele(nachtC: 16)));
    }

    [Fact]
    public void RampenBodenC_findet_den_tiefsten_Nachtwert_der_Rampe()
    {
        var grow = new GrowRun
        {
            Id = 2,
            Name = "Mit Rampe",
            StartDate = DateTime.Today.AddDays(-60),
            FlipDate = DateTime.Today.AddDays(-30),
            NightRampEnabled = true,
        };

        // Start 18 (Bluete-Nacht), Boden 16 (Finish-Nacht): die Rampe endet dort.
        Assert.Equal(16, Wasserband.RampenBodenC(grow, Ziele(nachtC: 18), Ziele(nachtC: 16)));
    }

    /// <summary>
    /// <b>Die Zählung.</b> Keine andere Datei darf die Grenzen noch einmal als
    /// Ziffer tragen.
    /// </summary>
    /// <remarks>
    /// Genau daran hing der Fehler: <c>MeasurementAssessmentService</c> und
    /// <c>DeviationAnalyzerService</c> trugen 17/22 unabhängig voneinander, und
    /// beim Bauen der Absenkung fiel keiner von beiden auf. Die Suche geht über
    /// den Quelltext der beiden Dienste und schliesst Kommentare aus — eine
    /// Erwähnung ist keine Verwendung.
    /// </remarks>
    [Fact]
    public void Keine_zweite_Fassung_der_Grenzen_im_Quelltext()
    {
        var wurzel = QuelltextWurzel();
        var verdaechtig = new[]
        {
            "Services/MeasurementAssessmentService.cs",
            "Services/DeviationAnalyzerService.cs",
        };

        var gefunden = new List<string>();
        foreach (var datei in verdaechtig)
        {
            var pfad = Path.Combine(wurzel, datei);
            Assert.True(File.Exists(pfad), $"{datei} gibt es nicht (mehr) — die Zählung sucht ins Leere.");

            var zeilen = File.ReadAllLines(pfad);
            for (var i = 0; i < zeilen.Length; i++)
            {
                var zeile = zeilen[i].Trim();
                // Kommentare und XML-Doku zaehlen nicht: dort DUERFEN die Zahlen
                // stehen, das ist ja die Begruendung.
                if (zeile.StartsWith("//") || zeile.StartsWith("///") || zeile.StartsWith("*")) continue;

                // Eine nackte 17 oder 22 im Zusammenhang mit Wassertemperatur.
                if (Regex.IsMatch(zeile, @"(actual|value|wert)\s*[<>]=?\s*(17|22|14|24)\b")
                    || Regex.IsMatch(zeile, @"WaterTempWork(Min|Max)"))
                {
                    gefunden.Add($"{datei}:{i + 1}  {zeile}");
                }
            }
        }

        Assert.True(gefunden.Count == 0,
            "Die Grenzen der Wassertemperatur stehen wieder als Ziffer im Quelltext statt in "
            + "Wasserband:\n" + string.Join("\n", gefunden));
    }

    /// <summary>
    /// Beisst die Zählung überhaupt? Ein erfundener Quelltext mit der alten
    /// Schreibweise muss auffallen.
    /// </summary>
    [Fact]
    public void Die_Zaehlung_wuerde_die_alte_Schreibweise_finden()
    {
        var alt = "        var warning = actual > 22 || actual < 17;";
        Assert.Matches(@"(actual|value|wert)\s*[<>]=?\s*(17|22|14|24)\b", alt.Trim());

        // Und ein Kommentar mit denselben Zahlen darf NICHT anschlagen.
        var kommentar = "        // Arbeitsbereich 17-22 Grad laut SOP.";
        Assert.StartsWith("//", kommentar.Trim());
    }

    /// <summary>Den Ordner mit dem Quelltext finden — vom Testlauf aus.</summary>
    private static string QuelltextWurzel()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null)
        {
            var kandidat = Path.Combine(ordner.FullName, "GrowDiary.Web");
            if (Directory.Exists(kandidat)) return kandidat;
            ordner = ordner.Parent;
        }

        throw new DirectoryNotFoundException(
            "GrowDiary.Web wurde von " + AppContext.BaseDirectory + " aus nicht gefunden.");
    }
}
