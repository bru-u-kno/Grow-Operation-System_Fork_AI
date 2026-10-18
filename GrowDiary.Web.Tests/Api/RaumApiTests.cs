using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (forkai.241): Der Raum durch die ganze Kette — Name setzen, leeren, zu lange Namen, und das Zelt, das darin steht.
/// Die App wird von allen Fällen der Sammlung geteilt; jeder Fall stellt den Namen wieder auf die Vorgabe zurück.
/// </summary>
[Collection(IntegrationsSammlung.Name)]
public sealed class RaumApiTests
{
    private readonly IntegrationsApp _app;

    public RaumApiTests(IntegrationsApp app) => _app = app;

    private const string Pfad = "/api/steuerung/raum";

    private static StringContent Roh(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<JsonObject> Lesen(string pfad)
    {
        var antwort = await _app.IngressClient().GetAsync(pfad);
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.True(antwort.IsSuccessStatusCode, $"{pfad}: {(int)antwort.StatusCode} {text}");
        return (JsonObject)JsonNode.Parse(text)!;
    }

    private async Task<(HttpStatusCode Status, JsonObject Antwort)> Senden(HttpMethod methode, string pfad, string json)
    {
        var antwort = await _app.IngressClient().SendAsync(new HttpRequestMessage(methode, pfad) { Content = Roh(json) });
        return (antwort.StatusCode, (JsonObject)JsonNode.Parse(await antwort.Content.ReadAsStringAsync())!);
    }

    [Fact]
    public async Task Name_GesetztGeleertUndZuLang()
    {
        try
        {
            var vorher = await Lesen(Pfad);
            Assert.Equal("Aufstellraum", vorher["vorgabe"]!.GetValue<string>());

            var (status, nach1) = await Senden(HttpMethod.Put, Pfad, """{"name":"Keller"}""");
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("Keller", nach1["name"]!.GetValue<string>());
            Assert.Equal("Keller", (await Lesen(Pfad))["name"]!.GetValue<string>());

            var (_, nach2) = await Senden(HttpMethod.Put, Pfad, """{"name":"  "}""");
            Assert.Equal("Aufstellraum", nach2["name"]!.GetValue<string>());

            var (zuLang, _) = await Senden(HttpMethod.Put, Pfad, $$"""{"name":"{{new string('x', 61)}}"}""");
            Assert.Equal(HttpStatusCode.BadRequest, zuLang);
            var (steuer, _) = await Senden(HttpMethod.Put, Pfad, """{"name":"a\nb"}""");
            Assert.Equal(HttpStatusCode.BadRequest, steuer);
            Assert.Equal("Aufstellraum", (await Lesen(Pfad))["name"]!.GetValue<string>());
        }
        finally
        {
            await Senden(HttpMethod.Put, Pfad, """{"name":null}""");
        }
    }

    [Fact]
    public async Task Zelt_StehtImRaumOderInKeinem_UndEinUnbekannterRaumGibt400()
    {
        var (status, angelegt) = await Senden(HttpMethod.Post, "/api/settings/tents", """{"name":"Raumtest","raumId":"haupt"}""");
        Assert.Equal(HttpStatusCode.Created, status);
        var id = angelegt["id"]!.GetValue<int>();
        try
        {
            Assert.Equal("haupt", angelegt["raumId"]!.GetValue<string>());
            Assert.Contains((await Lesen(Pfad))["zelte"]!.AsArray(), z => z!["id"]!.GetValue<int>() == id);

            var (fremd, antwort) = await Senden(HttpMethod.Put, $"/api/settings/tents/{id}", """{"name":"Raumtest","raumId":"nebenan"}""");
            Assert.Equal(HttpStatusCode.BadRequest, fremd);
            Assert.Contains(antwort["fieldErrors"]!.AsObject().Select(p => p.Key), k => k.Equals("raumId", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("haupt", (await Lesen($"/api/settings/tents/{id}"))["raumId"]!.GetValue<string>());

            var (ok, nach) = await Senden(HttpMethod.Put, $"/api/settings/tents/{id}", """{"name":"Raumtest","raumId":null}""");
            Assert.Equal(HttpStatusCode.OK, ok);
            Assert.Null(nach["raumId"]);
            Assert.DoesNotContain((await Lesen(Pfad))["zelte"]!.AsArray(), z => z!["id"]!.GetValue<int>() == id);
        }
        finally
        {
            await _app.IngressClient().DeleteAsync($"/api/settings/tents/{id}");
        }
    }
}
