using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// forkai.157: Eine verteilte Anschaffung übersteht Anlegen, Speichern und
/// nochmal Speichern — über HTTP, wie das Formular sie schickt.
/// </summary>
/// <remarks>
/// <para><b>Warum auf HTTP-Ebene.</b> Drei neue Felder laufen durch
/// Model-Binding, Controller, Repository und die Seitenrechnung. Ein Feld, das
/// unterwegs verlorengeht, sieht die reine Rechnung
/// (<c>AnschaffungVerteilungTests</c>) nicht.</para>
///
/// <para><b>Warum der Rückweg zu „einmalig" dazugehört.</b> Zelt und
/// Ausmusterung gelten nur beim Verteilen. Wer zurückstellt, darf keinen
/// unsichtbaren Rest in der Datenbank behalten, der beim nächsten Umschalten
/// wieder auftaucht.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class AnschaffungVerteilenRundwegTests
{
    private readonly IntegrationsApp _app;

    public AnschaffungVerteilenRundwegTests(IntegrationsApp app) => _app = app;

    [Fact]
    public async Task VerteiltAnlegenZweimalSpeichernUndZurueckAufEinmalig()
    {
        using var client = _app.IngressClient();
        var seite = await Seite(client);
        var zelte = seite["zelte"]!.AsArray();
        Assert.True(zelte.Count >= 1, "Der Demobestand hat kein Zelt — der Rundweg prüfte das Zelt-Feld nicht.");
        var zeltId = zelte[0]!["id"]!.GetValue<int>();
        var growId = seite["durchgaenge"]!.AsArray().First()!["growId"]!.GetValue<int>();

        var rumpf = new JsonObject
        {
            ["name"] = "Rundweg LED verteilt",
            ["datum"] = "2026-01-01T12:00:00Z",
            ["stueck"] = 1,
            ["einzelpreisEur"] = 365.0,
            ["growId"] = growId,
            ["nutzungsdauerMonate"] = 36,
            ["tentId"] = zeltId,
            ["ausgemustertAm"] = "2026-12-31T12:00:00Z",
            ["journal"] = false,
        };
        var angelegt = await client.PostAsJsonAsync("/api/kosten/anschaffungen", rumpf);
        Assert.Equal(HttpStatusCode.Created, angelegt.StatusCode);
        var id = (await angelegt.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();

        VerteiltWieGesendet(await Zeile(client, id), zeltId);

        // Speichern, dann NOCHMAL speichern — der zweite Durchgang trifft den
        // Fall „schon verteilt", den das Anlegen nicht berührt.
        for (var i = 0; i < 2; i++)
        {
            var gespeichert = await client.PutAsJsonAsync($"/api/kosten/anschaffungen/{id}", rumpf);
            Assert.Equal(HttpStatusCode.OK, gespeichert.StatusCode);
            VerteiltWieGesendet(await Zeile(client, id), zeltId);
        }

        // Zurück auf einmalig: der Grow zählt wieder, Zelt und Ausmusterung sind weg.
        rumpf["nutzungsdauerMonate"] = null;
        var einmalig = await client.PutAsJsonAsync($"/api/kosten/anschaffungen/{id}", rumpf);
        Assert.Equal(HttpStatusCode.OK, einmalig.StatusCode);
        var zeile = await Zeile(client, id);
        Assert.Null(zeile["nutzungsdauerMonate"]);
        Assert.Null(zeile["tentId"]);
        Assert.Null(zeile["ausgemustertAmUtc"]);
        Assert.Null(zeile["verteilung"]);
        Assert.Equal(growId, zeile["growId"]!.GetValue<int>());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/kosten/anschaffungen/{id}")).StatusCode);
    }

    [Theory]
    [InlineData(0, null, null, "nutzungsdauer_invalid")]
    [InlineData(601, null, null, "nutzungsdauer_invalid")]
    [InlineData(12, 999_999, null, "tent_not_found")]
    [InlineData(12, null, "2025-12-31T12:00:00Z", "ausgemustert_invalid")]
    public async Task UnmoeglicheVerteilungWirdAbgelehnt(int monate, int? zelt, string? ausgemustert, string code)
    {
        using var client = _app.IngressClient();
        var antwort = await client.PostAsJsonAsync("/api/kosten/anschaffungen", new JsonObject
        {
            ["name"] = "Rundweg abgelehnt",
            ["datum"] = "2026-01-01T12:00:00Z",
            ["stueck"] = 1,
            ["einzelpreisEur"] = 10.0,
            ["nutzungsdauerMonate"] = monate,
            ["tentId"] = zelt,
            ["ausgemustertAm"] = ausgemustert,
            ["journal"] = false,
        });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Contains(code, await antwort.Content.ReadAsStringAsync());
    }

    private static void VerteiltWieGesendet(JsonObject zeile, int zeltId)
    {
        Assert.Equal(36, zeile["nutzungsdauerMonate"]!.GetValue<int>());
        Assert.Equal(zeltId, zeile["tentId"]!.GetValue<int>());
        Assert.StartsWith("2026-12-31", zeile["ausgemustertAmUtc"]!.GetValue<string>());
        Assert.Null(zeile["growId"]); // verteilt gehört sie keinem einzelnen Grow
        var verteilung = zeile["verteilung"]!.AsObject();
        Assert.StartsWith("2026-12-31", verteilung["ausgemustertTag"]!.GetValue<string>());
        // 365 € über 36 Monate (1096 Tage)
        Assert.Equal(365.0 / 1096, verteilung["eurProTag"]!.GetValue<double>(), precision: 6);
    }

    private static async Task<JsonObject> Seite(HttpClient client)
        => (await client.GetFromJsonAsync<JsonObject>("/api/kosten"))!;

    private static async Task<JsonObject> Zeile(HttpClient client, int id)
        => (await Seite(client))["anschaffungen"]!.AsArray().Single(a => a!["id"]!.GetValue<int>() == id)!.AsObject();
}
