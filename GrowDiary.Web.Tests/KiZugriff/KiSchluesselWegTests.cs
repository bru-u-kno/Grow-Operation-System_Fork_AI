using System.Net;
using System.Net.Http.Json;
using System.Text;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Tests.Api;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Der Schlüsselweg an der echten App — Sperre,
/// Routing, Einstufung, Protokoll, alles in einer Kette.
/// </summary>
/// <remarks>
/// <para>Jeder Fall nimmt eine eigene Absenderadresse, wo Fehlversuche im
/// Spiel sind: die Sperre gilt je Adresse und hält 15 Minuten — ein Fall,
/// der sie auslöst, sperrte sonst alle folgenden aus.</para>
///
/// <para>Die echten Controller sind hier noch nicht eingestuft (das machen
/// andere Arbeitsstränge); geprüft wird an <see cref="KiTestController"/>.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class KiSchluesselWegTests : IClassFixture<KiZugriffApp>
{
    private readonly KiZugriffApp _ki;

    public KiSchluesselWegTests(KiZugriffApp ki) => _ki = ki;

    private IntegrationsApp App => _ki.App;

    private static async Task<string?> Code(HttpResponseMessage antwort)
        => (await antwort.Content.ReadFromJsonAsync<ApiError>())?.Code;

    private static async Task Erwarte(HttpResponseMessage antwort, HttpStatusCode status, string? code = null)
    {
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.True(antwort.StatusCode == status,
            $"{antwort.RequestMessage?.Method} {antwort.RequestMessage?.RequestUri?.AbsolutePath}: erwartet {(int)status}, kam {(int)antwort.StatusCode} — {text}");
        if (code is not null)
        {
            Assert.True(text.Contains($"\"code\":\"{code}\"", StringComparison.Ordinal),
                $"Erwartet Fehlercode {code}, kam: {text}");
        }
    }

    private static readonly string[] AlleStufen = ["Dokumentieren", "GrowPlanen", "GeraeteSchalten", "Verwaltung"];

    // ------------------------------------------------------- wie heute

    [Fact]
    public async Task OhneSchluesselBleibtSchreibenAusDemAddonNetzGesperrt()
    {
        await _ki.SchalterAsync(true);
        var nachbar = App.AddonClient("172.30.33.5");

        await Erwarte(await nachbar.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.Forbidden, "admin_access_required");
        // Lesen wie bisher.
        await Erwarte(await nachbar.GetAsync("/api/ki-test/lesen"), HttpStatusCode.OK);
        // Ein anderes Bearer-Token ist kein Schlüssel und öffnet nichts.
        var fremd = App.AddonClient("172.30.33.5", "eyJhbGciOiJIUzI1NiJ9.fremd");
        await Erwarte(await fremd.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.Forbidden, "admin_access_required");
    }

    // ---------------------------------------------------------- durch

    [Fact]
    public async Task GueltigerSchluesselMitPassenderStufeKommtDurch()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Durchgang", "Dokumentieren");

        var antwort = await App.AddonClient("172.30.33.6", klartext).PostAsync("/api/ki-test/dokumentieren", null);
        await Erwarte(antwort, HttpStatusCode.OK);
        Assert.Contains("dokumentieren", await antwort.Content.ReadAsStringAsync());

        // Von Loopback ebenso — und mit denselben Grenzen.
        var lokal = App.AddonClient("127.0.0.1", klartext);
        await Erwarte(await lokal.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);
        await Erwarte(await lokal.PostAsync("/api/ki-test/schalten", null), HttpStatusCode.Forbidden, "ki_stufe_fehlt");
    }

    // ----------------------------------------------------- abgewiesen

    [Fact]
    public async Task FehlendeStufeWirdAbgewiesen()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Nur Doku", "Dokumentieren");

        var antwort = await App.AddonClient("172.30.33.7", klartext).PostAsync("/api/ki-test/schalten", null);
        await Erwarte(antwort, HttpStatusCode.Forbidden, "ki_stufe_fehlt");
        Assert.Contains("Geräte schalten", (await antwort.Content.ReadFromJsonAsync<ApiError>())!.Message);
    }

    [Fact]
    public async Task NichtEingestuftIstGesperrt_AuchMitAllenStufen()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Alles", AlleStufen);

        await Erwarte(await App.AddonClient("172.30.33.8", klartext).PostAsync("/api/ki-test/uneingestuft", null),
            HttpStatusCode.Forbidden, "ki_nicht_eingestuft");
    }

    [Fact]
    public async Task KeinKiZugriffIstGesperrt_AuchMitAllenStufen()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Alles", AlleStufen);

        await Erwarte(await App.AddonClient("172.30.33.9", klartext).PostAsync("/api/ki-test/nie", null),
            HttpStatusCode.Forbidden, "ki_kein_zugriff");
    }

    [Fact]
    public async Task DieStufeDerAktionGewinntVorDerDesControllers()
    {
        await _ki.SchalterAsync(true);
        var (_, doku) = await _ki.SchluesselAsync("Doku", "Dokumentieren");
        var (_, planen) = await _ki.SchluesselAsync("Planen", "GrowPlanen");

        var mitDoku = App.AddonClient("172.30.33.10", doku);
        await Erwarte(await mitDoku.PostAsync("/api/ki-test-planen/von-der-aktion", null), HttpStatusCode.OK);
        await Erwarte(await mitDoku.PostAsync("/api/ki-test-planen/vom-controller", null), HttpStatusCode.Forbidden, "ki_stufe_fehlt");

        var mitPlanen = App.AddonClient("172.30.33.10", planen);
        await Erwarte(await mitPlanen.PostAsync("/api/ki-test-planen/vom-controller", null), HttpStatusCode.OK);
        await Erwarte(await mitPlanen.PostAsync("/api/ki-test-planen/von-der-aktion", null), HttpStatusCode.Forbidden, "ki_stufe_fehlt");
    }

    [Fact]
    public async Task HauptschalterAusWeistJedenSchluesselAb()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Abgeschaltet", AlleStufen);
        var client = App.AddonClient("172.30.33.11", klartext);
        await Erwarte(await client.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);

        await _ki.SchalterAsync(false);
        try
        {
            await Erwarte(await client.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.Forbidden, "ki_zugriff_aus");
            await Erwarte(await client.GetAsync("/api/ki-zugriff/ich"), HttpStatusCode.Forbidden, "ki_zugriff_aus");
        }
        finally
        {
            await _ki.SchalterAsync(true);
        }
        await Erwarte(await client.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);
    }

    [Fact]
    public async Task FalscherSchluesselGibt401_UndNachZehnVersuchen429()
    {
        await _ki.SchalterAsync(true);
        var (_, gueltig) = await _ki.SchluesselAsync("Gültig", "Dokumentieren");
        var falsch = KiZugriffDienst.NeuerKlartext();
        const string adresse = "172.30.33.99";

        for (var i = 1; i <= KiZugriffDienst.FehlversucheBisSperre; i++)
        {
            await Erwarte(await App.AddonClient(adresse, falsch).PostAsync("/api/ki-test/dokumentieren", null),
                HttpStatusCode.Unauthorized, "ki_schluessel_ungueltig");
        }

        await Erwarte(await App.AddonClient(adresse, falsch).PostAsync("/api/ki-test/dokumentieren", null),
            (HttpStatusCode)429, "ki_zu_viele_versuche");
        // Gesperrt wird die Adresse, ohne zu prüfen — auch ein gültiger Schlüssel kommt nicht durch.
        await Erwarte(await App.AddonClient(adresse, gueltig).PostAsync("/api/ki-test/dokumentieren", null),
            (HttpStatusCode)429, "ki_zu_viele_versuche");
        // Eine andere Adresse bleibt unberührt.
        await Erwarte(await App.AddonClient("172.30.33.98", gueltig).PostAsync("/api/ki-test/dokumentieren", null),
            HttpStatusCode.OK);
    }

    [Fact]
    public async Task GesperrterSchluesselWirdAbgewiesen()
    {
        await _ki.SchalterAsync(true);
        var (id, klartext) = await _ki.SchluesselAsync("Wird gesperrt", "Dokumentieren");
        var client = App.AddonClient("172.30.33.20", klartext);
        await Erwarte(await client.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);

        var gesperrt = await _ki.Oberflaeche().PostAsync($"/api/settings/ki-zugriff/schluessel/{id}/sperren", null);
        await Erwarte(gesperrt, HttpStatusCode.OK);
        Assert.NotNull((await gesperrt.Content.ReadFromJsonAsync<KiSchluesselDto>())!.GesperrtAmUtc);

        await Erwarte(await client.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.Unauthorized, "ki_schluessel_ungueltig");
        await Erwarte(await client.GetAsync("/api/ki-test/lesen"), HttpStatusCode.Unauthorized, "ki_schluessel_ungueltig");
    }

    [Fact]
    public async Task SchluesselVonAusserhalbDesAddonNetzesWirdNieGeprueft()
    {
        await _ki.SchalterAsync(true);
        var (id, klartext) = await _ki.SchluesselAsync("Von aussen", AlleStufen);

        // Gültiger Schlüssel aus dem Heimnetz: der Weg von heute, 403.
        var heimnetz = App.AddonClient("192.168.1.50", klartext);
        await Erwarte(await heimnetz.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.Forbidden, "admin_access_required");
        await Erwarte(await heimnetz.GetAsync("/api/ki-test/lesen"), HttpStatusCode.Forbidden, "admin_access_required");

        // Falsche Schlüssel von aussen zählen nie als Fehlversuch — es gibt nie 401 oder 429.
        for (var i = 0; i < KiZugriffDienst.FehlversucheBisSperre + 2; i++)
        {
            await Erwarte(await App.AddonClient("192.168.1.51", KiZugriffDienst.NeuerKlartext()).PostAsync("/api/ki-test/dokumentieren", null),
                HttpStatusCode.Forbidden, "admin_access_required");
        }

        // Und der gültige wurde nie benutzt.
        var seite = await _ki.Oberflaeche().GetFromJsonAsync<KiZugriffSeiteDto>("/api/settings/ki-zugriff");
        Assert.Null(seite!.Schluessel.Single(s => s.Id == id).ZuletztGenutztAmUtc);
    }

    [Fact]
    public async Task EchterIngressBleibtDieOberflaeche_AuchMitSchluesselImKopf()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Im Ingress", "Dokumentieren");
        var oberflaeche = _ki.Oberflaeche();
        oberflaeche.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", klartext);

        // Ein Mensch in der Oberfläche bekommt nicht die Grenzen eines Schlüssels.
        await Erwarte(await oberflaeche.PostAsync("/api/ki-test/nie", null), HttpStatusCode.OK);
        await Erwarte(await oberflaeche.GetAsync("/api/settings/ki-zugriff"), HttpStatusCode.OK);
    }

    // ---------------------------------------------------- Verwaltung

    [Fact]
    public async Task VerwaltungswegLesenBrauchtVerwaltung()
    {
        await _ki.SchalterAsync(true);
        var (_, doku) = await _ki.SchluesselAsync("Doku liest", "Dokumentieren");
        var (_, verwaltung) = await _ki.SchluesselAsync("Verwaltung liest", "Verwaltung");

        await Erwarte(await App.AddonClient("172.30.33.21", doku).GetAsync("/api/settings"), HttpStatusCode.Forbidden, "ki_stufe_fehlt");
        await Erwarte(await App.AddonClient("172.30.33.21", verwaltung).GetAsync("/api/settings"), HttpStatusCode.OK);
        // Lesen ausserhalb der Verwaltungswege braucht keine Stufe.
        await Erwarte(await App.AddonClient("172.30.33.21", doku).GetAsync("/api/ki-test/lesen"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task DieSchluesselverwaltungIstMitKeinemSchluesselErreichbar()
    {
        await _ki.SchalterAsync(true);
        var (id, klartext) = await _ki.SchluesselAsync("Darf alles", AlleStufen);
        var ki = App.AddonClient("172.30.33.22", klartext);
        var vorher = (await _ki.Oberflaeche().GetFromJsonAsync<KiZugriffSeiteDto>("/api/settings/ki-zugriff"))!.Schluessel.Count;

        var versuche = new (HttpMethod Methode, string Pfad, object? Inhalt)[]
        {
            (HttpMethod.Get, "/api/settings/ki-zugriff", null),
            (HttpMethod.Put, "/api/settings/ki-zugriff", new { aktiv = true, rueckfrageAbStufe = (string?)null }),
            (HttpMethod.Post, "/api/settings/ki-zugriff/schluessel", new { name = "Zweitschlüssel", stufen = AlleStufen }),
            (HttpMethod.Put, $"/api/settings/ki-zugriff/schluessel/{id}", new { name = "Umbenannt", stufen = AlleStufen }),
            (HttpMethod.Post, $"/api/settings/ki-zugriff/schluessel/{id}/sperren", null),
            (HttpMethod.Delete, $"/api/settings/ki-zugriff/schluessel/{id}", null),
        };
        Assert.Equal(6, versuche.Length);

        foreach (var (methode, pfad, inhalt) in versuche)
        {
            var anfrage = new HttpRequestMessage(methode, pfad);
            if (inhalt is not null) anfrage.Content = JsonContent.Create(inhalt);
            await Erwarte(await ki.SendAsync(anfrage), HttpStatusCode.Forbidden, "ki_kein_zugriff");
        }

        var nachher = (await _ki.Oberflaeche().GetFromJsonAsync<KiZugriffSeiteDto>("/api/settings/ki-zugriff"))!;
        Assert.Equal(vorher, nachher.Schluessel.Count);
        var selbst = nachher.Schluessel.Single(s => s.Id == id);
        Assert.Equal("Darf alles", selbst.Name);
        Assert.Null(selbst.GesperrtAmUtc);
    }

    [Fact]
    public async Task IchSagtDemAssistentenWasErDarf()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Claude am Telefon", "Dokumentieren", "GeraeteSchalten");

        var ich = await App.AddonClient("172.30.33.23", klartext).GetFromJsonAsync<KiZugriffIchDto>("/api/ki-zugriff/ich");
        Assert.Equal("Claude am Telefon", ich!.SchluesselName);
        Assert.Equal(new[] { "Dokumentieren", "GeraeteSchalten" }, ich.Stufen);
        Assert.Equal("GrowPlanen", ich.RueckfrageAbStufe);
        Assert.Equal(10.0, ich.Hoechstwerte.MaxDosisMlJeBefehl);

        await Erwarte(await _ki.Oberflaeche().GetAsync("/api/ki-zugriff/ich"), HttpStatusCode.Unauthorized, "ki_schluessel_fehlt");
    }

    // ------------------------------------------------------ Sicherung

    [Fact]
    public async Task SicherungVorherLegtEineSicherungAn_UndOhneSicherungLaeuftNichts()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Sichert", "Verwaltung");
        var client = App.AddonClient("172.30.33.24", klartext);
        var ordner = App.Services.GetRequiredService<AppPaths>().BackupsPath;
        int Sicherungen() => Directory.Exists(ordner) ? Directory.GetFiles(ordner, "*.zip").Length : 0;

        var vorher = Sicherungen();
        var aufrufeVorher = KiTestController.Aufrufe.MitSicherung;
        await Erwarte(await client.PostAsync("/api/ki-test/sicherung", null), HttpStatusCode.OK);
        Assert.Equal(vorher + 1, Sicherungen());
        Assert.Equal(aufrufeVorher + 1, KiTestController.Aufrufe.MitSicherung);

        _ki.Sicherung.Versagen = true;
        try
        {
            await Erwarte(await client.PostAsync("/api/ki-test/sicherung", null), HttpStatusCode.ServiceUnavailable, "ki_sicherung_fehlgeschlagen");
        }
        finally
        {
            _ki.Sicherung.Versagen = false;
        }
        Assert.Equal(vorher + 1, Sicherungen());
        Assert.Equal(aufrufeVorher + 1, KiTestController.Aufrufe.MitSicherung);

        // Aus der Oberfläche keine Sicherung: dort entscheidet der Mensch.
        await Erwarte(await _ki.Oberflaeche().PostAsync("/api/ki-test/sicherung", null), HttpStatusCode.OK);
        Assert.Equal(vorher + 1, Sicherungen());
    }

    // ------------------------------------------------------ Speicher

    [Fact]
    public async Task DerKlartextStehtNirgendsInDerDatenbank()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Geheim", "Dokumentieren");
        await Erwarte(await App.AddonClient("172.30.33.25", klartext).PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);

        var pfade = App.Services.GetRequiredService<AppPaths>();
        var dateien = new[] { pfade.DatabasePath, pfade.DatabasePath + "-wal" }.Where(File.Exists).ToList();
        Assert.NotEmpty(dateien);

        // Gelesen wird mit geteiltem Zugriff — die App hält die Datei offen.
        string Inhalt(string datei)
        {
            using var strom = new FileStream(datei, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var puffer = new MemoryStream();
            strom.CopyTo(puffer);
            return Encoding.Latin1.GetString(puffer.ToArray());
        }
        var alles = string.Concat(dateien.Select(Inhalt));

        // Mengenwächter: der Hash MUSS drinstehen — sonst läse die Suche die falsche Datei.
        Assert.Contains(KiZugriffDienst.Hash(klartext), alles);
        Assert.DoesNotContain(klartext, alles);
        Assert.DoesNotContain(klartext[KiZugriffDienst.Vorsilbe.Length..], alles);
    }

    // ------------------------------------------------------ Protokoll

    [Fact]
    public async Task JedeSchreibendeAnfrageKommtInsPruefprotokoll()
    {
        await _ki.SchalterAsync(true);
        var name = "Protokoll " + Guid.NewGuid().ToString("N")[..6];
        var (id, klartext) = await _ki.SchluesselAsync(name, "Dokumentieren");
        var client = App.AddonClient("172.30.33.26", klartext);

        await Erwarte(await client.PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);
        await Erwarte(await client.PostAsync("/api/ki-test/schalten", null), HttpStatusCode.Forbidden, "ki_stufe_fehlt");
        await Erwarte(await client.GetAsync("/api/ki-test/lesen"), HttpStatusCode.OK);

        var zeilen = App.Services.GetRequiredService<SystemAuditRepository>().GetRecent(500, "security")
            .Where(e => e.Source == "ki-zugriff" && e.Summary.Contains(name, StringComparison.Ordinal))
            .Select(e => e.Summary)
            .ToList();
        Assert.Contains($"über KI-Assistent ‚{name}‘: POST /api/ki-test/dokumentieren → 200", zeilen);
        Assert.Contains($"über KI-Assistent ‚{name}‘: POST /api/ki-test/schalten → 403", zeilen);
        // Lesen ausserhalb der Verwaltung füllt das Protokoll nicht.
        Assert.DoesNotContain(zeilen, z => z.Contains("/api/ki-test/lesen", StringComparison.Ordinal));

        var seite = await _ki.Oberflaeche().GetFromJsonAsync<KiZugriffSeiteDto>("/api/settings/ki-zugriff");
        Assert.NotNull(seite!.Schluessel.Single(s => s.Id == id).ZuletztGenutztAmUtc);
    }

    // ---------------------------------------------------- Höchstwerte

    [Fact]
    public async Task HoechstwertSchaltbefehleJeStundeGreift()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Schaltet", "GeraeteSchalten");
        var client = App.AddonClient("172.30.33.27", klartext);

        // Das Fenster zählt über die ganze App. Andere Fälle können schon
        // gezählt haben — deshalb: höchstens drei gehen durch, und spätestens
        // der vierte wird abgewiesen.
        await _ki.SchalterAsync(true, maxSchaltbefehle: 3);
        try
        {
            var durch = 0;
            HttpResponseMessage? abgewiesen = null;
            for (var i = 0; i < 4 && abgewiesen is null; i++)
            {
                var antwort = await client.PostAsync("/api/ki-test/schalten", null);
                if (antwort.StatusCode == HttpStatusCode.OK) durch++;
                else abgewiesen = antwort;
            }

            Assert.True(durch <= 3, $"{durch} Schaltbefehle gingen durch, erlaubt waren 3.");
            Assert.NotNull(abgewiesen);
            await Erwarte(abgewiesen!, (HttpStatusCode)429, "ki_hoechstwert");

            // Andere Stufen zählen nicht ins Fenster.
            var (_, doku) = await _ki.SchluesselAsync("Doku neben dem Fenster", "Dokumentieren");
            await Erwarte(await App.AddonClient("172.30.33.27", doku).PostAsync("/api/ki-test/dokumentieren", null), HttpStatusCode.OK);

            // Eine Aktion mit mehreren Stufen zählt, sobald GeraeteSchalten darunter ist
            // (wie das Speichern der Licht-Steuerung: Verwaltung | GeraeteSchalten).
            var (_, alles) = await _ki.SchluesselAsync("Licht und Verwaltung", "Verwaltung", "GeraeteSchalten");
            await Erwarte(await App.AddonClient("172.30.33.27", alles).PostAsync("/api/ki-test/licht", null), (HttpStatusCode)429, "ki_hoechstwert");

            // Der Stopp ist ein Sicherheitsbefehl: er scheitert nie am Höchstwert.
            for (var i = 0; i < 5; i++)
            {
                await Erwarte(await client.PostAsync("/api/ki-test/stopp", null), HttpStatusCode.OK);
            }
        }
        finally
        {
            await _ki.SchalterAsync(true);
        }

        await Erwarte(await client.PostAsync("/api/ki-test/schalten", null), HttpStatusCode.OK);
    }

    // ---------------------------------------------------- Validierung

    /// <summary>Ein Feldfehler — die Schreibweise des Schlüssels macht die JSON-Einstellung (camelCase).</summary>
    private static string[] Feld(ApiError fehler, string feld)
        => fehler.FieldErrors?.FirstOrDefault(e => string.Equals(e.Key, feld, StringComparison.OrdinalIgnoreCase)).Value
           ?? throw new Xunit.Sdk.XunitException($"Kein Feldfehler für {feld}: {string.Join(", ", fehler.FieldErrors?.Keys ?? [])}");

    [Fact]
    public async Task UnbekannteStufeUndFalscherNameGeben400MitFeldfehler()
    {
        var oberflaeche = _ki.Oberflaeche();

        async Task<ApiError> Fehler(object inhalt)
        {
            var antwort = await oberflaeche.PostAsJsonAsync("/api/settings/ki-zugriff/schluessel", inhalt);
            await Erwarte(antwort, HttpStatusCode.BadRequest, "validation_failed");
            return (await antwort.Content.ReadFromJsonAsync<ApiError>())!;
        }

        var stufe = await Fehler(new { name = "Test", stufen = new[] { "Dokumentieren", "Fliegen" } });
        Assert.Contains("Fliegen", Feld(stufe, "Stufen").Single());

        var ohneName = await Fehler(new { name = "  ", stufen = new[] { "Dokumentieren" } });
        Assert.NotEmpty(Feld(ohneName, "Name"));

        var zuLang = await Fehler(new { name = new string('x', KiZugriffDienst.NameHoechstLaenge + 1), stufen = Array.Empty<string>() });
        Assert.NotEmpty(Feld(zuLang, "Name"));

        // Genau 60 Zeichen gehen, und ohne Stufen gilt die Vorbelegung.
        var antwort = await oberflaeche.PostAsJsonAsync("/api/settings/ki-zugriff/schluessel",
            new { name = new string('y', KiZugriffDienst.NameHoechstLaenge) });
        await Erwarte(antwort, HttpStatusCode.Created);
        var angelegt = (await antwort.Content.ReadFromJsonAsync<KiSchluesselAngelegtDto>())!;
        Assert.Equal(new[] { "Dokumentieren" }, angelegt.Schluessel.Stufen);
        Assert.Equal(angelegt.Klartext.Substring(4, 8), angelegt.Schluessel.Praefix);

        var rueckfrage = await oberflaeche.PutAsJsonAsync("/api/settings/ki-zugriff", new { aktiv = true, rueckfrageAbStufe = "Quatsch" });
        await Erwarte(rueckfrage, HttpStatusCode.BadRequest, "validation_failed");
        Assert.NotEmpty(Feld((await rueckfrage.Content.ReadFromJsonAsync<ApiError>())!, "RueckfrageAbStufe"));
    }

    [Fact]
    public async Task EinstellungenKommenAnUndBleiben()
    {
        var oberflaeche = _ki.Oberflaeche();
        try
        {
            // Zweimal speichern, mit verschiedenen Werten — der Zustand „schon gespeichert" ist ein eigener Fall.
            foreach (var (rueckfrage, dosis, schalt) in new[] { ("Verwaltung", 2.5, 7), ((string?)null, 0.5, 0) })
            {
                var antwort = await oberflaeche.PutAsJsonAsync("/api/settings/ki-zugriff", new
                {
                    aktiv = true,
                    rueckfrageAbStufe = rueckfrage,
                    hoechstwerte = new { maxDosisMlJeBefehl = dosis, maxSchaltbefehleJeStunde = schalt },
                });
                await Erwarte(antwort, HttpStatusCode.OK);

                var seite = (await oberflaeche.GetFromJsonAsync<KiZugriffSeiteDto>("/api/settings/ki-zugriff"))!;
                Assert.True(seite.Aktiv);
                Assert.Equal(rueckfrage, seite.RueckfrageAbStufe);
                Assert.Equal(dosis, seite.Hoechstwerte.MaxDosisMlJeBefehl);
                Assert.Equal(schalt, seite.Hoechstwerte.MaxSchaltbefehleJeStunde);
            }

            // Ohne Höchstwerte bleiben die bisherigen.
            await Erwarte(await oberflaeche.PutAsJsonAsync("/api/settings/ki-zugriff", new { aktiv = false, rueckfrageAbStufe = "GrowPlanen" }), HttpStatusCode.OK);
            var danach = (await oberflaeche.GetFromJsonAsync<KiZugriffSeiteDto>("/api/settings/ki-zugriff"))!;
            Assert.False(danach.Aktiv);
            Assert.Equal(0.5, danach.Hoechstwerte.MaxDosisMlJeBefehl);

            var unsinn = await oberflaeche.PutAsJsonAsync("/api/settings/ki-zugriff", new
            {
                aktiv = true,
                hoechstwerte = new { maxDosisMlJeBefehl = -1.0, maxSchaltbefehleJeStunde = 5000 },
            });
            await Erwarte(unsinn, HttpStatusCode.BadRequest, "validation_failed");
        }
        finally
        {
            await _ki.SchalterAsync(true);
        }
    }

    [Fact]
    public async Task StoppZaehltNichtInsFenster()
    {
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Stoppt", "GeraeteSchalten");
        var client = App.AddonClient("172.30.33.28", klartext);

        // Ein Fenster ohne einen einzigen erlaubten Schaltbefehl.
        await _ki.SchalterAsync(true, maxSchaltbefehle: 0);
        try
        {
            // Bei 0 erlaubten Schaltbefehlen geht kein Schalten — der Stopp schon.
            await Erwarte(await client.PostAsync("/api/ki-test/schalten", null), (HttpStatusCode)429, "ki_hoechstwert");
            await Erwarte(await client.PostAsync("/api/ki-test/stopp", null), HttpStatusCode.OK);
            await Erwarte(await client.PostAsync("/api/ki-test/stopp", null), HttpStatusCode.OK);
        }
        finally
        {
            await _ki.SchalterAsync(true);
        }
    }

    [Fact]
    public async Task EineAusnahmeBleibtEine500_AuchUeberEinenSchluessel()
    {
        // Im Add-on läuft UseExceptionHandler("/api/error"); er wiederholt die
        // Anfrage intern — mit dem Kontext in Items. Vorher wurde daraus 403.
        await _ki.SchalterAsync(true);
        var name = "Wirft " + Guid.NewGuid().ToString("N")[..6];
        var (_, klartext) = await _ki.SchluesselAsync(name, "Dokumentieren");

        var antwort = await App.AddonClient("172.30.33.29", klartext).PostAsync("/api/ki-test/wirft", null);
        await Erwarte(antwort, HttpStatusCode.InternalServerError, "invalid_operation");
        var fehler = (await antwort.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal(ApiErrorFactory.SchemaVersion, fehler.SchemaVersion);
        Assert.Equal(500, fehler.Status);

        var zeilen = App.Services.GetRequiredService<SystemAuditRepository>().GetRecent(500, "security")
            .Where(e => e.Source == "ki-zugriff" && e.Summary.Contains(name, StringComparison.Ordinal))
            .Select(e => e.Summary)
            .ToList();
        Assert.Contains($"über KI-Assistent ‚{name}‘: POST /api/ki-test/wirft → 500", zeilen);
    }

    [Fact]
    public async Task SchluesselBrauchtNameUndMindestensEineStufe_BeimAnlegenUndAendern()
    {
        var oberflaeche = _ki.Oberflaeche();
        var (id, _) = await _ki.SchluesselAsync("Bestehend", "Dokumentieren");

        async Task<ApiError> Abgelehnt(HttpResponseMessage antwort)
        {
            await Erwarte(antwort, HttpStatusCode.BadRequest, "validation_failed");
            return (await antwort.Content.ReadFromJsonAsync<ApiError>())!;
        }

        // Anlegen: ausdrücklich leere Liste ist ein Fehler (fehlendes Feld: Vorbelegung, siehe unten).
        Assert.NotEmpty(Feld(await Abgelehnt(await oberflaeche.PostAsJsonAsync("/api/settings/ki-zugriff/schluessel",
            new { name = "Leer", stufen = Array.Empty<string>() })), "Stufen"));

        // Ändern: ohne Stufen, mit leerer Liste, ohne Namen, mit zu langem Namen.
        var weg = $"/api/settings/ki-zugriff/schluessel/{id}";
        Assert.NotEmpty(Feld(await Abgelehnt(await oberflaeche.PutAsJsonAsync(weg, new { name = "Bestehend" })), "Stufen"));
        Assert.NotEmpty(Feld(await Abgelehnt(await oberflaeche.PutAsJsonAsync(weg, new { name = "Bestehend", stufen = Array.Empty<string>() })), "Stufen"));
        Assert.NotEmpty(Feld(await Abgelehnt(await oberflaeche.PutAsJsonAsync(weg, new { name = "", stufen = new[] { "Dokumentieren" } })), "Name"));
        Assert.NotEmpty(Feld(await Abgelehnt(await oberflaeche.PutAsJsonAsync(weg,
            new { name = new string('x', KiZugriffDienst.NameHoechstLaenge + 1), stufen = new[] { "Dokumentieren" } })), "Name"));

        // Nichts davon hat etwas verändert.
        var seite = (await oberflaeche.GetFromJsonAsync<KiZugriffSeiteDto>("/api/settings/ki-zugriff"))!;
        var schluessel = seite.Schluessel.Single(s => s.Id == id);
        Assert.Equal("Bestehend", schluessel.Name);
        Assert.Equal(new[] { "Dokumentieren" }, schluessel.Stufen);
    }

    [Theory]
    [InlineData(0.0, 20, "MaxDosisMlJeBefehl")]
    [InlineData(-1.0, 20, "MaxDosisMlJeBefehl")]
    [InlineData(1000.5, 20, "MaxDosisMlJeBefehl")]
    [InlineData(10.0, -1, "MaxSchaltbefehleJeStunde")]
    [InlineData(10.0, 1001, "MaxSchaltbefehleJeStunde")]
    public async Task HoechstwerteAusserhalbDerGrenzenGeben400(double dosis, int schalt, string feld)
    {
        var antwort = await _ki.Oberflaeche().PutAsJsonAsync("/api/settings/ki-zugriff", new
        {
            aktiv = true,
            rueckfrageAbStufe = "GrowPlanen",
            hoechstwerte = new { maxDosisMlJeBefehl = dosis, maxSchaltbefehleJeStunde = schalt },
        });
        await Erwarte(antwort, HttpStatusCode.BadRequest, "validation_failed");
        Assert.NotEmpty(Feld((await antwort.Content.ReadFromJsonAsync<ApiError>())!, "Hoechstwerte." + feld));
    }

    [Fact]
    public async Task HoechstwerteAnDenGrenzenGehen_UndSchaltbefehleSindGanzzahlig()
    {
        var oberflaeche = _ki.Oberflaeche();
        try
        {
            foreach (var (dosis, schalt) in new[] { (1000.0, 0), (0.1, 1000) })
            {
                await Erwarte(await oberflaeche.PutAsJsonAsync("/api/settings/ki-zugriff", new
                {
                    aktiv = true,
                    rueckfrageAbStufe = (string?)null,
                    hoechstwerte = new { maxDosisMlJeBefehl = dosis, maxSchaltbefehleJeStunde = schalt },
                }), HttpStatusCode.OK);
            }

            // Eine Kommazahl im Ganzzahlfeld scheitert schon beim Lesen — auch dann
            // ein Feldfehler mit dem Eigenschaftsnamen und deutschem Satz.
            var halb = await oberflaeche.PutAsync("/api/settings/ki-zugriff", new StringContent(
                "{\"aktiv\":true,\"hoechstwerte\":{\"maxDosisMlJeBefehl\":10,\"maxSchaltbefehleJeStunde\":2.5}}",
                Encoding.UTF8, "application/json"));
            await Erwarte(halb, HttpStatusCode.BadRequest, "validation_failed");
            var meldung = Feld((await halb.Content.ReadFromJsonAsync<ApiError>())!, "Hoechstwerte.MaxSchaltbefehleJeStunde").Single();
            Assert.Contains("ganze Zahl", meldung);
        }
        finally
        {
            await _ki.SchalterAsync(true);
        }
    }

    [Fact]
    public async Task EinGesperrterSchluesselBleibtGesperrt_EsGibtKeinEntsperren()
    {
        var oberflaeche = _ki.Oberflaeche();
        var (id, _) = await _ki.SchluesselAsync("Bleibt zu", "Dokumentieren");
        await Erwarte(await oberflaeche.PostAsync($"/api/settings/ki-zugriff/schluessel/{id}/sperren", null), HttpStatusCode.OK);

        // Kein Weg zum Entsperren …
        await Erwarte(await oberflaeche.PostAsync($"/api/settings/ki-zugriff/schluessel/{id}/entsperren", null), HttpStatusCode.NotFound);
        // … und Ändern öffnet ihn nicht nebenbei.
        var geaendert = await oberflaeche.PutAsJsonAsync($"/api/settings/ki-zugriff/schluessel/{id}",
            new { name = "Bleibt zu", stufen = new[] { "Dokumentieren", "GrowPlanen" } });
        await Erwarte(geaendert, HttpStatusCode.OK);
        Assert.NotNull((await geaendert.Content.ReadFromJsonAsync<KiSchluesselDto>())!.GesperrtAmUtc);

        // Löschen geht.
        await Erwarte(await oberflaeche.DeleteAsync($"/api/settings/ki-zugriff/schluessel/{id}"), HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SchluesselAendernUndLoeschen()
    {
        var oberflaeche = _ki.Oberflaeche();
        var (id, _) = await _ki.SchluesselAsync("Vorher", "Dokumentieren");

        var geaendert = await oberflaeche.PutAsJsonAsync($"/api/settings/ki-zugriff/schluessel/{id}",
            new { name = "Nachher", stufen = new[] { "GrowPlanen", "Verwaltung" } });
        await Erwarte(geaendert, HttpStatusCode.OK);
        var dto = (await geaendert.Content.ReadFromJsonAsync<KiSchluesselDto>())!;
        Assert.Equal("Nachher", dto.Name);
        Assert.Equal(new[] { "GrowPlanen", "Verwaltung" }, dto.Stufen);

        await Erwarte(await oberflaeche.DeleteAsync($"/api/settings/ki-zugriff/schluessel/{id}"), HttpStatusCode.NoContent);
        await Erwarte(await oberflaeche.DeleteAsync($"/api/settings/ki-zugriff/schluessel/{id}"), HttpStatusCode.NotFound, "ki_schluessel_nicht_gefunden");
        var seite = await oberflaeche.GetFromJsonAsync<KiZugriffSeiteDto>("/api/settings/ki-zugriff");
        Assert.DoesNotContain(seite!.Schluessel, s => s.Id == id);
    }
}
