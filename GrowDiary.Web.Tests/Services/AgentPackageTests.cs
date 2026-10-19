using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Berater-Mappe: das Wissen von Cultivate Control als lesbarer Text.
/// </summary>
/// <remarks>
/// Geprüft wird nicht die Formatierung, sondern was ein Berater daraus
/// beantworten kann. Eine Mappe, in der die Verzweigung eines Ablaufs oder das
/// Verbot einer Behandlung fehlt, sieht vollständig aus und führt trotzdem zu
/// falschen Empfehlungen.
/// </remarks>
public sealed class AgentPackageTests
{
    [Fact]
    public void ASopKeepsItsBranchesAndOrder()
    {
        var sop = new SopDefinition
        {
            Id = "root-rot-treatment",
            Name = "Wurzelfäule behandeln",
            Type = "MultiDay",
            RequiredMaterials = ["HOCl 750 ppm", "Luftsteine"],
            Steps =
            [
                new SopStepDefinition
                {
                    Order = 2,
                    Title = "Wurzeln spülen",
                    Description = "Jede Pflanze einzeln.",
                    RepeatFor = "plant",
                    Condition = new SopStepCondition { Key = "severity", EqualsAny = ["severe"] },
                },
                new SopStepDefinition { Order = 1, Title = "Reservoir leeren", Description = "Komplett ablassen." },
            ],
        };

        var text = AgentKnowledgeRenderer.Sops([sop]);

        // Die Reihenfolge kommt aus Order, nicht aus der Liste — sonst stünde
        // Schritt 2 vor Schritt 1.
        Assert.True(text.IndexOf("Reservoir leeren", StringComparison.Ordinal)
                  < text.IndexOf("Wurzeln spülen", StringComparison.Ordinal));

        // Ohne die Bedingung liest sich ein Zweig wie eine Anweisung an alle.
        Assert.Contains("nur wenn severity = severe", text);
        Assert.Contains("je plant wiederholen", text);
        Assert.Contains("HOCl 750 ppm", text);
    }

    [Fact]
    public void ATreatmentCarriesItsProhibitions()
    {
        var mittel = new TreatmentDefinition
        {
            Id = "h2o2-reservoir",
            Name = "H₂O₂ ins Reservoir",
            Type = "Reservoir",
            Dosage = new TreatmentDosage { Standard = "1 ml/L" },
            Application = new TreatmentApplication { Method = "ins Becken geben" },
            PhaseFilter = new PhaseFilter { Blocked = ["Finish"], BlockAfterFlowerWeek = 6 },
            Restrictions = ["Nicht mit lebenden Mikroorganismen kombinieren."],
            Conflicts = [new TreatmentConflict { With = "beneficial-bacteria", MinimumGapHours = 48, Reason = "tötet die Kultur ab" }],
        };

        var text = AgentKnowledgeRenderer.Treatments([mittel]);

        Assert.Contains("1 ml/L", text);
        // Verbote stehen absichtlich fett — sie sind der teuerste Teil.
        Assert.Contains("**Nicht in Phase:** Finish", text);
        Assert.Contains("Nicht ab Blütewoche 6", text);
        Assert.Contains("Nicht mit lebenden Mikroorganismen", text);
        // Ein Konflikt ohne Abstand und Grund ist als Warnung wertlos.
        Assert.Contains("beneficial-bacteria", text);
        Assert.Contains("48 h Abstand", text);
        Assert.Contains("tötet die Kultur ab", text);
    }

    [Fact]
    public void ASymptomKeepsTheQuestionThatSeparatesItsCauses()
    {
        var symptom = new SymptomDefinition
        {
            Id = "interveinal-chlorosis",
            Name = "Interveinale Chlorose",
            Category = "Leaf",
            PossibleCauses = ["Magnesium-Mangel (untere Blätter zuerst)", "Eisen-Mangel (obere Blätter zuerst)"],
            DiagnosticChecks = ["Sind obere oder untere Blätter betroffen?"],
            SuggestedTreatmentIds = ["calmag-foliar-acute-athena"],
        };

        var text = AgentKnowledgeRenderer.SymptomsAndPathogens([symptom], []);

        Assert.Contains("Magnesium-Mangel", text);
        Assert.Contains("Eisen-Mangel", text);
        // Ohne die Prüffrage bliebe nur Raten zwischen zwei Ursachen.
        Assert.Contains("Sind obere oder untere Blätter betroffen?", text);
    }

