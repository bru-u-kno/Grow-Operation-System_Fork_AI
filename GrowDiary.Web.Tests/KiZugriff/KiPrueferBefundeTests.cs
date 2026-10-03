using System.Net;
using System.Net.Http.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.Api;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-003): die vier Befunde des Prüfers vom 03.10.2026, je ein Fall.
/// </summary>
/// <remarks>
/// <para>1. Ein Schlüssel mit nur „Dokumentieren" setzte über das
/// Kalibrier-Ergebnis die Fördermenge einer Pumpe auf 1 ml/min — danach lief ein
/// Kalibrierlauf von 300 s durch die 10-ml-Grenze (real etwa 225 ml).</para>
/// <para>2. Ein heruntergestufter Schlüssel spielte eine ältere Sicherung zurück
/// und hatte danach wieder alle Stufen.</para>
/// <para>3. Ein Schlüssel mit „Verwaltung" lud eine Sicherung herunter und las
/// darin das HA-Token.</para>
/// <para>4. CO₂, Zuluft, Entfeuchter und Kühler speichern schaltet Automationen,
/// zählte aber nicht als Schaltbefehl — das steht in <c>KiEinstufungBTests</c>.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class KiPrueferBefundeTests : IClassFixture<KiZugriffApp>
{
    private static readonly string[] AlleStufen = ["Dokumentieren", "GrowPlanen", "GeraeteSchalten", "Verwaltung"];
    private const string Sicherungsdatei = "grow-os-backup-20260101-120000.zip";

    private readonly KiZugriffApp _ki;

    public KiPrueferBefundeTests(KiZugriffApp ki) => _ki = ki;

    private static async Task Erwarte(HttpResponseMessage antwort, HttpStatusCode status, string code)
    {
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.True(antwort.StatusCode == status && text.Contains($"\"code\":\"{code}\"", StringComparison.Ordinal),
            $"{antwort.RequestMessage?.Method} {antwort.RequestMessage?.RequestUri?.AbsolutePath}: erwartet {(int)status} {code}, kam {(int)antwort.StatusCode} — {text}");
    }

    [Fact]
    public async Task Befund1_Dokumentieren_darf_die_Foerdermenge_einer_Pumpe_nicht_setzen()
    {
        await _ki.SchalterAsync(true);
        var (_, doku) = await _ki.SchluesselAsync("Befund 1", "Dokumentieren", "GeraeteSchalten");

        var antwort = await _ki.App.AddonClient("172.30.33.40", doku)
            .PostAsJsonAsync("/api/dosing/pumps/1/calibration", new { measuredMl = 1, seconds = 60 });

        await Erwarte(antwort, HttpStatusCode.Forbidden, "ki_stufe_fehlt");
    }

    [Fact]
    public async Task Befund2_Zurueckspielen_geht_ueber_keinen_Schluessel()
    {
        await _ki.SchalterAsync(true);
        var (_, alle) = await _ki.SchluesselAsync("Befund 2", AlleStufen);

        var antwort = await _ki.App.AddonClient("172.30.33.41", alle).PostAsync($"/api/system/backup/{Sicherungsdatei}/restore", null);

        await Erwarte(antwort, HttpStatusCode.Forbidden, "ki_kein_zugriff");
    }

    [Fact]
    public async Task Befund3_Sicherung_herunterladen_geht_ueber_keinen_Schluessel()
    {
        await _ki.SchalterAsync(true);
        var (_, alle) = await _ki.SchluesselAsync("Befund 3", AlleStufen);

        var antwort = await _ki.App.AddonClient("172.30.33.42", alle).GetAsync($"/api/system/backup/{Sicherungsdatei}");

        await Erwarte(antwort, HttpStatusCode.Forbidden, "ki_kein_zugriff");
    }

    /// <summary>
    /// Auch ein MENSCH, der zurückspielt, holt keine gelöschten Schlüssel zurück —
    /// und verliert keine, die nach der Sicherung angelegt wurden.
    /// </summary>
    /// <remarks>
    /// Eigene App: das Zurückspielen tauscht die ganze Datenbank aus und darf den
    /// anderen Fällen der Klasse nichts unter den Füßen wegziehen.
    /// </remarks>
    [Fact]
    public async Task Befund2_Zurueckspielen_von_Hand_laesst_die_Schluessel_wie_sie_sind()
    {
        using var app = new IntegrationsApp
        {
            Zusatzdienste = dienste => dienste.AddSingleton<IAppNeustart>(new KeinNeustart()),
        };
        var oberflaeche = app.IngressClient();
        (await oberflaeche.PutAsJsonAsync("/api/settings/ki-zugriff", new
        {
            aktiv = true,
            rueckfrageAbStufe = "GrowPlanen",
            hoechstwerte = new { maxDosisMlJeBefehl = 10.0, maxSchaltbefehleJeStunde = 20 },
        })).EnsureSuccessStatusCode();

        var alt = await AnlegenAsync(oberflaeche, "vor der Sicherung");
        var sicherung = await (await oberflaeche.PostAsync("/api/system/backup", null)).Content.ReadFromJsonAsync<BackupManifestDto>();
        Assert.NotNull(sicherung);

        // Nach der Sicherung: der alte Schlüssel gilt als verraten und wird gelöscht,
        // ein neuer kommt dazu, der Zugriff wird enger.
        Assert.Equal(HttpStatusCode.NoContent, (await oberflaeche.DeleteAsync($"/api/settings/ki-zugriff/schluessel/{alt.Schluessel.Id}")).StatusCode);
        var neu = await AnlegenAsync(oberflaeche, "nach der Sicherung");
        (await oberflaeche.PutAsJsonAsync("/api/settings/ki-zugriff", new
        {
            aktiv = true,
            rueckfrageAbStufe = (string?)null,
            hoechstwerte = new { maxDosisMlJeBefehl = 2.5, maxSchaltbefehleJeStunde = 3 },
        })).EnsureSuccessStatusCode();

        var zurueck = await oberflaeche.PostAsync($"/api/system/backup/{sicherung!.FileName}/restore", null);
        Assert.True(zurueck.IsSuccessStatusCode, await zurueck.Content.ReadAsStringAsync());

        var mitAltem = await app.AddonClient("172.30.33.43", alt.Klartext).GetAsync("/api/ki-zugriff/ich");
        Assert.Equal(HttpStatusCode.Unauthorized, mitAltem.StatusCode);

        var mitNeuem = await app.AddonClient("172.30.33.44", neu.Klartext).GetAsync("/api/ki-zugriff/ich");
        Assert.Equal(HttpStatusCode.OK, mitNeuem.StatusCode);
        var ich = await mitNeuem.Content.ReadFromJsonAsync<KiZugriffIchDto>();
        Assert.Equal(2.5, ich!.Hoechstwerte.MaxDosisMlJeBefehl);
        Assert.Equal(3, ich.Hoechstwerte.MaxSchaltbefehleJeStunde);
        Assert.Null(ich.RueckfrageAbStufe);
    }

    private static async Task<KiSchluesselAngelegtDto> AnlegenAsync(HttpClient oberflaeche, string name)
    {
        var antwort = await oberflaeche.PostAsJsonAsync("/api/settings/ki-zugriff/schluessel", new { name, stufen = new[] { "Dokumentieren" } });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<KiSchluesselAngelegtDto>())!;
    }

    private sealed class KeinNeustart : IAppNeustart
    {
        public bool Planen(string grund) => false;
    }
}
