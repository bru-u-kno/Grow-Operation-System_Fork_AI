using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Live-Kachel „Strom": Leistung der Steckdose, darunter heute verbraucht und was es kostet.
/// </summary>
/// <remarks>
/// Zusagen: die Kachel gibt es nur mit Leistungs-Wert (kein leerer Platzhalter), sie hat kein Ziel
/// (Strom wird nicht geregelt), die Einheit ist immer Watt — auch wenn der Sensor kW meldet —, und
/// die Fußzeile sagt nur, was sie weiß: ohne Verbrauch nichts, ohne Preis nur die kWh.
/// </remarks>
public sealed class StromKachelTests : IDisposable
{
    private readonly string _wurzel = Path.Combine(Path.GetTempPath(), "StromKachel_" + Guid.NewGuid().ToString("N"));
    private readonly AppSettingsRepository _einstellungen;
    private readonly AppPaths _pfade;
    private static readonly HomeAssistantSettings Ha = new() { BaseUrl = "http://ha.local:8123", AccessToken = "t", Enabled = true };

    public StromKachelTests()
    {
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(_pfade);
        _einstellungen = new AppSettingsRepository(_pfade);
    }

    /// <summary>Home Assistant als Attrappe: Zustände je Entität (Zustand, Einheit) und ein Verlauf je Entität.</summary>
    private static (StromKachel Kachel, RecordingHttpHandler Handler) Kachel(
        AppSettingsRepository einstellungen,
        Dictionary<string, (string Zustand, string? Einheit)> zustaende,
        Dictionary<string, string[]>? verlauf = null)
    {
        var handler = new RecordingHttpHandler((anfrage, _) =>
        {
            var pfad = anfrage.RequestUri!.AbsolutePath;
            if (pfad.Contains("/api/states/", StringComparison.Ordinal))
            {
                var id = pfad.Split('/').Last();
                return zustaende.TryGetValue(id, out var z)
                    ? RecordingHttpHandler.Json(RecordingHttpHandler.EntityStateJson(id, z.Zustand, z.Einheit))
                    : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            }
            if (pfad.Contains("/api/history/period/", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(anfrage.RequestUri.Query.Split("filter_entity_id=")[1].Split('&')[0]);
                var punkte = verlauf is not null && verlauf.TryGetValue(id, out var v)
                    ? string.Join(',', v.Select((w, i) => $"{{\"state\":\"{w}\",\"last_changed\":\"2026-10-10T0{i}:00:00+00:00\"}}"))
                    : "";
                return RecordingHttpHandler.Json($"[[{punkte}]]");
            }
            return RecordingHttpHandler.Json("[]");
        });
        var ha = new HomeAssistantService(new StubHttpClientFactory(handler), NullLogger<HomeAssistantService>.Instance);
        return (new StromKachel(einstellungen, ha), handler);
    }

    private static string Eindeutig(string name) => $"{name}_{Guid.NewGuid():N}";

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch (IOException) { /* Aufräumen ist kein Testfall */ }
    }

    private void LeistungEintragen()
        => KostenSeiteService.StromQuelleSchreiben(_einstellungen, new StromQuelle
        {
            ZaehlerEntityId = "sensor.zaehler", LeistungEntityId = "sensor.leistung",
        });

    private static DashboardLayout EigenesLayout() => new()
    {
        TentId = 1,
        Version = DashboardLayout.CurrentVersion,
        Sections =
        [
            new DashboardSection
            {
                Id = "klima", Title = "Klima",
                Tiles =
                [
                    new DashboardTile { MetricKey = "temperature" },
                    new DashboardTile { MetricKey = "co2" },
                    new DashboardTile { Kind = DashboardTileKind.Chart, MetricKeys = ["temperature"] },
                ],
            },
            new DashboardSection { Id = "wasser", Title = "Wasser", Tiles = [new DashboardTile { MetricKey = "reservoir-ph" }] },
        ],
    };

    private static readonly GrowDashboardComposer Composer = new(null!, null!);

    private static Tent Zelt() => new() { Id = 1, Name = "Zelt 1", ActiveGrows = new() };

    private static HomeAssistantState Leistung(double wert, string einheit = "W")
        => new()
        {
            State = wert.ToString(CultureInfo.InvariantCulture), NumericValue = wert,
            UnitOfMeasurement = einheit, FriendlyName = "Keller DECT Steckdose Leistung",
        };

