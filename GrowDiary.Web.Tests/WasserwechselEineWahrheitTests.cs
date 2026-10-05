using System.Text.RegularExpressions;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Niemand rechnet „wann war der letzte Wasserwechsel" auf eigene Faust.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (31.08.2026).</b> Ein Wasserwechsel kann auf zwei Wegen
/// erfasst werden: als Häkchen <c>SolutionChange</c> an einer Messung oder als
/// eigener Satz über das Formular auf /addback. Vier Dienste rechneten mit
/// „zuletzt gewechselt" — <b>drei</b> davon sahen nur den ersten Weg. Wer den
/// Wechsel im Formular eintrug, räumte damit keine einzige Mahnung weg.</para>
///
/// <para><b>Warum eine Zählung und keine Liste.</b> Eine handgeschriebene Liste
/// der vier Dienste hätte den fünften nicht gefunden. Diese Prüfung geht über
/// die <b>Grundmenge</b> — alle <c>.cs</c>-Dateien des Web-Projekts — und
/// verlangt für jede Stelle, die Messungen nach <c>SolutionChange</c> filtert,
/// entweder den Weg über <see cref="GrowDiary.Web.Services.Wasserwechsel"/>
/// oder einen ausgeschriebenen Grund.</para>
///
/// <para><b>Kommentare zählen nicht.</b> Die Regel „eine Erwähnung ist keine
/// Verwendung" ist in diesem Projekt mehrfach zugeschnappt: eine Textsuche, die
/// Kommentare mitliest, wird von ihrer eigenen Dokumentation grün gemacht.
/// Deshalb werden Zeilen- und Blockkommentare vorher entfernt.</para>
/// </remarks>
public sealed class WasserwechselEineWahrheitTests
{
    /// <summary>
    /// Die einzige Datei, in der der Filter stehen darf — sie IST die Antwort.
    /// </summary>
    private const string DieQuelle = "Wasserwechsel.cs";

    /// <summary>
    /// Dateien, die <c>SolutionChange</c> filtern dürfen, ohne zu rechnen —
    /// je mit ausgeschriebenem Grund.
    /// </summary>
    private static readonly Dictionary<string, string> Ausnahmen = new(StringComparer.Ordinal)
    {
        ["Demobestand.cs"] =
            "Der Testbestand SETZT die Markierung beim Säen, er liest sie nicht. "
            + "Er ist die Quelle der Daten, gegen die alles andere prüft.",

        ["TagebuchService.cs"] =
            "Das Grow-Tagebuch (A-006) zeigt JEDE Messung mit Haken an ihrem Tag — als Teil des "
            + "Wasserwechsel-Vorgangs oder mit dem Vermerk Lösungswechsel — und nimmt sie als "
            + "Eintrag, der einen Sprung im Sensorverlauf erklärt. Einen „letzten Wechsel“ rechnet "
            + "es nicht aus; dafür bleibt Wasserwechsel.cs die eine Antwort.",

        ["MeasurementRepository.cs"] =
            "Trägt die Spalte in beide Richtungen — Zeile lesen, Zeile schreiben "
            + "(`measurement.SolutionChange ? 1 : 0` im INSERT). Ein Transportweg "
            + "rechnet nichts aus; ohne ihn käme die Markierung gar nicht erst in "
            + "die Datenbank, aus der Wasserwechsel sie holt.",
    };

    /// <summary>
    /// Erkennt jedes <b>Lesen</b> von <c>SolutionChange</c> — also jeden
    /// Versuch, aus Messungen selbst den letzten Wechsel zu ziehen.
    /// </summary>
    /// <remarks>
    /// <para><b>Die erste Fassung kannte nur LINQ</b> (<c>=&gt; m.SolutionChange</c>
    /// und <c>Where(… .SolutionChange</c>). Der Prüfer hat eine zweite Wahrheit
    /// als gewöhnliche <c>foreach</c>-Schleife eingebaut, gebaut, den Test
    /// laufen lassen — <b>grün</b>. Eine Zählung, die nur eine Schreibweise
    /// kennt, prüft die Schreibweise und nicht die Sache.</para>
    ///
    /// <para>Jetzt wird <b>zeilenweise</b> geprüft, und die Unterscheidung ist
    /// eine andere: gelesen wird überall <c>irgendwas.SolutionChange</c> —
    /// <c>if (m.SolutionChange)</c>, <c>m.SolutionChange ? …</c>,
    /// <c>&amp;&amp; m.SolutionChange</c>, jede LINQ-Form. Draußen bleibt nur
    /// die <b>Kopie</b>: eine Zeile, die zugleich ein Feld dieses Namens
    /// beschreibt (<c>SolutionChange = quelle.SolutionChange,</c>). Das ist ein
    /// Transportweg zwischen zwei Schichten und rechnet nichts aus.</para>
    /// </remarks>
    private static readonly Regex LiestSolutionChange =
        new(@"\.SolutionChange\b", RegexOptions.Compiled);

