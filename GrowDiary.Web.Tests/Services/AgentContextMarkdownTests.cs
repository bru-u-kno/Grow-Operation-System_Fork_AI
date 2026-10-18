using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Der Lagebericht für einen fremden Agenten.
/// </summary>
/// <remarks>
/// Geprüft wird nicht das Layout, sondern was inhaltlich drinstehen muss. Der
/// entscheidende Punkt ist die Herkunft jedes Ziels: ohne sie kann ein Agent
/// nicht unterscheiden, ob 6,05 eine bewusste Entscheidung des Betreibers ist
/// oder unser mitgelieferter Vorschlag — und widerspricht dem einen, als wäre es
/// das andere.
/// </remarks>
public sealed class AgentContextMarkdownTests
{
    private static readonly DateTime Jetzt = new(2026, 7, 28, 14, 30, 0, DateTimeKind.Utc);

    private static AgentContext Context(
        IReadOnlyList<AgentMetricLine>? metrics = null,
        IReadOnlyList<string>? issues = null,
        IReadOnlyList<string>? journal = null,
        IReadOnlyList<string>? doses = null,
        IReadOnlyList<string>? water = null)
        => new(
            GrowName: "Purple Lemonade #1",
            Stage: "Blüte",
            DayInStage: null,
            DayTotal: 70,
            HydroStyle: "RDWC",
            ProfileName: "Meine RDWC-Werte (am Grow gesetzt)",
            ReservoirLiters: 25,
            WaterNotes: water ?? [],
            Metrics: metrics ?? [],
            OpenIssues: issues ?? [],
            RecentJournal: journal ?? [],
            RecentDoses: doses ?? [],
            GeneratedAtUtc: Jetzt);

    private static AgentMetricLine Line(
        string label = "pH", double? value = 6.07, double? min = 5.8, double? max = 6.2,
        string source = "vom Nutzer eingetragen", int? age = 2, string verdict = "im Ziel")
        => new(label, label.ToLowerInvariant(), value, null, min, max, source, age, verdict);

    [Fact]
    public void TheHeaderCarriesPhaseSystemAndProfile()
    {
        var text = AgentContextBuilder.ToMarkdown(Context());

        Assert.Contains("Purple Lemonade #1", text);
        Assert.Contains("Blüte", text);
        Assert.Contains("Tag 70", text);
        Assert.Contains("RDWC", text);
        Assert.Contains("25 L", text);
        Assert.Contains("Meine RDWC-Werte (am Grow gesetzt)", text);
    }

    [Fact]
    public void TapWaterShowsUpBeforeTheReadings()
    {
        // Wer die Messwerte liest, muss vorher wissen, was davon schon das
        // Ausgangswasser ist — sonst wird EC 0,28 vor dem Düngen zum Rest-Salz
        // erklärt und ein Wasserwechsel empfohlen, der nichts ändern würde.
        var text = AgentContextBuilder.ToMarkdown(Context(
            water: ["Leitungswasser — Quelle: EBW Solingen", "Start-EC 0,28 mS/cm"]));

        Assert.Contains("## Ausgangswasser", text);
        var wasser = text.IndexOf("## Ausgangswasser", StringComparison.Ordinal);
        var werte = text.IndexOf("## Aktuelle Werte", StringComparison.Ordinal);
        Assert.True(wasser < werte, "Das Ausgangswasser gehört VOR die Messwerte.");
        Assert.Contains("EBW Solingen", text);
    }

    [Fact]
    public void AnRoGrowCarriesNoWaterSection()
    {
        // RO bringt nichts mit; ein leerer Abschnitt wäre nur Rauschen.
        Assert.DoesNotContain("## Ausgangswasser", AgentContextBuilder.ToMarkdown(Context()));
    }

    [Fact]
    public void EveryTargetSaysWhereItCameFrom()
    {
        var text = AgentContextBuilder.ToMarkdown(Context([
            Line(source: "vom Nutzer eingetragen"),
            Line("EC", 1.6, 1.0, 1.2, "Phasen-Profil", 2, "über dem Ziel"),
        ]));

        Assert.Contains("vom Nutzer eingetragen", text);
        Assert.Contains("Phasen-Profil", text);
        // Und der Hinweis, was der Unterschied bedeutet.
        Assert.Contains("bewusste Entscheidung", text);
    }

