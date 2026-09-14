using System.Text.RegularExpressions;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Phase eines Grows kommt aus <b>einer</b> Quelle.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (02.09.2026).</b> Vier Stellen leiteten „in welcher Phase
/// ist dieser Grow gerade" aus der <b>letzten Messung</b> ab
/// (<c>latest?.Stage ?? GrowStage.Veg</c>), während der Rest der App
/// <see cref="GrowStageResolver"/> fragt.</para>
///
/// <para><b>Warum das auseinanderläuft.</b> Die Phase auf einer Messung ist
/// eine Momentaufnahme: sie beschreibt <i>diese Messung</i>, nicht den Grow von
/// heute. Wer am 1. August misst, am 10. flippt und danach die Sensoren machen
/// lässt, hat oben in der Kopfzeile „Blüte · Tag 14" stehen — und daneben
/// Zielbänder aus der Vegetation. Genau diese Sorte Widerspruch (EC 0,6–0,8 in
/// der Diagnose gegen 0,9–1,1 in der Kachel) steht in <c>CLAUDE.md</c> unter
/// „EINE WAHRHEIT JE ZAHL".</para>
///
/// <para>Schlimmer noch beim Urlaubswächter: der schickt <b>Nachrichten aufs
/// Telefon</b>. Ein Grow, der seit Wochen automatisch misst, bekommt Warnungen
/// gegen die falschen Bänder — und wer im Urlaub ist, kann nicht nachsehen.</para>
///
/// <para>Und die Automessung stempelte die Phase der letzten Messung auf jede
/// neue: einmal falsch, für immer falsch, weil die nächste wieder von dort
/// abschreibt.</para>
/// </remarks>
public sealed class EinePhaseJeGrowTests : IDisposable
{
    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly GrowRepository _grows;

    public EinePhaseJeGrowTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "EinePhase_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(_pfade);
        _grows = new GrowRepository(_pfade);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    /// <summary>
    /// Der Urlaubswächter urteilt nach der Phase von <b>heute</b>.
    /// </summary>
    /// <remarks>
    /// Der Aufbau: ein Grow, der vor 30 Tagen geflippt hat — also unstrittig in
    /// der Blüte —, dessen Messungen aber alle noch „Veg" tragen, weil sie
    /// automatisch erfasst wurden und die Automessung die Phase der letzten
    /// Messung weiterschrieb.
    ///
    /// Geprüft wird nicht ein bestimmtes Band, sondern die <b>Gleichheit</b>:
    /// derselbe Grow mit denselben Zahlen muss dasselbe Urteil bekommen, egal
    /// welche Beschriftung auf den Messzeilen steht. Damit hängt die Prüfung an
    /// keiner Zahl, die sich morgen ändern darf.
    /// </remarks>
    [Fact]
    public void DieAufschriftDerMessungAendertDasUrteilNicht()
    {
        var mitVegAufschrift = BefundeFuerBluetegrow(aufschrift: GrowStage.Veg);
        var mitBlueteAufschrift = BefundeFuerBluetegrow(aufschrift: GrowStage.Flower);

        // Mengenwaechter: ohne Befunde waere jede Gleichheit darunter wertlos.
        Assert.True(mitBlueteAufschrift.Count > 0,
            "Der Aufbau erzeugt gar keinen Befund — dann prueft der Vergleich darunter nichts. "
            + "Die Messreihe muss so weit aus dem Band laufen, dass der Waechter anschlaegt.");

        Assert.True(
            mitVegAufschrift.SequenceEqual(mitBlueteAufschrift),
            "Derselbe Grow, dieselben Zahlen — aber ein anderes Urteil, je nachdem was auf den "
            + "Messzeilen steht:\n"
            + $"  Aufschrift „Veg\":    {string.Join(", ", mitVegAufschrift)}\n"
            + $"  Aufschrift „Bluete\": {string.Join(", ", mitBlueteAufschrift)}\n"
            + "Der Grow ist vor 30 Tagen geflippt; die Aufschrift ist eine Momentaufnahme von "
            + "damals. Der Waechter schickt Nachrichten aufs Telefon — hier gegen die falschen "
            + "Baender.");
    }

