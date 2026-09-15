using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Wer eine Messung ändert, verschiebt sie nicht in die Gegenwart.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (15.09.2026).</b> Eine Messung von 21:02 bekam eine
/// Notiz nachgetragen — per PUT, ohne <c>takenAtLocal</c> im Körper. Danach
/// stand sie auf 21:06, dem Moment des Speicherns. Ursache: der Vertrag
/// <c>MeasurementUpsertRequest</c> setzte für ein fehlendes
/// <c>TakenAtLocal</c> still <c>DateTime.Now</c> ein, und der PUT übernahm das.
/// Bei vier Minuten fällt das kaum auf; eine Stunden später korrigierte
/// Messung landet damit an der falschen Stelle im Verlauf.</para>
///
/// <para><b>Warum auf HTTP-Ebene.</b> Der Fehler entsteht im Model-Binding —
/// ein direkter Controller-Aufruf mit fertigem Request-Objekt sieht ihn nicht.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class MessungBehaeltZeitpunktBeimAendernTests
{
    private const string Ursprung = "2026-03-01T08:15";

    private readonly IntegrationsApp _app;

    public MessungBehaeltZeitpunktBeimAendernTests(IntegrationsApp app) => _app = app;

    [Fact]
    public async Task AendernOhneZeitangabeBehaeltDenZeitpunkt_AuchBeimZweitenMal()
    {
        using var client = _app.IngressClient();
        var id = await MessungAnlegen(client, Ursprung);

        // Erstes Ändern: nur die Notiz, kein takenAtLocal.
        await Aendern(client, id, new JsonObject { ["stage"] = await Phase(client, id), ["notes"] = "erste Änderung" });
        Assert.Equal(Ursprung, await Zeitpunkt(client, id));

        // Zweites Ändern, wieder ohne Zeit — der Fall „schon einmal gespeichert".
        await Aendern(client, id, new JsonObject { ["stage"] = await Phase(client, id), ["notes"] = "zweite Änderung" });
        Assert.Equal(Ursprung, await Zeitpunkt(client, id));
        Assert.Equal("zweite Änderung", (await Lesen(client, id))["notes"]!.GetValue<string>());
    }

    [Fact]
    public async Task AendernMitLeeremZeitfeldBehaeltDenZeitpunkt()
    {
        using var client = _app.IngressClient();
        var id = await MessungAnlegen(client, Ursprung);

        await Aendern(client, id, new JsonObject { ["stage"] = await Phase(client, id), ["takenAtLocal"] = "  " });

        Assert.Equal(Ursprung, await Zeitpunkt(client, id));
    }

    [Fact]
    public async Task AendernMitZeitangabeSetztDenNeuenZeitpunkt()
    {
        using var client = _app.IngressClient();
        var id = await MessungAnlegen(client, Ursprung);

        await Aendern(client, id, new JsonObject { ["stage"] = await Phase(client, id), ["takenAtLocal"] = "2026-03-02T09:30" });

        Assert.Equal("2026-03-02T09:30", await Zeitpunkt(client, id));
    }

    [Fact]
    public async Task AnlegenOhneZeitangabeNimmtWeiterDieGegenwart()
    {
        using var client = _app.IngressClient();
        var vorher = DateTime.Now.AddMinutes(-1);

        var antwort = await client.PostAsJsonAsync("/api/grows/1/measurements",
            new JsonObject { ["stage"] = await PhaseDesGrows(client), ["notes"] = "ohne Zeit" });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var angelegt = (await antwort.Content.ReadFromJsonAsync<JsonObject>())!;

        var zeitpunkt = DateTime.Parse(angelegt["takenAt"]!.GetValue<string>()[..16]);
        Assert.InRange(zeitpunkt, vorher, DateTime.Now.AddMinutes(1));
    }

    private static async Task<int> MessungAnlegen(HttpClient client, string zeitpunkt)
    {
        var antwort = await client.PostAsJsonAsync("/api/grows/1/measurements", new JsonObject
        {
            ["takenAtLocal"] = zeitpunkt,
            ["stage"] = await PhaseDesGrows(client),
            ["notes"] = "Ausgangsstand",
        });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var angelegt = (await antwort.Content.ReadFromJsonAsync<JsonObject>())!;
        return angelegt["id"]!.GetValue<int>();
    }

    private static async Task Aendern(HttpClient client, int id, JsonObject koerper)
    {
        var antwort = await client.PutAsJsonAsync($"/api/measurements/{id}", koerper);
        Assert.True(antwort.IsSuccessStatusCode, $"PUT antwortete {(int)antwort.StatusCode}: {await antwort.Content.ReadAsStringAsync()}");
    }

    private static async Task<JsonObject> Lesen(HttpClient client, int id)
        => (await client.GetFromJsonAsync<JsonObject>($"/api/measurements/{id}"))!;

    /// <summary>Zeitpunkt auf die Minute, ohne Zeitzonen-Anhang.</summary>
    private static async Task<string> Zeitpunkt(HttpClient client, int id)
        => (await Lesen(client, id))["takenAt"]!.GetValue<string>()[..16];

    private static async Task<string> Phase(HttpClient client, int id)
        => (await Lesen(client, id))["stage"]!.GetValue<string>();

    /// <summary>Die Phase der vorhandenen Messungen — damit die Plausibilitätsprüfung nicht anschlägt.</summary>
    private static async Task<string> PhaseDesGrows(HttpClient client)
    {
        var liste = (await client.GetFromJsonAsync<JsonArray>("/api/grows/1/measurements"))!;
        Assert.True(liste.Count >= 1, "Der Demobestand hat keine Messung für Grow 1.");
        return liste[0]!["stage"]!.GetValue<string>();
    }
}
