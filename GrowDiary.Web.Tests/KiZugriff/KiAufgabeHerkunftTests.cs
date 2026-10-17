using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Tests.Api;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (17.10.2026): Eine Aufgabe, die ein KI-Schlüssel anlegt, merkt sich dessen Namen — die Aufgabenliste zeigt sie damit als „von KI".
/// </summary>
[Collection(IntegrationsSammlung.Name)]
public sealed class KiAufgabeHerkunftTests : IClassFixture<KiZugriffApp>
{
    private readonly KiZugriffApp _ki;

    public KiAufgabeHerkunftTests(KiZugriffApp ki) => _ki = ki;

    private async Task<int> ErsteGrowIdAsync()
    {
        using var grows = JsonDocument.Parse(await _ki.Oberflaeche().GetStringAsync("/api/grows"));
        return grows.RootElement.EnumerateArray().First().GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task UeberDenSchluesselAngelegt_steht_der_Schluesselname_an_der_Aufgabe_von_Hand_angelegt_nicht()
    {
        await _ki.SchalterAsync(true);
        var name = "Claude " + Guid.NewGuid().ToString("N")[..6];
        var (_, klartext) = await _ki.SchluesselAsync(name, "Dokumentieren");
        var growId = await ErsteGrowIdAsync();
        var ki = _ki.App.AddonClient("172.30.33.101", klartext);

        var perKi = await ki.PostAsJsonAsync($"/api/grows/{growId}/tasks", new { title = "Von der KI " + name });
        Assert.Equal(HttpStatusCode.Created, perKi.StatusCode);
        var perHand = await _ki.Oberflaeche().PostAsJsonAsync($"/api/grows/{growId}/tasks", new { title = "Von Hand " + name });
        Assert.Equal(HttpStatusCode.Created, perHand.StatusCode);

        using var liste = JsonDocument.Parse(await _ki.Oberflaeche().GetStringAsync($"/api/grows/{growId}/tasks"));
        var meine = liste.RootElement.EnumerateArray().Where(t => t.GetProperty("title").GetString()!.EndsWith(name)).ToList();
        Assert.Equal(name, meine.Single(t => t.GetProperty("title").GetString()!.StartsWith("Von der KI")).GetProperty("createdByAssistant").GetString());
        // Leere Felder lässt die API weg: von Hand angelegt heißt „kein Feld" oder null.
        var vonHand = meine.Single(t => t.GetProperty("title").GetString()!.StartsWith("Von Hand"));
        Assert.True(!vonHand.TryGetProperty("createdByAssistant", out var wert) || wert.ValueKind == JsonValueKind.Null);

        foreach (var t in meine)
            await _ki.Oberflaeche().DeleteAsync($"/api/tasks/{t.GetProperty("id").GetInt32()}");
    }
}
