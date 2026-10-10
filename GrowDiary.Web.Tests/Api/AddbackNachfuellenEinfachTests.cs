using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Das vereinfachte Addback: „nur Zusätze" ohne Liter, Verbrauch in Litern, Füllstand danach,
/// die automatische Nachmessung aus den Sensoren und ihre Vorgabe.
/// </summary>
/// <remarks>Gegen die echte App wie <see cref="AddbackVorgangTests"/> — Model-Binding und Routen gehören dazu.</remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class AddbackNachfuellenEinfachTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly IntegrationsApp _app;

    public AddbackNachfuellenEinfachTests(IntegrationsApp app) => _app = app;

    private T Dienst<T>() where T : notnull => _app.Services.GetRequiredService<T>();

    private (int GrowId, int ZeltId) EigenerGrow()
    {
        var grows = Dienst<GrowRepository>();
        var vorlage = grows.GetActiveGrows().First();
        var id = grows.CreateGrow(new GrowRun
        {
            Name = "Einfach " + Guid.NewGuid().ToString("N")[..6],
            TentId = vorlage.TentId,
            HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Completed,
            StartDate = DateTime.Today.AddDays(-60),
        });
        return (id, vorlage.TentId!.Value);
    }

    private int ArtikelId(string name) => Dienst<KostenRepository>().GetArtikel().First(a => a.Name == name).Id;
    private static string Zeit(DateTime wann) => wann.ToString("yyyy-MM-ddTHH:mm");

    private async Task<HttpResponseMessage> Senden(int growId, object body)
        => await _app.IngressClient().PostAsJsonAsync($"/api/grows/{growId}/addback/vorgaenge", body, Json);

    private async Task<AddbackVorgangDto> Anlegen(int growId, object body)
    {
        var antwort = await Senden(growId, body);
        Assert.True(antwort.StatusCode == HttpStatusCode.Created, await antwort.Content.ReadAsStringAsync());
        return (await antwort.Content.ReadFromJsonAsync<AddbackVorgangDto>(Json))!;
    }

    /// <summary>Ein Nur-Wasser-Eintrag wie aus der neuen Seite.</summary>
    private object NurWasser(DateTime wann, object? extra = null) => new
    {
        zeitpunktLokal = Zeit(wann),
        art = "TopOff",
        liter = 18.0,
        wasser = "Tap",
        wasserEcMsCm = 0.28,
        vorher = new { herkunft = "Sensor", sensorZeitUtc = wann.ToUniversalTime(), reservoirEc = 1.58, reservoirPh = 5.9 },
        buchungen = new object[] { new { artikelId = ArtikelId("Leitungswasser"), menge = 18.0 } },
        tagebuch = new { titel = "Nachfüllen 18 L Leitungswasser", text = "EC 1,58 → —" },
        extra,
    };

    // ---- „Nur Zusätze" ohne Wasser

    [Fact]
    public async Task NurZusaetzeBrauchenKeineLiter()
    {
        var (growId, _) = EigenerGrow();
        var vorgang = await Anlegen(growId, new
        {
            zeitpunktLokal = Zeit(DateTime.Now.AddMinutes(-20)),
            art = "Correction",
            wasser = "Tap",
            buchungen = new object[] { new { artikelId = ArtikelId("Purolyt"), menge = 12.0 } },
            tagebuch = new { titel = "Korrektur", text = "Zugaben: Purolyt 12 ml" },
        });

        Assert.Equal(AddbackLogKind.Correction, vorgang.Eintrag!.Kind);
        Assert.Null(vorgang.Eintrag.LitersAdded);
        // Ohne Wasser steht auch keine Wasserquelle am Eintrag.
        Assert.Null(vorgang.Eintrag.WaterUsed);
        Assert.Single(vorgang.Buchungen);
    }

    [Theory]
    [InlineData("TopOff")]
    [InlineData("Addback")]
    public async Task JedeAndereArtBrauchtLiter(string art)
    {
        var (growId, _) = EigenerGrow();
        var antwort = await Senden(growId, new { art, wasser = "Tap", buchungen = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Contains("Liter", await antwort.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NegativeLiterAuchBeiKorrekturNicht()
    {
        var (growId, _) = EigenerGrow();
        var antwort = await Senden(growId, new { art = "Correction", liter = -1.0, wasser = "Tap", buchungen = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    // ---- Verbrauch und Füllstand

    [Fact]
    public async Task VerbrauchInLiternUndFuellstandDanachWerdenGespeichertUndGelesen()
    {
        var (growId, _) = EigenerGrow();
        var vorgang = await Anlegen(growId, new
        {
            zeitpunktLokal = Zeit(DateTime.Now.AddMinutes(-30)),
            art = "TopOff", liter = 18.0, wasser = "Tap", verbrauchLiter = 14.5, fuellstandDanachLiter = 118.0,
            buchungen = Array.Empty<object>(),
        });

        Assert.Equal(14.5, vorgang.Eintrag!.ConsumedLiters);
        Assert.Equal(118.0, vorgang.Eintrag.NewReservoirVolumeLiters);

        // Wiederfinden über die Liste der Einträge — dort lesen die übrigen Seiten.
        var logs = await _app.IngressClient().GetFromJsonAsync<List<AddbackLogDto>>($"/api/grows/{growId}/addback/logs", Json);
        Assert.Equal(14.5, logs!.Single(l => l.Id == vorgang.Eintrag.Id).ConsumedLiters);
    }

    [Theory]
    [InlineData("verbrauchLiter", -1.0)]
    [InlineData("verbrauchLiter", 100001.0)]
    [InlineData("fuellstandDanachLiter", 0.0)]
    [InlineData("fuellstandDanachLiter", -5.0)]
    public async Task UnsinnigeMengenWerdenAbgelehnt(string feld, double wert)
    {
        var (growId, _) = EigenerGrow();
        var body = new Dictionary<string, object?> { ["art"] = "TopOff", ["liter"] = 10.0, ["wasser"] = "Tap", [feld] = wert };
        var antwort = await Senden(growId, body);
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    // ---- Die automatische Nachmessung

    [Fact]
    public async Task NachmessungMinutenPlantEinenAuftragZurRichtigenZeit()
    {
        var (growId, _) = EigenerGrow();
        var wann = DateTime.Now.AddMinutes(-5);
        var vorgang = await Anlegen(growId, new
        {
            zeitpunktLokal = Zeit(wann), art = "TopOff", liter = 18.0, wasser = "Tap", nachmessungMinuten = 15,
            buchungen = Array.Empty<object>(),
        });

        Assert.Equal(AddbackNachmessung.Offen, vorgang.NachmessungStatus);
        // Zeit(…) schneidet die Sekunden ab, deshalb auf die Minute genau.
        var erwartet = DateTime.Parse(Zeit(wann)).ToUniversalTime().AddMinutes(15);
        Assert.Equal(erwartet, vorgang.NachmessungFaelligUtc);
    }

    [Fact]
    public async Task EineMessungNachherVonHandHatVorrangVorDerAutomatik()
    {
        var (growId, _) = EigenerGrow();
        var vorgang = await Anlegen(growId, new
        {
            zeitpunktLokal = Zeit(DateTime.Now.AddMinutes(-5)), art = "TopOff", liter = 18.0, wasser = "Tap", nachmessungMinuten = 15,
            nachher = new { herkunft = "Hand", reservoirEc = 1.35, reservoirPh = 5.9 },
            buchungen = Array.Empty<object>(),
        });

        Assert.Null(vorgang.NachmessungStatus);
        Assert.Equal(1.35, vorgang.Nachher!.ReservoirEc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public async Task NachmessungNurNachEinerBisZweihundertvierzigMinuten(int minuten)
    {
        var (growId, _) = EigenerGrow();
        var antwort = await Senden(growId, new { art = "TopOff", liter = 10.0, wasser = "Tap", nachmessungMinuten = minuten });
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }

    [Fact]
    public async Task DieNachmessungTraegtDieSensorwerteZurFaelligkeitEin()
    {
        var (growId, zeltId) = EigenerGrow();
        var start = DateTime.UtcNow.AddMinutes(-40);
        var vorgang = await Anlegen(growId, new
        {
            zeitpunktLokal = Zeit(start.ToLocalTime()), art = "TopOff", liter = 18.0, wasser = "Tap", nachmessungMinuten = 15,
            vorher = new { herkunft = "Hand", reservoirEc = 1.58, reservoirPh = 5.9 },
            buchungen = Array.Empty<object>(),
            tagebuch = new { titel = "Nachfüllen 18 L Leitungswasser", text = "EC 1,58 → —" },
        });
        var faellig = vorgang.NachmessungFaelligUtc!.Value;

        using var scope = _app.Services.CreateScope();
        var rohwerte = scope.ServiceProvider.GetRequiredService<SensorReadingRepository>();
        // Ein Wert VOR der Fälligkeit zählt, einer danach nicht — gelesen wird zur Fälligkeit.
        rohwerte.AddReading(new TentSensorReading { TentId = zeltId, MetricKey = "reservoir-ec", Value = 1.36, Unit = "mS/cm", CapturedAtUtc = faellig.AddMinutes(-3) });
        rohwerte.AddReading(new TentSensorReading { TentId = zeltId, MetricKey = "reservoir-ph", Value = 5.8, CapturedAtUtc = faellig.AddMinutes(-3) });
        rohwerte.AddReading(new TentSensorReading { TentId = zeltId, MetricKey = "reservoir-ec", Value = 9.99, Unit = "mS/cm", CapturedAtUtc = faellig.AddMinutes(2) });

        var geschlossen = scope.ServiceProvider.GetRequiredService<AddbackNachmessungService>().FaelligeAbarbeiten(DateTime.UtcNow);
        Assert.True(geschlossen >= 1);

        var danach = await _app.IngressClient().GetFromJsonAsync<AddbackVorgangDto>($"/api/grows/{growId}/addback/vorgaenge/{vorgang.Id}", Json);
        Assert.Equal(AddbackNachmessung.Erledigt, danach!.NachmessungStatus);
        Assert.Equal(1.36, danach.Nachher!.ReservoirEc);
        Assert.Equal(5.8, danach.Nachher.ReservoirPh);
        Assert.Equal(ValueOrigin.HomeAssistant, danach.Nachher.Source);
        Assert.False(danach.Nachher.SolutionChange);
        // Der Addback-Eintrag und die Tagebuchzeile ziehen nach.
        Assert.Equal(1.36, danach.Eintrag!.EcAfter);
        Assert.Contains("Nachmessung (automatisch", danach.Tagebuch!.Body);
        Assert.Contains("EC 1,36", danach.Tagebuch.Body);
        Assert.Equal(danach.Nachher.Id, danach.Tagebuch.MeasurementId);

        // Ein zweiter Takt tut nichts mehr — keine zweite Messung.
        var messungen = Dienst<GrowRepository>().GetMeasurementsForGrow(growId).Count;
        scope.ServiceProvider.GetRequiredService<AddbackNachmessungService>().FaelligeAbarbeiten(DateTime.UtcNow);
        Assert.Equal(messungen, Dienst<GrowRepository>().GetMeasurementsForGrow(growId).Count);
    }

    [Fact]
    public async Task OhneSensorwertBleibtDerAuftragNochOffenUndGibtDannAuf()
    {
        var (growId, _) = EigenerGrow();
        var vorgang = await Anlegen(growId, new
        {
            zeitpunktLokal = Zeit(DateTime.Now.AddMinutes(-60)), art = "TopOff", liter = 18.0, wasser = "Tap", nachmessungMinuten = 5,
            buchungen = Array.Empty<object>(),
        });
        var faellig = vorgang.NachmessungFaelligUtc!.Value;

        using var scope = _app.Services.CreateScope();
        var dienst = scope.ServiceProvider.GetRequiredService<AddbackNachmessungService>();
        var repo = Dienst<AddbackVorgangRepository>();
        var auftrag = repo.NachmessungZu(vorgang.Id)!;

        // Gerade fällig, keine Rohwerte: noch warten (nachgelieferte Werte).
        Assert.False(dienst.Ausfuehren(auftrag, faellig.AddMinutes(1)));
        Assert.Equal(AddbackNachmessung.Offen, repo.NachmessungZu(vorgang.Id)!.Status);

        // Nach der Nachfrist: aufgeben, ohne eine Phantom-Messung zu schreiben.
        var messungen = Dienst<GrowRepository>().GetMeasurementsForGrow(growId).Count;
        Assert.True(dienst.Ausfuehren(auftrag, faellig.AddMinutes(AddbackNachmessungService.NachfristMinuten + 1)));
        var danach = repo.NachmessungZu(vorgang.Id)!;
        Assert.Equal(AddbackNachmessung.OhneWert, danach.Status);
        Assert.False(string.IsNullOrWhiteSpace(danach.Hinweis));
        Assert.Equal(messungen, Dienst<GrowRepository>().GetMeasurementsForGrow(growId).Count);
    }

    [Fact]
    public async Task UnplausibleSensorwerteSchreibenKeineMessung()
    {
        var (growId, zeltId) = EigenerGrow();
        var vorgang = await Anlegen(growId, new
        {
            zeitpunktLokal = Zeit(DateTime.Now.AddMinutes(-60)), art = "TopOff", liter = 18.0, wasser = "Tap", nachmessungMinuten = 5,
            buchungen = Array.Empty<object>(),
        });
        var faellig = vorgang.NachmessungFaelligUtc!.Value;

        using var scope = _app.Services.CreateScope();
        // pH 25 gibt es nicht — ein gestörter Sensor soll nicht in den Verlauf.
        scope.ServiceProvider.GetRequiredService<SensorReadingRepository>()
            .AddReading(new TentSensorReading { TentId = zeltId, MetricKey = "reservoir-ph", Value = 25, CapturedAtUtc = faellig.AddMinutes(-2) });

        var messungen = Dienst<GrowRepository>().GetMeasurementsForGrow(growId).Count;
        scope.ServiceProvider.GetRequiredService<AddbackNachmessungService>().FaelligeAbarbeiten(DateTime.UtcNow);

        var auftrag = Dienst<AddbackVorgangRepository>().NachmessungZu(vorgang.Id)!;
        Assert.Equal(AddbackNachmessung.OhneWert, auftrag.Status);
        Assert.Contains("nicht plausibel", auftrag.Hinweis);
        Assert.Equal(messungen, Dienst<GrowRepository>().GetMeasurementsForGrow(growId).Count);
    }

    [Fact]
    public async Task LoeschenDesVorgangsRaeumtDenAuftragAb()
    {
        var (growId, _) = EigenerGrow();
        var vorgang = await Anlegen(growId, new
        {
            zeitpunktLokal = Zeit(DateTime.Now.AddMinutes(-5)), art = "TopOff", liter = 18.0, wasser = "Tap", nachmessungMinuten = 30,
            buchungen = Array.Empty<object>(),
        });
        Assert.NotNull(Dienst<AddbackVorgangRepository>().NachmessungZu(vorgang.Id));

        var antwort = await _app.IngressClient().DeleteAsync($"/api/grows/{growId}/addback/vorgaenge/{vorgang.Id}");
        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);

        Assert.Null(Dienst<AddbackVorgangRepository>().NachmessungZu(vorgang.Id));
        Assert.DoesNotContain(Dienst<AddbackVorgangRepository>().FaelligeNachmessungen(DateTime.UtcNow.AddYears(1)), a => a.VorgangId == vorgang.Id);
    }

    // ---- Die Vorgabe

    [Fact]
    public async Task DieVorgabeStartetAnNachFuenfzehnMinutenUndMerktSich()
    {
        var client = _app.IngressClient();
        // Ausgangslage unabhängig von anderen Fällen: erst eine bekannte Vorgabe setzen und zurücklesen.
        var erst = await client.PutAsJsonAsync("/api/addback/einstellungen", new AddbackEinstellungenDto(true, AddbackEinstellungenApiController.StandardMinuten), Json);
        Assert.True(erst.IsSuccessStatusCode);

        var gesetzt = await client.PutAsJsonAsync("/api/addback/einstellungen", new AddbackEinstellungenDto(false, 30), Json);
        Assert.Equal(HttpStatusCode.OK, gesetzt.StatusCode);
        var gelesen = await client.GetFromJsonAsync<AddbackEinstellungenDto>("/api/addback/einstellungen", Json);
        Assert.Equal(new AddbackEinstellungenDto(false, 30), gelesen);

        // Zurück auf den Standard, damit andere Fälle nichts davon sehen.
        await client.PutAsJsonAsync("/api/addback/einstellungen", new AddbackEinstellungenDto(true, AddbackEinstellungenApiController.StandardMinuten), Json);
        Assert.Equal(15, AddbackEinstellungenApiController.StandardMinuten);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public async Task DieVorgabeLehntUnsinnigeMinutenAb(int minuten)
    {
        var antwort = await _app.IngressClient().PutAsJsonAsync("/api/addback/einstellungen", new AddbackEinstellungenDto(true, minuten), Json);
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    }
}
