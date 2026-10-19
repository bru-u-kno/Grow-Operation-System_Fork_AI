using System.Text.Json.Nodes;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (forkai.253): Die Anzeige „Geräte N / M“ auf den Steuerungsseiten darf nie mehr zählen, als es gibt.
/// </summary>
/// <remarks>
/// Belegter Fehler: Die Zuluft-Seite zeigte „10 / 8“. Der Zähler kam aus <c>EntitiesFuerModul</c> und enthält
/// seit forkai.241 die mitbenutzten Raum-Rollen (Raum-Temperatur, Raum-Feuchte); der Nenner zählte nur die
/// eigenen Rollen. Geprüft wird, was die Seite über die ganze Kette liefert.
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class GeraeteZaehlerTests
{
    private readonly IntegrationsApp _app;

    public GeraeteZaehlerTests(IntegrationsApp app) => _app = app;

    private async Task<JsonObject> Lesen(string pfad)
    {
        var antwort = await _app.IngressClient().GetAsync(pfad);
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.True(antwort.IsSuccessStatusCode, $"{pfad}: {(int)antwort.StatusCode} {text}");
        return (JsonObject)JsonNode.Parse(text)!;
    }

    public static TheoryData<string, string> Seiten => new()
    {
        { "zuluft", "zuluft" },
        { "entfeuchter", "entfeuchter" },
        { "entfeuchter-zusatz", "entfeuchter-zusatz" },
        { "co2", "co2" },
        { "licht", "licht" },
        { "chiller", "chiller" },
    };

    [Theory]
    [MemberData(nameof(Seiten))]
    public async Task Zaehler_ist_nie_groesser_als_der_Nenner(string seite, string modul)
    {
        var antwort = await Lesen($"/api/steuerung/{seite}");
        var zugeordnet = antwort["geraeteZugeordnet"]!.GetValue<int>();
        var gesamt = antwort["geraeteGesamt"]!.GetValue<int>();

        Assert.True(gesamt > 0, $"{modul}: Nenner {gesamt} — die Seite kennt keine Geräte");
        Assert.True(zugeordnet <= gesamt, $"{modul}: Anzeige „Geräte {zugeordnet} / {gesamt}“ zählt mehr, als es gibt");
    }

    [Fact]
    public async Task Zuluft_zaehlt_die_mitbenutzten_Raum_Rollen_im_Nenner_mit()
    {
        var antwort = await Lesen("/api/steuerung/zuluft");

        var erwartet = SteuerungGeraeteRollen.FuerModulMitMitbenutzten("zuluft").Count;
        // Mengenwächter: die Zuluft benutzt wirklich Rollen mit — sonst prüft dieser Fall nichts.
        Assert.True(erwartet > SteuerungGeraeteRollen.FuerModul("zuluft").Count);
        Assert.Equal(erwartet, antwort["geraeteGesamt"]!.GetValue<int>());
    }
}