    /// <summary>
    /// Die Zeile beschreibt ein Feld dieses Namens — also eine Kopie.
    /// </summary>
    /// <remarks>
    /// Beide Schreibweisen: <c>SolutionChange = quelle.SolutionChange,</c> im
    /// Objekt-Initialisierer und <c>SolutionChange: quelle.SolutionChange,</c>
    /// als benanntes Argument eines Records. Die zweite fehlte zuerst, und
    /// <c>MeasurementMapping.cs</c> stand prompt als Verstoß da, obwohl es ein
    /// Feld nur weiterreicht.
    /// </remarks>
    private static readonly Regex SchreibtSolutionChange =
        new(@"\bSolutionChange\s*[:=](?!=)", RegexOptions.Compiled);

    /// <summary>Rechnet diese Datei selbst mit dem letzten Wechsel?</summary>
    private static bool RechnetSelbst(string code)
        => code.Split('\n').Any(zeile =>
            LiestSolutionChange.IsMatch(zeile) && !SchreibtSolutionChange.IsMatch(zeile));

    private static readonly Regex Blockkommentar = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Zeilenkommentar = new(@"//.*?$", RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void NiemandRechnetDenLetztenWasserwechselSelbst()
    {
        var dateien = WebDateien();

        // Mengenwächter: ohne Grundmenge liefe die Schleife null Mal durch und
        // wäre grün. Genau so war die Kontrast-Prüfung dieses Projekts dreimal
        // blind.
        Assert.True(dateien.Count >= 200,
            $"Nur {dateien.Count} .cs-Dateien gefunden — die Prüfung sieht ihre Grundmenge nicht.");

        var verstoesse = new List<string>();
        var treffer = 0;

        foreach (var datei in dateien)
        {
            var name = Path.GetFileName(datei);
            var code = OhneKommentare(File.ReadAllText(datei));
            if (!RechnetSelbst(code)) continue;

            treffer += 1;
            if (name == DieQuelle || Ausnahmen.ContainsKey(name)) continue;

            verstoesse.Add($"{name} filtert Messungen selbst nach SolutionChange");
        }

        // Zweiter Mengenwächter — und er zählt die Treffer INKLUSIVE der Quelle.
        //
        // Die erste Fassung zählte nur die Stellen ausserhalb von
        // Wasserwechsel.cs und verlangte dort mindestens einen Treffer. Das ist
        // genau der Zustand, den der Fix herstellt: draussen filtert niemand
        // mehr. Der Wächter wäre also ab dem Tag rot gewesen, an dem er recht
        // hatte. Ein toter Suchausdruck fällt trotzdem auf: Wasserwechsel.cs
        // selbst filtert, und wenn nicht einmal dort etwas gefunden wird, misst
        // die Prüfung nichts mehr.
        Assert.True(treffer >= 1,
            "Der Suchausdruck findet nicht einmal in " + DieQuelle + " einen Filter auf "
            + "SolutionChange. Er greift nicht mehr — die Prüfung ist blind, nicht zufrieden.");

        Assert.True(verstoesse.Count == 0,
            "Diese Stellen rechnen „wann war der letzte Wasserwechsel\" selbst und sehen damit "
            + "nur die Messungen, nicht die Einträge aus dem Formular:\n  "
            + string.Join("\n  ", verstoesse)
            + "\n\nRichtig ist Wasserwechsel.ZuletztOrtszeit(…) bzw. .ZuletztUtc(…). "
            + "Wer wirklich nur schreibt oder abbildet, trägt sich mit Grund in Ausnahmen ein.");
    }

    /// <summary>
    /// Und die Quelle selbst liest wirklich beide Wege — sonst wäre die
    /// Zählung darüber eine Prüfung auf einen Namen statt auf ein Verhalten.
    /// </summary>
    [Fact]
    public void DieQuelleLiestBeideWege()
    {
        var quelle = WebDateien().Single(d => Path.GetFileName(d) == DieQuelle);
        var code = OhneKommentare(File.ReadAllText(quelle));

        Assert.True(RechnetSelbst(code), "Wasserwechsel.cs liest SolutionChange nicht mehr — "
            + "dann misst die Zaehlung darueber nichts.");
        Assert.Contains("SolutionChange", code, StringComparison.Ordinal);
        Assert.Contains("PerformedAtUtc", code, StringComparison.Ordinal);
    }

    private static string OhneKommentare(string code)
        => Zeilenkommentar.Replace(Blockkommentar.Replace(code, string.Empty), string.Empty);

    private static List<string> WebDateien()
    {
        var wurzel = Path.Combine(ProjektWurzel(), "GrowDiary.Web");
        return Directory.EnumerateFiles(wurzel, "*.cs", SearchOption.AllDirectories)
            .Where(pfad => !pfad.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(pfad => !pfad.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
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
