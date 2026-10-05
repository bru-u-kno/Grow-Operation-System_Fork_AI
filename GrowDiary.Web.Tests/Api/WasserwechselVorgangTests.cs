using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Der Wasserwechsel als ein Vorgang (A-006): anlegen, alles-oder-nichts,
/// löschen mit allem, Altdaten.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Bru musste einen Wechsel an vier Stellen getrennt
/// erfassen, und beim Löschen der Messung blieben die Buchungen stehen. Die
/// Fälle hier halten fest, dass ein Speichern alles anlegt und verknüpft, und
/// dass Löschen am Vorgang wirklich alles mitnimmt — auch über den alten Weg
/// <c>DELETE /changeouts/{id}</c>.</para>
/// <para>Gegen die echte App (<see cref="IntegrationsApp"/>) — Model-Binding,
/// Routen und Transaktion gehören zu dem, was hier geprüft wird.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class WasserwechselVorgangTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly IntegrationsApp _app;

    public WasserwechselVorgangTests(IntegrationsApp app) => _app = app;

    private int EigenerGrow()
    {
        using var bereich = _app.Services.CreateScope();
        var grows = bereich.ServiceProvider.GetRequiredService<GrowRepository>();
        var vorlage = grows.GetActiveGrows().First();
        return grows.CreateGrow(new GrowRun
        {
            Name = "Vorgang " + Guid.NewGuid().ToString("N")[..6],
            TentId = vorlage.TentId,
            HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Completed,
            StartDate = DateTime.Today.AddDays(-60),
        });
    }

    private T Dienst<T>() where T : notnull => _app.Services.GetRequiredService<T>();

    private int ArtikelId(string name)
        => Dienst<KostenRepository>().GetArtikel().First(a => a.Name == name).Id;

    /// <summary>Ein vollständiger Vorgang wie aus dem Ablauf: Sensor vorher, Hand nachher, drei Buchungen, Tagebuch.</summary>
    private object VollerVorgang(string zeitpunkt, WaterSource wasser = WaterSource.Tap, bool erinnerung = true) => new
    {
        zeitpunktLokal = zeitpunkt,
        art = "Full",
        liter = 160.0,
        wasser = wasser.ToString(),
        osmoseProzent = wasser == WaterSource.Mixed ? 50.0 : (double?)null,
        wasserEcMsCm = 0.5,
        vorher = new { herkunft = "Sensor", sensorZeitUtc = DateTime.UtcNow.AddHours(-2), reservoirEc = 1.63, reservoirPh = 6.12, reservoirWaterTempC = 20.0, dissolvedOxygenMgL = 7.8 },
        nachher = new { herkunft = "Hand", reservoirEc = 1.15, reservoirPh = 6.15, reservoirWaterTempC = 18.1, orpMv = 450.0 },
        buchungen = new object[]
        {
            new { artikelId = ArtikelId("Aqua Flores A"), menge = 180.0 },
            new { artikelId = ArtikelId("Purolyt"), menge = 200.0 },
            new { wasser = wasser == WaterSource.Tap ? "Tap" : "RO", menge = 160.0 },
        },
        erinnerungNeuStarten = erinnerung,
        notiz = "Bewusst unter Plan.",
        tagebuch = new { titel = "Wasserwechsel 160 L", text = "EC 1,63 → 1,15" },
    };

    /// <summary>Der Stand, wie die Seite ihn liest — über die API, aus der einen Wahrheit.</summary>
    private static async Task<int?> TageSeit(HttpClient client, int growId)
        => (await client.GetFromJsonAsync<WasserwechselStand>($"/api/grows/{growId}/changeouts/stand", Json))!.TageSeit;

    private static string Zeit(DateTime wann) => wann.ToString("yyyy-MM-ddTHH:mm");

    private async Task<WasserwechselVorgangDto> Anlegen(HttpClient client, int growId, object body)
    {
        var antwort = await client.PostAsJsonAsync($"/api/grows/{growId}/wasserwechsel", body, Json);
        Assert.True(antwort.StatusCode == HttpStatusCode.Created, await antwort.Content.ReadAsStringAsync());
        return (await antwort.Content.ReadFromJsonAsync<WasserwechselVorgangDto>(Json))!;
    }

    [Fact]
    public async Task EinSpeichernLegtAllesAnUndVerknuepftEs()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var wann = DateTime.Now.AddHours(-2);

        var vorgang = await Anlegen(client, growId, VollerVorgang(Zeit(wann)));

        // Wechsel
        Assert.NotNull(vorgang.Wechsel);
        Assert.Equal(160, vorgang.Wechsel!.VolumeChangedLiters);
        Assert.Equal(ChangeoutKind.Full, vorgang.Wechsel.Kind);
        Assert.Equal(1.63, vorgang.Wechsel.EcBefore);
        Assert.Equal(1.15, vorgang.Wechsel.EcAfter);
        Assert.Equal(WaterSource.Tap, vorgang.Wechsel.WaterUsed);
        Assert.Equal(0.5, vorgang.Wechsel.WaterEcMsCm);

        // Messung vorher: vom Sensor, DO von Hand; nachher mit SolutionChange
        Assert.NotNull(vorgang.Vorher);
        Assert.Equal(ValueOrigin.HomeAssistant, vorgang.Vorher!.Source);
        Assert.Equal(7.8, vorgang.Vorher.DissolvedOxygenMgL);
        Assert.False(vorgang.Vorher.SolutionChange);
        Assert.NotNull(vorgang.Nachher);
        Assert.Equal(ValueOrigin.Manual, vorgang.Nachher!.Source);
        Assert.Equal(450, vorgang.Nachher.OrpMv);
        Assert.True(vorgang.Nachher.SolutionChange, "Ohne SolutionChange an der Messung „nachher“ läse ein alter Stand den Wechsel nicht.");
        Assert.True(vorgang.Vorher.TakenAt < vorgang.Nachher.TakenAt);

        // Buchungen: drei, alle am Vorgang und an der Messung „nachher"
        Assert.Equal(3, vorgang.Buchungen.Count);
        var verbraeuche = Dienst<KostenRepository>().GetVerbraeuche().Where(v => v.VorgangId == vorgang.Id).ToList();
        Assert.Equal(3, verbraeuche.Count);
        Assert.All(verbraeuche, v => Assert.Equal(vorgang.Nachher.Id, v.MessungId));
        Assert.All(verbraeuche, v => Assert.Equal(growId, v.GrowId));
        Assert.Contains(vorgang.Buchungen, b => b.ArtikelName == WasserwechselVorgangRepository.LeitungswasserArtikel && b.Menge == 160);

        // Tagebuch: Art Wasserwechsel, an der Messung „nachher"
        Assert.NotNull(vorgang.Tagebuch);
        Assert.Equal(JournalEntryType.ReservoirChange, vorgang.Tagebuch!.EntryType);
        Assert.Equal(vorgang.Nachher.Id, vorgang.Tagebuch.MeasurementId);

        // Und der Stand rechnet den Wechsel — die eine Wahrheit.
        Assert.Equal(0, await TageSeit(client, growId));

        // Wiederfinden über die Liste
        var liste = await client.GetFromJsonAsync<List<WasserwechselVorgangDto>>($"/api/grows/{growId}/wasserwechsel", Json);
        Assert.Contains(liste!, v => v.Id == vorgang.Id && v.Wechsel!.Id == vorgang.Wechsel.Id);
    }

    [Fact]
    public async Task OsmosewasserWirdBeimErstenMalAngelegtUndDanachWiederverwendet()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var kosten = Dienst<KostenRepository>();

        var erster = await Anlegen(client, growId, VollerVorgang(Zeit(DateTime.Now.AddHours(-3)), WaterSource.RO));
        var osmose = kosten.GetArtikel().Where(a => a.Name == WasserwechselVorgangRepository.OsmosewasserArtikel).ToList();
        Assert.Single(osmose);
        Assert.Equal("L", osmose[0].Einheit);

        // Zweites Speichern: derselbe Artikel, kein zweiter.
        var zweiter = await Anlegen(client, growId, VollerVorgang(Zeit(DateTime.Now.AddHours(-1)), WaterSource.RO));
        Assert.Single(kosten.GetArtikel(), a => a.Name == WasserwechselVorgangRepository.OsmosewasserArtikel);
        Assert.Contains(erster.Buchungen, b => b.ArtikelId == osmose[0].Id);
        Assert.Contains(zweiter.Buchungen, b => b.ArtikelId == osmose[0].Id);
    }

    /// <summary>Alles oder nichts: scheitert ein Teil, steht hinterher kein einziger Satz.</summary>
    [Fact]
    public void ScheitertEineBuchungBleibtNichtsStehen()
    {
        var growId = EigenerGrow();
        var repo = Dienst<WasserwechselVorgangRepository>();
        var grows = Dienst<GrowRepository>();
        var messungenVorher = grows.GetMeasurementsForGrow(growId).Count;

        // Ein Artikel, den es nicht gibt: die Datenbank lehnt die Buchung ab
        // (Fremdschlüssel) — NACH Wechsel und Messungen.
        Assert.ThrowsAny<Exception>(() => repo.Anlegen(new WasserwechselVorgangEntwurf
        {
            GrowId = growId,
            Wechsel = new ChangeoutEntry { GrowId = growId, Kind = ChangeoutKind.Full, VolumeChangedLiters = 100, PerformedAtUtc = DateTime.UtcNow.AddMinutes(-5) },
            Vorher = new Measurement { GrowId = growId, TakenAt = DateTime.Now.AddMinutes(-6), ReservoirEc = 1.6 },
            Nachher = new Measurement { GrowId = growId, TakenAt = DateTime.Now.AddMinutes(-5), ReservoirEc = 1.1, SolutionChange = true },
            Buchungen = [new VorgangBuchungEntwurf(987654, null, 10)],
            Tagebuch = new JournalEntry { GrowId = growId, Title = "Wasserwechsel", EntryType = JournalEntryType.ReservoirChange },
        }));

        Assert.Empty(grows.GetChangeoutsForGrow(growId));
        Assert.Equal(messungenVorher, grows.GetMeasurementsForGrow(growId).Count);
        Assert.Empty(repo.FuerGrow(growId));
        Assert.DoesNotContain(Dienst<JournalRepository>().GetForGrow(growId), j => j.EntryType == JournalEntryType.ReservoirChange);
    }

    [Fact]
    public async Task LoeschenNimmtWechselMessungenBuchungenUndTagebuchMit()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var kosten = Dienst<KostenRepository>();
        var grows = Dienst<GrowRepository>();

        var vorgang = await Anlegen(client, growId, VollerVorgang(Zeit(DateTime.Now.AddHours(-2))));

        // Fremde Sätze im selben Grow, die stehen bleiben müssen.
        var fremdeMessung = grows.CreateMeasurement(new Measurement { GrowId = growId, TakenAt = DateTime.Now.AddHours(-5), ReservoirEc = 1.4 });
        var fremdeBuchung = kosten.CreateVerbrauch(new Verbrauch { ArtikelId = ArtikelId("Purolyt"), GrowId = growId, MessungId = fremdeMessung, Menge = 5, ZeitpunktUtc = DateTime.UtcNow });
        Assert.Equal(3, kosten.GetVerbraeuche().Count(v => v.VorgangId == vorgang.Id));

        var weg = await client.DeleteAsync($"/api/grows/{growId}/wasserwechsel/{vorgang.Id}");
        Assert.Equal(HttpStatusCode.NoContent, weg.StatusCode);

        Assert.Empty(kosten.GetVerbraeuche().Where(v => v.VorgangId == vorgang.Id));
        Assert.Null(grows.GetMeasurement(vorgang.Vorher!.Id));
        Assert.Null(grows.GetMeasurement(vorgang.Nachher!.Id));
        Assert.DoesNotContain(grows.GetChangeoutsForGrow(growId), w => w.Id == vorgang.Wechsel!.Id);
        Assert.Null(Dienst<JournalRepository>().Get(vorgang.Tagebuch!.Id));
        Assert.Null(Dienst<WasserwechselVorgangRepository>().Get(growId, vorgang.Id));

        // Gegenprobe: das Fremde steht noch.
        Assert.NotNull(grows.GetMeasurement(fremdeMessung));
        Assert.Contains(kosten.GetVerbraeuche(), v => v.Id == fremdeBuchung);

        // Zweites Löschen: 404, nicht 204.
        var nochmal = await client.DeleteAsync($"/api/grows/{growId}/wasserwechsel/{vorgang.Id}");
        Assert.Equal(HttpStatusCode.NotFound, nochmal.StatusCode);
    }

    /// <summary>Der alte Löschweg am Wechsel nimmt den ganzen Vorgang — „Löschen dort löscht den Vorgang".</summary>
    [Fact]
    public async Task LoeschenUeberDenWechselNimmtDenVorgangMit()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var vorgang = await Anlegen(client, growId, VollerVorgang(Zeit(DateTime.Now.AddHours(-2))));

        var weg = await client.DeleteAsync($"/api/grows/{growId}/changeouts/{vorgang.Wechsel!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, weg.StatusCode);

        Assert.Empty(Dienst<KostenRepository>().GetVerbraeuche().Where(v => v.VorgangId == vorgang.Id));
        Assert.Null(Dienst<GrowRepository>().GetMeasurement(vorgang.Nachher!.Id));
        Assert.Null(Dienst<WasserwechselVorgangRepository>().Get(growId, vorgang.Id));
    }

    /// <summary>Altdaten: ein Wechsel ohne Vorgang bleibt lesbar, zählt weiter und lässt sich wie bisher löschen.</summary>
    [Fact]
    public async Task AltdatenOhneVorgangZaehlenWeiter()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var vor3Tagen = DateTime.UtcNow.AddDays(-3);

        var alt = await client.PostAsJsonAsync($"/api/grows/{growId}/changeouts", new { kind = "Full", performedAtUtc = vor3Tagen, ecAfter = 1.2 });
        Assert.True(alt.IsSuccessStatusCode, await alt.Content.ReadAsStringAsync());
        var altDto = (await alt.Content.ReadFromJsonAsync<ChangeoutDto>(Json))!;
        Assert.True(altDto.ErinnerungNeuStarten, "Ein Wechsel über den alten Weg muss weiter für die Erinnerung zählen.");

        Assert.Equal(3, await TageSeit(client, growId));
        Assert.Empty(await client.GetFromJsonAsync<List<WasserwechselVorgangDto>>($"/api/grows/{growId}/wasserwechsel", Json) ?? []);

        var weg = await client.DeleteAsync($"/api/grows/{growId}/changeouts/{altDto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, weg.StatusCode);
        Assert.Empty(Dienst<GrowRepository>().GetChangeoutsForGrow(growId));
    }

    /// <summary>
    /// „Erinnerung neu starten" aus: der Wechsel steht da, verschiebt den Stand aber nicht —
    /// weder über den Wechsel noch über die Messung „nachher".
    /// </summary>
    [Fact]
    public async Task OhneErinnerungVerschiebtDerWechselDenStandNicht()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();
        var alt = await client.PostAsJsonAsync($"/api/grows/{growId}/changeouts", new { kind = "Full", performedAtUtc = DateTime.UtcNow.AddDays(-4) });
        Assert.True(alt.IsSuccessStatusCode);

        var vorgang = await Anlegen(client, growId, VollerVorgang(Zeit(DateTime.Now.AddHours(-1)), erinnerung: false));
        Assert.False(vorgang.Wechsel!.ErinnerungNeuStarten);
        Assert.False(vorgang.Nachher!.SolutionChange);
        Assert.Equal(4, await TageSeit(client, growId));
    }

    [Fact]
    public async Task OhneLiterOderMitUnmoeglichemWertEntstehtNichts()
    {
        var growId = EigenerGrow();
        var client = _app.IngressClient();

        var ohneLiter = await client.PostAsJsonAsync($"/api/grows/{growId}/wasserwechsel", new { art = "Full" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, ohneLiter.StatusCode);
        Assert.Contains("\"liter\"", await ohneLiter.Content.ReadAsStringAsync());

        var zuHoch = await client.PostAsJsonAsync($"/api/grows/{growId}/wasserwechsel",
            new { liter = 100.0, nachher = new { reservoirEc = 1250.0 } }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, zuHoch.StatusCode);
        var text = await zuHoch.Content.ReadAsStringAsync();
        Assert.Contains("physikalisch", text);
        Assert.Contains("nachher.ReservoirEc", text, StringComparison.OrdinalIgnoreCase);

        var mischungOhneAnteil = await client.PostAsJsonAsync($"/api/grows/{growId}/wasserwechsel", new { liter = 100.0, wasser = "Mixed" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, mischungOhneAnteil.StatusCode);

        var zukunft = await client.PostAsJsonAsync($"/api/grows/{growId}/wasserwechsel",
            new { liter = 100.0, zeitpunktLokal = Zeit(DateTime.Now.AddDays(2)) }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, zukunft.StatusCode);

        Assert.Empty(Dienst<GrowRepository>().GetChangeoutsForGrow(growId));
        Assert.Empty(Dienst<WasserwechselVorgangRepository>().FuerGrow(growId));
    }

    // ---------------------------------------------------- Zählung der Messfelder

    /// <summary>Jedes Zahlenfeld der Vorgangs-Messung — Grundmenge per Reflexion.</summary>
    public static IEnumerable<object[]> VorgangsMessfelder()
        => typeof(VorgangMessungRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(double?))
            .Select(p => new object[] { p.Name });

    [Fact]
    public void DieZaehlungSiehtIhreFelder()
        => Assert.True(VorgangsMessfelder().Count() >= 5, "Die Vorgangs-Messung hat weniger Zahlenfelder als gedacht — die Zählung sähe nichts.");

    /// <summary>
    /// Jedes Zahlenfeld der Vorgangs-Messung trägt dieselbe Sperre wie die Messung.
    /// </summary>
    /// <remarks>
    /// Die Felder heißen wie an <see cref="Measurement"/>; wer eines ergänzt, das
    /// es dort nicht gibt, wird hier rot — dann hätte es keine Sperre.
    /// </remarks>
    [Theory]
    [MemberData(nameof(VorgangsMessfelder))]
    public async Task JedesMessfeldIstGesperrtWieInDerMessung(string feld)
    {
        Assert.NotNull(typeof(Measurement).GetProperty(feld));
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
        var antwort = await client.PostAsJsonAsync($"/api/grows/{growId}/wasserwechsel",
            new Dictionary<string, object> { ["liter"] = 50.0, ["vorher"] = new Dictionary<string, object> { [json] = unmoeglich } }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Contains($"vorher.{feld}", await antwort.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    // --------------------------------------------------------- Sensor „vorher"

    [Fact]
    public async Task SensorVorherNimmtDenJuengstenWertImFenster()
    {
        // Ein eigenes Zelt: die Testdaten-App schreibt in ihre Zelte laufend Sensorwerte.
        var grows = Dienst<GrowRepository>();
        var zelt = grows.CreateTent("Sensor-Vorher " + Guid.NewGuid().ToString("N")[..6]);
        var growId = grows.CreateGrow(new GrowRun
        {
            Name = "Sensor " + Guid.NewGuid().ToString("N")[..6], TentId = zelt.Id, HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Completed, StartDate = DateTime.Today.AddDays(-30),
        });
        var client = _app.IngressClient();
        var grow = grows.GetGrow(growId)!;
        var zeitpunkt = DateTime.UtcNow.AddMinutes(-3);

        using (var bereich = _app.Services.CreateScope())
        {
            var sensoren = bereich.ServiceProvider.GetRequiredService<SensorReadingRepository>();
            // Ein Wert NACH dem Zeitpunkt darf nicht zählen — dann zeigte „vorher" schon das neue Wasser.
            sensoren.AddReading(new TentSensorReading { TentId = grow.TentId!.Value, MetricKey = "reservoir-ec", Value = 1.15, CapturedAtUtc = zeitpunkt.AddMinutes(2) });
            sensoren.AddReading(new TentSensorReading { TentId = grow.TentId!.Value, MetricKey = "reservoir-ec", Value = 1.637, CapturedAtUtc = zeitpunkt.AddMinutes(-1) });
        }

        var dto = await client.GetFromJsonAsync<WasserwechselSensorDto>(
            $"/api/grows/{growId}/wasserwechsel/sensor?zeitpunkt={Uri.EscapeDataString(zeitpunkt.ToString("O"))}", Json);
        Assert.NotNull(dto!.Ec);
        Assert.Equal(1.637, dto.Ec!.Wert);
        Assert.Equal(WasserwechselApiController_Fenster, dto.FensterMinuten);
        Assert.Null(dto.Ph);

        // Gegenprobe: eine halbe Stunde früher gefragt, liegen beide Werte nach dem Zeitpunkt.
        var spaeter = await client.GetFromJsonAsync<WasserwechselSensorDto>(
            $"/api/grows/{growId}/wasserwechsel/sensor?zeitpunkt={Uri.EscapeDataString(zeitpunkt.AddMinutes(-32).ToString("O"))}", Json);
        Assert.Null(spaeter!.Ec);
        Assert.NotNull(spaeter.Hinweis);
    }

    private const int WasserwechselApiController_Fenster = GrowDiary.Web.Api.Controllers.WasserwechselApiController.SensorFensterMinuten;
}
