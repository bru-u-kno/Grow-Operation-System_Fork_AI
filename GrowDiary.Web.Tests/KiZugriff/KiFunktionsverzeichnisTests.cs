using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Tests.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (Paket D, 18.10.2026): Das Funktionsverzeichnis (<c>GET /api/ki-zugriff/funktionen</c>) führt jede Route
/// und verspricht für jede genau das, was die Sperre entscheidet.
/// </summary>
/// <remarks>
/// <para><b>Grundmenge</b> sind die Endpunkte der laufenden App (<see cref="EndpointDataSource"/>), hier selbst
/// gelesen — nicht über die Hilfen des Verzeichnisses, sonst prüfte es sich selbst.</para>
/// <para><b>Einstufung:</b> Für jede Route und sechs Schlüssel (keine Stufe, jede einzeln, alle) wird
/// <see cref="KiZugriffSperre.Entscheiden"/> gefragt und mit dem verglichen, was der Assistent im Verzeichnis liest
/// (zugang, stufen, schaltbefehl, sicherungVorher) — so, wie ein Assistent es auslegen würde.</para>
/// <para><b>Was diese Zählung nicht sieht:</b> einen Fehler in <see cref="KiZugriffSperre.Einstufen"/> selbst — beide Seiten
/// rufen dieselbe Funktion. Den halten die älteren Prüfungen der Regel (<c>KiZugriffRegelTests</c>, <c>KiSchluesselWegTests</c>);
/// der Prüfer hat am 18.10.2026 zusätzlich über 100.000 Fälle gegen die alte Fassung von <c>Entscheiden</c> verglichen.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class KiFunktionsverzeichnisTests : IClassFixture<KiZugriffApp>
{
    private const int MindestensRouten = 300;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly KiZugriffApp _ki;

    public KiFunktionsverzeichnisTests(KiZugriffApp ki) => _ki = ki;

    private async Task<KiFunktionenDto> VerzeichnisAsync()
    {
        var antwort = await _ki.Oberflaeche().GetAsync("/api/ki-zugriff/funktionen");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        return (await antwort.Content.ReadFromJsonAsync<KiFunktionenDto>(Json))!;
    }

    /// <summary>Alle /api-Routen der App mit ihrer Methode — unabhängig vom Verzeichnis gelesen.</summary>
    private List<(string Schluessel, RouteEndpoint Endpunkt, string Methode, string BeispielPfad)> Routen()
    {
        var ergebnis = new List<(string, RouteEndpoint, string, string)>();
        var methoden = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" };
        foreach (var endpunkt in _ki.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            if (endpunkt.Metadata.GetMetadata<ControllerActionDescriptor>() is null) continue;
            var roh = "/" + endpunkt.RoutePattern.RawText!.TrimStart('/', '~');
            if (!roh.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) continue;

            var angegeben = endpunkt.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            var hier = angegeben is { Count: > 0 } ? methoden.Where(m => angegeben.Contains(m)).ToArray() : methoden;
            var pfad = Regex.Replace(roh, @"\{\**([A-Za-z0-9_]+)[^}]*\}", "{$1}");
            var beispiel = Regex.Replace(roh, @"\{[^}]+\}", "1");
            foreach (var methode in hier) ergebnis.Add(($"{methode} {pfad}", endpunkt, methode, beispiel));
        }
        return ergebnis;
    }

    [Fact]
    public async Task Jede_Route_der_App_steht_im_Verzeichnis_und_keine_erfundene()
    {
        await _ki.SchalterAsync(true);
        var verzeichnis = await VerzeichnisAsync();
        var routen = Routen().Select(r => r.Schluessel).ToHashSet(StringComparer.Ordinal);

        // Mengenwächter: beide Seiten müssen ihre Grundmenge sehen (18.10.2026: 356 Einträge).
        Assert.True(routen.Count >= MindestensRouten, $"Nur {routen.Count} Routen gelesen — die Zählung sieht ihre Grundmenge nicht.");
        Assert.Equal(verzeichnis.Funktionen.Count, verzeichnis.Anzahl);

        var eintraege = verzeichnis.Funktionen.Select(f => $"{f.Methode} {f.Pfad}").ToList();
        Assert.True(eintraege.Count == eintraege.Distinct().Count(), "Ein Eintrag steht doppelt: "
            + string.Join(", ", eintraege.GroupBy(e => e).Where(g => g.Count() > 1).Select(g => g.Key)));

        var fehlend = routen.Except(eintraege).Order().ToList();
        var erfunden = eintraege.Except(routen).Order().ToList();
        Assert.True(fehlend.Count == 0, "Diese Routen fehlen im Verzeichnis:\n  " + string.Join("\n  ", fehlend));
        Assert.True(erfunden.Count == 0, "Diese Einträge nennen keine Route:\n  " + string.Join("\n  ", erfunden));
    }

    [Fact]
    public async Task Die_Einstufung_im_Verzeichnis_ist_die_Entscheidung_der_Sperre()
    {
        await _ki.SchalterAsync(true);
        var verzeichnis = (await VerzeichnisAsync()).Funktionen.ToDictionary(f => $"{f.Methode} {f.Pfad}", StringComparer.Ordinal);
        var schluessel = new[] { KiStufe.Keine, KiStufe.Dokumentieren, KiStufe.GrowPlanen, KiStufe.GeraeteSchalten, KiStufe.Verwaltung,
            KiStufe.Dokumentieren | KiStufe.GrowPlanen | KiStufe.GeraeteSchalten | KiStufe.Verwaltung };

        var geprueft = 0;
        var abweichend = new List<string>();
        foreach (var (name, endpunkt, methode, beispiel) in Routen())
        {
            var eintrag = verzeichnis[name];
            foreach (var stufen in schluessel)
            {
                var kontext = new KiZugriffKontext(1, "Test", stufen, new KiHoechstwerteDto(10, 20));
                var sperre = KiZugriffSperre.Entscheiden(kontext, endpunkt, methode, new PathString(beispiel));
                var versprochen = Versprochen(eintrag, stufen);
                if (versprochen != (sperre.Durch, sperre.Code, sperre.Durch && sperre.SchaltbefehlZaehlen, sperre.Durch && sperre.SicherungVorher))
                {
                    abweichend.Add($"{name} mit [{stufen}]: Verzeichnis {versprochen}, Sperre ({sperre.Durch}, {sperre.Code}, {sperre.SchaltbefehlZaehlen}, {sperre.SicherungVorher})");
                }
                geprueft++;
            }
            if (eintrag.Zugang == "nie")
            {
                Assert.False(string.IsNullOrWhiteSpace(eintrag.NieGrund), $"{name}: „nie\" ohne Grund.");
            }
        }

        Assert.True(geprueft >= MindestensRouten * schluessel.Length, $"Nur {geprueft} Entscheidungen geprüft.");
        Assert.True(abweichend.Count == 0, "Das Verzeichnis verspricht etwas anderes als die Sperre:\n  " + string.Join("\n  ", abweichend.Take(30)));
    }

    /// <summary>Was ein Assistent aus dem Eintrag liest: kommt er durch, mit welchem Fehler, zählt es, Sicherung?</summary>
    private static (bool Durch, string? Code, bool Schalt, bool Sicherung) Versprochen(KiFunktionDto eintrag, KiStufe schluessel)
    {
        var verlangt = eintrag.Stufen.Aggregate(KiStufe.Keine, (summe, n) => summe | Enum.Parse<KiStufe>(n));
        bool Darf(KiStufe s) => (schluessel & s) == s;
        return eintrag.Zugang switch
        {
            "frei" => (true, null, false, false),
            "nie" => (false, "ki_kein_zugriff", false, false),
            "nicht-eingestuft" => eintrag.Verwaltungsweg && !Darf(KiStufe.Verwaltung)
                ? (false, "ki_stufe_fehlt", false, false)
                : (false, "ki_nicht_eingestuft", false, false),
            "stufe" => verlangt != KiStufe.Keine && Darf(verlangt)
                ? (true, null, eintrag.Schaltbefehl, eintrag.SicherungVorher)
                : (false, "ki_stufe_fehlt", false, false),
            _ => throw new InvalidOperationException($"Unbekannter Zugang „{eintrag.Zugang}\" bei {eintrag.Methode} {eintrag.Pfad}."),
        };
    }

    [Fact]
    public async Task Die_vier_Sperren_stehen_als_nie_im_Verzeichnis()
    {
        await _ki.SchalterAsync(true);
        var nie = (await VerzeichnisAsync()).Funktionen.Where(f => f.Zugang == "nie").Select(f => $"{f.Methode} {f.Pfad}").ToHashSet();

        // Schlüsselverwaltung, KI-Hauptschalter, Sicherung zurückspielen/herunterladen, HA-Adresse und -Token.
        Assert.Contains("POST /api/settings/ki-zugriff/schluessel", nie);
        Assert.Contains("PUT /api/settings/ki-zugriff/schluessel/{id}", nie);
        Assert.Contains("PUT /api/settings/ki", nie);
        Assert.Contains("POST /api/system/backup/{fileName}/restore", nie);
        Assert.Contains("GET /api/system/backup/{fileName}", nie);
        Assert.Contains("PUT /api/settings/home-assistant", nie);
    }

    [Fact]
    public async Task Ein_Eintrag_nennt_Stufen_Sicherung_Rumpf_und_Beschreibung()
    {
        await _ki.SchalterAsync(true);
        var funktionen = (await VerzeichnisAsync()).Funktionen;
        KiFunktionDto Eintrag(string methode, string pfad) => Assert.Single(funktionen, f => f.Methode == methode && f.Pfad == pfad);

        // Entfeuchter speichern: zwei Stufen, zählt als Schaltbefehl, Rumpf mit Feldern unter JSON-Namen.
        var entfeuchter = Eintrag("PUT", "/api/steuerung/entfeuchter");
        Assert.Equal("stufe", entfeuchter.Zugang);
        Assert.Equal(["GeraeteSchalten", "Verwaltung"], entfeuchter.Stufen.Order().ToArray());
        Assert.True(entfeuchter.Schaltbefehl);
        Assert.NotNull(entfeuchter.Rumpf);
        Assert.Contains(entfeuchter.Rumpf!.Felder, f => f.Name == "hystereseProzent" && f.Typ == "Zahl");

        // Lesen ist frei; der Weg-Parameter mit Typ.
        var pflanze = Eintrag("GET", "/api/plants/{id}");
        Assert.Equal("frei", pflanze.Zugang);
        Assert.Equal([new KiParameterDto("id", "Ganzzahl", true)], pflanze.Routenparameter.ToArray());

        // Ein Verwaltungsweg verlangt auch lesend Verwaltung.
        var zelt = Eintrag("GET", "/api/settings/tents/{id}");
        Assert.True(zelt.Verwaltungsweg);
        Assert.Equal(["Verwaltung"], zelt.Stufen.ToArray());

        // Das Livebild ist eine Datei, kein JSON.
        Assert.True(Eintrag("GET", "/api/live/tents/{tentId}/camera").LiefertDatei);

        // Die Beschreibung kommt aus der Code-Doku, nicht aus dem Namen der Aktion.
        Assert.Equal("Was der anfragende Schlüssel darf — die erste Frage eines Assistenten.", Eintrag("GET", "/api/ki-zugriff/ich").Beschreibung);
        Assert.StartsWith("Alle Funktionen von Grow OS unter /api", Eintrag("GET", "/api/ki-zugriff/funktionen").Beschreibung);
    }

    [Fact]
    public void Die_Code_Doku_liegt_neben_der_Assembly()
    {
        // Selbsttest für die Beschreibungen: ohne die XML-Datei stünde überall nur Controller.Aktion.
        var doku = XmlDoku.Laden(typeof(KiFunktionenApiController).Assembly);
        Assert.True(doku.Anzahl >= 500, $"Nur {doku.Anzahl} Zusammenfassungen gelesen — fehlt GenerateDocumentationFile?");
        Assert.Equal("Was der anfragende Schlüssel darf — die erste Frage eines Assistenten.",
            doku.Zusammenfassung(typeof(KiZugriffApiController).GetMethod(nameof(KiZugriffApiController.Ich))!));
    }

    [Fact]
    public async Task Das_Verzeichnis_lesen_der_kleinste_Schluessel_und_ein_Nachbar_ohne_Schluessel()
    {
        await _ki.SchalterAsync(true);
        // Ein Schlüssel hat mindestens eine Stufe; lesen braucht keine — Dokumentieren ist die kleinste.
        var (_, klartext) = await _ki.SchluesselAsync("Nur dokumentieren", "Dokumentieren");

        var mitSchluessel = await _ki.App.AddonClient("172.30.33.5", klartext).GetAsync("/api/ki-zugriff/funktionen");
        Assert.Equal(HttpStatusCode.OK, mitSchluessel.StatusCode);

        var ohneSchluessel = await _ki.App.AddonClient("172.30.33.5").GetAsync("/api/ki-zugriff/funktionen");
        Assert.Equal(HttpStatusCode.OK, ohneSchluessel.StatusCode);
    }

    [Theory]
    [InlineData("/api/plants/{id:int}", "/api/plants/{id}")]
    [InlineData("/api/steuerung/probelauf/{id:long}/abbrechen", "/api/steuerung/probelauf/{id}/abbrechen")]
    [InlineData("/api/x/{**rest}", "/api/x/{rest}")]
    [InlineData("/api/x/{name?}", "/api/x/{name}")]
    [InlineData("/api/x/{wert=3}", "/api/x/{wert}")]
    public void Platzhalter_verlieren_ihre_Einschraenkungen(string vorlage, string erwartet)
        => Assert.Equal(erwartet, KiFunktionsverzeichnis.PfadOhneEinschraenkungen(vorlage));
}
