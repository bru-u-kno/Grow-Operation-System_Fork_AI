using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Tests.Api;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (Paket C1, 18.10.2026): Kosten löschen braucht Verwaltung, der Probelauf-Abbruch ist ein Not-Aus.
/// </summary>
/// <remarks>
/// <para>Bis hierher waren die vier Kosten-Löschwege (Nachfüllung, Anschaffung, Zählerstand, Verbrauch) mit der
/// niedrigsten Schreibstufe „Dokumentieren" erreichbar — ein Schlüssel, der nur eintragen soll, konnte Rechnungen
/// löschen. Die Sicherung vorher hatten sie schon.</para>
/// <para>Der Probelauf-Abbruch stellt einen Eingriff zurück. Wie der Pumpen-Stopp darf er nie am Stundenkontingent
/// der Schaltbefehle scheitern (<see cref="KiOhneHoechstwertAttribute"/>).</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class KiPaketC1Tests : IClassFixture<KiZugriffApp>
{
    private readonly KiZugriffApp _ki;

    public KiPaketC1Tests(KiZugriffApp ki) => _ki = ki;

    public static IEnumerable<object[]> KostenLoeschwege()
        => new[] { "DeleteNachfuellung", "DeleteAnschaffung", "DeleteZaehlerstand", "DeleteVerbrauch" }.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(KostenLoeschwege))]
    public void Jeder_Kosten_Loeschweg_braucht_Verwaltung_und_sichert_vorher(string aktion)
    {
        var methode = typeof(KostenApiController).GetMethod(aktion)!;
        Assert.NotNull(methode.GetCustomAttribute<HttpDeleteAttribute>());
        Assert.Equal(KiStufe.Verwaltung, methode.GetCustomAttribute<KiStufeAttribute>()?.Stufe);
        Assert.NotNull(methode.GetCustomAttribute<KiSicherungVorherAttribute>());
    }

    [Fact]
    public void Jede_Loeschaktion_im_Kosten_Controller_ist_erfasst()
    {
        // Mengenwächter gegen die Liste oben: kommt ein fünfter Löschweg dazu, steht er hier.
        var loeschen = typeof(KostenApiController).GetMethods()
            .Where(m => m.GetCustomAttribute<HttpDeleteAttribute>() is not null)
            .Select(m => m.Name).Order().ToList();
        Assert.Equal(["DeleteAnschaffung", "DeleteArtikel", "DeleteNachfuellung", "DeleteVerbrauch", "DeleteZaehlerstand"], loeschen);
    }

    [Fact]
    public async Task Ohne_Verwaltung_loescht_ein_Schluessel_keine_Nachfuellung_mit_Verwaltung_schon()
    {
        await _ki.SchalterAsync(true);
        var oberflaeche = _ki.Oberflaeche();

        // Eine eigene Nachfüllung auf einem vorhandenen Artikel, ohne die vorige zu schließen.
        var artikel = await oberflaeche.GetFromJsonAsync<JsonElement>("/api/kosten/artikel");
        var artikelId = artikel.EnumerateArray().First().GetProperty("id").GetInt32();
        var angelegt = await oberflaeche.PostAsJsonAsync("/api/kosten/nachfuellungen",
            new { artikelId, menge = 1.0, kostenEur = 1.0, ohneGrow = true, vorherigeLeer = false, journal = false });
        Assert.Equal(HttpStatusCode.Created, angelegt.StatusCode);
        var id = (await angelegt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var (_, doku) = await _ki.SchluesselAsync("Nur Doku", "Dokumentieren");
        var abgewiesen = await _ki.App.AddonClient("172.30.33.5", doku).DeleteAsync($"/api/kosten/nachfuellungen/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, abgewiesen.StatusCode);
        Assert.Equal("ki_stufe_fehlt", (await abgewiesen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.True(await NochDaAsync(id), "Die Nachfüllung ist trotz Absage weg.");

        var (_, verwaltung) = await _ki.SchluesselAsync("Verwaltung", "Verwaltung");
        var geloescht = await _ki.App.AddonClient("172.30.33.5", verwaltung).DeleteAsync($"/api/kosten/nachfuellungen/{id}");
        Assert.Equal(HttpStatusCode.NoContent, geloescht.StatusCode);
        Assert.False(await NochDaAsync(id), "Gelöscht gemeldet, aber noch da.");
    }

    private async Task<bool> NochDaAsync(int id)
    {
        var seite = await _ki.Oberflaeche().GetFromJsonAsync<JsonElement>("/api/kosten");
        return seite.GetProperty("nachfuellungen").EnumerateArray().Any(f => f.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public void Kosten_aendern_sichert_vorher_und_der_Artikelpreis_braucht_Verwaltung()
    {
        // Prüfer 18.10.2026: ein Preis von 0 per PUT wirkt wie ein Löschen; Preis und Gebinde sind Grundlage jeder Bewertung.
        foreach (var name in new[] { "UpdateNachfuellung", "UpdateAnschaffung", "UpdateArtikel" })
        {
            Assert.NotNull(typeof(KostenApiController).GetMethod(name)!.GetCustomAttribute<KiSicherungVorherAttribute>());
        }
        Assert.Equal(KiStufe.Verwaltung, typeof(KostenApiController).GetMethod("UpdateArtikel")!.GetCustomAttribute<KiStufeAttribute>()?.Stufe);
    }

    [Fact]
    public async Task Ueber_einen_Schluessel_lehnt_Grow_OS_Zukunft_und_den_CO2_Artikel_selbst_ab()
    {
        await _ki.SchalterAsync(true);
        var oberflaeche = _ki.Oberflaeche();
        var artikelId = (await oberflaeche.GetFromJsonAsync<JsonElement>("/api/kosten/artikel")).EnumerateArray().First().GetProperty("id").GetInt32();
        var (_, doku) = await _ki.SchluesselAsync("Kosten", "Dokumentieren");
        var ui = _ki.App.AddonClient("172.30.33.5", doku);

        var zukunft = await ui.PostAsJsonAsync("/api/kosten/verbrauch", new { growId = 1, zeitpunkt = "2099-01-01T00:00:00Z", zeilen = new[] { new { artikelId, menge = 1.0 } } });
        Assert.Equal(HttpStatusCode.BadRequest, zukunft.StatusCode);
        Assert.Equal("zeitpunkt_future", (await zukunft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var kauf = await ui.PostAsJsonAsync("/api/kosten/anschaffungen", new { name = "Morgen", einzelpreisEur = 1.0, datum = "2099-01-01T00:00:00Z" });
        Assert.Equal(HttpStatusCode.BadRequest, kauf.StatusCode);

        var steuerung = _ki.App.Services.GetRequiredService<SteuerungRepository>();
        var co2 = steuerung.GetEinstellungen<Co2Einstellungen>(Co2SteuerungService.Modul) ?? new Co2Einstellungen();
        var vorher = co2.KostenArtikelId;
        co2.KostenArtikelId = artikelId;
        steuerung.SetEinstellungen(Co2SteuerungService.Modul, co2);
        try
        {
            var doppelt = await ui.PostAsJsonAsync("/api/kosten/verbrauch", new { growId = 1, zeilen = new[] { new { artikelId, menge = 0.1 } } });
            Assert.Equal(HttpStatusCode.BadRequest, doppelt.StatusCode);
            Assert.Equal("co2_bucht_selbst", (await doppelt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

            // Die Oberfläche bleibt, wie sie war: die Messseite bucht Zugaben erst nach dem Speichern der Messung.
            var vonHand = await oberflaeche.PostAsJsonAsync("/api/kosten/verbrauch", new { growId = 1, zeilen = new[] { new { artikelId, menge = 0.1 } } });
            Assert.Equal(HttpStatusCode.Created, vonHand.StatusCode);
        }
        finally
        {
            co2.KostenArtikelId = vorher;
            steuerung.SetEinstellungen(Co2SteuerungService.Modul, co2);
        }
    }

    [Fact]
    public void Der_Probelauf_Abbruch_scheitert_nie_am_Hoechstwert()
    {
        var abbrechen = typeof(ProbelaufApiController).GetMethod(nameof(ProbelaufApiController.Abbrechen))!;
        Assert.Equal(KiStufe.GeraeteSchalten, abbrechen.GetCustomAttribute<KiStufeAttribute>()?.Stufe);
        Assert.False(string.IsNullOrWhiteSpace(abbrechen.GetCustomAttribute<KiOhneHoechstwertAttribute>()?.Grund));
    }

    [Fact]
    public async Task Im_Verzeichnis_zaehlt_der_Abbruch_nicht_als_Schaltbefehl_der_Start_schon()
    {
        await _ki.SchalterAsync(true);
        var verzeichnis = await _ki.Oberflaeche().GetFromJsonAsync<JsonElement>("/api/ki-zugriff/funktionen");
        JsonElement Eintrag(string methode, string pfad) => verzeichnis.GetProperty("funktionen").EnumerateArray()
            .Single(f => f.GetProperty("methode").GetString() == methode && f.GetProperty("pfad").GetString() == pfad);

        Assert.False(Eintrag("POST", "/api/steuerung/probelauf/{id}/abbrechen").GetProperty("schaltbefehl").GetBoolean());
        Assert.True(Eintrag("POST", "/api/steuerung/probelauf").GetProperty("schaltbefehl").GetBoolean());
        Assert.Equal(["Verwaltung"], Eintrag("DELETE", "/api/kosten/verbrauch/{id}").GetProperty("stufen").EnumerateArray().Select(s => s.GetString()).ToArray());
    }
}
