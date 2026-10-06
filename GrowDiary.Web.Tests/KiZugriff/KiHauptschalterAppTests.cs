using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Tests.Api;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-011, 06.10.2026): Der Hauptschalter „KI-Funktionen" an der echten App.
/// </summary>
/// <remarks>
/// Bei „aus" ist nichts von der KI erreichbar — weder die KI-Controller in der
/// Oberfläche noch ein Schlüssel von aussen, egal welche Stufen er hat. Beim
/// Einschalten ist alles wieder da, mit den früheren Einstellungen.
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class KiHauptschalterAppTests : IClassFixture<KiZugriffApp>
{
    private static readonly string[] AlleStufen = ["Dokumentieren", "GrowPlanen", "GeraeteSchalten", "Verwaltung"];

    private readonly KiZugriffApp _ki;

    public KiHauptschalterAppTests(KiZugriffApp ki) => _ki = ki;

    private IntegrationsApp App => _ki.App;

    private async Task KiAsync(bool aktiv)
    {
        var antwort = await _ki.Oberflaeche().PutAsJsonAsync("/api/settings/ki", new { aktiv });
        antwort.EnsureSuccessStatusCode();
    }

    private static async Task Erwarte(HttpResponseMessage antwort, HttpStatusCode status, string? code = null)
    {
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.True(antwort.StatusCode == status,
            $"{antwort.RequestMessage?.Method} {antwort.RequestMessage?.RequestUri?.AbsolutePath}: erwartet {(int)status}, kam {(int)antwort.StatusCode} — {text}");
        if (code is not null)
            Assert.True(text.Contains($"\"code\":\"{code}\"", StringComparison.Ordinal), $"Erwartet Fehlercode {code}, kam: {text}");
    }

    [Fact]
    public async Task KiAus_SchluesselWirdAbgewiesen_AuchMitAllenStufen()
    {
        await KiAsync(true); // die Einstellungsseite des KI-Zugriffs gibt es nur bei „KI an"
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Alles", AlleStufen);
        await KiAsync(true);
        await Erwarte(await App.AddonClient("172.30.33.21", klartext).PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);

        await KiAsync(false);

        await Erwarte(await App.AddonClient("172.30.33.21", klartext).PostAsync("/api/ki-test/dokumentieren", null),
            HttpStatusCode.Forbidden, "ki_aus");
        await Erwarte(await App.AddonClient("172.30.33.21", klartext).GetAsync("/api/ki-test/lesen"),
            HttpStatusCode.Forbidden, "ki_aus");
    }

    [Fact]
    public async Task KiAus_RueckschaltenStelltAllesWiederHer_MitDenAltenSchluesseln()
    {
        await KiAsync(true); // die Einstellungsseite des KI-Zugriffs gibt es nur bei „KI an"
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Bleibt", "Dokumentieren");
        await KiAsync(false);
        await Erwarte(await App.AddonClient("172.30.33.22", klartext).PostAsync("/api/ki-test/dokumentieren", null),
            HttpStatusCode.Forbidden, "ki_aus");

        await KiAsync(true);

        await Erwarte(await App.AddonClient("172.30.33.22", klartext).PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);
        var einstellungen = await _ki.Oberflaeche().GetAsync("/api/settings/ki-zugriff");
        await Erwarte(einstellungen, HttpStatusCode.OK);
        Assert.Contains("\"aktiv\":true", await einstellungen.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/settings/ki-zugriff")]
    [InlineData("/api/agent-export/mappe")]
    [InlineData("/api/ki-ha/bereiche")]
    public async Task KiAus_KiControllerInDerOberflaecheAntwortenMit404(string pfad)
    {
        await KiAsync(false);

        await Erwarte(await _ki.Oberflaeche().GetAsync(pfad), HttpStatusCode.NotFound, "ki_aus");
    }

    [Fact]
    public async Task KiAus_DerSchalterSelbstBleibtErreichbar()
    {
        await KiAsync(false);

        var antwort = await _ki.Oberflaeche().GetAsync("/api/settings/ki");
        await Erwarte(antwort, HttpStatusCode.OK);
        Assert.Contains("\"aktiv\":false", await antwort.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EinSchluesselDarfDieKiNichtSelbstEinschalten()
    {
        await KiAsync(true); // die Einstellungsseite des KI-Zugriffs gibt es nur bei „KI an"
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Alles", AlleStufen);
        await KiAsync(true);

        var antwort = await App.AddonClient("172.30.33.23", klartext)
            .PutAsJsonAsync("/api/settings/ki", new { aktiv = true });

        await Erwarte(antwort, HttpStatusCode.Forbidden, "ki_kein_zugriff");
    }

    /// <summary>
    /// Jeder Controller der KI-Familie trägt <see cref="NurMitKiAttribute"/> — gezählt über die
    /// Reflexion, damit auch ein Controller, den es erst morgen gibt, nicht vergessen wird.
    /// </summary>
    [Fact]
    public void JederKiControllerTraegtNurMitKi()
    {
        var kiRouten = new[] { "api/ki-", "api/settings/ki-zugriff", "api/agent-export" };
        var controller = typeof(KiHauptschalterApiController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => (Typ: t, Route: t.GetCustomAttribute<RouteAttribute>()?.Template))
            .Where(x => x.Route is not null && kiRouten.Any(p => x.Route!.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Wächter: Die Erkennung muss die bekannten drei sehen, sonst wäre die Prüfung grundlos grün.
        Assert.True(controller.Count >= 3, "Erkannt: " + string.Join(", ", controller.Select(c => c.Typ.Name)));
        var ohne = controller.Where(c => c.Typ.GetCustomAttribute<NurMitKiAttribute>() is null).Select(c => c.Typ.Name).ToList();
        Assert.True(ohne.Count == 0, "Ohne [NurMitKi]: " + string.Join(", ", ohne));
    }
}