    /// <summary>
    /// Eine automatisch erfasste Messung trägt die Phase von <b>heute</b>.
    /// </summary>
    /// <remarks>
    /// Der Fehler nährte sich selbst: die Automessung schrieb die Phase der
    /// <i>letzten</i> Messung auf jede neue. Ein einziger falscher Stempel — oder
    /// schlicht ein Flip, an dem niemand von Hand gemessen hat — pflanzte sich
    /// dann durch jede weitere Zeile fort.
    /// </remarks>
    [Fact]
    public void EineAutoMessungStempeltDiePhaseVonHeute()
    {
        var quelltext = Quelltext("Services", "AutoMeasurementExecutionService.cs");

        Assert.False(
            Regex.IsMatch(quelltext, @"Stage\s*=\s*_repository\.GetLatestMeasurement\("),
            "Die Automessung schreibt die Phase der letzten Messung ab. Nach einem Flip, an dem "
            + "niemand von Hand gemessen hat, traegt ab da JEDE automatische Zeile „Veg\" — und "
            + "jede naechste schreibt es weiter. Richtig ist GrowStageResolver.Resolve.");
    }

    /// <summary>
    /// Das Muster: hier wird die Aufschrift der <b>letzten</b> Messung zur Phase.
    /// </summary>
    /// <remarks>
    /// <para><b>Die Grenze.</b> Die Phase auf einer Messung zu lesen ist
    /// richtig, solange es um <i>diese</i> Messung geht — sie in die Datenbank
    /// schreiben, sie in ein DTO abbilden, sie protokollieren. Falsch wird es,
    /// sobald jemand die Aufschrift der <b>letzten</b> Messung nimmt und daraus
    /// die Phase von heute macht.</para>
    ///
    /// <para>Drei Formen, alle belegt:</para>
    /// <code>
    ///   latest?.Stage ?? GrowStage.Veg                 vier Stellen
    ///   latest?.Stage ?? (grow is null ? …)            Dashboard-Bauer
    ///   Zielband.FuerGrow(…, latest.Stage, …)          Diagnose
    /// </code>
    ///
    /// <para><b>Warum der Selbsttest sein muss.</b> Beim Weiten des Musters
    /// wurde eine Wortgrenze zu einem Steuerzeichen — danach konnte es
    /// überhaupt nicht mehr treffen, und die Zählung war trotzdem <i>grün</i>.
    /// Eine Zählung, deren Muster niemand gegengeprüft hat, läuft bei einem
    /// kaputten Muster null Mal durch und meldet Erfolg.</para>
    /// </remarks>
    private static readonly Regex BORGT_DIE_PHASE_VON_DER_LETZTEN_MESSUNG = new(
        @"(latest|GetLatestMeasurement|LatestMeasurement|letzte|neueste)\w*(\([^)]*\))?\??\.Stage",
        RegexOptions.IgnoreCase);

    /// <summary>Der Selbsttest: trifft das Muster die belegten Formen?</summary>
    [Theory]
    [InlineData("var stage = latest?.Stage ?? GrowStage.Veg;", true)]
    [InlineData("var stage = latestByTime?.Stage ?? GrowStage.Veg;", true)]
    [InlineData("var s = latest?.Stage ?? (grow is null ? (GrowStage?)null : Hol(g));", true)]
    [InlineData("_targetValues, _wissen, grow, latest.Stage, systemProfileId, regeln);", true)]
    [InlineData("Stage = _repository.GetLatestMeasurement(id)?.Stage ?? GrowStage.Veg,", true)]
    [InlineData("LatestStage: grow.LatestMeasurement?.Stage,", true)]
    // Und was RICHTIG ist: es geht um DIESE Messung, nicht um die letzte.
    [InlineData("var stage = GrowStageResolver.Resolve(grow, DateTime.Today);", false)]
    [InlineData("command.Parameters.AddWithValue(\"$stage\", measurement.Stage.ToString());", false)]
    [InlineData("Stage = measurement.Stage,", false)]
    [InlineData("Stage: measurement.Stage,", false)]
    [InlineData("_audit.LogCreated(growId, measurement.Id, measurement.Stage, m.TakenAt);", false)]
    [InlineData("public GrowStage Stage { get; set; }", false)]
    public void DasMusterTrifftDieBelegtenFormen(string zeile, bool erwartet)
    {
        Assert.True(BORGT_DIE_PHASE_VON_DER_LETZTEN_MESSUNG.IsMatch(zeile) == erwartet,
            $"Das Muster sagt zu <{zeile}> das Gegenteil von dem, was es soll. Eine Zaehlung "
            + "mit kaputtem Muster laeuft null Mal durch und ist gruen — genau so ist der "
            + "fuenfte Fundort am 02.09.2026 durchgerutscht.");
    }

