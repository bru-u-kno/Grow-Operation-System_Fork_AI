using GrowMcp;

namespace GrowMcp.Tests;

/// <summary>
/// Fork AI (02.10.2026): der Grow MCP des Forks ist wirklich der des Forks.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Bis 0.1.8 stand in <c>grow-mcp/config.yaml</c> das
/// Image des Originals (<c>ghcr.io/nerdstreak/…</c>) und dessen Adresse. Wer den
/// Fork-MCP aus dem Store installierte, bekam das Original — jede Änderung am
/// Fork-MCP (etwa die Einrichtungsseite nur noch über Ingress) kam nie an. Dazu
/// belegte er denselben Port 5079 wie der Grow MCP des Originals.</para>
///
/// <para>Gelesen werden die Dateien, die Home Assistant und der Docker-Bau lesen
/// — nicht abgetippte Werte.</para>
/// </remarks>
public sealed class ForkAusstattungTests
{
    private static string Wurzel()
    {
        var ort = AppContext.BaseDirectory;
        while (ort is not null && !File.Exists(Path.Combine(ort, "repository.yaml")))
            ort = Path.GetDirectoryName(ort);
        Assert.NotNull(ort);
        return ort!;
    }

    private static string Wert(IEnumerable<string> zeilen, string schluessel)
        => zeilen.Single(z => z.StartsWith(schluessel + ":", StringComparison.Ordinal))[(schluessel.Length + 1)..].Trim().Trim('"');

    [Fact]
    public void ImageUndAdresseGehoerenZumRepositoryDesForks()
    {
        var wurzel = Wurzel();
        var repoUrl = Wert(File.ReadLines(Path.Combine(wurzel, "repository.yaml")), "url");
        var config = File.ReadAllLines(Path.Combine(wurzel, "grow-mcp", "config.yaml"));

        Assert.Equal(repoUrl, Wert(config, "url"));
        // docker-publish-mcp.yml baut ghcr.io/${{ github.repository }}-mcp,
        // die Registry schreibt den Namen klein.
        var repository = new Uri(repoUrl).AbsolutePath.Trim('/').ToLowerInvariant();
        Assert.Equal($"ghcr.io/{repository}-mcp", Wert(config, "image"));
    }

    [Fact]
    public void DerNetzPortStehtUeberallGleich()
    {
        var wurzel = Wurzel();
        var config = File.ReadAllText(Path.Combine(wurzel, "grow-mcp", "config.yaml"));
        var dockerfile = File.ReadAllText(Path.Combine(wurzel, "grow-mcp", "Dockerfile"));

        Assert.Contains($"  {Tueren.NetzPort}/tcp: {Tueren.NetzPort}", config);
        Assert.Contains($"EXPOSE {Tueren.IngressPort} {Tueren.NetzPort}", dockerfile);
        // Neben dem Grow MCP des Originals (5079) muss er starten können.
        Assert.NotEqual(5079, Tueren.NetzPort);
        Assert.DoesNotContain("5079/tcp", config);
    }
}
