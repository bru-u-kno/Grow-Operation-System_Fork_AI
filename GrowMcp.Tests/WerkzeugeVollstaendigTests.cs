using GrowMcp.Tools;

namespace GrowMcp.Tests;

/// <summary>
/// Jedes MCP-Werkzeug wird mindestens einmal ausgeführt.
///
/// <para><b>Der Anlass.</b> Der Server bietet 22 Werkzeuge an, und <b>keines
/// davon</b> wurde je von einem Test aufgerufen — <c>GrowOsReader</c> kam in
/// keiner der sieben Testdateien vor. Getestet waren die Türen, der
/// Token-Speicher und das Zusammenlegen von Listen; das, was der Nutzer
/// tatsächlich benutzt, nicht.</para>
///
/// <para><b>Was diese Zählung fängt.</b> Ein Werkzeug, das bei einem
/// Standardargument sofort abstürzt. Ein Pfad, der nicht mehr existiert. Ein
/// neues Werkzeug, das jemand hinzufügt und nie ausprobiert. Sie ist keine
/// Prüfung der Inhalte — die hängen an der laufenden App — sondern die
/// Zusicherung, dass jedes Werkzeug überhaupt läuft und antwortet.</para>
///
/// <para><b>Der Aufbau.</b> Ein Stub-Server statt Home Assistant: die Adresse
/// wird von Hand gesetzt (<see cref="GrowOsOptions.Adresse"/>), und ein
/// eigener <see cref="HttpMessageHandler"/> beantwortet jeden Pfad mit einem
/// leeren JSON-Rumpf und merkt sich, wonach gefragt wurde.</para>
///
/// <para><b>Fork AI (A-004, 03.10.2026).</b> Gezählt wird über ALLE Klassen mit
/// <c>[McpServerToolType]</c>, nicht mehr nur über <see cref="GrowTools"/>:
/// mit den Schreib- und Home-Assistant-Werkzeugen kamen zwei Klassen dazu, und
/// eine Zählung über eine feste Klasse hätte sie nie gesehen. Jedes Werkzeug
/// läuft zweimal — mit dem MCP-Schlüssel (nur lesen) und mit einem Schlüssel
/// aus Grow OS.</para>
/// </summary>
public sealed class WerkzeugeVollstaendigTests
{
    public static IEnumerable<object?[]> Werkzeuge()
        => Werkzeugkasten.Werkzeugmethoden()
            .SelectMany(m => new object?[][]
            {
                [Werkzeugkasten.Name(m), null],
                [Werkzeugkasten.Name(m), Werkzeugkasten.ForkSchluessel],
            });

    [Fact]
    public void Der_Test_sieht_die_Werkzeuge()
    {
        // Sonst laeuft die Schleife null Mal und der Test ist gruen, ohne etwas
        // geprueft zu haben — die Falle, in die seine Vorgaenger gelaufen sind.
        // 23 lesende, 11 schreibende, 4 fuer Home Assistant.
        var anzahl = Werkzeugkasten.Werkzeugmethoden().Length;
        Assert.True(anzahl >= 38, $"Nur {anzahl} Werkzeuge gefunden — die Reflexion greift ins Leere.");
        Assert.True(Werkzeugkasten.Werkzeugklassen().Length >= 3,
            "Weniger als drei Werkzeugklassen — die Schreib- oder Home-Assistant-Werkzeuge fehlen in der Zaehlung.");
    }

    [Fact]
    public void Kein_Werkzeugname_kommt_doppelt_vor()
    {
        // Zwei Klassen, ein Name: der Klient saehe nur eines der beiden.
        var doppelt = Werkzeugkasten.Werkzeugmethoden()
            .GroupBy(Werkzeugkasten.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(doppelt);
    }

    [Theory]
    [MemberData(nameof(Werkzeuge))]
    public async Task Jedes_Werkzeug_laesst_sich_ausfuehren_und_antwortet(string werkzeug, string? schluessel)
    {
        var fork = new ForkAttrappe();
        var leser = Werkzeugkasten.Leser(fork, schluessel);

        // Pflichtargumente mit etwas Plausiblem fuellen: Ids mit 1, Texte mit
        // einem Wort. Optionale bleiben auf ihrem Standard — genau so ruft ein
        // Klient das Werkzeug beim ersten Mal auf.
        //
        // NICHT alle Werkzeuge geben Text zurueck: `foto_ansehen` liefert ein
        // Bild als ContentBlock-Folge. Ein erster Anlauf dieses Tests hat
        // stumpf auf Task<string> gecastet und ausgerechnet daran gescheitert —
        // an einem Werkzeug, das voellig in Ordnung ist.
        var wert = await Werkzeugkasten.AufrufenAsync(Werkzeugkasten.Werkzeug(werkzeug), leser);
        Assert.NotNull(wert);

        // Der Inhalt haengt an echten Daten und wird hier nicht geprueft. Was
        // geprueft wird: das Werkzeug laeuft durch und sagt etwas.
        if (wert is string text)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"{werkzeug} antwortet mit nichts.");
        }
    }

    [Fact]
    public async Task Die_Werkzeuge_fragen_Grow_OS_wirklich()
    {
        // Gegenprobe: liefe der Aufbau ins Leere, wuerde jedes Werkzeug nur eine
        // Fehlermeldung zurueckgeben und der Test darueber waere trotzdem gruen.
        var fork = new ForkAttrappe();
        var werkzeuge = new GrowTools(Werkzeugkasten.Leser(fork, null));
        await werkzeuge.GrowsAuflistenAsync(cancellationToken: CancellationToken.None);

        Assert.Contains(fork.VonWerkzeugen, a => a.Weg.StartsWith("api/grows", StringComparison.Ordinal));
    }
}
