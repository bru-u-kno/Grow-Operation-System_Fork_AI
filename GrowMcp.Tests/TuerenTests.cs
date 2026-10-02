using System.Net;
using GrowMcp;

namespace GrowMcp.Tests;

/// <summary>
/// Wer über welchen Port was sehen darf.
/// </summary>
/// <remarks>
/// <para>Grow OS selbst hat keinen offenen Port; der MCP-Server braucht einen,
/// sonst käme kein Klient aus dem Heimnetz heran. Damit dieser Port nicht zum
/// Scheunentor wird, hängen zwei Türen an zwei Ports: die Einrichtungsseite mit
/// dem Schlüssel darauf am Ingress-Port, die Schnittstelle am Netz-Port.</para>
///
/// <para>Der Fehler, den diese Tests verhindern sollen, ist ein einziger: dass die
/// Seite mit dem Schlüssel auch über das WLAN abrufbar wird. Dann könnte sich
/// jeder im Netz den Schlüssel abholen, und das Absichern wäre eine Geste.</para>
/// </remarks>
public sealed class TuerenTests
{
    /// <summary>Der Ingress-Proxy des Supervisors — der Absender aller echten Seitenaufrufe.</summary>
    private static readonly IPAddress Proxy = IPAddress.Parse("172.30.32.2");

    [Fact]
    public void TheSetupPageIsNotReachableFromTheHomeNetwork()
    {
        // Der eine Test, um den es geht.
        var zutritt = Tueren.Pruefen(Tueren.NetzPort, "/", schluesselStimmt: true, Proxy);

        Assert.Equal(Zutritt.NichtGefunden, zutritt);
    }

    [Fact]
    public void TheInterfaceIsNotReachableThroughIngress()
    {
        // Andersherum genauso: durch den Ingress kommt man nur an die Seite. Sonst
        // haette jeder, der in Home Assistant angemeldet ist, die Werkzeuge —
        // ohne je einen Schluessel gesehen zu haben.
        var zutritt = Tueren.Pruefen(Tueren.IngressPort, "/mcp", schluesselStimmt: true, Proxy);

        Assert.Equal(Zutritt.NichtGefunden, zutritt);
    }

    [Fact]
    public void TheInterfaceNeedsTheKey()
    {
        Assert.Equal(Zutritt.SchluesselFehlt, Tueren.Pruefen(Tueren.NetzPort, "/mcp", schluesselStimmt: false, Proxy));
        Assert.Equal(Zutritt.Erlaubt, Tueren.Pruefen(Tueren.NetzPort, "/mcp", schluesselStimmt: true, Proxy));
    }

    [Fact]
    public void TheSetupPageNeedsNoKeyBecauseHomeAssistantAlreadyAsked()
    {
        var zutritt = Tueren.Pruefen(Tueren.IngressPort, "/", schluesselStimmt: false, Proxy);

        Assert.Equal(Zutritt.Erlaubt, zutritt);
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/")]
    [InlineData("/mcp/nachricht")]
    public void EveryPathUnderTheInterfaceCountsAsTheInterface(string pfad)
    {
        Assert.Equal(Zutritt.SchluesselFehlt, Tueren.Pruefen(Tueren.NetzPort, pfad, schluesselStimmt: false, Proxy));
    }

    [Fact]
    public void APathThatMerelyStartsWithThoseLettersIsNotTheInterface()
    {
        // „/mcp-einstellungen" faengt mit /mcp an, ist aber ein anderer Weg. Ohne
        // die Pruefung auf den Trenner haetten solche Pfade den Schluessel
        // verlangt — oder, schlimmer, ihn spaeter einmal umgangen.
        Assert.Equal(Zutritt.NichtGefunden, Tueren.Pruefen(Tueren.NetzPort, "/mcpx", schluesselStimmt: false, Proxy));
        Assert.Equal(Zutritt.Erlaubt, Tueren.Pruefen(Tueren.IngressPort, "/mcpx", schluesselStimmt: false, Proxy));
    }

    /// <summary>
    /// Ein anderes Add-on im internen Netz holt sich den Schlüssel nicht ab.
    /// </summary>
    /// <remarks>
    /// Befund 02.10.2026: Port 5078 lauscht auf allen Adressen. Nach draussen
    /// ist er zu, im Add-on-Netz (172.30.32.0/23) aber offen — die Seite mit
    /// dem Schlüssel antwortete dort jedem. Ein Nachbar-Add-on hatte damit den
    /// Schlüssel zur MCP-Schnittstelle, ohne dass der Betreiber je etwas
    /// freigegeben hätte.
    /// </remarks>
    [Theory]
    [InlineData("172.30.33.5")]   // ein anderes Add-on im internen Netz
    [InlineData("172.30.32.1")]   // das Gateway der Brücke, nicht der Proxy
    [InlineData("192.168.178.20")] // ein Rechner im Heimnetz
    [InlineData("fd0c:ac1e:2100::2")] // IPv6 im Add-on-Netz — vergibt Docker frei
    public void TheSetupPageAnswersOnlyTheIngressProxy(string adresse)
    {
        var absender = IPAddress.Parse(adresse);

        Assert.Equal(Zutritt.Verboten, Tueren.Pruefen(Tueren.IngressPort, "/", schluesselStimmt: false, absender));
        // Auch mit Schlüssel nicht — die Seite verlangt keinen, also darf er
        // auch nichts öffnen.
        Assert.Equal(Zutritt.Verboten, Tueren.Pruefen(Tueren.IngressPort, "/", schluesselStimmt: true, absender));
    }

    [Fact]
    public void TheSetupPageAnswersWithoutAKnownSenderNobody()
    {
        Assert.Equal(Zutritt.Verboten, Tueren.Pruefen(Tueren.IngressPort, "/", schluesselStimmt: false, absender: null));
    }

    [Theory]
    [InlineData("172.30.32.2")]
    [InlineData("::ffff:172.30.32.2")] // derselbe Proxy, wie Kestrel ihn je nach Aufbau liefert
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void TheSetupPageAnswersTheProxyAndTheOwnMachine(string adresse)
    {
        Assert.Equal(Zutritt.Erlaubt, Tueren.Pruefen(Tueren.IngressPort, "/", schluesselStimmt: false, IPAddress.Parse(adresse)));
    }

    [Theory]
    [InlineData("192.168.178.20")]
    [InlineData("172.30.33.5")]
    [InlineData("10.0.0.7")]
    public void TheInterfaceStaysOpenForEveryAddressWithTheKey(string adresse)
    {
        // Gegenprobe: die Schnittstelle darf NICHT an der Herkunft hängen —
        // Claude Code läuft auf irgendeinem Rechner im Heimnetz.
        var absender = IPAddress.Parse(adresse);

        Assert.Equal(Zutritt.Erlaubt, Tueren.Pruefen(Tueren.NetzPort, "/mcp", schluesselStimmt: true, absender));
        Assert.Equal(Zutritt.SchluesselFehlt, Tueren.Pruefen(Tueren.NetzPort, "/mcp", schluesselStimmt: false, absender));
    }
}