    [Fact]
    public void SetpointsAreWrittenInGerman()
    {
        var profil = new SetpointDefinition
        {
            Id = "rdwc-default",
            Name = "RDWC Standard",
            SystemType = "RDWC",
            Stages = { ["Veg"] = new StageSetpoints { PhMin = 5.8, PhMax = 6.2, EcMin = 1.0, EcMax = 1.2 } },
        };

        var text = AgentKnowledgeRenderer.Setpoints([profil], []);

        // Auf einem englischen Rechner stuende hier sonst "5.8-6.2".
        Assert.Contains("5,8–6,2", text);
        Assert.Contains("1–1,2", text);
    }

    /// <summary>
    /// Jede Prüffrage muss aus dem mitgelieferten Wissen beantwortbar sein.
    /// </summary>
    /// <remarks>
    /// Beim Schreiben der Fragen ist genau das schiefgegangen: die erste Fassung
    /// fragte nach „gelben Blatträndern", die im Material gar nicht vorkommen.
    /// Eine Prüffrage ohne Antwort in den Unterlagen prüft nicht den Berater,
    /// sondern bestraft ihn für eine Lücke der Mappe.
    /// </remarks>
    [Theory]
    [InlineData("root-rot-treatment")]
    [InlineData("flip-to-flower")]
    [InlineData("interveinal-chlorosis")]
    public void EveryTestQuestionPointsAtSomethingTheKnowledgeContains(string kuerzel)
    {
        Assert.Contains(kuerzel, AgentPromptTexts.Pruefragen);

        var verzeichnis = Path.Combine(FindProjectRoot(), "GrowDiary.Web", "wwwroot", "knowledge-defaults");
        var dateien = Directory.EnumerateFiles(verzeichnis, "*.json", SearchOption.AllDirectories);

        Assert.True(
            dateien.Any(datei => Path.GetFileNameWithoutExtension(datei) == kuerzel),
            $"Die Prüffragen nennen „{kuerzel}“, im mitgelieferten Wissen gibt es das aber nicht.");
    }

    [Fact]
    public void TheInstructionForbidsInventingNumbers()
    {
        // Der Satz ist der Kern des Ganzen. Faellt er beim Umformulieren raus,
        // ist die Mappe ein Ratespiel mit Quellenangabe.
        Assert.Contains("erfindest keine Zahlen", AgentPromptTexts.Systemanweisung);
        Assert.Contains("schaltest nichts", AgentPromptTexts.Systemanweisung);
        Assert.Contains("vom Nutzer eingetragen", AgentPromptTexts.Systemanweisung);
    }

    /// <summary>
    /// Ein Verbot allein macht den Berater stumm.
    /// </summary>
    /// <remarks>
    /// Im ersten echten Test mit ChatGPT ging genau das schief: Auf die Frage
    /// nach Wurzelfaeule nannte es die richtigen Behandlungen, verschwieg aber
    /// deren Dosierung — „ich nenne bewusst keine Dosierungen". Die Menge stand
    /// in der Mappe. Die Anweisung kannte nur das Verbot, nie die Erlaubnis;
    /// wer am Becken steht, braucht aber die Zahl und nicht den Hinweis, dass
    /// es eine gibt.
    /// </remarks>
    [Fact]
    public void TheInstructionAlsoAllowsQuotingDocumentedNumbers()
    {
        Assert.Contains("nenne sie", AgentPromptTexts.Systemanweisung);
        Assert.Contains("Zurückhalten hilft niemandem", AgentPromptTexts.Systemanweisung);
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.sln").Length > 0 ||
                Directory.Exists(Path.Combine(dir, "GrowDiary.Web")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
