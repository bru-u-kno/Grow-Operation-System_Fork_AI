using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Das Nachfüllen als ein Vorgang (A-006, Etappe 3): anlegen, alles-oder-nichts,
/// löschen mit allem, Altdaten, keine Verwechslung mit dem Wasserwechsel.
/// </summary>
/// <remarks>
/// Gegen die echte App (<see cref="IntegrationsApp"/>) — Model-Binding, Routen
/// und Transaktion gehören zu dem, was hier geprüft wird. Dieselben Fälle wie
/// <see cref="WasserwechselVorgangTests"/>, weil es dasselbe Muster ist.
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class AddbackVorgangTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly IntegrationsApp _app;

    public AddbackVorgangTests(IntegrationsApp app) => _app = app;

    private T Dienst<T>() where T : notnull => _app.Services.GetRequiredService<T>();

    private int EigenerGrow()
    {
        var grows = Dienst<GrowRepository>();
        var vorlage = grows.GetActiveGrows().First();
        return grows.CreateGrow(new GrowRun
        {
            Name = "Nachfüllen " + Guid.NewGuid().ToString("N")[..6],
            TentId = vorlage.TentId,
            HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Completed,
            StartDate = DateTime.Today.AddDays(-60),
        });
    }

    private int ArtikelId(string name) => Dienst<KostenRepository>().GetArtikel().First(a => a.Name == name).Id;

    private static string Zeit(DateTime wann) => wann.ToString("yyyy-MM-ddTHH:mm");

    /// <summary>Ein Nachfüllen wie aus dem Ablauf: Sensor vorher, Hand nachher (mit DO/ORP), drei Buchungen, Tagebuch.</summary>
    private object VollesNachfuellen(string zeitpunkt, WaterSource wasser = WaterSource.Tap) => new
    {
        zeitpunktLokal = zeitpunkt,
        art = "Addback",
        liter = 20.0,
        wasser = wasser.ToString(),
        osmoseProzent = wasser == WaterSource.Mixed ? 50.0 : (double?)null,
        wasserEcMsCm = 0.5,
        ecZiel = 1.65,
        vorher = new { herkunft = "Sensor", sensorZeitUtc = DateTime.UtcNow.AddHours(-2), reservoirEc = 1.75, reservoirPh = 6.05, reservoirWaterTempC = 19.5, dissolvedOxygenMgL = 7.6 },
        nachher = new { herkunft = "Hand", reservoirEc = 1.61, reservoirPh = 6.0, orpMv = 430.0 },
        buchungen = new object[]
        {
            new { artikelId = ArtikelId("Aqua Flores A"), menge = 30.0 },
            new { artikelId = ArtikelId("Purolyt"), menge = 25.0 },
            new { wasser = wasser == WaterSource.Tap ? "Tap" : "RO", menge = 20.0 },
        },
        notiz = "Nachgefüllt nach dem Sprung.",
        tagebuch = new { titel = "Nachfüllen 20 L Leitungswasser", text = "EC 1,75 → 1,61" },
    };

    private async Task<AddbackVorgangDto> Anlegen(HttpClient client, int growId, object body)
    {
        var antwort = await client.PostAsJsonAsync($"/api/grows/{growId}/addback/vorgaenge", body, Json);
        Assert.True(antwort.StatusCode == HttpStatusCode.Created, await antwort.Content.ReadAsStringAsync());
        return (await antwort.Content.ReadFromJsonAsync<AddbackVorgangDto>(Json))!;
    }

    [Fact]
    public async Task EinSpeichernLegtAllesAnUndVerknuepftEs()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var standVorher = (await client.GetFromJsonAsync<WasserwechselStand>($"/api/grows/{growId}/changeouts/stand", Json))!.TageSeit;

        var vorgang = await Anlegen(client, growId, VollesNachfuellen(Zeit(DateTime.Now.AddHours(-2))));

        // Addback-Eintrag
        Assert.NotNull(vorgang.Eintrag);
        Assert.Equal(AddbackLogKind.Addback, vorgang.Eintrag!.Kind);
        Assert.Equal(20, vorgang.Eintrag.LitersAdded);
        Assert.Equal(1.75, vorgang.Eintrag.EcBefore);
        Assert.Equal(1.61, vorgang.Eintrag.EcAfter);
        Assert.Equal(6.05, vorgang.Eintrag.PhBefore);
        Assert.Equal(1.65, vorgang.Eintrag.EcTarget);
        Assert.Equal(WaterSource.Tap, vorgang.Eintrag.WaterUsed);
        Assert.Equal(0.5, vorgang.Eintrag.WaterEcMsCm);

        // Messungen: vorher vom Sensor mit DO von Hand, nachher von Hand — und
        // KEIN Lösungswechsel: Nachfüllen startet die Wechsel-Erinnerung nicht.
        Assert.Equal(ValueOrigin.HomeAssistant, vorgang.Vorher!.Source);
        Assert.Equal(7.6, vorgang.Vorher.DissolvedOxygenMgL);
        Assert.Equal(ValueOrigin.Manual, vorgang.Nachher!.Source);
        Assert.Equal(430, vorgang.Nachher.OrpMv);
        Assert.False(vorgang.Vorher.SolutionChange);
        Assert.False(vorgang.Nachher.SolutionChange);
        Assert.Equal("Sensor", vorgang.VorherHerkunft);

        // Buchungen: drei, am Nachfüll-Vorgang — nicht am Wasserwechsel-Verweis.
        Assert.Equal(3, vorgang.Buchungen.Count);
        var verbraeuche = Dienst<KostenRepository>().GetVerbraeuche().Where(v => v.AddbackVorgangId == vorgang.Id).ToList();
        Assert.Equal(3, verbraeuche.Count);
        Assert.All(verbraeuche, v => Assert.Null(v.VorgangId));
        Assert.All(verbraeuche, v => Assert.Equal(vorgang.Nachher.Id, v.MessungId));
        Assert.All(verbraeuche, v => Assert.Equal("addback", v.Quelle));
        Assert.Contains(vorgang.Buchungen, b => b.ArtikelName == WasserwechselVorgangRepository.LeitungswasserArtikel && b.Menge == 20);

        // Tagebuch: Art Fütterung, an der Messung „nachher"
        Assert.Equal(JournalEntryType.Feeding, vorgang.Tagebuch!.EntryType);
        Assert.Equal(vorgang.Nachher.Id, vorgang.Tagebuch.MeasurementId);

        // Der Eintrag steht auch dort, wo ihn die bisherigen Leser suchen.
        var logs = await client.GetFromJsonAsync<List<AddbackLogDto>>($"/api/grows/{growId}/addback/logs", Json);
        Assert.Contains(logs!, l => l.Id == vorgang.Eintrag.Id);

        // Wiederfinden über die Liste und einzeln
        var liste = await client.GetFromJsonAsync<List<AddbackVorgangDto>>($"/api/grows/{growId}/addback/vorgaenge", Json);
        Assert.Contains(liste!, v => v.Id == vorgang.Id && v.Eintrag!.Id == vorgang.Eintrag.Id);
        var einzeln = await client.GetFromJsonAsync<AddbackVorgangDto>($"/api/grows/{growId}/addback/vorgaenge/{vorgang.Id}", Json);
        Assert.Equal(3, einzeln!.Buchungen.Count);

        // Der Wasserwechsel-Stand bleibt, wo er war.
        Assert.Equal(standVorher, (await client.GetFromJsonAsync<WasserwechselStand>($"/api/grows/{growId}/changeouts/stand", Json))!.TageSeit);
    }

    /// <summary>„Speichern, dann nochmal speichern": zwei Vorgänge, je mit eigenen Teilen.</summary>
    [Fact]
    public async Task ZweimalSpeichernGibtZweiGetrennteVorgaenge()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var erster = await Anlegen(client, growId, VollesNachfuellen(Zeit(DateTime.Now.AddHours(-3))));
        var zweiter = await Anlegen(client, growId, VollesNachfuellen(Zeit(DateTime.Now.AddHours(-1)), WaterSource.RO));

        Assert.NotEqual(erster.Id, zweiter.Id);
        Assert.NotEqual(erster.Eintrag!.Id, zweiter.Eintrag!.Id);
        Assert.NotEqual(erster.Nachher!.Id, zweiter.Nachher!.Id);
        Assert.Equal(3, Dienst<KostenRepository>().GetVerbraeuche().Count(v => v.AddbackVorgangId == zweiter.Id));
        Assert.Contains(zweiter.Buchungen, b => b.ArtikelName == WasserwechselVorgangRepository.OsmosewasserArtikel);
        // Osmosewasser gibt es genau einmal — egal, ob Wechsel oder Nachfüllen es zuerst brauchte.
        Assert.Single(Dienst<KostenRepository>().GetArtikel(), a => a.Name == WasserwechselVorgangRepository.OsmosewasserArtikel);

        var liste = await client.GetFromJsonAsync<List<AddbackVorgangDto>>($"/api/grows/{growId}/addback/vorgaenge", Json);
        Assert.Equal([zweiter.Id, erster.Id], liste!.Select(v => v.Id).ToList());
    }

    /// <summary>Alles oder nichts: scheitert ein Teil, steht hinterher kein einziger Satz.</summary>
    [Fact]
    public void ScheitertEineBuchungBleibtNichtsStehen()
    {
        var growId = EigenerGrow();
        var repo = Dienst<AddbackVorgangRepository>();
        var grows = Dienst<GrowRepository>();
        var messungenVorher = grows.GetMeasurementsForGrow(growId).Count;

        Assert.ThrowsAny<Exception>(() => repo.Anlegen(new AddbackVorgangEntwurf
        {
            GrowId = growId,
            Eintrag = new AddbackLogEntry { GrowId = growId, Kind = AddbackLogKind.TopOff, LitersAdded = 15, PerformedAtUtc = DateTime.UtcNow.AddMinutes(-5) },
            Vorher = new Measurement { GrowId = growId, TakenAt = DateTime.Now.AddMinutes(-6), ReservoirEc = 1.7 },
            Nachher = new Measurement { GrowId = growId, TakenAt = DateTime.Now.AddMinutes(-5), ReservoirEc = 1.6 },
            // Ein Artikel, den es nicht gibt: die Datenbank lehnt die Buchung ab (Fremdschlüssel).
            Buchungen = [new VorgangBuchungEntwurf(987654, null, 10)],
            Tagebuch = new JournalEntry { GrowId = growId, Title = "Nachfüllen", EntryType = JournalEntryType.Feeding },
        }));

        Assert.Empty(grows.GetAddbackLogsForGrow(growId));
        Assert.Equal(messungenVorher, grows.GetMeasurementsForGrow(growId).Count);
        Assert.Empty(repo.FuerGrow(growId));
        Assert.DoesNotContain(Dienst<JournalRepository>().GetForGrow(growId), j => j.EntryType == JournalEntryType.Feeding);
    }

    [Fact]
    public async Task LoeschenNimmtEintragMessungenBuchungenUndTagebuchMit()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var kosten = Dienst<KostenRepository>();
        var grows = Dienst<GrowRepository>();

        var vorgang = await Anlegen(client, growId, VollesNachfuellen(Zeit(DateTime.Now.AddHours(-2))));

        // Fremde Sätze im selben Grow, die stehen bleiben müssen.
        var fremdeMessung = grows.CreateMeasurement(new Measurement { GrowId = growId, TakenAt = DateTime.Now.AddHours(-5), ReservoirEc = 1.4 });
        var fremdeBuchung = kosten.CreateVerbrauch(new Verbrauch { ArtikelId = ArtikelId("Purolyt"), GrowId = growId, MessungId = fremdeMessung, Menge = 5, ZeitpunktUtc = DateTime.UtcNow });
        var altEintrag = await client.PostAsJsonAsync($"/api/grows/{growId}/addback/logs", new { kind = "TopOff", litersAdded = 10.0 }, Json);
        Assert.True(altEintrag.IsSuccessStatusCode, await altEintrag.Content.ReadAsStringAsync());
        var altId = (await altEintrag.Content.ReadFromJsonAsync<AddbackLogDto>(Json))!.Id;

        var weg = await client.DeleteAsync($"/api/grows/{growId}/addback/vorgaenge/{vorgang.Id}");
        Assert.Equal(HttpStatusCode.NoContent, weg.StatusCode);

        Assert.Empty(kosten.GetVerbraeuche().Where(v => v.AddbackVorgangId == vorgang.Id));
        Assert.Null(grows.GetMeasurement(vorgang.Vorher!.Id));
        Assert.Null(grows.GetMeasurement(vorgang.Nachher!.Id));
        Assert.DoesNotContain(grows.GetAddbackLogsForGrow(growId), e => e.Id == vorgang.Eintrag!.Id);
        Assert.Null(Dienst<JournalRepository>().Get(vorgang.Tagebuch!.Id));
        Assert.Null(Dienst<AddbackVorgangRepository>().Get(growId, vorgang.Id));

        // Gegenprobe: das Fremde steht noch — auch der Altdaten-Eintrag.
        Assert.NotNull(grows.GetMeasurement(fremdeMessung));
        Assert.Contains(kosten.GetVerbraeuche(), v => v.Id == fremdeBuchung);
        Assert.Contains(grows.GetAddbackLogsForGrow(growId), e => e.Id == altId);

        // Zweites Löschen: 404, nicht 204.
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/grows/{growId}/addback/vorgaenge/{vorgang.Id}")).StatusCode);
    }

    /// <summary>
    /// Im Grow-Tagebuch steht das Nachfüllen als EIN Ereignis: Messungen, Buchungen
    /// und Tagebuchzeile hängen am Eintrag, nicht noch einmal als eigene Zeilen.
    /// </summary>
    [Fact]
    public async Task DasTagebuchBuendeltDenVorgang()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var vorgang = await Anlegen(client, growId, VollesNachfuellen(Zeit(DateTime.Now.AddHours(-2))));

        var seite = await client.GetFromJsonAsync<TagebuchSeiteDto>($"/api/grows/{growId}/tagebuch", Json);
        var ereignisse = seite!.Tage.SelectMany(t => t.Ereignisse).ToList();
        Assert.True(ereignisse.Count >= 1, "Das Tagebuch zeigt nichts — der Fall sähe nichts.");

        var nachfuellen = Assert.Single(ereignisse, e => e.Art == "addback");
        Assert.Equal(vorgang.Id, nachfuellen.Addback!.VorgangId);
        Assert.Equal(3, nachfuellen.Posten.Count);
        Assert.Equal(1.75, nachfuellen.Addback.Vorher!.Ec);
        Assert.Equal(7.6, nachfuellen.Addback.Vorher.SauerstoffMgL);
        Assert.Equal(430, nachfuellen.Addback.Nachher!.OrpMv);
        Assert.Equal(vorgang.Tagebuch!.Id, nachfuellen.Addback.Journal!.Id);

        // Nicht doppelt: keine eigene Messzeile, keine eigene Notiz, keine Verbrauchszeile.
        Assert.DoesNotContain(ereignisse, e => e.Schluessel == $"messung-{vorgang.Vorher!.Id}" || e.Schluessel == $"messung-{vorgang.Nachher!.Id}");
        Assert.DoesNotContain(ereignisse, e => e.Schluessel == $"notiz-{vorgang.Tagebuch.Id}");
        Assert.DoesNotContain(ereignisse, e => e.Art == "verbrauch");
    }

    /// <summary>
    /// Der Löschweg am Eintrag (<c>DELETE /addback/logs/{id}</c>, vom Tagebuch benutzt)
    /// nimmt den ganzen Vorgang — ein Weg, nicht zwei.
    /// </summary>
    [Fact]
    public async Task LoeschenUeberDenEintragNimmtDenVorgangMit()
    {
        var growId = EigenerGrow();
        var fremderGrow = EigenerGrow();
        var client = _app.IngressClient();
        var vorgang = await Anlegen(client, growId, VollesNachfuellen(Zeit(DateTime.Now.AddHours(-2))));

        // Über einen fremden Grow: nichts.
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/grows/{fremderGrow}/addback/logs/{vorgang.Eintrag!.Id}")).StatusCode);
        Assert.NotNull(Dienst<AddbackVorgangRepository>().Get(growId, vorgang.Id));

        var weg = await client.DeleteAsync($"/api/grows/{growId}/addback/logs/{vorgang.Eintrag.Id}");
        Assert.Equal(HttpStatusCode.NoContent, weg.StatusCode);

        Assert.Empty(Dienst<KostenRepository>().GetVerbraeuche().Where(v => v.AddbackVorgangId == vorgang.Id));
        Assert.Null(Dienst<GrowRepository>().GetMeasurement(vorgang.Nachher!.Id));
        Assert.Null(Dienst<JournalRepository>().Get(vorgang.Tagebuch!.Id));
        Assert.Null(Dienst<AddbackVorgangRepository>().Get(growId, vorgang.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/grows/{growId}/addback/logs/{vorgang.Eintrag.Id}")).StatusCode);
    }

    /// <summary>
    /// Die Buchungen von Wechsel und Nachfüllen hängen an getrennten Verweisen.
    /// </summary>
    /// <remarks>
    /// Die Kennungen der beiden Vorgang-Tabellen zählen unabhängig. Über EINEN
    /// Verweis nähme das Löschen des Nachfüllens 7 die Buchungen des Wechsels 7
    /// mit. Der Fall stellt genau diese Gleichheit her und prüft beide Richtungen.
    /// </remarks>
    [Fact]
    public async Task WechselUndNachfuellenMitGleicherKennungBleibenGetrennt()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var kosten = Dienst<KostenRepository>();

        // Bis beide Tabellen dieselbe nächste Kennung haben, je einen Vorgang
        // anlegen und wieder löschen (höchstens ein paar Dutzend Schritte).
        WasserwechselVorgangDto? wechsel = null;
        AddbackVorgangDto? nachfuellen = null;
        for (var i = 0; i < 200 && (wechsel is null || nachfuellen is null || wechsel.Id != nachfuellen.Id); i++)
        {
            if (wechsel is null || (nachfuellen is not null && wechsel.Id < nachfuellen.Id))
            {
                if (wechsel is not null) await client.DeleteAsync($"/api/grows/{growId}/wasserwechsel/{wechsel.Id}");
                var antwort = await client.PostAsJsonAsync($"/api/grows/{growId}/wasserwechsel", new
                {
                    zeitpunktLokal = Zeit(DateTime.Now.AddHours(-4)), art = "Full", liter = 100.0,
                    buchungen = new object[] { new { wasser = "Tap", menge = 100.0 } },
                }, Json);
                Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());
                wechsel = await antwort.Content.ReadFromJsonAsync<WasserwechselVorgangDto>(Json);
            }
            else
            {
                if (nachfuellen is not null) await client.DeleteAsync($"/api/grows/{growId}/addback/vorgaenge/{nachfuellen.Id}");
                nachfuellen = await Anlegen(client, growId, VollesNachfuellen(Zeit(DateTime.Now.AddHours(-2))));
            }
        }

        Assert.Equal(wechsel!.Id, nachfuellen!.Id);
        Assert.Single(kosten.GetVerbraeuche(), v => v.VorgangId == wechsel.Id);
        Assert.Equal(3, kosten.GetVerbraeuche().Count(v => v.AddbackVorgangId == nachfuellen.Id));

        // Nachfüllen löschen: die Wechsel-Buchung bleibt.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/grows/{growId}/addback/vorgaenge/{nachfuellen.Id}")).StatusCode);
        Assert.Single(kosten.GetVerbraeuche(), v => v.VorgangId == wechsel.Id);

        // Und die Antworten zeigen je nur ihre eigenen.
        var wechselJetzt = await client.GetFromJsonAsync<WasserwechselVorgangDto>($"/api/grows/{growId}/wasserwechsel/{wechsel.Id}", Json);
        Assert.Single(wechselJetzt!.Buchungen);
    }

    [Fact]
    public async Task OhneLiterOderMitUnmoeglichemWertEntstehtNichts()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var pfad = $"/api/grows/{growId}/addback/vorgaenge";

        var ohneLiter = await client.PostAsJsonAsync(pfad, new { art = "Addback" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, ohneLiter.StatusCode);
        Assert.Contains("\"liter\"", await ohneLiter.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var zuHoch = await client.PostAsJsonAsync(pfad, new { liter = 20.0, nachher = new { reservoirEc = 1250.0 } }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, zuHoch.StatusCode);
        var text = await zuHoch.Content.ReadAsStringAsync();
        Assert.Contains("physikalisch", text);
        Assert.Contains("nachher.ReservoirEc", text, StringComparison.OrdinalIgnoreCase);

        var zielZuHoch = await client.PostAsJsonAsync(pfad, new { liter = 20.0, ecZiel = 1650.0 }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, zielZuHoch.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(pfad, new { liter = 20.0, wasser = "Mixed" }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(pfad, new { liter = 20.0, zeitpunktLokal = Zeit(DateTime.Now.AddDays(2)) }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(pfad,
            new { liter = 20.0, buchungen = new object[] { new { artikelId = 987654, menge = 5.0 } } }, Json)).StatusCode);

        Assert.Empty(Dienst<GrowRepository>().GetAddbackLogsForGrow(growId));
        Assert.Empty(Dienst<AddbackVorgangRepository>().FuerGrow(growId));
    }

    /// <summary>Löschen und Lesen gehen nie über die Grenze des Grows.</summary>
    [Fact]
    public async Task EinFremderGrowLoeschtNichts()
    {
        var eigenerGrow = EigenerGrow();
        var fremderGrow = EigenerGrow();
        var client = _app.IngressClient();
        var vorgang = await Anlegen(client, eigenerGrow, VollesNachfuellen(Zeit(DateTime.Now.AddHours(-2))));

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/grows/{fremderGrow}/addback/vorgaenge/{vorgang.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/grows/{fremderGrow}/addback/vorgaenge/{vorgang.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/grows/987654/addback/vorgaenge/{vorgang.Id}")).StatusCode);

        Assert.NotNull(Dienst<AddbackVorgangRepository>().Get(eigenerGrow, vorgang.Id));
        Assert.NotNull(Dienst<GrowRepository>().GetMeasurement(vorgang.Nachher!.Id));
        Assert.Equal(3, Dienst<KostenRepository>().GetVerbraeuche().Count(v => v.AddbackVorgangId == vorgang.Id));

        // Auch das Repository selbst schützt — nicht nur der Controller.
        Assert.False(Dienst<AddbackVorgangRepository>().Loeschen(fremderGrow, vorgang.Id));
        Assert.Contains(Dienst<GrowRepository>().GetAddbackLogsForGrow(eigenerGrow), e => e.Id == vorgang.Eintrag!.Id);
    }

    /// <summary>Altdaten: ein Addback-Eintrag ohne Vorgang bleibt lesbar und zählt weiter.</summary>
    [Fact]
    public async Task AltdatenOhneVorgangBleibenLesbar()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var alt = await client.PostAsJsonAsync($"/api/grows/{growId}/addback/logs",
            new { kind = "Addback", performedAtUtc = DateTime.UtcNow.AddDays(-2), ecBefore = 1.3, ecAfter = 1.5, litersAdded = 12.5 }, Json);
        Assert.True(alt.IsSuccessStatusCode, await alt.Content.ReadAsStringAsync());

        var logs = await client.GetFromJsonAsync<List<AddbackLogDto>>($"/api/grows/{growId}/addback/logs", Json);
        Assert.Single(logs!);
        Assert.Equal(12.5, logs![0].LitersAdded);
        Assert.Empty(await client.GetFromJsonAsync<List<AddbackVorgangDto>>($"/api/grows/{growId}/addback/vorgaenge", Json) ?? []);
        Assert.Null(Dienst<AddbackVorgangRepository>().ZumEintrag(growId, logs[0].Id));

        // Und lässt sich wie jeder Eintrag entfernen.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/grows/{growId}/addback/logs/{logs[0].Id}")).StatusCode);
        Assert.Empty(Dienst<GrowRepository>().GetAddbackLogsForGrow(growId));
    }

    /// <summary>
    /// Der Vorschlag rechnet auf die nachgefüllten Liter und die Wasserart —
    /// derselbe Endpunkt wie beim Wechsel, nur mit der kleinen Literzahl.
    /// </summary>
    [Fact]
    public async Task DerVorschlagRechnetAufDieNachgefuelltenLiterUndDieWasserart()
    {
        var grows = Dienst<GrowRepository>();
        var growId = grows.CreateGrow(new GrowRun
        {
            Name = "Vorschlag " + Guid.NewGuid().ToString("N")[..6],
            TentId = grows.GetActiveGrows().First().TentId,
            HydroStyle = HydroStyle.RDWC,
            FeedProgramId = "skx-canna-aqua",
            Status = GrowStatus.Running,
            StartDate = DateTime.Today.AddDays(-30),
        });
        var client = _app.IngressClient();
        var store = Dienst<WaterProfileStore>();
        var profil = store.Get();
        try
        {
            store.Save(new WaterProfile { SourceLabel = "Test", ConductivityUsCm = 600, CalciumMgL = 80, TotalHardnessDh = 14 });
            var zwanzig = await client.GetFromJsonAsync<MischplanVorschlag>($"/api/grows/{growId}/mixing-plan/vorschlag?liter=20&wasser=Tap", Json);
            var vierzig = await client.GetFromJsonAsync<MischplanVorschlag>($"/api/grows/{growId}/mixing-plan/vorschlag?liter=40&wasser=Tap", Json);
            var osmose = await client.GetFromJsonAsync<MischplanVorschlag>($"/api/grows/{growId}/mixing-plan/vorschlag?liter=20&wasser=RO", Json);

            Assert.Null(zwanzig!.Luecke);
            var grund = zwanzig.Zeilen.Where(z => z.Rolle == MischplanRolle.Grundduenger).ToList();
            Assert.True(grund.Count >= 2, "Der Testbestand hat keinen Plan mit Grunddüngern — der Fall sähe nichts.");
            foreach (var zeile in grund)
            {
                Assert.Equal(Math.Round(zeile.MlProLiter * 20, 0, MidpointRounding.AwayFromZero), zeile.VorschlagMl);
                var doppelt = vierzig!.Zeilen.Single(z => z.Komponente == zeile.Komponente);
                Assert.Equal(Math.Round(zeile.MlProLiter * 40, 0, MidpointRounding.AwayFromZero), doppelt.VorschlagMl);
            }

            // Wasserart: Leitung bringt ihren EC mit, Osmose nicht.
            Assert.Equal(0.6, zwanzig.WasserEc);
            Assert.Equal(0, osmose!.WasserEc);
        }
        finally
        {
            if (profil is not null) store.Save(profil);
        }
    }

    /// <summary>Jedes Zahlenfeld der Vorgangs-Messung trägt dieselbe Sperre wie die Messung — auch beim Nachfüllen.</summary>
    [Theory]
    [MemberData(nameof(WasserwechselVorgangTests.VorgangsMessfelder), MemberType = typeof(WasserwechselVorgangTests))]
    public async Task JedesMessfeldIstGesperrtWieInDerMessung(string feld)
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var unmoeglich = feld switch
        {
            nameof(Measurement.ReservoirPh) => 99,
            nameof(Measurement.DissolvedOxygenMgL) => 900,
            nameof(Measurement.OrpMv) => 99999,
            nameof(Measurement.ReservoirWaterTempC) => 9000,
            _ => -99999,
        };
        var json = JsonNamingPolicy.CamelCase.ConvertName(feld);
        var antwort = await client.PostAsJsonAsync($"/api/grows/{growId}/addback/vorgaenge",
            new Dictionary<string, object> { ["liter"] = 20.0, ["nachher"] = new Dictionary<string, object> { [json] = unmoeglich } }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Contains($"nachher.{feld}", await antwort.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