    /// <summary>
    /// Die Zählung: <b>niemand</b> leitet die heutige Phase aus einer Messung ab.
    /// </summary>
    /// <remarks>
    /// <para>Vier Stellen an einem Tag sind kein Zufall, sondern ein Muster —
    /// also wird über die Grundmenge geprüft und nicht über eine Liste. Gesucht
    /// wird die Form <c>…?.Stage ?? GrowStage.…</c>: eine Messung, aus der eine
    /// Phase für <i>jetzt</i> wird.</para>
    ///
    /// <para>Ausnahmen brauchen einen ausgeschriebenen Grund und stehen
    /// unten.</para>
    /// </remarks>
    [Fact]
    public void NiemandLeitetDieHeutigePhaseAusEinerMessungAb()
    {
        /* Ausgeschriebene Ausnahmen. Eine Stelle darf die AUFGESCHRIEBENE Phase
           lesen, wenn es ihr um die Vergangenheit geht — nicht um heute. */
        var ausnahmen = new Dictionary<string, string>
        {
            ["MeasurementAssessmentService.cs"] =
                "Vergleicht die aufgeschriebene Phase mit der gerechneten und meldet den "
                + "Unterschied. Die aufgeschriebene ist hier der Gegenstand, nicht die Abkuerzung.",
        };

        var treffer = new List<string>();
        var gesehen = 0;

        foreach (var datei in Directory.EnumerateFiles(
                     Path.Combine(ProjektWurzel(), "GrowDiary.Web"), "*.cs", SearchOption.AllDirectories))
        {
            gesehen += 1;
            var name = Path.GetFileName(datei);
            if (ausnahmen.ContainsKey(name)) continue;

            // Auch Blockkommentare ausblenden. Im ersten Lauf meldete die
            // Zaehlung die eigene PROSA einer Reparatur als Fund: der Kommentar,
            // der den alten Ausdruck zitiert, sieht aus wie der alte Ausdruck.
            var zeilen = OhneBlockKommentare(File.ReadAllLines(datei));
            for (var i = 0; i < zeilen.Length; i += 1)
            {
                var zeile = zeilen[i];

                // Kommentare zaehlen nicht: eine Erwaehnung ist keine Verwendung.
                var ohneKommentar = zeile.Split("//")[0];
                /* Drei Formen, alle belegt — und das Muster war zweimal zu eng:

                     latest?.Stage ?? GrowStage.Veg            (vier Stellen)
                     latest?.Stage ?? (grow is null ? …)       (Dashboard-Bauer)
                     latest.Stage                              (Diagnose)

                   Die dritte hat der Prüfer gefunden, nachdem die Zählung schon
                   grün war: kein `?.`, kein `??`, dieselbe Wirkung. Gesucht wird
                   deshalb jede Stelle, an der eine MESSUNG nach ihrer Phase
                   gefragt wird — und die Namen der Messvariablen sind in diesem
                   Projekt einheitlich. */
                var trifft = BORGT_DIE_PHASE_VON_DER_LETZTEN_MESSUNG.IsMatch(ohneKommentar);

                if (!trifft) continue;

                treffer.Add($"{name}:{i + 1}  {zeile.Trim()}");
            }
        }

        // Mengenwaechter: sieht die Zaehlung ihre Grundmenge ueberhaupt?
        Assert.True(gesehen >= 200,
            $"Nur {gesehen} Quelldateien gefunden — die Zaehlung sieht ihre Grundmenge nicht "
            + "und waere auch bei jedem Fehler gruen.");

        Assert.True(treffer.Count == 0,
            "Diese Stellen machen aus der Aufschrift einer Messung die Phase von HEUTE:\n"
            + string.Join("\n", treffer)
            + "\n\nDie Phase eines Grows kommt aus GrowStageResolver.Resolve(grow, heute). Was auf "
            + "einer Messzeile steht, beschreibt DIESE Messung — nach einem Flip laeuft es "
            + "auseinander, und dann widerspricht die Kachel der Kopfzeile.\n"
            + "Wer eine Ausnahme braucht, traegt sie oben mit ausgeschriebenem Grund ein.");
    }

    // ------------------------------------------------------------------ Hilfe

