using System.Text.Json;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Das Backend liest Zahlen nach denselben zwei Regeln wie das Frontend — und
/// nur an einer Stelle.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (Durchsicht 01.–03.10.2026, offene Punkte B1/B2).</b>
/// Vier Stellen lasen Home-Assistant-Zustände mit <c>NumberStyles.Any</c> —
/// ein Vorlagen-Sensor mit „5,8" wurde dort zu 58. Und der freie Text
/// „Reservoir" („1.200 L", so schreibt ihn das Grow-Formular) wurde zu 1,2
/// Litern.</para>
/// </remarks>
public sealed class ZahlenlesenTests
{
    private sealed record Tabelle(List<JsonElement[]> Getippt, List<JsonElement[]> Maschine);

    private static Tabelle Laden()
    {
        var pfad = Path.Combine(ProjektWurzel(), "GrowDiary.React", "src", "zahlen-leseregeln.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(pfad));
        List<JsonElement[]> Faelle(string name) => doc.RootElement.GetProperty(name).EnumerateArray()
            .Select(fall => fall.EnumerateArray().Select(x => x.Clone()).ToArray()).ToList();
        return new Tabelle(Faelle("getippt"), Faelle("maschine"));
    }

    private static double? Erwartet(JsonElement wert)
        => wert.ValueKind == JsonValueKind.Null ? null : wert.GetDouble();

    /// <summary>Die Tabelle ist da und nicht leer — sonst prüfen die beiden Fälle unten nichts.</summary>
    [Fact]
    public void DieFalltabelleSiehtIhreGrundmenge()
    {
        var tabelle = Laden();
        Assert.True(tabelle.Getippt.Count >= 30, $"Nur {tabelle.Getippt.Count} getippte Fälle.");
        Assert.True(tabelle.Maschine.Count >= 15, $"Nur {tabelle.Maschine.Count} Maschinen-Fälle.");
    }

    /// <summary>Getippter Text: dieselben Fälle wie <c>zahlOderNull</c> im Frontend.</summary>
    [Fact]
    public void GetipptErfuelltJedenFallDerGemeinsamenTabelle()
    {
        var falsch = Laden().Getippt
            .Select(f => (roh: f[0].GetString()!, soll: Erwartet(f[1])))
            .Where(f => Zahlenlesen.Getippt(f.roh) != f.soll)
            .Select(f => $"„{f.roh}“ → {Zahlenlesen.Getippt(f.roh)?.ToString() ?? "null"} statt {f.soll?.ToString() ?? "null"}")
            .ToList();

        Assert.True(falsch.Count == 0,
            "Backend und Frontend lesen getippten Text verschieden: " + string.Join("; ", falsch));
    }

    /// <summary>Maschinen-Text: dieselben Fälle wie <c>maschinenZahl</c> im Frontend.</summary>
    [Fact]
    public void MaschineErfuelltJedenFallDerGemeinsamenTabelle()
    {
        var falsch = Laden().Maschine
            .Select(f => (roh: f[0].GetString()!, soll: Erwartet(f[1])))
            .Where(f => Zahlenlesen.Maschine(f.roh) != f.soll)
            .Select(f => $"„{f.roh}“ → {Zahlenlesen.Maschine(f.roh)?.ToString() ?? "null"} statt {f.soll?.ToString() ?? "null"}")
            .ToList();

        Assert.True(falsch.Count == 0,
            "Backend und Frontend lesen Home-Assistant-Zustände verschieden: " + string.Join("; ", falsch));
    }

    /// <summary>Der freie Reservoir-Text eines Grows (B2).</summary>
    [Theory]
    [InlineData("1.200 L", 1200.0)]                    // so schreibt das Grow-Formular 1200 Liter
    [InlineData("1.200,5 L", 1200.5)]
    [InlineData("1200.5 L Gesamtvolumen", 1200.5)]     // so schreibt GrowsApiController
    [InlineData("38 L", 38.0)]
    [InlineData("100 L Tank", 100.0)]
    [InlineData("60L", 60.0)]
    [InlineData("12,5 l", 12.5)]
    [InlineData("Tank: 80 Liter.", 80.0)]
    [InlineData("1.2,5 L", null)]                      // Gemisch: lieber keine Zahl als eine geratene
    [InlineData("groß", null)]
    [InlineData("–", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ErsteGetippteZahl_LiestDenReservoirText(string? text, double? liter)
    {
        Assert.Equal(liter, Zahlenlesen.ErsteGetippteZahl(text));
    }

    /// <summary>
    /// Kein Backend-Code liest Fließkommazahlen selbst — nur <see cref="Zahlenlesen"/>.
    /// </summary>
    /// <remarks>
    /// <para>Vor dem 03.10.2026 gab es 22 eigene Fassungen in drei Spielarten
    /// (<c>NumberStyles.Any</c>, <c>NumberStyles.Float</c>,
    /// <c>Replace(',', '.')</c>). Eine 23. würde wieder nach eigener Regel
    /// lesen.</para>
    /// <para>Kommentare zählen nicht — eine Erwähnung ist keine
    /// Verwendung.</para>
    /// </remarks>
    [Fact]
    public void NiemandLiestFliesskommazahlenSelbst()
    {
        var treffer = EigeneFassungen().Where(t => !t.StartsWith("Zahlenlesen.cs:", StringComparison.Ordinal)).ToList();

        Assert.True(treffer.Count == 0,
            "Eigene Zahlen-Umwandlung gefunden: " + string.Join(", ", treffer)
            + ". Benutze Zahlenlesen.Maschine (Home Assistant, Datenbank) oder Zahlenlesen.Getippt "
            + "(vom Menschen getippt, deutsche Leseregel: „1.200\" = 1200).");
    }

    /// <summary>Und die Zählung findet die eine erlaubte Stelle — sonst sieht sie nichts.</summary>
    [Fact]
    public void DieZaehlungFindetDieEineErlaubteStelle()
    {
        Assert.Contains(EigeneFassungen(), t => t.StartsWith("Zahlenlesen.cs:", StringComparison.Ordinal));
    }

    /// <summary>Jede Schreibweise wird erkannt — und keine Erwähnung.</summary>
    /// <remarks>
    /// Die erste Fassung der Zählung übersah vier Schreibweisen (Prüfer,
    /// 03.10.2026): <c>Double.Parse</c>, <c>Replace(",", ".")</c> mit
    /// Zeichenketten und jede Fassung hinter einem <c>"http://…"</c> in derselben
    /// Zeile — dort schnitt <c>Split("//")</c> den Code ab.
    /// </remarks>
    [Theory]
    [InlineData("var x = double.TryParse(s, out var v);", true)]
    [InlineData("var x = Double.Parse(s);", true)]
    [InlineData("var x = decimal.Parse(s, CultureInfo.InvariantCulture);", true)]
    [InlineData("var x = float.TryParse(s, out var f);", true)]
    [InlineData("var x = Single.Parse(s);", true)]
    [InlineData("var t = s.Replace(',', '.');", true)]
    [InlineData("var t = s.Replace(\",\", \".\");", true)]
    [InlineData("var stil = NumberStyles.Any;", true)]
    [InlineData("var u = \"http://ha.local\"; var x = double.Parse(s);", true)]
    [InlineData("// double.Parse(s) war hier", false)]
    [InlineData("var x = 1; // double.Parse(s)", false)]
    [InlineData("/// <c>double.TryParse</c> stand hier", false)]
    [InlineData("var u = \"http://ha.local\";", false)]
    public void DieZaehlungErkenntJedeSchreibweise(string zeile, bool fassung)
    {
        Assert.Equal(fassung, IstEigeneFassung(zeile));
    }

    /// <summary>Das Kennzeichen einer eigenen Fassung (Groß-/Kleinschreibung egal).</summary>
    /// <remarks>
    /// <c>Convert.ToDouble</c> steht NICHT darin: im Backend wandelt es
    /// ausschließlich Datenbankwerte, die schon Zahlen sind
    /// (<c>RepositoryBase</c>, <c>HydroSetupRepository</c>). Ob ihm ein Text
    /// übergeben wird, sieht eine Textsuche nicht.
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex Kennzeichen = new(
        @"\b(double|single|float|decimal)\.(try)?parse\(|numberstyles\.any|\.replace\(\s*(',',\s*'\.'|"","",\s*""\."")\s*\)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IstEigeneFassung(string zeile)
    {
        var code = OhneKommentar(zeile.Trim());
        return code.Length > 0 && Kennzeichen.IsMatch(code);
    }

    /// <summary>
    /// Die Zeile ohne Kommentar — ein <c>//</c> in einer Zeichenkette ist keiner.
    /// </summary>
    private static string OhneKommentar(string zeile)
    {
        if (zeile.StartsWith('*') || zeile.StartsWith("/*", StringComparison.Ordinal)) return "";
        var inText = false;
        var inZeichen = false;
        for (var i = 0; i < zeile.Length; i++)
        {
            var c = zeile[i];
            if ((inText || inZeichen) && c == '\\') { i++; continue; }
            if (!inZeichen && c == '"') inText = !inText;
            else if (!inText && c == '\'') inZeichen = !inZeichen;
            else if (!inText && !inZeichen && c == '/' && i + 1 < zeile.Length && zeile[i + 1] == '/') return zeile[..i];
        }
        return zeile;
    }

    private static List<string> EigeneFassungen()
    {
        var quelle = Path.Combine(ProjektWurzel(), "GrowDiary.Web");
        var dateien = Directory.EnumerateFiles(quelle, "*.cs", SearchOption.AllDirectories)
            .Where(d => !d.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !d.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        // Mengenwächter: ohne Grundmenge läuft die Schleife null Mal.
        Assert.True(dateien.Count >= 100, $"Nur {dateien.Count} Quelldateien gefunden — die Grundmenge stimmt nicht.");

        var treffer = new List<string>();
        foreach (var datei in dateien)
        {
            var zeilen = File.ReadAllLines(datei);
            for (var i = 0; i < zeilen.Length; i += 1)
            {
                if (IstEigeneFassung(zeilen[i])) treffer.Add($"{Path.GetFileName(datei)}:{i + 1}");
            }
        }

        return treffer;
    }

    private static string ProjektWurzel()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))
                && Directory.Exists(Path.Combine(dir, "GrowDiary.React"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
