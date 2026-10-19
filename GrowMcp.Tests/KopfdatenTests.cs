using System.Text.Json;
using GrowMcp.Tools;

namespace GrowMcp.Tests;

/// <summary>
/// Die Übersicht darf keine Übersicht nur dem Namen nach sein.
/// </summary>
/// <remarks>
/// Beim ersten echten Einsatz lieferte <c>wissen_liste</c> alle elf Abläufe
/// vollständig aus — mit jedem Schritt, jeder Materialliste und jeder Quelle.
/// Das ist genau der Papierstapel, den dieses Add-on gegenüber der
/// Berater-Mappe vermeiden soll: das Modell soll gezielt nachschlagen, nicht
/// alles auf einmal vorgelegt bekommen.
/// </remarks>
public sealed class KopfdatenTests
{
    /// <summary>Ein Ablauf, wie Cultivate Control ihn ausliefert — verkürzt, aber echt gebaut.</summary>
    private const string Ablauf = """
        [{
          "name": "Wurzelfäule-Behandlung (14 Tage)",
          "type": "MultiDay",
          "durationDays": 14,
          "applicableSetups": ["RDWC", "DWC"],
          "steps": [{ "id": "s1", "title": "Symptome dokumentieren" }],
          "sources": [{ "type": "UserDocument", "title": "SOP-RDWC-CAN-S1" }],
          "id": "root-rot-treatment"
        }]
        """;

    [Fact]
    public void TheOverviewKeepsWhatYouNeedToLookSomethingUp()
    {
        using var json = JsonDocument.Parse(GrowTools.NurKopfdaten(Ablauf));
        var eintrag = json.RootElement[0];

        // Ohne das Kuerzel ist die Uebersicht wertlos — sie ist genau dafuer da.
        Assert.Equal("root-rot-treatment", eintrag.GetProperty("id").GetString());
        Assert.Equal("Wurzelfäule-Behandlung (14 Tage)", eintrag.GetProperty("name").GetString());
        Assert.Equal("MultiDay", eintrag.GetProperty("type").GetString());
        Assert.Equal(14, eintrag.GetProperty("durationDays").GetInt32());
    }

    [Fact]
    public void TheOverviewDropsEverythingDeep()
    {
        var gekuerzt = GrowTools.NurKopfdaten(Ablauf);

        Assert.DoesNotContain("steps", gekuerzt);
        Assert.DoesNotContain("sources", gekuerzt);
        Assert.DoesNotContain("applicableSetups", gekuerzt);
        // Die Kuerzung muss sich lohnen, sonst ist sie nur Umstand.
        Assert.True(gekuerzt.Length < Ablauf.Length / 2);
    }

    [Fact]
    public void UmlautsSurviveTheShortening()
    {
        // Der Text wird neu geschrieben, nicht durchgereicht — eine falsche
        // Kodierung faellt sonst erst beim Nutzer auf.
        Assert.Contains("Wurzelfäule", GrowTools.NurKopfdaten(Ablauf));
    }

    [Fact]
    public void SomethingThatIsNotAListIsHandedThroughUntouched()
    {
        const string kaputt = """{"fehler":"kein Feld"}""";

        Assert.Equal(kaputt, GrowTools.NurKopfdaten(kaputt));
    }
}
