using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Dieselbe Messgrösse bekommt überall dasselbe Zielband.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (01.09.2026).</b> Die Gesamtdurchsicht fand für ORP
/// <b>vier</b> Bänder nebeneinander: das Profil je Phase, fest verdrahtete
/// 300/500 in der Diagnose, 250/650 als kritische Grenzen und die Werte aus
/// dem Wissen. Bei 470 mV in der Blüte sagte die Live-Kachel „daneben, Ziel
/// 400–450" und zog zehn Punkte vom Score ab — die Diagnose fand nichts.</para>
///
/// <para><b>Warum eine Prüfung und nicht nur ein Fix.</b> Genau diese Klasse
/// ist in diesem Projekt schon dreimal aufgetreten: das EC-Ziel (Diagnose
/// 0,6–0,8 gegen Kachel 0,9–1,1), die physikalischen Grenzen (drei Tabellen,
/// sieben Widersprüche) und die Sauerstoff-Schwelle (viermal 6,5). „Steht
/// dieselbe Zahl an zwei Stellen, laufen sie auseinander — das ist kein Risiko,
/// sondern eine Frage der Zeit."</para>
///
/// <para><b>Was gemessen wird.</b> Für einen Grow mit Profil werden die beiden
/// Wege verglichen, die dem Nutzer nebeneinander auf dem Schirm stehen: das
/// Urteil des Messprotokolls und die Abweichungen der Diagnose. Sagen sie
/// Verschiedenes über denselben Messwert, ist das ein Befund.</para>
/// </remarks>
public sealed class EinZielbandJeMessgroesseTests : IDisposable
{
    private readonly string _wurzel;
    private readonly GrowRepository _grows;
    private readonly TargetValueService _ziele;

    public EinZielbandJeMessgroesseTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Zielband_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        KopiereWissen(Path.Combine(ProjektWurzel(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _wurzel);

        var pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(pfade);
        _grows = new GrowRepository(pfade);

        var lader = new KnowledgeBaseLoader(pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        lader.Initialize();
        _ziele = new TargetValueService(lader);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    /// <summary>
    /// Ein ORP-Wert im Graubereich: Diagnose und Zielband sagen dasselbe.
    /// </summary>
    /// <remarks>
    /// 470 mV war der gemeldete Fall: über dem Profil-Maximum der Blüte, aber
    /// innerhalb der alten fest verdrahteten 300–500.
    /// </remarks>
    [Theory]
    [InlineData(470)]
    [InlineData(520)]
    [InlineData(380)]
    [InlineData(290)]
    public void OrpUrteil_StimmtMitDemZielbandUeberein(double orp)
    {
        var grow = BlueteGrow();
        var messung = new Measurement
        {
            GrowId = grow.Id,
            TakenAt = DateTime.Now,
            Stage = GrowStage.Flower,
            OrpMv = orp,
        };

        var band = ProfilBand(grow);

        var ausserhalb = orp < band!.OrpMin || orp > band.OrpMax;

        var analyzer = new DeviationAnalyzerService(_ziele);
        var abweichungen = analyzer.Analyze(grow, [messung]);
        var gemeldet = abweichungen.Any(a => a.Metric == DeviationMetric.Orp);

        Assert.True(gemeldet == ausserhalb,
            $"ORP {orp:0} mV liegt {(ausserhalb ? "AUSSERHALB" : "innerhalb")} des Zielbands "
            + $"({band.OrpMin:0}–{band.OrpMax:0} mV), die Diagnose meldet {(gemeldet ? "" : "nichts")}. "
            + "Zwei Auskuenfte ueber denselben Messwert stehen dem Nutzer nebeneinander auf dem Schirm.");
    }

    /// <summary>
    /// Und die gemeldete Abweichung nennt dasselbe Band, gegen das sie geurteilt hat.
    /// </summary>
    /// <remarks>
    /// Die alte Fassung schrieb 300 und 500 in den Befund, urteilte aber gegen
    /// dieselben Zahlen — die Anzeige war also in sich stimmig und trotzdem
    /// falsch gegenüber der Kachel daneben. Hier wird der Befund gegen das
    /// Profil gehalten, nicht gegen sich selbst.
    /// </remarks>
    [Fact]
    public void DerBefundNenntDasProfilBand()
    {
        var grow = BlueteGrow();
        var band = ProfilBand(grow);

        var analyzer = new DeviationAnalyzerService(_ziele);
        var abweichung = analyzer.Analyze(grow, [new Measurement
        {
            GrowId = grow.Id, TakenAt = DateTime.Now, Stage = GrowStage.Flower, OrpMv = 900,
        }]).Single(a => a.Metric == DeviationMetric.Orp);

        Assert.Equal(band.OrpMin, abweichung.TargetMin);
        Assert.Equal(band.OrpMax, abweichung.TargetMax);
    }

    /// <summary>
    /// Das Zielband ueber die volle Profil-Kette — nicht ueber den Anbaustil.
    /// </summary>
    /// <remarks>
    /// <c>GetTargets(HydroStyle, stage)</c> landet immer beim Standardprofil
    /// und uebergeht das eigene Profil des Nutzers; genau dieser Fehler stand
    /// einmal in der Diagnose und meldete EC 0,6-0,8, waehrend die Kachel fuer
    /// denselben Grow 0,9-1,1 sagte. Hier derselbe Weg wie im Analyzer.
    /// </remarks>
    private HydroTargetValues ProfilBand(GrowRun grow)
    {
        var profil = SetpointProfileResolver.Resolve(grow.SetpointProfileId, null, grow.HydroStyle);
        var band = _ziele.GetTargets(profil.ProfileId, GrowStage.Flower);
        Assert.True(band is not null, "Ohne Zielband prueft dieser Fall nichts.");
        return band!;
    }

    /// <summary>
    /// Die Feedchart-Ziele gelten überall — nicht nur auf der Live-Kachel.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026).</b> Der Schalter „Diese Wochen-Ziele
    /// auch auf dem Bildschirm verwenden" wirkte nur im
    /// <c>GrowDashboardComposer</c>. Ein Grow mit Athena Blended in Blütewoche 4:
    /// das Chart nennt EC 2,6, das Profil <c>rdwc-default</c> für Flower
    /// 1,0–1,2. Bei gemessenem EC 2,60 sagte die Kachel „im Ziel", das
    /// Messprotokoll derselben Messung „weit über dem Ziel".</para>
    ///
    /// <para>Geprüft wird die Kette selbst: mit Schalter muss ein anderes Band
    /// herauskommen als ohne — sonst greift der Schalter gar nicht.</para>
    /// </remarks>
    [Fact]
    public void FeedchartZiele_GeltenInDerGanzenKette()
    {
        var grow = BlueteGrow();
        var wissen = new KnowledgeBaseLoader(new AppPaths(_wurzel), NullLogger<KnowledgeBaseLoader>.Instance);
        wissen.Initialize();

        grow.FeedProgramId = ProgrammMitBlueteChart(wissen);
        grow.UseFeedChartTargets = false;
        var ohne = GrowDiary.Web.Services.Zielband.FuerGrow(_ziele, wissen, grow, GrowStage.Flower, null, null);

        grow.UseFeedChartTargets = true;
        var mit = GrowDiary.Web.Services.Zielband.FuerGrow(_ziele, wissen, grow, GrowStage.Flower, null, null);

        Assert.True(ohne is not null && mit is not null, "Ohne Zielband prueft dieser Fall nichts.");
        Assert.True(Math.Abs(ohne!.EcMax - mit!.EcMax) > 0.01 || Math.Abs(ohne.EcMin - mit.EcMin) > 0.01,
            $"Mit und ohne Feedchart kommt dasselbe EC-Band heraus ({ohne.EcMin:0.00}-{ohne.EcMax:0.00}). "
            + "Dann wirkt der Schalter fuer die Wochen-Ziele in dieser Kette gar nicht - "
            + "und Kachel und Messprotokoll sagen weiter Verschiedenes ueber denselben Messwert.");
    }

    /// <summary>
    /// Eine eigene Wassertemperatur-Grenze gilt auch im Messprotokoll — oben wie unten.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026, vom Prüfer gefunden).</b> Die
    /// Wassertemperatur nahm ihre Untergrenze aus dem Zielband, ihre
    /// <b>Obergrenze</b> aber aus der Konstante <c>ArbeitsbereichMaxC</c>.
    /// Bei einer eingetragenen Regel 15–20 °C schrieb die Live-Kachel
    /// „Ziel 15 – 20", das Messprotokoll „Ziel 15 – 22" — und beurteilte
    /// 21 °C als <b>im Ziel</b>.</para>
    ///
    /// <para>Dazu ein falscher Satz auf dem Schirm: die Begründung erklärte
    /// die 15 als „deine Absenkung", also als Nachtrampe. Sie kam aus der
    /// Alarmregel.</para>
    ///
    /// <para>Geprüft wird am <see cref="MeasurementAssessmentService"/>
    /// selbst, nicht an der Hilfsmethode darunter: der Fehler sass in dem, was
    /// der Nutzer liest.</para>
    /// </remarks>
    [Theory]
    [InlineData(21, false)] // ueber der eigenen Obergrenze 20
    [InlineData(19, true)]  // mitten drin
    [InlineData(14, false)] // unter der eigenen Untergrenze 15
    public void EigeneWassertempGrenze_GiltAuchImMessprotokoll(double gemessen, bool erwartetImZiel)
    {
        var grow = BlueteGrow();
        var regeln = EigeneRegel(grow, "reservoir-temp", 15, 20);

        var dienst = new MeasurementAssessmentService(_ziele, regeln);
        var bericht = dienst.Assess(grow,
        [
            new Measurement
            {
                GrowId = grow.Id, TakenAt = DateTime.Now,
                Stage = GrowStage.Flower, ReservoirWaterTempC = gemessen,
            },
        ]);

        var urteil = bericht.Measurements.SelectMany(z => z.Metrics)
            .FirstOrDefault(w => w.Metric == "water-temp");

        Assert.True(urteil is not null,
            "Das Messprotokoll faellt gar kein Urteil ueber die Wassertemperatur — "
            + "dann prueft dieser Fall nichts.");
        Assert.True((urteil!.Verdict == AssessmentVerdict.InTarget) == erwartetImZiel,
            $"{gemessen:0.#} °C bei eigener Grenze 15–20 °C: das Messprotokoll sagt "
            + $"'{urteil.Verdict}' und nennt {urteil.TargetMin:0.#}–{urteil.TargetMax:0.#} °C. "
            + "Die Kachel daneben zeigt 15–20. Zwei Auskuenfte ueber denselben Messwert.");
    }

    /// <summary>
    /// Die Begründung erklärt eine eigene Grenze nicht als Nachtabsenkung.
    /// </summary>
    /// <remarks>
    /// Der Satz stand vor dem 01.09.2026 nur dann da, wenn wirklich eine Rampe
    /// lief. Seit die eigenen Grenzen in dieselbe Rechnung gehen, erfände er
    /// eine Regelung, die es nicht gibt.
    /// </remarks>
    [Fact]
    public void EigeneUntergrenze_HeisstNichtAbsenkung()
    {
        var grow = BlueteGrow();
        var regeln = EigeneRegel(grow, "reservoir-temp", 15, 20);

        var bericht = new MeasurementAssessmentService(_ziele, regeln).Assess(grow,
        [
            new Measurement
            {
                GrowId = grow.Id, TakenAt = DateTime.Now,
                Stage = GrowStage.Flower, ReservoirWaterTempC = 19,
            },
        ]);

        var urteil = bericht.Measurements.SelectMany(z => z.Metrics).First(w => w.Metric == "water-temp");

        Assert.False(urteil.Note.Contains("Absenkung", StringComparison.OrdinalIgnoreCase),
            $"Die Begruendung sagt '{urteil.Note}'. Die 15 °C stehen aber in einer "
            + "Alarmregel des Nutzers, nicht in einer Nachtrampe — der Satz erfindet eine "
            + "Regelung, die es nicht gibt.");
    }

    /// <summary>
    /// Die Feedchart-Ziele gelten auch im Messprotokoll — nicht nur in der Hilfsmethode.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026, vom Prüfer gefunden).</b> Die erste
    /// Fassung dieser Datei rief <see cref="Zielband.FuerGrow"/> <b>direkt</b>
    /// auf — also die neue Hilfsmethode, nicht ihre Verwender. Wer die
    /// Feedchart-Zeile im <see cref="MeasurementAssessmentService"/> wieder
    /// entfernte, bekam trotzdem sechs grüne Fälle.</para>
    ///
    /// <para>Hier wird das Urteil des Protokolls gemessen: ein EC-Wert, den
    /// Chart-Band und Profil-Band verschieden beurteilen, muss beim Protokoll
    /// so herauskommen, wie das Chart-Band ihn sieht.</para>
    /// </remarks>
    [Fact]
    public void FeedchartZiele_GeltenAuchImMessprotokoll()
    {
        var grow = BlueteGrow();
        var wissen = new KnowledgeBaseLoader(new AppPaths(_wurzel), NullLogger<KnowledgeBaseLoader>.Instance);
        wissen.Initialize();

        grow.FeedProgramId = ProgrammMitBlueteChart(wissen);

        grow.UseFeedChartTargets = false;
        var ohne = Zielband.FuerGrow(_ziele, wissen, grow, GrowStage.Flower, null, null);
        grow.UseFeedChartTargets = true;
        var mit = Zielband.FuerGrow(_ziele, wissen, grow, GrowStage.Flower, null, null);
        Assert.True(ohne is not null && mit is not null, "Ohne Zielband prueft dieser Fall nichts.");

        // Ein EC-Wert, den die beiden Baender verschieden beurteilen. Gibt es
        // keinen, sagt der Waechter das — statt gruen zu sein.
        bool ImBand(double wert, HydroTargetValues b) => wert >= b.EcMin && wert <= b.EcMax;
        var trenner = new[] { mit!.EcMax, mit.EcMin, ohne!.EcMax, ohne.EcMin }
            .FirstOrDefault(w => ImBand(w, mit) != ImBand(w, ohne));
        Assert.True(trenner > 0,
            $"Chart-Band {mit.EcMin:0.00}-{mit.EcMax:0.00} und Profil-Band "
            + $"{ohne.EcMin:0.00}-{ohne.EcMax:0.00} beurteilen jeden Wert gleich — "
            + "dann prueft dieser Fall nichts.");

        var imChart = ImBand(trenner, mit);

        var urteil = new MeasurementAssessmentService(_ziele, null, wissen)
            .Assess(grow,
            [
                new Measurement
                {
                    GrowId = grow.Id, TakenAt = DateTime.Now,
                    Stage = GrowStage.Flower, ReservoirEc = trenner,
                },
            ]).Measurements.SelectMany(z => z.Metrics).First(w => w.Metric == "ec");

        Assert.True((urteil.Verdict == AssessmentVerdict.InTarget) == imChart,
            $"EC {trenner:0.00} liegt {(imChart ? "im" : "ausserhalb vom")} Chart-Band "
            + $"({mit.EcMin:0.00}-{mit.EcMax:0.00}), das Messprotokoll sagt '{urteil.Verdict}' "
            + $"und nennt {urteil.TargetMin:0.00}-{urteil.TargetMax:0.00}. "
            + "Der Schalter fuer die Wochen-Ziele wirkt im Protokoll also nicht.");
    }

    /// <summary>
    /// Eine halbe eigene Grenze laesst die andere Hälfte in Ruhe.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026, vom Prüfer gefunden).</b>
    /// <c>UserTargets.IsUserSet</c> ist schon <c>true</c>, wenn <b>eine</b> der
    /// beiden Grenzen gesetzt ist. Die Diagnose warf damit die ganze
    /// Komfortzone weg und nahm die Profil-Untergrenze 5,9, während die Kachel
    /// bei 5,8 blieb.</para>
    ///
    /// <para>Am laufenden Stand gesehen: Regel „nur Obergrenze 6,3", pH 5,82 —
    /// die Kachel schrieb „im Ziel", die Diagnose „WARNUNG, liegt unter dem
    /// Handlungsbereich 5,9–6,3, Handlung: pH-Plus".</para>
    /// </remarks>
    [Fact]
    public void HalbeEigeneGrenze_WirftDieKomfortzoneNichtWeg()
    {
        var grow = BlueteGrow();
        var regeln = EigeneRegel(grow, "reservoir-ph", null, 6.3);
        var liste = regeln.GetForTent(grow.TentId!.Value);

        var mitNutzer = UserTargets.Overlay(ProfilBand(grow)!, liste);
        var (unten, oben) = DeviationAnalyzerService.PhHandlungsbereich(
            mitNutzer, UserTargets.For("reservoir-ph", liste));

        Assert.True(Math.Abs(oben - 6.3) < 0.001,
            $"Die eingetragene Obergrenze 6,3 wirkt nicht — der Handlungsbereich endet bei {oben:0.00}.");
        Assert.True(unten <= DeviationAnalyzerService.PhComfortMin + 0.001,
            $"Nur die OBERGRENZE wurde eingetragen, der Handlungsbereich beginnt aber bei {unten:0.00} "
            + $"statt bei der Komfortzone {DeviationAnalyzerService.PhComfortMin:0.00}. Die Diagnose "
            + "meldet dadurch 'zu niedrig' fuer Werte, die die Kachel daneben 'im Ziel' nennt.");
    }

    /// <summary>Eine eigene Grenze fuer dieses Zelt — und die Ablage dazu.</summary>
    private AlertRuleRepository EigeneRegel(GrowRun grow, string kennung, double? min, double? max)
    {
        var regeln = new AlertRuleRepository(new AppPaths(_wurzel));
        regeln.ReplaceForTent(grow.TentId!.Value,
        [
            new TentAlertRule
            {
                TentId = grow.TentId!.Value, MetricKey = kennung,
                MinValue = min, MaxValue = max, Enabled = true,
            },
        ]);
        return regeln;
    }

    /// <summary>
    /// Ein Düngerprogramm, dessen Feedchart die Blüte überhaupt kennt.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026).</b> Hier stand
    /// <c>NutrientPrograms.FirstOrDefault()</c>. Es gibt <b>drei</b>
    /// Programme — <c>athena-blended</c>, <c>canna-aqua</c>,
    /// <c>hydro-research-vbx</c> — und nur athena hat Blüte-Spalten im
    /// Chart. Welches zuerst kommt, entscheidet
    /// <c>Directory.EnumerateFiles</c>, und das ist keine Zusage:
    /// unter Windows praktisch alphabetisch, unter Linux nicht.</para>
    ///
    /// <para>Der Test war deshalb hier grün und im Tor <b>rot</b> — dort kam
    /// ein Programm ohne Blüte-Spalten zuerst, <c>SpalteFuer</c> gab
    /// <c>null</c> zurück, und mit wie ohne Schalter kam dasselbe Band heraus.
    /// Dieselbe Klasse wie „mein Rechner ist nicht die Anlage": das Add-on
    /// läuft im Linux-Container.</para>
    ///
    /// <para>Gewählt wird jetzt nach dem, worauf es ankommt — ein Chart mit
    /// Blüte-Spalten — und nicht nach der Stelle in einer Aufzählung. Gibt es
    /// keins, sagt der Wächter das, statt still grün zu sein.</para>
    /// </remarks>
    private static string ProgrammMitBlueteChart(KnowledgeBaseLoader wissen)
    {
        var programme = wissen.NutrientPrograms;

        // Mengenwaechter: ohne Programme prueft der Fall nichts.
        Assert.True(programme.Count > 0, "Kein Duengerprogramm im Wissen — der Fall prueft nichts.");

        var passend = MitBlueteChart(programme);

        Assert.True(passend is not null,
            $"Keines der {programme.Count} Duengerprogramme hat Wochen-Spalten fuer die Bluete "
            + $"({string.Join(", ", programme.Select(p => p.Id))}). Dann kann der Schalter fuer die "
            + "Wochen-Ziele gar nichts bewirken, und dieser Fall prueft nichts.");

        return passend!.Id;
    }

    /// <summary>Die Wahl selbst — als reine Funktion, damit sie prüfbar ist.</summary>
    // Fork AI: Seit <c>skx-canna-aqua</c> gibt es ZWEI Programme mit
    // Blüte-Wochen. "Das erste mit Blüte-Spalten" hängt damit wieder an der
    // Dateireihenfolge — deshalb entscheidet unter den Kandidaten die Id.
    internal static NutrientProgramDefinition? MitBlueteChart(
        IEnumerable<NutrientProgramDefinition> programme)
        => programme
            .Where(HatBlueteWochen)
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    internal static bool HatBlueteWochen(NutrientProgramDefinition p)
        => p.FeedChart is { } chart
            && chart.Columns.Any(c =>
                string.Equals(c.Stage, "Flower", StringComparison.OrdinalIgnoreCase) && c.Week is not null);

    /// <summary>
    /// Die Wahl überlebt eine andere Dateireihenfolge — und die alte nicht.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Bissnachweis zum CI-Ausfall vom 01.09.2026.</b> Zwei Fälle
    /// dieser Datei waren hier grün und auf dem Linux-Runner rot. Die Ursache
    /// war <c>NutrientPrograms.FirstOrDefault()</c>: von den drei Programmen
    /// hat nur <c>athena</c> Blüte-Spalten, und welches zuerst kommt,
    /// entscheidet <c>Directory.EnumerateFiles</c> — unter Windows praktisch
    /// alphabetisch, unter Linux nicht.</para>
    ///
    /// <para>Dieser Fall dreht die Reihenfolge <b>um</b> und zeigt beides: die
    /// neue Wahl liefert weiter ein taugliches Programm, die alte läge daneben.
    /// Ohne den zweiten Teil wäre nicht belegt, dass die Umkehrung den Fehler
    /// überhaupt auslöst — dann prüfte dieser Fall nichts.</para>
    /// </remarks>
    [Fact]
    public void DieProgrammwahl_HaengtNichtAnDerDateireihenfolge()
    {
        var wissen = new KnowledgeBaseLoader(new AppPaths(_wurzel), NullLogger<KnowledgeBaseLoader>.Instance);
        wissen.Initialize();
        var programme = wissen.NutrientPrograms;

        Assert.True(programme.Count >= 2,
            $"Nur {programme.Count} Duengerprogramm(e) — mit einem einzigen kann eine "
            + "Reihenfolge nichts kaputt machen, und dieser Fall prueft nichts.");

        var vorwaerts = MitBlueteChart(programme);
        var rueckwaerts = MitBlueteChart(programme.Reverse());

        Assert.True(vorwaerts is not null && rueckwaerts is not null,
            "In einer der beiden Richtungen wurde gar kein Programm gefunden.");
        Assert.True(vorwaerts!.Id == rueckwaerts!.Id,
            $"Vorwaerts kommt {vorwaerts.Id} heraus, rueckwaerts {rueckwaerts.Id} — die Wahl "
            + "haengt weiter an der Reihenfolge, in der die Dateien gelesen wurden.");

        // Und der Nachweis, dass die Umkehrung den alten Fehler wirklich
        // ausloest: eine schlicht reihenfolgeabhaengige Wahl liefert
        // rueckwaerts ein anderes Programm. Ohne diesen Teil waere oben nicht
        // belegt, dass ueberhaupt etwas auf dem Spiel steht.
        //
        // Fork AI: Im Original hatte nur athena Bluete-Spalten, die alte Wahl
        // war "erster Eintrag". Im Fork hat auch skx-canna-aqua Bluete-Wochen;
        // dann ist "erster Eintrag MIT Bluete-Spalten" die Wahl, die kippt.
        var kandidaten = programme.Where(HatBlueteWochen).ToList();
        var alteWahlVorwaerts = kandidaten.Count >= 2 ? kandidaten[0] : programme[0];
        var alteWahlRueckwaerts = kandidaten.Count >= 2 ? kandidaten[^1] : programme.Reverse().First();
        Assert.True(alteWahlVorwaerts.Id != alteWahlRueckwaerts.Id,
            "Vorwaerts und rueckwaerts steht dasselbe Programm vorn — dann zeigt die "
            + "Umkehrung den Fehler gar nicht, den dieser Fall belegen soll.");
        Assert.True(
            kandidaten.Count >= 2
            || !HatBlueteWochen(alteWahlRueckwaerts)
            || !HatBlueteWochen(alteWahlVorwaerts),
            $"Sowohl {alteWahlVorwaerts.Id} als auch {alteWahlRueckwaerts.Id} haben "
            + "Bluete-Spalten — dann waere die alte Wahl in beiden Richtungen tauglich "
            + "gewesen, und der CI-Ausfall haette eine andere Ursache gehabt.");
    }

    private GrowRun BlueteGrow()
    {
        var zelt = _grows.CreateTent(new Tent { Name = "Zelt", TentType = TentType.Production });
        var id = _grows.CreateGrow(new GrowRun
        {
            Name = "Lauf",
            TentId = zelt.Id,
            HydroStyle = HydroStyle.RDWC,
            IrrigationType = IrrigationType.ActiveHydro,
            MediumType = MediumType.Hydro,
            Status = GrowStatus.Running,
            StartDate = DateTime.Today.AddDays(-70),
            FlipDate = DateTime.Today.AddDays(-35),
            BreederFlowerWeeksMin = 8,
            BreederFlowerWeeksMax = 9,
        });
        return _grows.GetGrow(id)!;
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

    private static void KopiereWissen(string quelle, string ziel)
    {
        var nach = Path.Combine(ziel, "wwwroot", "knowledge-defaults");
        foreach (var datei in Directory.EnumerateFiles(quelle, "*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(quelle, datei);
            var pfad = Path.Combine(nach, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.Copy(datei, pfad);
        }
    }
}
