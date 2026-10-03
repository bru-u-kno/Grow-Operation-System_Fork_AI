using System.Net;
using System.Net.Http.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Tests.Api;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): „Was die KI zuletzt getan hat" —
/// <c>GET /api/settings/ki-zugriff/protokoll</c> an der echten App.
/// </summary>
/// <remarks>
/// <para>Geprüft wird die ganze Kette: eine Anfrage über einen Schlüssel geht
/// durch die Sperre, Schritt 3 schreibt ins Prüfprotokoll, die Oberfläche liest
/// es zurück. Ein Eintrag, den nur das Repository kennt, belegt nichts.</para>
/// <para>Jeder Fall nimmt einen eigenen Schlüssel und filtert nach ihm, wo er
/// zählt — die App ist allen Fällen dieser Klasse gemeinsam.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class KiProtokollTests : IClassFixture<KiZugriffApp>
{
    private const string Weg = "/api/settings/ki-zugriff/protokoll";

    private readonly KiZugriffApp _ki;

    public KiProtokollTests(KiZugriffApp ki) => _ki = ki;

    private IntegrationsApp App => _ki.App;

    private async Task<List<KiProtokollEintragDto>> LesenAsync(string abfrage = "")
    {
        var antwort = await _ki.Oberflaeche().GetAsync(Weg + abfrage);
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.True(antwort.StatusCode == HttpStatusCode.OK, $"GET {Weg}{abfrage}: {(int)antwort.StatusCode} — {text}");
        return (await antwort.Content.ReadFromJsonAsync<List<KiProtokollEintragDto>>())!;
    }

    private static async Task Erwarte(HttpResponseMessage antwort, HttpStatusCode status, string? code = null)
    {
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.True(antwort.StatusCode == status,
            $"{antwort.RequestMessage?.Method} {antwort.RequestMessage?.RequestUri?.PathAndQuery}: erwartet {(int)status}, kam {(int)antwort.StatusCode} — {text}");
        if (code is not null)
        {
            Assert.True(text.Contains($"\"code\":\"{code}\"", StringComparison.Ordinal), $"Erwartet Fehlercode {code}, kam: {text}");
        }
    }

    // ------------------------------------------------------ Eintrag

    [Fact]
    public async Task EineAnfrageUeberDenSchluesselErscheint_NeuesteZuerst_MitAllemWasDieListeBraucht()
    {
        await _ki.SchalterAsync(true);
        var (id, klartext) = await _ki.SchluesselAsync("Claude am Telefon", "Dokumentieren");
        var client = App.AddonClient("172.30.33.101", klartext);

        await Erwarte(await client.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);
        await Erwarte(await client.PostAsync("/api/ki-test/schalten", null), HttpStatusCode.Forbidden, "ki_stufe_fehlt");
        // Lesen ausserhalb der Verwaltung protokolliert die Sperre nicht — also steht es auch hier nicht.
        await Erwarte(await client.GetAsync("/api/ki-test/lesen"), HttpStatusCode.OK);

        var eintraege = await LesenAsync($"?schluesselId={id}");

        Assert.Equal(2, eintraege.Count);
        var (abgewiesen, erledigt) = (eintraege[0], eintraege[1]);

        Assert.Equal("/api/ki-test/schalten", abgewiesen.Pfad);
        Assert.Equal("POST", abgewiesen.Methode);
        Assert.Equal(403, abgewiesen.Status);
        Assert.Equal("ki_stufe_fehlt", abgewiesen.Fehlercode);
        Assert.False(abgewiesen.Erfolg);
        Assert.Equal(id, abgewiesen.SchluesselId);
        Assert.Equal("Claude am Telefon", abgewiesen.SchluesselName);
        Assert.Equal(KiProtokollArt.Schreibend, abgewiesen.Art);

        Assert.Equal("/api/ki-test/dokumentieren", erledigt.Pfad);
        Assert.Equal(200, erledigt.Status);
        Assert.Null(erledigt.Fehlercode);
        Assert.True(erledigt.Erfolg);
        Assert.Contains("über KI-Assistent ‚Claude am Telefon‘: POST /api/ki-test/dokumentieren → 200", erledigt.Beschreibung);

        Assert.True(abgewiesen.ZeitpunktUtc >= erledigt.ZeitpunktUtc, "Neueste zuerst.");
        Assert.Equal(DateTimeKind.Utc, erledigt.ZeitpunktUtc.Kind);
        Assert.DoesNotContain(eintraege, e => e.Pfad == "/api/ki-test/lesen");
    }

    [Fact]
    public async Task DieHandgriffeDesBetreibersStehenNichtDarin()
    {
        await _ki.SchalterAsync(true);
        var name = "Betreiber " + Guid.NewGuid().ToString("N")[..6];
        var (id, _) = await _ki.SchluesselAsync(name, "Dokumentieren");
        var gesperrt = await _ki.Oberflaeche().PostAsync($"/api/settings/ki-zugriff/schluessel/{id}/sperren", null);
        await Erwarte(gesperrt, HttpStatusCode.OK);

        // Mengenwächter: die Handgriffe stehen im Prüfprotokoll — sonst prüfte der Fall nichts.
        var roh = App.Services.GetRequiredService<SystemAuditRepository>().GetRecent(500, "security")
            .Where(e => e.Source == KiProtokollArt.Quelle && e.Summary.Contains(name, StringComparison.Ordinal))
            .ToList();
        Assert.Contains(roh, e => e.Action == "ki-schluessel-angelegt");
        Assert.Contains(roh, e => e.Action == "ki-schluessel-gesperrt");

        var eintraege = await LesenAsync($"?anzahl={KiZugriffApiController.ProtokollHoechstens}");
        Assert.DoesNotContain(eintraege, e => e.Beschreibung.Contains(name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EinGesperrterSchluesselStehtBeiSich_EinUngueltigerNurOhneFilter()
    {
        await _ki.SchalterAsync(true);
        var (id, klartext) = await _ki.SchluesselAsync("Später gesperrt", "Dokumentieren");
        await Erwarte(await _ki.Oberflaeche().PostAsync($"/api/settings/ki-zugriff/schluessel/{id}/sperren", null), HttpStatusCode.OK);

        await Erwarte(await App.AddonClient("172.30.33.102", klartext).PostAsync("/api/ki-test/dokumentieren", null),
            HttpStatusCode.Unauthorized, "ki_schluessel_ungueltig");
        var falsch = "gok_" + new string('A', KiZugriffDienst.KlartextLaengeOhneVorsilbe);
        await Erwarte(await App.AddonClient("172.30.33.103", falsch).PostAsync("/api/ki-test/ungueltig-gesucht", null),
            HttpStatusCode.Unauthorized, "ki_schluessel_ungueltig");

        var beimSchluessel = Assert.Single(await LesenAsync($"?schluesselId={id}"));
        Assert.Equal(KiProtokollArt.SchluesselAbgewiesen, beimSchluessel.Art);
        Assert.Equal(401, beimSchluessel.Status);
        Assert.Equal("ki_schluessel_ungueltig", beimSchluessel.Fehlercode);
        Assert.Equal("Später gesperrt", beimSchluessel.SchluesselName);

        var alle = await LesenAsync();
        var ungueltig = Assert.Single(alle, e => e.Pfad == "/api/ki-test/ungueltig-gesucht");
        Assert.Null(ungueltig.SchluesselId);
        Assert.Equal(401, ungueltig.Status);
    }

    // ------------------------------------------------------ Filter

    [Fact]
    public async Task DerFilterNachSchluesselGreift()
    {
        await _ki.SchalterAsync(true);
        var (a, klartextA) = await _ki.SchluesselAsync("Filter A", "Dokumentieren");
        var (b, klartextB) = await _ki.SchluesselAsync("Filter B", "Dokumentieren");

        await Erwarte(await App.AddonClient("172.30.33.104", klartextA).PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);
        await Erwarte(await App.AddonClient("172.30.33.104", klartextB).PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);
        await Erwarte(await App.AddonClient("172.30.33.104", klartextB).PostAsync("/api/ki-test/schalten", null), HttpStatusCode.Forbidden);

        var nurA = await LesenAsync($"?schluesselId={a}");
        var nurB = await LesenAsync($"?schluesselId={b}");
        var alle = await LesenAsync($"?anzahl={KiZugriffApiController.ProtokollHoechstens}");

        Assert.Single(nurA);
        Assert.All(nurA, e => Assert.Equal(a, e.SchluesselId));
        Assert.Equal(2, nurB.Count);
        Assert.All(nurB, e => Assert.Equal(b, e.SchluesselId));
        // Ohne Filter beide — sonst belegte „nur A" nichts.
        Assert.Contains(alle, e => e.SchluesselId == a);
        Assert.Contains(alle, e => e.SchluesselId == b);

        // Ein Schlüssel, den es nicht gibt: leer, kein Fehler.
        Assert.Empty(await LesenAsync("?schluesselId=987654"));
    }

    [Fact]
    public async Task EinAlterEintragOhneSchluesselId_ErscheintNurOhneFilter_UndWirdAusDemSatzGelesen()
    {
        // So schrieb forkai.163: Methode, Pfad und Status nur im Satz.
        var protokoll = App.Services.GetRequiredService<SystemAuditRepository>();
        protokoll.Add(new SystemAuditEvent
        {
            EventType = "security",
            Action = KiProtokollArt.Schreibend,
            Summary = "über KI-Assistent ‚Aus forkai.163‘: POST /api/grows/1/measurements → 201",
            Severity = "info",
            Source = KiProtokollArt.Quelle,
            Success = true,
        });

        var alt = Assert.Single(await LesenAsync("?anzahl=5"), e => e.Beschreibung.Contains("Aus forkai.163", StringComparison.Ordinal));
        Assert.Null(alt.SchluesselId);
        Assert.Equal("Aus forkai.163", alt.SchluesselName);
        Assert.Equal("POST", alt.Methode);
        Assert.Equal("/api/grows/1/measurements", alt.Pfad);
        Assert.Equal(201, alt.Status);
        Assert.True(alt.Erfolg);

        var (id, _) = await _ki.SchluesselAsync("Neu", "Dokumentieren");
        Assert.DoesNotContain(await LesenAsync($"?schluesselId={id}"), e => e.Id == alt.Id);
    }

    // ------------------------------------------------------ Anzahl

    [Fact]
    public async Task DieAnzahlGrenzeGreift()
    {
        // Mehr als die Obergrenze herstellen — sonst bestünde der Fall auch ohne sie.
        var protokoll = App.Services.GetRequiredService<SystemAuditRepository>();
        var schluesselId = 4242;
        for (var i = 0; i < KiZugriffApiController.ProtokollHoechstens + 5; i++)
        {
            protokoll.Add(new SystemAuditEvent
            {
                EventType = "security",
                Action = KiProtokollArt.Schreibend,
                Summary = $"Menge {i}",
                Source = KiProtokollArt.Quelle,
                KiSchluesselId = schluesselId,
                Methode = "POST",
                Pfad = "/api/ki-test/dokumentieren",
                HttpStatus = 200,
            });
        }

        Assert.Equal(KiZugriffApiController.ProtokollVorgabe, (await LesenAsync($"?schluesselId={schluesselId}")).Count);
        Assert.Equal(3, (await LesenAsync($"?schluesselId={schluesselId}&anzahl=3")).Count);
        Assert.Equal(KiZugriffApiController.ProtokollHoechstens, (await LesenAsync($"?schluesselId={schluesselId}&anzahl=5000")).Count);
        Assert.Equal(KiZugriffApiController.ProtokollHoechstens, (await LesenAsync("?anzahl=5000")).Count);

        // Neueste zuerst: der zuletzt geschriebene steht oben.
        Assert.Equal($"Menge {KiZugriffApiController.ProtokollHoechstens + 4}", (await LesenAsync($"?schluesselId={schluesselId}&anzahl=1"))[0].Beschreibung);

        await Erwarte(await _ki.Oberflaeche().GetAsync(Weg + "?anzahl=0"), HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------ nur Oberfläche

    [Theory]
    [InlineData("Dokumentieren")]
    [InlineData("Verwaltung")]
    [InlineData("Dokumentieren", "GrowPlanen", "GeraeteSchalten", "Verwaltung")]
    public async Task MitSchluesselGibtEs403_AuchMitAllenStufen(params string[] stufen)
    {
        await _ki.SchalterAsync(true);
        var (id, klartext) = await _ki.SchluesselAsync("Neugierig", stufen);
        var ki = App.AddonClient("172.30.33.105", klartext);

        await Erwarte(await ki.GetAsync(Weg), HttpStatusCode.Forbidden, "ki_kein_zugriff");
        await Erwarte(await ki.GetAsync($"{Weg}?schluesselId={id}"), HttpStatusCode.Forbidden, "ki_kein_zugriff");
    }
}
