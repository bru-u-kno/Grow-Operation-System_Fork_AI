using GrowOsAccess;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Grow OS im internen Add-on-Netz finden.
/// </summary>
/// <remarks>
/// <para>Der Hostname eines Add-ons setzt sich aus Repository und Slug zusammen —
/// <c>local_grow_os</c> bei einer lokalen Installation, sonst mit dem Hash des
/// Repositories davor. Als DNS-Name werden die Unterstriche zu Bindestrichen.</para>
///
/// <para>Der Hash ist von aussen nicht vorhersagbar, aber er ist ableitbar: alle
/// Add-ons aus diesem Repository tragen denselben Vorsatz. Ein Add-on fragt den
/// Supervisor nach seinem eigenen Namen und tauscht den hinteren Teil aus. Genau
/// das erspart die Manager-Rolle.</para>
///
/// <para>Die Tests fahren jedes Add-on durch, nicht nur das erste. Als der eigene
/// Slug hier noch fest verdrahtet war und auf ein anderes Add-on zeigte, sah
/// alles gruen aus — und Grow OS aus dem Store wurde trotzdem nicht gefunden.</para>
/// </remarks>
public sealed class GrowOsLocatorTests
{
    /// <summary>
    /// Die Slugs, unter denen Add-ons dieses Repositories laufen.
    /// </summary>
    /// <remarks>
    /// Kommt ein Add-on dazu, gehört sein Slug hier hinein — sonst wiederholt
    /// sich genau der Fehler, der diese Liste ueberhaupt erst hervorgebracht hat.
    /// </remarks>
    public static TheoryData<string> Addons => ["grow_mcp"];

    [Theory]
    [MemberData(nameof(Addons))]
    public void EveryAddonDerivesGrowOsFromItsOwnName(string eigener)
    {
        var namen = GrowOsLocator.Kandidaten($"a1b2c3d4_{eigener}", eigener);

        // Erst der abgeleitete Name — dasselbe Repository, derselbe Vorsatz.
        Assert.Equal("a1b2c3d4_grow_os_fork_ai", namen[0]);
        Assert.Equal("a1b2c3d4-grow-os-fork-ai", GrowOsLocator.Hostname(namen[0]));
        Assert.Equal("http://a1b2c3d4-grow-os-fork-ai:5076", GrowOsLocator.BaseUrl(GrowOsLocator.Hostname(namen[0])));
    }

    [Fact]
    public void DerSlugIstDerAusDerConfigYamlVonGrowOs()
    {
        // Fork AI (02.10.2026): hier stand der Slug des Originals („grow_os"), der
        // Fork heißt „grow_os_fork_ai". Der Fork-MCP hätte im eigenen Store ein
        // Grow OS gesucht, das es dort nicht gibt. Gelesen wird die Datei, die
        // Home Assistant liest — nicht abgetippt.
        var wurzel = AppContext.BaseDirectory;
        while (wurzel is not null && !File.Exists(Path.Combine(wurzel, "grow-os", "config.yaml")))
            wurzel = Path.GetDirectoryName(wurzel);
        Assert.NotNull(wurzel);
        var zeile = File.ReadLines(Path.Combine(wurzel!, "grow-os", "config.yaml"))
            .Single(z => z.StartsWith("slug:", StringComparison.Ordinal));
        Assert.Equal(zeile["slug:".Length..].Trim().Trim('"'), GrowOsLocator.Slug);
    }

    [Fact]
    public void TheNameOfAnotherAddonDoesNotDeriveAnything()
    {
        // Der Fehler, der in freier Wildbahn zugeschlagen hat: das Add-on heisst
        // „…_grow_mcp", gesucht wurde aber nach der Endung eines anderen. Ohne
        // Treffer bleiben nur die Namen ohne Hash — und die gibt es bei einer
        // Installation aus dem Store nicht.
        var namen = GrowOsLocator.Kandidaten("a1b2c3d4_grow_mcp", eigenerBasisSlug: "grow_anderes");

        Assert.DoesNotContain("a1b2c3d4_grow_os_fork_ai", namen);
        Assert.Equal(["local_grow_os_fork_ai", "grow_os_fork_ai"], namen);
    }

    [Fact]
    public void ALocalInstallIsTriedEvenWithoutAnyDerivation()
    {
        // Wer Grow OS aus dem Ordner heraus installiert hat, findet es hier —
        // ohne dass der Supervisor etwas herausrücken muss.
        var namen = GrowOsLocator.Kandidaten(vollerSlug: null, eigenerBasisSlug: "grow_mcp");

        Assert.Contains("local_grow_os_fork_ai", namen);
        Assert.Equal("local-grow-os-fork-ai", GrowOsLocator.Hostname("local_grow_os_fork_ai"));
    }

    [Theory]
    [MemberData(nameof(Addons))]
    public void TheDerivedNameComesBeforeTheFixedGuesses(string eigener)
    {
        var namen = GrowOsLocator.Kandidaten($"a1b2c3d4_{eigener}", eigener);

        // Sonst antwortete bei zwei Installationen die falsche zuerst.
        Assert.Equal(["a1b2c3d4_grow_os_fork_ai", "local_grow_os_fork_ai", "grow_os_fork_ai"], namen);
    }

    [Theory]
    [MemberData(nameof(Addons))]
    public void ALocallyInstalledAddonDoesNotProduceTheSameNameTwice(string eigener)
    {
        // „local_grow_mcp" leitet auf „local_grow_os" ab — das steht ohnehin
        // schon auf der Liste. Doppelt anklopfen wäre nur Wartezeit.
        var namen = GrowOsLocator.Kandidaten($"local_{eigener}", eigener);

        Assert.Equal(["local_grow_os_fork_ai", "grow_os_fork_ai"], namen);
    }

    [Fact]
    public void AnUnexpectedOwnNameFallsBackInsteadOfInventingAHost()
    {
        var namen = GrowOsLocator.Kandidaten("irgendwas_anderes", "grow_mcp");

        Assert.Equal(["local_grow_os_fork_ai", "grow_os_fork_ai"], namen);
    }

    [Fact]
    public void TheFailureMessageSaysWhatToDoNextWithoutNamingTheWrongAddon()
    {
        // Diese Meldung erscheint in JEDEM Add-on, das die Bibliothek nutzt. Sie
        // stand auf der MCP-Seite und schickte den Nutzer in die Einstellungen
        // des Beraters — dort gibt es das Feld zwar auch, es half nur nichts.
        var meldung = GrowOsLocator.NichtGefunden.Meldung;

        Assert.False(GrowOsLocator.NichtGefunden.Gefunden);
        Assert.Contains("dieses Add-ons", meldung);
        Assert.DoesNotContain("Berater", meldung);
        // Ohne beta.24 antwortet Grow OS aus dem internen Netz mit 403, und das
        // sieht von hier aus genauso aus wie „nicht da".
        Assert.Contains("beta.24", meldung);
    }
}
