using System.Text.RegularExpressions;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Niemand rechnet Phasenbeginne an <c>Phasenanker</c> vorbei.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (02.10.2026).</b> „Wann beginnt die Vegi" stand an vier
/// Stellen verschieden (Resolver, Mischplan, Plan-Auswertung, Wochenzähler),
/// dazu Demobestand, Nachtabsenkung und die Wochenplan-Seite mit eigenem
/// Flip-Lesen. Jede las die Rohfelder des Grows selbst.</para>
///
/// <para><b>Die Grundmenge</b> sind alle Quelldateien des Backends; gezählt wird
/// jede Erwähnung eines Rohfelds, aus dem ein Phasenbeginn entsteht —
/// Kommentare und XML-Doku ausgenommen. Wer ein Feld liest, steht unten mit
/// Grund, oder der Test wird rot.</para>
/// </remarks>
public sealed class PhasenankerVollstaendigTests
{
    private static readonly Regex Rohfeld = new(
        @"\b(VegStartedAt|RootedAt|FlipDate|GerminatedAt|FinishStartedAt|DaysAlreadyInPhase|AutoflowerDaysSinceGermination|CloneIsRooted|SeedlingDays|AutoflowerBluetenStart)\b",
        RegexOptions.Compiled);

    private static readonly Regex Blockkommentar = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Zeilenkommentar = new(@"//.*?$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Wer die Rohfelder anfassen darf — Pfad relativ zu GrowDiary.Web, mit Grund.</summary>
    private static readonly Dictionary<string, string> Ausnahmen = new()
    {
        ["Services/Phasenanker.cs"] = "Die eine Stelle, an der Phasenbeginne entstehen.",
        ["Services/PhasenankerUebernahme.cs"] = "Eingefrorene alte Schätzung für die einmalige Übernahme; schreibt die Anker.",
        ["Models/GrowRun.cs"] = "Die Felder selbst.",
        ["Infrastructure/GrowCoreRepository.cs"] = "Speichern und Laden.",
        ["Infrastructure/DatabaseInitializer.Schema.cs"] = "Spalten im Schema.",
        ["Api/Contracts/GrowDetailDto.cs"] = "Reicht die Rohfelder an Formular und Export durch.",
        ["Api/Contracts/GrowSummaryDto.cs"] = "Reicht die Rohfelder an die Listen durch (Knöpfe prüfen, ob schon bestätigt).",
        ["Api/Contracts/GrowUpsertRequest.cs"] = "Formularfelder (Einstieg, mitgebrachte Tage, Flipdatum).",
        ["Api/Mapping/GrowMapping.cs"] = "Abbildung Grow → DTO, ohne Rechnung.",
        ["Api/Mapping/RequestMapping.cs"] = "Abbildung Formular → Grow, ohne Rechnung.",
        ["ViewModels/GrowFormViewModel.cs"] = "Formular; setzt das Keimdatum beim späteren Einstieg (der Anker wertet es NICHT als Vegi).",
        ["Api/Controllers/GrowsApiController.cs"] = "Bewahrt bestätigte Beginne beim Bearbeiten (Zeilen-Ersatz).",
        ["Api/Controllers/GrowWorkflowApiController.cs"] = "Die Bestätigungen selbst: setzt die Anker, prüft nur „schon gesetzt?“.",
        ["Api/Controllers/GrowExportsApiController.Import.cs"] = "Kopiert die Anker eines importierten Laufs.",
        ["Services/Demobestand.cs"] = "Legt die Testdaten mit bestätigten Ankern an.",
        ["Services/Demobestand.Strom.cs"] = "Legt den zweiten Test-Grow (Blütezelt 2) mit bestätigten Ankern an — schreibt, rechnet nicht.",
    };

    [Fact]
    public void NiemandLiestDieRohfelderAnDerFunktionVorbei()
    {
        var treffer = Treffer();
        var fremd = treffer
            .Where(t => !Ausnahmen.ContainsKey(t.Key))
            .Select(t => $"{t.Key}: {string.Join(", ", t.Value)}")
            .ToList();

        Assert.True(fremd.Count == 0,
            "Diese Dateien rechnen Phasenbeginne selbst — Phasenanker.Fuer benutzen oder mit Grund eintragen:\n  "
            + string.Join("\n  ", fremd));
    }

    [Fact]
    public void JedeAusnahmeIstEchtUndNochNoetig()
    {
        var wurzel = WebWurzel();
        var treffer = Treffer();
        foreach (var (pfad, grund) in Ausnahmen)
        {
            Assert.True(File.Exists(Path.Combine(wurzel, pfad)), $"Ausnahme {pfad} gibt es nicht — Kennung aus dem Kopf?");
            Assert.False(string.IsNullOrWhiteSpace(grund));
            Assert.True(treffer.ContainsKey(pfad), $"{pfad} liest kein Rohfeld mehr — Ausnahme streichen.");
        }
    }

    [Fact]
    public void DieZaehlungSiehtIhreGrundmenge()
    {
        // Mengenwächter: ohne Dateien liefe alles null Mal und wäre grün.
        Assert.True(WebDateien().Count >= 300, $"nur {WebDateien().Count} Dateien gesehen");
        var treffer = Treffer();
        Assert.True(treffer["Services/Phasenanker.cs"].Count >= 8);

        // Kommentare zählen nicht, Code schon.
        Assert.Empty(Felder("// grow.FlipDate\n/* VegStartedAt */\n/// <see cref=\"GrowRun.RootedAt\"/>"));
        Assert.Equal(["FlipDate"], Felder("var x = grow.FlipDate; // VegStartedAt"));
    }

    private static List<string> Felder(string code)
        => Rohfeld.Matches(OhneKommentare(code)).Select(m => m.Value).ToList();

    private static Dictionary<string, List<string>> Treffer()
    {
        var wurzel = WebWurzel();
        var ergebnis = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var datei in WebDateien())
        {
            var felder = Felder(File.ReadAllText(datei));
            if (felder.Count == 0) continue;
            var rel = Path.GetRelativePath(wurzel, datei).Replace(Path.DirectorySeparatorChar, '/');
            ergebnis[rel] = felder.Distinct().ToList();
        }
        return ergebnis;
    }

    private static string OhneKommentare(string code)
        => Zeilenkommentar.Replace(Blockkommentar.Replace(code, string.Empty), string.Empty);

    private static List<string> WebDateien()
        => Directory.EnumerateFiles(WebWurzel(), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

    private static string WebWurzel()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) return Path.Combine(dir, "GrowDiary.Web");
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
