using GrowMcp.Services;

namespace GrowMcp.Tests;

/// <summary>
/// Welche Adresse taugt für die MCP-Tür?
/// </summary>
/// <remarks>
/// <para><b>Der gemeldete Fehler.</b> Die Einrichtungsseite baute den
/// Verbindungsbefehl aus dem Namen der laufenden Anfrage. Wer Home Assistant
/// über eine eigene Domain aufruft, bekam damit
/// <c>http://smarthome.example.org:5079/mcp</c> — eine Adresse, unter der die
/// Tür nie erreichbar ist, denn Port 5079 steht absichtlich nur im Heimnetz
/// offen. Auf derselben Seite stand darunter „Erreichbar ist das nur in deinem
/// eigenen Netz."</para>
///
/// <para>Der Betreiber hat den Fehler selbst gefunden, indem er die Domain durch
/// die lokale IP ersetzte. Das soll er nicht müssen.</para>
/// </remarks>
public sealed class HeimnetzadresseTests
{
    [Theory]
    [InlineData("192.168.1.50")]
    [InlineData("192.168.178.23")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.4.9")]
    [InlineData("172.31.255.254")]
    [InlineData("169.254.10.10")]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("homeassistant")]
    [InlineData("homeassistant.local")]
    [InlineData("ha.fritz.box")]
    [InlineData("nas.lan")]
    [InlineData("server.home")]
    public void AddressesInsideTheHouseAreAccepted(string host)
    {
        Assert.True(Heimnetzadresse.IstLokal(host), $"„{host}“ zeigt ins Heimnetz, wurde aber abgelehnt.");
    }

    [Theory]
    [InlineData("smarthome.k9d.world")]          // der gemeldete Fall
    [InlineData("meinhaus.duckdns.org")]
    [InlineData("abc123.ui.nabu.casa")]          // Nabu Casa
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]                   // knapp UNTER dem privaten Bereich
    [InlineData("172.32.0.1")]                   // knapp DARÜBER
    [InlineData("192.169.1.1")]                  // sieht privat aus, ist es nicht
    [InlineData("example.com")]
    public void AddressesFromOutsideAreRefused(string host)
    {
        Assert.False(Heimnetzadresse.IstLokal(host), $"„{host}“ kommt von aussen, wurde aber als Heimnetz durchgewinkt.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingIsNotAnAddress(string? host)
    {
        Assert.False(Heimnetzadresse.IstLokal(host));
    }

    [Fact]
    public void TheCommandCarriesAddressPortAndKey()
    {
        var befehl = Einrichtungsseite.Befehl("192.168.1.50", "geheim123");

        Assert.Contains($"http://192.168.1.50:{Tueren.NetzPort}/mcp", befehl);
        Assert.Contains("Authorization: Bearer geheim123", befehl);
        Assert.StartsWith("claude mcp add --transport http grow-os-fork-ai ", befehl);
    }
}
