using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (Paket C2, 18.10.2026): Tagesbilder auflisten und ausliefern, Kamerabild als Grow-Foto speichern.
/// </summary>
/// <remarks>
/// Eigene App mit einem nachgestellten Home Assistant, der für <c>camera.zelt</c> ein JPEG liefert und für
/// <c>camera.kaputt</c> HTML (wie ein Proxy, der eine Fehlerseite schickt). Die Tagesbilder liegen im Datenordner der
/// App, wie sie <see cref="HomeAssistantSnapshotWorker"/> ablegt.
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class KameraBilderApiTests : IClassFixture<KameraBilderApiTests.KameraApp>
{
    /// <summary>Ein JPEG-Kopf mit Füllung — <see cref="CameraFrameCache.IsLikelyImage"/> verlangt mindestens 256 Bytes.</summary>
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', .. new byte[300], 0xFF, 0xD9];

    public sealed class KameraHa : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var pfad = request.RequestUri!.AbsolutePath;
            if (pfad == "/api/camera_proxy/camera.zelt")
            {
                var bild = new ByteArrayContent(Jpeg);
                bild.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = bild });
            }
            if (pfad == "/api/camera_proxy/camera.kaputt")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Fehler</html>", Encoding.UTF8, "text/html") });
            }
            if (pfad == "/api/states") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    public sealed class KameraApp : IDisposable
    {
        public IntegrationsApp App { get; }
        public int ZeltId { get; }

        public KameraApp()
        {
            App = new IntegrationsApp
            {
                Zusatzdienste = dienste =>
                {
                    dienste.RemoveAll<HomeAssistantService>();
                    dienste.AddSingleton(sp => new HomeAssistantService(
                        new StubHttpClientFactory(new KameraHa()),
                        sp.GetRequiredService<ILogger<HomeAssistantService>>(),
                        sp.GetService<HydroSetupRepository>()));
                },
            };
            App.Services.GetRequiredService<HomeAssistantSettingsRepository>().SaveHomeAssistantSettings(new HomeAssistantSettings
            {
                BaseUrl = "http://ha.test",
                AccessToken = "nur-fuer-den-test",
                Enabled = true,
            });
            App.Services.GetRequiredService<GrowDiary.Web.Infrastructure.KiZugriff.KiHauptschalter>().Setzen(true);

            // Das Zelt des laufenden Grows 1 bekommt zwei Kameras (die zweite liefert kein Bild).
            var repo = App.Services.GetRequiredService<GrowRepository>();
            var grow = repo.GetGrow(1)!;
            ZeltId = grow.TentId!.Value;
            var zelt = repo.GetTent(ZeltId)!;
            var (ids, erste) = TentCameraList.Serialize(["camera.zelt", "camera.kaputt"]);
            zelt.CameraEntityIds = ids;
            zelt.CameraEntityId = erste;
            App.Services.GetRequiredService<TentRepository>().UpdateTent(zelt);
        }

        public string Schnappschuesse => Path.Combine(App.Services.GetRequiredService<AppPaths>().SnapshotsPath, ZeltId.ToString());

        public void Dispose() => App.Dispose();
    }

    private readonly KameraApp _k;

    public KameraBilderApiTests(KameraApp k) => _k = k;

    private HttpClient Oberflaeche => _k.App.IngressClient();

    private void BilderAblegen()
    {
        Directory.CreateDirectory(_k.Schnappschuesse);
        File.WriteAllBytes(Path.Combine(_k.Schnappschuesse, "2026-10-16.jpg"), Jpeg);
        File.WriteAllBytes(Path.Combine(_k.Schnappschuesse, "2026-10-17.jpg"), Jpeg);
        File.WriteAllText(Path.Combine(_k.Schnappschuesse, "notiz.jpg"), "kein Datum");
        File.WriteAllText(Path.Combine(_k.Schnappschuesse, "2026-1-7.jpg"), "falsche Form");
        // Eine Datei NEBEN dem Zeltordner — sie darf über die Tagesbilder nie herauskommen.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_k.Schnappschuesse)!, "geheim.jpg"), "GEHEIM");
    }

    // ------------------------------------------------------------------ Tagesbilder

    [Fact]
    public async Task Die_Liste_nennt_nur_echte_Tagesbilder_neueste_zuerst_und_die_Kameras()
    {
        BilderAblegen();
        var liste = await Oberflaeche.GetFromJsonAsync<JsonElement>($"/api/tents/{_k.ZeltId}/tagesbilder");

        var daten = liste.GetProperty("bilder").EnumerateArray().Select(b => b.GetProperty("datum").GetString()).ToArray();
        Assert.Equal(["2026-10-17", "2026-10-16"], daten);
        Assert.Equal($"/api/tents/{_k.ZeltId}/tagesbilder/2026-10-17", liste.GetProperty("bilder")[0].GetProperty("pfad").GetString());
        Assert.Equal(["camera.zelt", "camera.kaputt"], liste.GetProperty("kameras").EnumerateArray().Select(k => k.GetString()).ToArray());
    }

    [Fact]
    public async Task Ein_Tagesbild_kommt_als_JPEG()
    {
        BilderAblegen();
        var antwort = await Oberflaeche.GetAsync($"/api/tents/{_k.ZeltId}/tagesbilder/2026-10-17");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        Assert.Equal("image/jpeg", antwort.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Jpeg, await antwort.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await Oberflaeche.GetAsync($"/api/tents/{_k.ZeltId}/tagesbilder/2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Oberflaeche.GetAsync("/api/tents/9999/tagesbilder/2026-10-17")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Oberflaeche.GetAsync("/api/tents/9999/tagesbilder")).StatusCode);
    }

    /// <summary>Kein Datum außer genau yyyy-MM-dd — und nie eine Datei außerhalb des Zeltordners.</summary>
    [Theory]
    [InlineData("..%2Fgeheim")]
    [InlineData("..%5Cgeheim")]
    [InlineData("%2E%2E%2Fgeheim")]
    [InlineData("2026-10-17.jpg")]
    [InlineData("2026-1-07")]
    [InlineData("2026-13-01")]
    [InlineData("17.10.2026")]
    [InlineData("notiz")]
    public async Task Ein_Datum_in_anderer_Form_wird_abgewiesen(string datum)
    {
        BilderAblegen();
        var antwort = await Oberflaeche.GetAsync($"/api/tents/{_k.ZeltId}/tagesbilder/{datum}");

        // 400 vom Controller, nicht bloß „keine Datei": auch eine Datei, die im Zeltordner liegt (notiz.jpg), kommt nur
        // über ein echtes Datum heraus.
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.DoesNotContain("GEHEIM", text);
        Assert.DoesNotContain("kein Datum", text);
    }

    [Theory]
    [InlineData("2026-10-17", "2026-10-17")]
    [InlineData("2026-02-29", null)]
    [InlineData("2028-02-29", "2028-02-29")]
    [InlineData("2026-10-17 ", null)]
    [InlineData("../2026-10", null)]
    public void Das_Datum_wird_streng_gelesen(string text, string? erwartet)
        => Assert.Equal(erwartet, KameraBilderApiController.DatumAus(text)?.ToString("yyyy-MM-dd"));

    // ------------------------------------------------------------------ Kamerafoto

    [Fact]
    public async Task Ein_Kamerafoto_landet_mit_Motiv_und_Unterschrift_im_Grow()
    {
        var vorher = (await Oberflaeche.GetFromJsonAsync<JsonElement>("/api/grows/1/photos")).GetArrayLength();
        var antwort = await Oberflaeche.PostAsJsonAsync("/api/grows/1/kamerafoto", new { motiv = "Canopy", bildunterschrift = "Blätter oben hell" });

        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var foto = await antwort.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Canopy", foto.GetProperty("tag").GetString());
        Assert.Equal("Blätter oben hell", foto.GetProperty("caption").GetString());

        var nachher = await Oberflaeche.GetFromJsonAsync<JsonElement>("/api/grows/1/photos");
        Assert.Equal(vorher + 1, nachher.GetArrayLength());
        var datei = await Oberflaeche.GetAsync(foto.GetProperty("relativePath").GetString());
        Assert.Equal(Jpeg, await datei.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData(1, """{"kamera":"camera.nachbar"}""", HttpStatusCode.BadRequest, "camera_not_in_tent")]
    [InlineData(1, """{"kamera":"camera.kaputt"}""", HttpStatusCode.BadGateway, "ha_camera_unavailable")]
    [InlineData(1, """{"motiv":"Selfie"}""", HttpStatusCode.BadRequest, "motiv_ungueltig")]
    [InlineData(2, "{}", HttpStatusCode.BadRequest, "grow_nicht_aktiv")]
    [InlineData(999, "{}", HttpStatusCode.NotFound, "grow_not_found")]
    public async Task Ohne_passende_Kamera_oder_laufenden_Grow_wird_nichts_gespeichert(int growId, string rumpf, HttpStatusCode status, string code)
    {
        var vorher = (await Oberflaeche.GetFromJsonAsync<JsonElement>("/api/grows/1/photos")).GetArrayLength();
        var antwort = await Oberflaeche.PostAsync($"/api/grows/{growId}/kamerafoto", new StringContent(rumpf, Encoding.UTF8, "application/json"));

        Assert.Equal(status, antwort.StatusCode);
        Assert.Equal(code, (await antwort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(vorher, (await Oberflaeche.GetFromJsonAsync<JsonElement>("/api/grows/1/photos")).GetArrayLength());
    }

    [Fact]
    public async Task Ueber_einen_Schluessel_braucht_das_Kamerafoto_Dokumentieren_die_Tagesbilder_nichts()
    {
        var ui = Oberflaeche;
        (await ui.PutAsJsonAsync("/api/settings/ki-zugriff", new { aktiv = true, hoechstwerte = new { maxDosisMlJeBefehl = 10.0, maxSchaltbefehleJeStunde = 1000 } })).EnsureSuccessStatusCode();
        async Task<string> Schluessel(string stufe)
        {
            var a = await ui.PostAsJsonAsync("/api/settings/ki-zugriff/schluessel", new { name = stufe, stufen = new[] { stufe } });
            return (await a.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("klartext").GetString()!;
        }

        var planen = _k.App.AddonClient("172.30.33.5", await Schluessel("GrowPlanen"));
        var abgewiesen = await planen.PostAsJsonAsync("/api/grows/1/kamerafoto", new { });
        Assert.Equal(HttpStatusCode.Forbidden, abgewiesen.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await planen.GetAsync($"/api/tents/{_k.ZeltId}/tagesbilder")).StatusCode);

        var doku = _k.App.AddonClient("172.30.33.5", await Schluessel("Dokumentieren"));
        Assert.Equal(HttpStatusCode.Created, (await doku.PostAsJsonAsync("/api/grows/1/kamerafoto", new { })).StatusCode);
    }
}