    [Fact]
    public void Mit_Leistung_steht_die_Kachel_Strom_in_Watt_ohne_Ziel_da()
    {
        var zustaende = new Dictionary<string, HomeAssistantState> { [StromKachel.Key] = Leistung(1092.68) };

        var karten = Composer.BuildTentMetrics(Zelt(), zustaende, new List<Measurement>());

        var karte = Assert.Single(karten, k => k.Key == "power");
        Assert.Equal("Strom", karte.Label);
        Assert.Equal("W", karte.Unit);
        Assert.Equal("1093", karte.Value);
        Assert.Equal("live", karte.ValueSource);
        Assert.Null(karte.TargetMin);
        Assert.Null(karte.TargetMax);
    }

    [Fact]
    public void Ohne_Leistung_gibt_es_keine_Kachel()
    {
        var karten = Composer.BuildTentMetrics(Zelt(), new Dictionary<string, HomeAssistantState>(), new List<Measurement>());

        Assert.DoesNotContain(karten, k => k.Key == "power");
    }

    [Fact]
    public void Der_Rohwert_der_Leistung_geht_in_die_Messreihe()
    {
        // Das ist der Weg zum Verlauf: dieselbe Rohwerte-Liste wie bei pH und EC.
        var zustaende = new Dictionary<string, HomeAssistantState> { [StromKachel.Key] = Leistung(640, "W") };

        var rohwerte = HomeAssistantSnapshotWorker.Rohwerte(1, zustaende, DateTime.UtcNow);

        var punkt = Assert.Single(rohwerte);
        Assert.Equal("power", punkt.MetricKey);
        Assert.Equal(640, punkt.Value);
        Assert.Equal("W", punkt.Unit);
    }

    [Fact]
    public void Kilowatt_werden_zu_Watt_umgerechnet()
    {
        var umgerechnet = StromKachel.InWatt(Leistung(1.09, "kW"));

        Assert.Equal("W", umgerechnet!.UnitOfMeasurement);
        Assert.Equal(1090, umgerechnet.NumericValue!.Value, 6);
        Assert.Equal("Keller DECT Steckdose Leistung", umgerechnet.FriendlyName);
    }

    [Fact]
    public void Watt_bleiben_unberuehrt()
    {
        var original = Leistung(1093);

        Assert.Same(original, StromKachel.InWatt(original));
        Assert.Null(StromKachel.InWatt(null));
    }

    [Theory]
    [InlineData(12.4, 29.0, "heute 12,4 kWh · ca. 3,60 €")]
    [InlineData(0.0, 29.0, "heute 0,0 kWh · ca. 0,00 €")]
    [InlineData(12.4, null, "heute 12,4 kWh")]
    public void Die_Fusszeile_nennt_Verbrauch_und_Kosten_auf_Deutsch(double kwh, double? cent, string erwartet)
        => Assert.Equal(erwartet, StromKachel.Fusszeile(kwh, cent));

    [Fact]
    public void Ohne_Verbrauch_bleibt_die_Fusszeile_leer()
        => Assert.Null(StromKachel.Fusszeile(null, 29.0));

    [Fact]
    public void Name_und_Einheit_im_Verlauf_kommen_aus_derselben_Tabelle()
    {
        Assert.Equal(("Strom", " W"), AlertEvaluationService.MetricDisplay("power"));
        Assert.Equal("W", Demoverlauf.Einheit("power"));
        Assert.Contains("power", Demoverlauf.Schluessel);
    }

    // ------------------------------------------- einmalig ins eigene Layout

    [Fact]
    public void Das_eigene_Layout_bekommt_die_Kachel_einmal_hinter_die_letzte_Messwert_Kachel_im_Klima_Bereich()
    {
        LeistungEintragen();
        var layout = EigenesLayout();

        Assert.True(StromKachel.Anbieten(layout, _einstellungen));

        var klima = layout.Sections[0].Tiles;
        Assert.Equal(["temperature", "co2", "power", null], klima.Select(t => t.MetricKey));
        Assert.Equal(DashboardTileKind.Chart, klima[3].Kind);   // der Verlauf bleibt der Schluss des Bereichs
        Assert.DoesNotContain(layout.Sections[1].Tiles, t => t.MetricKey == "power");
    }

    [Fact]
    public void Ein_zweites_Mal_und_nach_dem_Entfernen_kommt_sie_nicht_wieder()
    {
        LeistungEintragen();
        var layout = EigenesLayout();
        Assert.True(StromKachel.Anbieten(layout, _einstellungen));

        layout.Sections[0].Tiles.RemoveAll(t => t.MetricKey == "power");   // der Nutzer will sie nicht

        Assert.False(StromKachel.Anbieten(layout, _einstellungen));
        Assert.DoesNotContain(layout.Sections[0].Tiles, t => t.MetricKey == "power");
    }