    /// <summary>Ein Grow, der vor 30 Tagen geflippt hat, mit driftendem pH.</summary>
    private IReadOnlyList<string> BefundeFuerBluetegrow(GrowStage aufschrift)
    {
        var zelt = _grows.CreateTent(new Tent { Name = "Zelt " + aufschrift, TentType = TentType.Production });
        var growId = _grows.CreateGrow(new GrowRun
        {
            Name = "Bluete " + aufschrift,
            TentId = zelt.Id,
            HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running,
            SeedType = SeedType.Feminized,
            StartDate = DateTime.Today.AddDays(-70),
            FlipDate = DateTime.Today.AddDays(-30),
        });
        var grow = _grows.GetGrow(growId)!;

        // Gegenprobe zum Aufbau: der Ermittler muss diesen Grow als Bluete sehen,
        // sonst prueft der Vergleich oben zwei gleiche Faelle.
        Assert.True(
            GrowStageResolver.Resolve(grow, DateTime.Today) is GrowStage.Flower or GrowStage.Finish
                or GrowStage.Transition,
            "Der Aufbau liefert keinen Bluetegrow — dann vergleicht der Test zweimal dasselbe.");

        /* Eine Reihe, die deutlich ueber jedem Band liegt: pH steigt von 6,4 auf
           7,2, EC faellt. Wichtig ist nur, dass ueberhaupt etwas anschlaegt. */
        for (var tag = 12; tag >= 0; tag -= 1)
        {
            _grows.CreateMeasurement(new Measurement
            {
                GrowId = growId,
                TakenAt = DateTime.Today.AddDays(-tag).AddHours(9),
                Stage = aufschrift,
                ReservoirPh = 6.4 + (12 - tag) * 0.07,
                ReservoirEc = 1.9 - (12 - tag) * 0.06,
                ReservoirWaterTempC = 19 + (12 - tag) * 0.25,
                Source = ValueOrigin.Manual,
            });
        }

        var laeufer = new TrendWatchRunner(
            _grows,
            new TargetValueService(Wissen()),
            Wissen(),
            new NotificationService(
                new NotificationSettingsRepository(_pfade),
                _grows,
                new HomeAssistantService(
                    new TestFakes.StubHttpClientFactory(
                        new TestFakes.RecordingHttpHandler((_, _) =>
                            new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK))),
                    NullLogger<HomeAssistantService>.Instance),
                NullLogger<NotificationService>.Instance),
            new AppSettingsRepository(_pfade),
            NullLogger<TrendWatchRunner>.Instance);

        return laeufer.Inspect(growId, DateTime.Now)
            .Select(b => $"{b.Code}/{b.Severity}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    private KnowledgeBaseLoader Wissen()
    {
        var ziel = Path.Combine(_wurzel, "wwwroot", "knowledge-defaults");
        if (!Directory.Exists(ziel))
        {
            var quelle = Path.Combine(ProjektWurzel(), "GrowDiary.Web", "wwwroot", "knowledge-defaults");
            foreach (var datei in Directory.EnumerateFiles(quelle, "*.json", SearchOption.AllDirectories))
            {
                var pfad = Path.Combine(ziel, Path.GetRelativePath(quelle, datei));
                Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
                File.Copy(datei, pfad);
            }
        }

        var lader = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        lader.Initialize();
        return lader;
    }

    /// <summary>Zeilen, in denen /* */-Blöcke durch Leerraum ersetzt sind.</summary>
    /// <remarks>
    /// Die Zeilennummern bleiben erhalten — die Meldung soll auf die richtige
    /// Stelle zeigen, nicht auf eine verschobene.
    /// </remarks>
    private static string[] OhneBlockKommentare(string[] zeilen)
    {
        var raus = new string[zeilen.Length];
        var drin = false;

        for (var i = 0; i < zeilen.Length; i += 1)
        {
            var zeile = zeilen[i];
            var gebaut = new System.Text.StringBuilder(zeile.Length);

            for (var j = 0; j < zeile.Length; j += 1)
            {
                if (!drin && j + 1 < zeile.Length && zeile[j] == '/' && zeile[j + 1] == '*')
                {
                    drin = true;
                    j += 1;
                    continue;
                }

                if (drin && j + 1 < zeile.Length && zeile[j] == '*' && zeile[j + 1] == '/')
                {
                    drin = false;
                    j += 1;
                    continue;
                }

                gebaut.Append(drin ? ' ' : zeile[j]);
            }

            raus[i] = gebaut.ToString();
        }

        return raus;
    }

    private static string Quelltext(params string[] teile)
        => File.ReadAllText(Path.Combine(new[] { ProjektWurzel(), "GrowDiary.Web" }.Concat(teile).ToArray()));

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