    [Fact]
    public void ATargetWithoutSpreadIsShownAsOneValue()
    {
        // „5,8–5,8“ las sich wie ein Fehler; ein Ziel ohne Spanne ist ein einzelner Wert.
        var text = AgentContextBuilder.ToMarkdown(Context([Line("pH", 6.08, 5.8, 5.8, "Phasen-Profil", 0, "über dem Ziel")]));

        Assert.Contains("| 5,8 |", text);
        Assert.DoesNotContain("5,8–5,8", text);
    }

    [Fact]
    public void EachRowCarriesItsVerdict()
    {
        // Damit ein Agent nicht selbst vergleichen muss — und dabei die Einheit
        // oder die Richtung verwechselt.
        var text = AgentContextBuilder.ToMarkdown(Context([Line(verdict: "über dem Ziel")]));

        Assert.Contains("über dem Ziel", text);
    }

    [Fact]
    public void AHalfOpenTargetIsWrittenAsSuch()
    {
        // Gegen dieselbe Kultur geprueft, die der Bericht benutzt. Auf das
        // Komma zu prüfen hiesse, die Kultur des Rechners zu prüfen: ohne ICU
        // faellt AppCulture auf invariant zurueck, und der Test waere rot,
        // obwohl der Bericht genau das Richtige tut.
        var nurOben = AgentContextBuilder.ToMarkdown(Context([Line(min: null, max: 6.2)]));
        var nurUnten = AgentContextBuilder.ToMarkdown(Context([Line(min: 5.8, max: null)]));

        Assert.Contains($"bis {6.2.ToString("0.##", AppCulture.German)}", nurOben);
        Assert.Contains($"ab {5.8.ToString("0.##", AppCulture.German)}", nurUnten);
    }

    [Fact]
    public void AMissingValueIsADashAndNotAZero()
    {
        // Eine 0 waere eine Aussage — und pH 0 eine dramatische.
        var text = AgentContextBuilder.ToMarkdown(Context([Line(value: null, age: null, verdict: "kein Messwert")]));

        Assert.Contains("| — | — |", text);
        Assert.DoesNotContain("| 0 |", text);
    }

    [Fact]
    public void TheAgeOfEachValueIsStated()
    {
        // Ein Agent muss wissen, ob er gegen einen frischen Wert oder gegen
        // vorgestern raet.
        Assert.Contains("2 min", AgentContextBuilder.ToMarkdown(Context([Line(age: 2)])));
        Assert.Contains("4320 min", AgentContextBuilder.ToMarkdown(Context([Line(age: 4320)])));
    }

    [Fact]
    public void EmptySectionsSaySoInsteadOfBeingBlank()
    {
        // Ein leerer Abschnitt liest sich wie fehlende Daten. „Keine offenen
        // Punkte" ist eine Aussage.
        var text = AgentContextBuilder.ToMarkdown(Context());

        Assert.Contains("Keine offenen Punkte.", text);
        Assert.Contains("Es wurde noch nichts dosiert.", text);
        Assert.Contains("Noch keine Einträge.", text);
    }

    [Fact]
    public void ListedItemsAppearAsBullets()
    {
        var text = AgentContextBuilder.ToMarkdown(Context(
            issues: ["2026-07-28 · Critical · ORP zu niedrig"],
            doses: ["2026-07-28 04:28 · pH Minus · 2,5 ml, 6,31 → 6,04"],
            journal: ["2026-07-27 · Blätter hängen morgens"]));

        Assert.Contains("- 2026-07-28 · Critical · ORP zu niedrig", text);
        Assert.Contains("- 2026-07-28 04:28 · pH Minus · 2,5 ml, 6,31 → 6,04", text);
        Assert.Contains("- 2026-07-27 · Blätter hängen morgens", text);
    }
}
