using System.Net;
using System.Net.Sockets;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (02.10.2026): Der WebSocket zu Home Assistant hängt nicht für immer.
/// </summary>
/// <remarks>
/// Vorher hatte er keine eigene Frist. Antwortete Home Assistant nicht, hingen
/// das Anlegen der Helfer, das Setzen des Mittelungsfensters und die
/// CO₂-Absicherung — Aufrufer ohne Abbruchsignal für immer.
/// </remarks>
public sealed class HomeAssistantSocketFristTests
{
    private static readonly TimeSpan Geduld = TimeSpan.FromSeconds(10);

    [Fact]
    public void DieStandardfristIstEndlich()
        => Assert.InRange(HomeAssistantSocket.Standardfrist, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

    [Fact]
    public async Task EinBefehlOhneAntwort_EndetNachDerFrist()
    {
        await using var server = await NachgebauterSocket.StartenAsync("automation.x", [], schweigtAufRegistry: true);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = server.Adresse, AccessToken = "test" };
        await using var socket = await HomeAssistantSocket.OeffnenAsync(einstellungen, CancellationToken.None);
        Assert.NotNull(socket);

        var befehl = socket!.BefehlAsync("config/entity_registry/get",
            new Dictionary<string, object?> { ["entity_id"] = "sensor.x" }, CancellationToken.None,
            frist: TimeSpan.FromMilliseconds(500));
        var fertig = await Task.WhenAny(befehl, Task.Delay(Geduld));

        Assert.Same(befehl, fertig);
        var antwort = await befehl;
        Assert.False(antwort.Erfolg);
        Assert.Contains("nicht rechtzeitig", antwort.Fehler);
    }

    [Fact]
    public async Task Selbsttest_EinBeantworteterBefehlKommtDurch()
    {
        await using var server = await NachgebauterSocket.StartenAsync("automation.x", ["automation.y"]);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = server.Adresse, AccessToken = "test" };
        await using var socket = await HomeAssistantSocket.OeffnenAsync(einstellungen, CancellationToken.None);

        var antwort = await socket!.BefehlAsync("search/related",
            new Dictionary<string, object?> { ["item_type"] = "entity", ["item_id"] = "automation.x" }, CancellationToken.None,
            frist: TimeSpan.FromSeconds(5));

        Assert.True(antwort.Erfolg);
    }

    /// <summary>Ein Gegenüber, das die Verbindung annimmt und dann schweigt — kein Handschlag.</summary>
    [Fact]
    public async Task EinGegenueberOhneHandschlag_EndetNachDerFrist()
    {
        using var lauscher = new TcpListener(IPAddress.Loopback, 0);
        lauscher.Start();
        var port = ((IPEndPoint)lauscher.LocalEndpoint).Port;
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = $"http://127.0.0.1:{port}", AccessToken = "test" };

        var oeffnen = HomeAssistantSocket.OeffnenAsync(einstellungen, CancellationToken.None, frist: TimeSpan.FromMilliseconds(500));
        var fertig = await Task.WhenAny(oeffnen, Task.Delay(Geduld));

        Assert.Same(oeffnen, fertig);
        Assert.Null(await oeffnen);
    }
}