    [Fact]
    public void Ohne_eingetragene_Leistung_passiert_nichts_und_nichts_wird_gemerkt()
    {
        var layout = EigenesLayout();

        Assert.False(StromKachel.Anbieten(layout, _einstellungen));
        Assert.DoesNotContain(layout.Sections[0].Tiles, t => t.MetricKey == "power");

        // Sobald die Quelle da ist, kommt die Kachel nach.
        LeistungEintragen();
        Assert.True(StromKachel.Anbieten(layout, _einstellungen));
    }

    [Fact]
    public void Eine_vorhandene_Strom_Kachel_wird_nicht_verdoppelt()
    {
        LeistungEintragen();
        var layout = EigenesLayout();
        layout.Sections[1].Tiles.Add(new DashboardTile { MetricKey = "power" });

        Assert.False(StromKachel.Anbieten(layout, _einstellungen));
        Assert.Single(layout.Sections.SelectMany(s => s.Tiles), t => t.MetricKey == "power");
    }

    [Fact]
    public void Der_Merker_gilt_je_Zelt()
    {
        LeistungEintragen();
        Assert.True(StromKachel.Anbieten(EigenesLayout(), _einstellungen));

        var anderes = EigenesLayout();
        anderes.TentId = 2;
        Assert.True(StromKachel.Anbieten(anderes, _einstellungen));
    }

    // ------------------------------------------- Zelte mit eigenem Zähler

    [Fact]
    public void Ein_Zelt_mit_eigenem_Zaehler_bekommt_keine_Strom_Kachel_angeboten()
    {
        KostenSeiteService.StromQuelleSchreiben(_einstellungen, new StromQuelle
        {
            ZaehlerEntityId = "sensor.zaehler", LeistungEntityId = "sensor.leistung",
            Zelte = [new ZeltZaehler { TentId = 1, ZaehlerEntityId = "sensor.zaehler_zelt1" }],
        });

        var eigener = EigenesLayout();                 // Zelt 1: eigener Zähler
        var gemeinsam = EigenesLayout(); gemeinsam.TentId = 2;

        Assert.False(StromKachel.Anbieten(eigener, _einstellungen));
        Assert.DoesNotContain(eigener.Sections.SelectMany(s => s.Tiles), t => t.MetricKey == "power");
        Assert.True(StromKachel.Anbieten(gemeinsam, _einstellungen));
    }

    [Fact]
    public async Task Ein_Zelt_mit_eigenem_Zaehler_bekommt_keine_Leistung_in_die_Zustaende()
    {
        var leistung = Eindeutig("sensor.leistung");
        KostenSeiteService.StromQuelleSchreiben(_einstellungen, new StromQuelle
        {
            ZaehlerEntityId = "sensor.zaehler", LeistungEntityId = leistung,
            Zelte = [new ZeltZaehler { TentId = 1, ZaehlerEntityId = "sensor.zaehler_zelt1" }],
        });
        var (kachel, handler) = Kachel(_einstellungen, new() { [leistung] = ("640", "W") });

        var eigener = new Dictionary<string, HomeAssistantState>();
        var gemeinsam = new Dictionary<string, HomeAssistantState>();
        await kachel.ErgaenzenAsync(1, eigener, Ha, CancellationToken.None);
        await kachel.ErgaenzenAsync(2, gemeinsam, Ha, CancellationToken.None);

        Assert.Empty(eigener);
        Assert.Equal(640, gemeinsam["power"].NumericValue);
        Assert.Single(handler.Requests);               // für das Zelt mit eigenem Zähler wurde gar nicht gefragt
    }

    // ------------------------------------------- Leistung aus Home Assistant

