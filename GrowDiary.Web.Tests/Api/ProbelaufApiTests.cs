using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (A-010, 07.10.2026): Der Probelauf an der echten App — mit einem Zelt zum Anfassen statt Home Assistant.
/// </summary>
/// <remarks>
/// Jeder Fall bekommt eine eigene App: ein gestarteter Lauf gilt erst nach dem Nachlauf als abgeschlossen
/// und würde sonst den nächsten Fall blockieren.
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class ProbelaufApiTests
{
    private const string Regelung = "automation.entfeuchter_regelung";
    private const string Port = "select.rdwc_dehumi_aktiver_modus";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Der Server schreibt Enums als Text („Knapp") — so, wie die Oberfläche sie liest.</summary>
    private static readonly JsonSerializerOptions MitTextEnums = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private sealed class Anlage : IDisposable
    {
        public FakeProbelaufHa Ha { get; } = new();
        public FakeProbelaufMessung Messung { get; } = new();
        public IntegrationsApp App { get; }

        public Anlage()
        {
            Ha.Regelungen.Add(Regelung);
            Ha.Zustaende[Regelung] = "on";
            Ha.Geraete["port_schalter"] = Port;
            Ha.Zustaende[Port] = "Auto";
            Messung.Aktuell = new ProbelaufMesswerte(DateTime.UtcNow, 55, 25, 1.2);

            App = new IntegrationsApp
            {
                Zusatzdienste = dienste =>
                {
                    dienste.RemoveAll<IProbelaufHa>();
                    dienste.AddSingleton<IProbelaufHa>(Ha);
                    dienste.RemoveAll<IProbelaufMessung>();
                    dienste.AddSingleton<IProbelaufMessung>(Messung);
                    dienste.RemoveAll<IProbelaufMeldung>();
                    dienste.AddSingleton<IProbelaufMeldung>(new FakeProbelaufMeldung());
                    dienste.RemoveAll<IChillerRegler>();
                    dienste.AddSingleton<IChillerRegler>(new FakeChillerRegler());
                },
            };
        }

        public HttpClient Client() => App.IngressClient();

        public void Dispose() => App.Dispose();
    }

    private static async Task<ProbelaufLaufDto?> LaufAsync(HttpResponseMessage antwort)
        => await antwort.Content.ReadFromJsonAsync<ProbelaufLaufDto>(Json);

    private static async Task<string?> CodeAsync(HttpResponseMessage antwort)
        => (await antwort.Content.ReadFromJsonAsync<ApiError>(Json))?.Code;

    [Fact]
    public async Task Module_NennenAlleFuenfSteuerungenMitDemZentralenTitel()
    {
        using var a = new Anlage();

        var module = await a.Client().GetFromJsonAsync<List<ProbelaufModulDto>>("/api/steuerung/probelauf/module", Json);

        Assert.Equal(5, module!.Count);
        Assert.Contains(module, m => m.Modul == "entfeuchter" && m.Titel == "Entfeuchter");
        Assert.Contains(module, m => m.Modul == "entfeuchter-zusatz" && m.Titel == "Zusatz-Entfeuchter");
        Assert.Contains(module, m => m.Modul == "chiller" && m.Titel == "Water Chiller");
        // Nichts ist eigener Text: jeder Titel kommt aus derselben Quelle wie die Steuerungsseiten.
        Assert.All(module, m => Assert.NotEqual(m.Modul, m.Titel));
    }

    [Theory]
    [InlineData("entfeuchter", 0, "probelauf_dauer_ungueltig")]
    [InlineData("entfeuchter", 61, "probelauf_dauer_ungueltig")]
    [InlineData("bluelab", 10, "probelauf_modul_unbekannt")]
    [InlineData(null, 10, "probelauf_modul_unbekannt")]
    public async Task Start_FehlerhafteAnfragenGeben400MitCode_UndNichtsWirdAngefasst(string? modul, int dauer, string code)
    {
        using var a = new Anlage();

        var antwort = await a.Client().PostAsJsonAsync("/api/steuerung/probelauf", new { modul, dauerMinuten = dauer });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(code, await CodeAsync(antwort));
        Assert.Empty(a.Ha.Protokoll);
    }

    [Fact]
    public async Task Start_OhneMessung_Gibt400()
    {
        using var a = new Anlage();
        a.Messung.Aktuell = null;

        var antwort = await a.Client().PostAsJsonAsync("/api/steuerung/probelauf", new { modul = "entfeuchter", dauerMinuten = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal("probelauf_keine_messung", await CodeAsync(antwort));
    }

    [Fact]
    public async Task Start_GrenzeSchonVerletzt_Gibt400()
    {
        using var a = new Anlage();
        a.Messung.Aktuell = new ProbelaufMesswerte(DateTime.UtcNow, 70, 25, 1.2);

        var antwort = await a.Client().PostAsJsonAsync("/api/steuerung/probelauf", new { modul = "entfeuchter", dauerMinuten = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal("probelauf_grenze_verletzt", await CodeAsync(antwort));
    }

    [Fact]
    public async Task Start_Rundweg_StartenListeEinzelnZweiterStartAbbrechen()
    {
        using var a = new Anlage();
        var client = a.Client();

        var start = await client.PostAsJsonAsync("/api/steuerung/probelauf", new { modul = "entfeuchter", dauerMinuten = 10 });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var lauf = (await LaufAsync(start))!;
        Assert.Equal("Laeuft", lauf.Status);
        Assert.Equal("Entfeuchter", lauf.ModulTitel);
        Assert.InRange(lauf.RestSekunden, 590, 600);
        Assert.Equal("off", a.Ha.Zustaende[Port]);

        // Ein zweiter Start, auch an einer anderen Steuerung, wird abgelehnt.
        var zweiter = await client.PostAsJsonAsync("/api/steuerung/probelauf", new { modul = "zuluft", dauerMinuten = 10 });
        Assert.Equal(HttpStatusCode.Conflict, zweiter.StatusCode);
        Assert.Equal("probelauf_laeuft_schon", await CodeAsync(zweiter));

        var liste = await client.GetFromJsonAsync<List<ProbelaufLaufDto>>("/api/steuerung/probelauf", Json);
        Assert.Contains(liste!, l => l.Id == lauf.Id);
        Assert.All(liste!, l => Assert.Null(l.Messreihe));

        var einzeln = await client.GetFromJsonAsync<ProbelaufLaufDto>($"/api/steuerung/probelauf/{lauf.Id}", Json);
        Assert.NotNull(einzeln!.Messreihe);

        var abbruch = await client.PostAsync($"/api/steuerung/probelauf/{lauf.Id}/abbrechen", null);
        Assert.Equal(HttpStatusCode.OK, abbruch.StatusCode);
        var danach = (await LaufAsync(abbruch))!;
        Assert.Equal("Nachlauf", danach.Status);
        Assert.Equal("Von Hand abgebrochen.", danach.AbbruchGrund);
        Assert.Equal("Auto", a.Ha.Zustaende[Port]);
        Assert.Equal("on", a.Ha.Zustaende[Regelung]);
    }

    [Fact]
    public async Task Einzeln_UnbekannteIdGibt404()
    {
        using var a = new Anlage();

        Assert.Equal(HttpStatusCode.NotFound, (await a.Client().GetAsync("/api/steuerung/probelauf/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client().PostAsync("/api/steuerung/probelauf/999/abbrechen", null)).StatusCode);
    }

    // ------------------------------------------------------------------- KI

    private static async Task KiAsync(HttpClient c, bool an)
        => (await c.PutAsJsonAsync("/api/settings/ki", new { aktiv = an })).EnsureSuccessStatusCode();

    [Fact]
    public async Task Empfehlung_WirdBeiKiAnGespeichert_BeiKiAusAberNichtGezeigt()
    {
        using var a = new Anlage();
        var client = a.Client();
        await KiAsync(client, true);
        var lauf = (await LaufAsync(await client.PostAsJsonAsync("/api/steuerung/probelauf", new { modul = "entfeuchter", dauerMinuten = 10 })))!;

        var ablegen = await client.PostAsJsonAsync($"/api/steuerung/probelauf/{lauf.Id}/empfehlung", new { text = "Nachlaufzeit auf 8 Minuten." });
        Assert.Equal(HttpStatusCode.OK, ablegen.StatusCode);
        Assert.Equal("Nachlaufzeit auf 8 Minuten.", (await LaufAsync(ablegen))!.Empfehlung);

        await KiAsync(client, false);
        var ohneKi = await client.GetFromJsonAsync<ProbelaufLaufDto>($"/api/steuerung/probelauf/{lauf.Id}", Json);
        Assert.Null(ohneKi!.Empfehlung);

        var abgelehnt = await client.PostAsJsonAsync($"/api/steuerung/probelauf/{lauf.Id}/empfehlung", new { text = "x" });
        Assert.Equal(HttpStatusCode.NotFound, abgelehnt.StatusCode);
        Assert.Equal(KiHauptschalter.FehlerCode, await CodeAsync(abgelehnt));
    }

    [Fact]
    public async Task Empfehlung_LeerGibt400()
    {
        using var a = new Anlage();
        var client = a.Client();
        await KiAsync(client, true);
        var lauf = (await LaufAsync(await client.PostAsJsonAsync("/api/steuerung/probelauf", new { modul = "entfeuchter", dauerMinuten = 10 })))!;

        var antwort = await client.PostAsJsonAsync($"/api/steuerung/probelauf/{lauf.Id}/empfehlung", new { text = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    // ------------------------------------------------------------ Kenntnisstand

    [Fact]
    public async Task Kenntnisstand_RechnetAusVerlaufUndLaeufen_OhneKi()
    {
        KenntnisstandService.Vergessen();
        using var a = new Anlage();
        var jetzt = DateTime.UtcNow;
        // Nacht: 200 Minuten, 150 davon bei Luftfeuchte ≤ 51 → 75 % → „knapp"; Tag unbekannt (nichts gemessen).
        a.Messung.Zeltverlauf = Enumerable.Range(0, 200)
            .Select(i => new ZeltMinute(jetzt.AddMinutes(-200 + i), i < 150 ? 50 : 55, 22, 1.4, false)).ToList();
        var repo = a.App.Services.GetRequiredService<GrowDiary.Web.Infrastructure.ProbelaufRepository>();
        repo.Anlegen(new ProbelaufLauf
        {
            Modul = "entfeuchter-zusatz", Status = ProbelaufStatus.Fertig, TagPhaseBeiStart = false,
            StartUtc = jetzt.AddHours(-1), EingriffEndeUtc = jetzt.AddHours(-1).AddMinutes(10),
            Auswertung = new ProbelaufAuswertung([new ProbelaufKennzahl("Feuchte", 48.5, 54, 54, 0.55, 8)], [], false, false),
        });
        var client = a.Client();
        await KiAsync(client, false); // KI aus: der Kenntnisstand gehört nicht zur KI

        var antwort = await client.GetAsync("/api/steuerung/probelauf/kenntnisstand");

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var k = (await antwort.Content.ReadFromJsonAsync<Kenntnisstand>(MitTextEnums))!;
        var feuchte = k.Zielabgleich.Single(z => z.Groesse == "Luftfeuchte");
        Assert.Equal(75, feuchte.Nacht.AnteilProzent);
        Assert.Equal(ZielUrteil.Knapp, feuchte.Nacht.Urteil);
        Assert.Equal(ZielUrteil.Unbekannt, feuchte.Tag.Urteil);
        var wirkung = Assert.Single(k.Wirkung);
        Assert.Equal("Zusatz-Entfeuchter", wirkung.Titel);
        Assert.False(wirkung.Tag);
        Assert.Equal(0.55, wirkung.Werte.Single().ProMinute);
        Assert.Equal(5, k.Abdeckung.Count);
        Assert.Contains(k.Hinweise, h => h.Contains("Zusatz-Entfeuchter") && h.Contains("75 %"));
        Assert.NotNull(k.Naechster);
    }
}
