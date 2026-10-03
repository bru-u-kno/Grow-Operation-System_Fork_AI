using System.Net;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Ein Nullbild der Wassersonde (pH 0 und EC 0 im selben Augenblick) wird zur
/// Lücke, nicht zu 0 — beim Lesen und in den schon gespeicherten Rohwerten.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (03.10.2026, Anlage des Nutzers).</b> Im 7-Tage-Verlauf
/// standen pH, EC und Wassertemperatur an derselben Stelle auf 0. Home
/// Assistant hatte für alle drei Entitäten der Bluelab-Sonde in derselben
/// Millisekunde den Zustand „0" aufgezeichnet — nicht <c>unavailable</c>.
/// Die Zustände hier sind deshalb genau die Antworten, die Home Assistant in
/// dieser Nacht gegeben hat, und laufen durch den echten
/// <see cref="HomeAssistantService"/> und die echte Auswahl des Snapshot-Takts.</para>
/// </remarks>
public sealed class WassersondenNullbildTests : IDisposable
{
    private readonly string _wurzel;

    public WassersondenNullbildTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Nullbild_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { }
    }

    private static readonly Tent Zelt = new()
    {
        Id = 1,
        Sensors =
        [
            new TentSensor { TentId = 1, MetricType = SensorMetricType.ReservoirPh, HaEntityId = "sensor.bluelab_guardian_ph", IsActive = true },
            new TentSensor { TentId = 1, MetricType = SensorMetricType.ReservoirEc, HaEntityId = "sensor.bluelab_guardian_electrical_conductivity", IsActive = true },
            new TentSensor { TentId = 1, MetricType = SensorMetricType.ReservoirWaterTemp, HaEntityId = "sensor.bluelab_guardian_temperature", IsActive = true },
            new TentSensor { TentId = 1, MetricType = SensorMetricType.AirTemperature, HaEntityId = "sensor.zelt_temperatur", IsActive = true },
            new TentSensor { TentId = 1, MetricType = SensorMetricType.Ppfd, HaEntityId = "sensor.zelt_ppfd", IsActive = true },
        ]
    };

    /// <summary>Liest die Zustände durch den echten Dienst und gibt zurück, was der Snapshot-Takt schreiben würde.</summary>
    private static async Task<(Dictionary<string, HomeAssistantState> Zustaende, IReadOnlyList<TentSensorReading> Rohwerte)> Runde(
        string ph, string ec, string wasser, string luft = "24.1", string ppfd = "0")
    {
        var antworten = new Dictionary<string, string>
        {
            ["sensor.bluelab_guardian_ph"] = ph,
            ["sensor.bluelab_guardian_electrical_conductivity"] = ec,
            ["sensor.bluelab_guardian_temperature"] = wasser,
            ["sensor.zelt_temperatur"] = luft,
            ["sensor.zelt_ppfd"] = ppfd,
        };
        var http = new RecordingHttpHandler((anfrage, _) =>
        {
            var entitaet = anfrage.RequestUri!.AbsolutePath.Split('/').Last();
            return antworten.TryGetValue(entitaet, out var zustand)
                ? RecordingHttpHandler.Json(RecordingHttpHandler.EntityStateJson(entitaet, zustand))
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var dienst = new HomeAssistantService(new StubHttpClientFactory(http), NullLogger<HomeAssistantService>.Instance);
        var zustaende = await dienst.GetStatesAsync(
            new HomeAssistantSettings { BaseUrl = "http://homeassistant.local:8123", AccessToken = "t", Enabled = true }, Zelt);
        return (zustaende, HomeAssistantSnapshotWorker.Rohwerte(Zelt.Id, zustaende, DateTime.UtcNow));
    }

    [Fact]
    public async Task Nullbild_SchreibtKeinenRohwert_FuerPhEcUndWassertemperatur()
    {
        var (zustaende, rohwerte) = await Runde(ph: "0", ec: "0", wasser: "0");

        var geschrieben = rohwerte.Select(r => r.MetricKey).ToHashSet();
        // Selbsttest: die Runde lief und hat geschrieben — die Lufttemperatur
        // (anderer Sensor) und PPFD 0 (nachts ein echter Wert) sind dabei.
        Assert.Contains("temperature", geschrieben);
        Assert.Contains("ppfd", geschrieben);
        Assert.Equal(0, rohwerte.Single(r => r.MetricKey == "ppfd").Value);

        Assert.DoesNotContain("reservoir-ph", geschrieben);
        Assert.DoesNotContain("reservoir-ec", geschrieben);
        Assert.DoesNotContain("reservoir-temp", geschrieben);

        // Auch für Kacheln, Alarme und Dosierung: kein Wert, Zustand „nicht verfügbar".
        Assert.Null(zustaende["reservoir-ph"].NumericValue);
        Assert.Equal("unavailable", zustaende["reservoir-ph"].State);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task NichtVerfuegbar_SchreibtKeinenRohwert(string zustand)
    {
        var (_, rohwerte) = await Runde(ph: zustand, ec: zustand, wasser: zustand);

        var geschrieben = rohwerte.Select(r => r.MetricKey).ToHashSet();
        Assert.Contains("temperature", geschrieben);
        Assert.DoesNotContain("reservoir-ph", geschrieben);
        Assert.DoesNotContain("reservoir-ec", geschrieben);
        Assert.DoesNotContain("reservoir-temp", geschrieben);
    }

    [Theory]
    // EC 0 ist ein echter Wert (Osmosewasser, trockene Sonde) — ohne pH 0 kein Widerspruch.
    [InlineData("6.2", "0", "0")]
    // pH 0 allein: kein Gegenbeweis aus derselben Runde, bleibt stehen.
    [InlineData("0", "1.72", "17.8")]
    public async Task EinzelneNull_IstKeinNullbild(string ph, string ec, string wasser)
    {
        var (_, rohwerte) = await Runde(ph, ec, wasser);

        var geschrieben = rohwerte.ToDictionary(r => r.MetricKey, r => r.Value);
        Assert.Equal(double.Parse(ph, System.Globalization.CultureInfo.InvariantCulture), geschrieben["reservoir-ph"]);
        Assert.Equal(double.Parse(ec, System.Globalization.CultureInfo.InvariantCulture), geschrieben["reservoir-ec"]);
        Assert.Equal(double.Parse(wasser, System.Globalization.CultureInfo.InvariantCulture), geschrieben["reservoir-temp"]);
    }

    [Fact]
    public void GespeicherteNullbilder_WerdenEntfernt_UndDerTageswertNeuBerechnet()
    {
        var pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(pfade);
        var repo = new SensorReadingRepository(pfade);

        var heute = DateOnly.FromDateTime(DateTime.Now);
        var tag = heute.AddDays(-2);
        var halberVortag = heute.AddDays(-3).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Local).ToUniversalTime();
        var tagesbeginn = tag.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var nullbildTag = tagesbeginn.AddHours(23).AddMinutes(15);
        var nullbildVortag = halberVortag.AddHours(6);
        var einzelneEcNull = tagesbeginn.AddHours(10);

        // Vom Mittag des Vortags bis zum Ende von „tag" alle fünf Minuten:
        // der Vortag ist also nur halb vorhanden (wie nach dem 7-Tage-Aufräumen).
        for (var zeit = halberVortag; zeit < tagesbeginn.AddDays(1); zeit = zeit.AddMinutes(5))
        {
            var nullbild = zeit == nullbildTag || zeit == nullbildVortag;
            Schreiben(repo, zeit, "reservoir-ph", nullbild ? 0 : 6.2);
            Schreiben(repo, zeit, "reservoir-ec", nullbild || zeit == einzelneEcNull ? 0 : 1.72);
            Schreiben(repo, zeit, "reservoir-temp", nullbild ? 0 : 17.8);
            Schreiben(repo, zeit, "temperature", 24.1);
        }

        // Die Tageswerte, wie sie die Nacht-Aggregation mit den Nullen gerechnet hat.
        foreach (var groesse in new[] { "reservoir-ph", "reservoir-ec", "reservoir-temp" })
        {
            repo.UpsertDailyStat(Tageswert.Berechnen(repo, 1, groesse, tag)!);
            repo.UpsertDailyStat(Tageswert.Berechnen(repo, 1, groesse, tag.AddDays(-1))!);
        }
        // Selbsttest: vorher steht das falsche Minimum wirklich drin.
        Assert.Equal(0, repo.GetDailyStats(1, "reservoir-ph", tag, tag).Single().Min);
        Assert.Equal(0, repo.GetDailyStats(1, "reservoir-temp", tag, tag).Single().Min);

        var ergebnis = WassersondenNullbild.GespeicherteEntfernen(repo, heute);

        // Zwei Nullbilder zu je drei Werten; die einzelne EC 0 bleibt.
        Assert.Equal(6, ergebnis.Entfernt);
        Assert.Equal(3, ergebnis.TageNeuBerechnet);
        Assert.Equal(3, ergebnis.TageNichtNeuBerechnet);

        Assert.Equal(6.2, repo.GetDailyStats(1, "reservoir-ph", tag, tag).Single().Min);
        Assert.Equal(17.8, repo.GetDailyStats(1, "reservoir-temp", tag, tag).Single().Min);
        Assert.Equal(0, repo.GetDailyStats(1, "reservoir-ec", tag, tag).Single().Min);
        Assert.Contains(repo.GetReadings(1, "reservoir-ec", einzelneEcNull, einzelneEcNull), r => r.Value == 0);
        Assert.Empty(repo.GetReadings(1, "reservoir-ph", nullbildTag, nullbildTag));

        // Der halbe Vortag wird nicht neu gerechnet: sein alter Tageswert bleibt.
        Assert.Equal(0, repo.GetDailyStats(1, "reservoir-ph", tag.AddDays(-1), tag.AddDays(-1)).Single().Min);

        // Ein zweiter Start findet nichts mehr.
        Assert.Equal(0, WassersondenNullbild.GespeicherteEntfernen(repo, heute).Entfernt);
    }

    /// <summary>
    /// Scheitert die Rechnung eines Tages, geht nichts verloren: die Nullbilder
    /// bleiben, und der nächste Start räumt sie auf.
    /// </summary>
    /// <remarks>
    /// <b>Der Anlass (offene Punkte 03.10.2026, B5).</b> Die Bereinigung löschte
    /// erst die Rohwerte und rechnete danach. Scheiterte die Rechnung, war die
    /// Grundlage weg, das Minimum 0 blieb im Tageswert, und ein zweiter Start
    /// fand kein Nullbild mehr — endgültig.
    /// </remarks>
    [Fact]
    public void ScheiterndeRechnung_LaesstDieGrundlageStehen()
    {
        var repo = NeueDatenbank();
        var heute = DateOnly.FromDateTime(DateTime.Now);
        var tag = heute.AddDays(-2);
        var tagesbeginn = tag.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var nullbild = tagesbeginn.AddHours(12);
        GanzeTageSchreiben(repo, tag.AddDays(-1), tag, nullbild);

        // pH wirft; EC lässt sich am selben Tag nicht rechnen (zu wenig Werte).
        TentSensorDailyStat? PhScheitert(IReadOnlyList<TentSensorReading> rohwerte, int zelt, string groesse, DateOnly datum)
            => groesse == WassersondenNullbild.Ph ? throw new InvalidOperationException("gestellt")
                : groesse == WassersondenNullbild.Ec ? null
                : Tageswert.Berechnen(rohwerte, zelt, groesse, datum);

        var ergebnis = WassersondenNullbild.GespeicherteEntfernen(repo, heute, PhScheitert);

        Assert.Equal(0, ergebnis.Entfernt);
        Assert.Equal(1, ergebnis.TageZurueckgestellt);
        // Der Tag wird neu versucht — „nicht berechenbar" wäre für ihn falsch.
        Assert.Equal(0, ergebnis.TageNichtNeuBerechnet);
        // Alle drei Werte des Nullbilds stehen noch — auch EC und Wasser, deren
        // Rechnung gelang: ohne die EC 0 daneben wäre die pH 0 kein Nullbild mehr.
        foreach (var groesse in WassersondenNullbild.Groessen)
        {
            Assert.Contains(repo.GetReadings(1, groesse, nullbild, nullbild), r => r.Value == 0);
            Assert.Equal(0, repo.GetDailyStats(1, groesse, tag, tag).Single().Min);
        }

        // Der nächste Start rechnet richtig und räumt auf.
        var zweiter = WassersondenNullbild.GespeicherteEntfernen(repo, heute);
        Assert.Equal(3, zweiter.Entfernt);
        Assert.Equal(3, zweiter.TageNeuBerechnet);
        Assert.Equal(6.2, repo.GetDailyStats(1, WassersondenNullbild.Ph, tag, tag).Single().Min);
        Assert.Equal(17.8, repo.GetDailyStats(1, WassersondenNullbild.Wassertemperatur, tag, tag).Single().Min);
    }

    /// <summary>
    /// Ein Nullbild zwischen 00:00 und 02:00 Ortszeit gehört zum Ortstag — in
    /// UTC liegt es noch am Vortag.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (offene Punkte 03.10.2026, B6).</b> Der Code nimmt
    /// den lokalen Tag, aber keine Prüfung hätte es gemerkt, wenn er den
    /// UTC-Tag nähme: das Nullbild der ersten Prüfung liegt um 23:15, wo beide
    /// Tage gleich sind. Hier liegt es um 01:00 Ortszeit — in Deutschland
    /// 23:00 oder 00:00 UTC am Vortag. Rechnete die Bereinigung den UTC-Tag
    /// neu, bliebe das Minimum 0 im Ortstag stehen.</para>
    /// <para><b>Beisst nur außerhalb von UTC.</b> In UTC sind Ortstag und
    /// UTC-Tag dasselbe, und diese Prüfung wäre grün ohne etwas zu prüfen.
    /// Deshalb verlangt sie eine Zeitzone mit Versatz und sagt es laut; jeder
    /// Lauf bekommt <c>TZ=Europe/Berlin</c> über <c>zeitzone.runsettings</c>
    /// (im Testprojekt) — so wie die Anlage.</para>
    /// </remarks>
    [Fact]
    public void NullbildNachMitternachtOrtszeit_GehoertZumOrtstag()
    {
        var repo = NeueDatenbank();
        var heute = DateOnly.FromDateTime(DateTime.Now);
        var tag = heute.AddDays(-2);
        var nullbildLokal = tag.ToDateTime(new TimeOnly(1, 0), DateTimeKind.Local);
        var nullbild = nullbildLokal.ToUniversalTime();

        Assert.True(DateOnly.FromDateTime(nullbild) != tag,
            $"Die Zeitzone dieses Laufs ({TimeZoneInfo.Local.Id}) legt 01:00 Ortszeit auf denselben UTC-Tag — "
            + "dann prüft dieser Fall nichts. Mit TZ=Europe/Berlin laufen lassen (so läuft das Tor).");

        GanzeTageSchreiben(repo, tag.AddDays(-1), tag, nullbild);
        Assert.Equal(0, repo.GetDailyStats(1, WassersondenNullbild.Ph, tag, tag).Single().Min);

        var ergebnis = WassersondenNullbild.GespeicherteEntfernen(repo, heute);

        Assert.Equal(3, ergebnis.Entfernt);
        Assert.Equal(6.2, repo.GetDailyStats(1, WassersondenNullbild.Ph, tag, tag).Single().Min);
        Assert.Equal(1.72, repo.GetDailyStats(1, WassersondenNullbild.Ec, tag, tag).Single().Min);
        Assert.Equal(17.8, repo.GetDailyStats(1, WassersondenNullbild.Wassertemperatur, tag, tag).Single().Min);
    }

    private SensorReadingRepository NeueDatenbank()
    {
        var pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(pfade);
        return new SensorReadingRepository(pfade);
    }

    /// <summary>
    /// Ganze Ortstage von <paramref name="von"/> bis <paramref name="bis"/> alle
    /// fünf Minuten, ein Nullbild zu <paramref name="nullbild"/> (UTC), dazu die
    /// Tageswerte, wie sie die Nacht-Aggregation MIT dem Nullbild gerechnet hat.
    /// </summary>
    private static void GanzeTageSchreiben(SensorReadingRepository repo, DateOnly von, DateOnly bis, DateTime nullbild)
    {
        var anfang = von.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var ende = bis.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var getroffen = false;
        for (var zeit = anfang; zeit < ende; zeit = zeit.AddMinutes(5))
        {
            var null_ = zeit == nullbild;
            getroffen |= null_;
            Schreiben(repo, zeit, WassersondenNullbild.Ph, null_ ? 0 : 6.2);
            Schreiben(repo, zeit, WassersondenNullbild.Ec, null_ ? 0 : 1.72);
            Schreiben(repo, zeit, WassersondenNullbild.Wassertemperatur, null_ ? 0 : 17.8);
        }
        Assert.True(getroffen, "Das Nullbild liegt nicht auf dem Fünf-Minuten-Raster — es wurde nie geschrieben.");

        for (var tag = von; tag <= bis; tag = tag.AddDays(1))
        {
            foreach (var groesse in WassersondenNullbild.Groessen)
            {
                repo.UpsertDailyStat(Tageswert.Berechnen(repo, 1, groesse, tag)!);
            }
        }
    }

    private static void Schreiben(SensorReadingRepository repo, DateTime zeit, string groesse, double wert)
        => repo.AddReading(new TentSensorReading { TentId = 1, MetricKey = groesse, Value = wert, CapturedAtUtc = zeit });
}