    [Fact]
    public async Task Die_Leistung_kommt_in_Watt_in_die_Zustaende_auch_wenn_der_Sensor_kW_meldet()
    {
        var leistung = Eindeutig("sensor.leistung");
        KostenSeiteService.StromQuelleSchreiben(_einstellungen, new StromQuelle { LeistungEntityId = leistung });
        var (kachel, _) = Kachel(_einstellungen, new() { [leistung] = ("1.09", "kW") });

        var zustaende = new Dictionary<string, HomeAssistantState>();
        await kachel.ErgaenzenAsync(1, zustaende, Ha, CancellationToken.None);

        Assert.Equal(1090, zustaende["power"].NumericValue!.Value, 6);
        Assert.Equal("W", zustaende["power"].UnitOfMeasurement);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unknown")]
    public async Task Ohne_Zahl_aus_Home_Assistant_gibt_es_keinen_Zustand_und_damit_keine_Kachel(string zustand)
    {
        var leistung = Eindeutig("sensor.leistung");
        KostenSeiteService.StromQuelleSchreiben(_einstellungen, new StromQuelle { LeistungEntityId = leistung });
        var (kachel, _) = Kachel(_einstellungen, new() { [leistung] = (zustand, "W") });

        var zustaende = new Dictionary<string, HomeAssistantState>();
        await kachel.ErgaenzenAsync(1, zustaende, Ha, CancellationToken.None);

        Assert.Empty(zustaende);
        Assert.DoesNotContain(
            Composer.BuildTentMetrics(Zelt(), zustaende, new List<Measurement>()), k => k.Key == "power");
    }

    [Fact]
    public async Task Ohne_eingetragene_Leistung_wird_Home_Assistant_nicht_gefragt()
    {
        var (kachel, handler) = Kachel(_einstellungen, new());

        await kachel.ErgaenzenAsync(1, new Dictionary<string, HomeAssistantState>(), Ha, CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Ein_vorhandener_Zustand_wird_nicht_ueberschrieben()
    {
        var leistung = Eindeutig("sensor.leistung");
        KostenSeiteService.StromQuelleSchreiben(_einstellungen, new StromQuelle { LeistungEntityId = leistung });
        var (kachel, handler) = Kachel(_einstellungen, new() { [leistung] = ("999", "W") });
        var vorhanden = Leistung(100);
        var zustaende = new Dictionary<string, HomeAssistantState> { ["power"] = vorhanden };

        await kachel.ErgaenzenAsync(1, zustaende, Ha, CancellationToken.None);

        Assert.Same(vorhanden, zustaende["power"]);
        Assert.Empty(handler.Requests);
    }

    // ------------------------------------------- heute verbraucht

    [Fact]
    public async Task Unter_der_Kachel_steht_der_Verbrauch_seit_Mitternacht_und_was_er_kostet_und_wird_zwischengespeichert()
    {
        var zaehler = Eindeutig("sensor.zaehler");
        KostenSeiteService.StromQuelleSchreiben(_einstellungen, new StromQuelle
        {
            ZaehlerEntityId = zaehler, LeistungEntityId = "sensor.leistung",
        });
        GrowCostService.StrompreisSchreiben(_einstellungen, 29);
        var (kachel, handler) = Kachel(_einstellungen, new(), new() { [zaehler] = ["100.0", "105.1", "112.4"] });

        var karten = new List<MetricCard> { new() { Key = "power", Label = "Strom", Hint = "Keller DECT Steckdose Leistung" } };
        await kachel.KartenErgaenzenAsync(karten, Zelt(), Ha, CancellationToken.None);
        await kachel.KartenErgaenzenAsync(karten, Zelt(), Ha, CancellationToken.None);

        Assert.Equal("heute 12,4 kWh · ca. 3,60 €", karten[0].Hint);
        Assert.Single(handler.Requests, r => r.Uri.AbsolutePath.Contains("/api/history/period/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ohne_Verlauf_des_Zaehlers_bleibt_die_Beschriftung_des_Sensors_stehen()
    {
        var zaehler = Eindeutig("sensor.zaehler");
        KostenSeiteService.StromQuelleSchreiben(_einstellungen, new StromQuelle
        {
            ZaehlerEntityId = zaehler, LeistungEntityId = "sensor.leistung",
        });
        var (kachel, _) = Kachel(_einstellungen, new());   // kein Verlauf bekannt

        var karten = new List<MetricCard> { new() { Key = "power", Label = "Strom", Hint = "Keller DECT Steckdose Leistung" } };
        await kachel.KartenErgaenzenAsync(karten, Zelt(), Ha, CancellationToken.None);

        Assert.Equal("Keller DECT Steckdose Leistung", karten[0].Hint);
    }

    // ------------------------------------------- Einrichtung im Erfassungstakt

    [Fact]
    public void Der_Takt_legt_die_Kachel_in_jedes_eigene_Layout_einmal_und_laesst_das_Standard_Layout_in_Ruhe()
    {
        var grows = new GrowRepository(_pfade);
        var layouts = new DashboardLayoutRepository(_pfade);
        var eigenes = grows.CreateTent(new Tent { Name = "Mit Layout" }).Id;
        var standard = grows.CreateTent(new Tent { Name = "Standard" }).Id;
        var layout = EigenesLayout();
        layout.TentId = eigenes;
        layouts.Save(layout);
        LeistungEintragen();

        Assert.Equal(1, StromKachel.AnbietenFuerAlleZelte(grows, layouts, _einstellungen));
        Assert.Contains(layouts.GetSaved(eigenes)!.Sections.SelectMany(s => s.Tiles), t => t.MetricKey == "power");
        Assert.Null(layouts.GetSaved(standard));         // nichts gespeichert, nichts angelegt

        Assert.Equal(0, StromKachel.AnbietenFuerAlleZelte(grows, layouts, _einstellungen));   // zweiter Takt ändert nichts
        Assert.Single(layouts.GetSaved(eigenes)!.Sections.SelectMany(s => s.Tiles), t => t.MetricKey == "power");
    }
}
