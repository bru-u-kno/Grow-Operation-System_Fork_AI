using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Der Weg von Home Assistant in den Regler — <see cref="KuehlerWorker.LageLesen"/>.
///
/// <para><b>Warum es diese Datei gibt.</b> Der reine Regler war mit vierzehn
/// Fällen geprüft, der <b>Zusammenbau der Lage</b> mit keinem einzigen. Genau
/// dort saß der schlimmste Fehler dieser Änderung: der Zustand der Steckdose
/// wurde in einem Wörterbuch gesucht, dessen Schlüssel <b>Metrik-Kennungen</b>
/// sind (<c>chiller</c>, <c>reservoir-temp</c>) und nie Entitäts-Kennungen. In
/// einer echten Anlage hätte der Regler deshalb <b>nie</b> geschaltet.</para>
///
/// <para><b>Und warum es niemandem auffiel:</b> der Testbestand trug diesen
/// einen Schlüssel eigens ein. Die Demo-Daten haben den Fehler verdeckt —
/// „ansehen" allein hat hier nicht gereicht, weil die Kulisse echt aussah.</para>
/// </summary>
public sealed class KuehlerLageTests : IDisposable
{
    private readonly string _temp;
    private readonly AppPaths _paths;
    private readonly GrowRepository _grows;
    private readonly AppSettingsRepository _einstellungen;
    private readonly NachtabsenkungWriter _writer;
    private readonly Tent _zelt;

    public KuehlerLageTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "KuehlerLage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _paths = new AppPaths(_temp);
        _zelt = TestDatabase.InitializeWithDefaultTent(_paths);
        _grows = new GrowRepository(_paths);
        _einstellungen = new AppSettingsRepository(_paths);

        // Der Writer wird von LageLesen nur angefasst, wenn es einen laufenden
        // Grow MIT eingeschalteter Rampe gibt — in diesen Fällen gibt es keinen.
        // Er muss trotzdem echt sein: eine Attrappe würde beweisen, dass die
        // Attrappe funktioniert.
        var loader = new KnowledgeBaseLoader(_paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        var tents = new TentRepository(_paths);
        _writer = new NachtabsenkungWriter(
            _grows,
            new TargetValueService(loader),
            new HomeAssistantService(new NullHttpClientFactory(), NullLogger<HomeAssistantService>.Instance),
            new SetpointProfileRepository(_paths),
            new HydroSetupRepository(_paths, tents),
            new SystemAuditRepository(_paths),
            NullLogger<NachtabsenkungWriter>.Instance);

        _zelt.ChillerControlEnabled = true;
        _zelt.ChillerSwitchEntityId = "switch.kuehler";
        _grows.UpdateTent(_zelt);
    }

    /// <summary>Ein Fabrikat, das nie benutzt wird — es fliesst kein Netzverkehr.</summary>
    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { /* Aufräumen darf scheitern. */ }
    }

    private static HomeAssistantState Zustand(string wert, DateTime? geaendert = null, DateTime? aktualisiert = null)
        => new()
        {
            EntityId = "sensor.egal",
            State = wert,
            NumericValue = Zahlenlesen.Maschine(wert),
            LastChanged = geaendert,
            LastUpdated = aktualisiert,
        };

    private static Dictionary<string, HomeAssistantState> Messwerte(DateTime jetzt)
        => new()
        {
            [TentSensorMetricKeyMap.Resolve(SensorMetricType.ReservoirWaterTemp)] =
                Zustand("19.0", geaendert: jetzt, aktualisiert: jetzt),
            [TentSensorMetricKeyMap.Resolve(SensorMetricType.LightStatus)] =
                Zustand("on", geaendert: jetzt, aktualisiert: jetzt),
        };

    private KuehlerLage Lesen(
        IReadOnlyDictionary<string, HomeAssistantState> zustaende, HomeAssistantState? steckdose)
        => KuehlerWorker.LageLesen(_grows, _writer, _einstellungen, _zelt, zustaende, steckdose);

    /* ---------------- Der Zustand der Steckdose ---------------- */

    [Fact]
    public void Die_Steckdose_liefert_den_Schaltzustand()
    {
        var jetzt = DateTime.UtcNow;
        var lage = Lesen(Messwerte(jetzt), Zustand("on", jetzt, jetzt));

        Assert.True(lage.KuehlerLaeuftGerade);
    }

    [Fact]
    public void Ein_Eintrag_unter_der_ENTITAETS_Kennung_wird_NICHT_gelesen()
    {
        // <b>Der Beweis, dass die Prüfung beisst.</b> Genau so sah der Fehler
        // aus: die Steckdose wurde im Messwert-Wörterbuch unter ihrer
        // Entitäts-Kennung gesucht. Das Wörterbuch kennt dort nur
        // Metrik-Kennungen — in einer echten Anlage stand dort nie etwas, und
        // der Regler schaltete nie. Baut jemand die alte Suche wieder ein,
        // wird dieser Fall grün und meldet „läuft" statt „unbekannt".
        var jetzt = DateTime.UtcNow;
        var zustaende = Messwerte(jetzt);
        zustaende["switch.kuehler"] = Zustand("on", jetzt, jetzt);

        var lage = Lesen(zustaende, steckdose: null);

        Assert.Null(lage.KuehlerLaeuftGerade);
    }

    [Fact]
    public void Ohne_Steckdose_springt_der_Chiller_Sensor_ein()
    {
        var jetzt = DateTime.UtcNow;
        var zustaende = Messwerte(jetzt);
        zustaende[TentSensorMetricKeyMap.Resolve(SensorMetricType.Chiller)] = Zustand("on", jetzt, jetzt);

        var lage = Lesen(zustaende, steckdose: null);

        Assert.True(lage.KuehlerLaeuftGerade);
    }

    /* ---------------- Wie alt ist der Messwert wirklich ---------------- */

    [Fact]
    public void Die_Frische_kommt_aus_LastUpdated_nicht_aus_LastChanged()
    {
        // Ein Sensor, der eine halbe Stunde lang denselben Wert meldet, ist
        // nicht veraltet — er ist stabil. Genau das passiert, wenn die Regelung
        // ihr Ziel getroffen hat: `last_changed` steht still, `last_updated`
        // läuft weiter. Auf `last_changed` zu gehen hiesse, dass der Regler
        // aufhört zu regeln, sobald er erfolgreich war.
        var jetzt = DateTime.UtcNow;
        var zustaende = Messwerte(jetzt);
        zustaende[TentSensorMetricKeyMap.Resolve(SensorMetricType.ReservoirWaterTemp)] =
            Zustand("19.0", geaendert: jetzt.AddMinutes(-30), aktualisiert: jetzt.AddMinutes(-1));

        var lage = Lesen(zustaende, Zustand("off", jetzt, jetzt));

        Assert.NotNull(lage.MesswertAlter);
        Assert.True(lage.MesswertAlter!.Value < TimeSpan.FromMinutes(3),
            $"Erwartet wurde etwa eine Minute, gemessen {lage.MesswertAlter}. "
            + "Damit gilt der Wert als zu alt und es wird nicht mehr geschaltet.");
    }

    [Fact]
    public void Ohne_beide_Zeitstempel_bleibt_das_Alter_unbekannt()
    {
        var jetzt = DateTime.UtcNow;
        var zustaende = Messwerte(jetzt);
        zustaende[TentSensorMetricKeyMap.Resolve(SensorMetricType.ReservoirWaterTemp)] = Zustand("19.0");

        var lage = Lesen(zustaende, Zustand("off", jetzt, jetzt));

        Assert.Null(lage.MesswertAlter);
        // Und der Regler macht daraus bewusst nichts.
        Assert.Equal(KuehlerSchaltung.Nichts,
            KuehlerService.Entscheiden(lage with { SollC = 19, IstC = 25 }, _zelt, jetzt).Schaltung);
    }

    /* ---------------- Der letzte Befehl ---------------- */

    [Fact]
    public void Ohne_Schaltung_gibt_es_keinen_letzten_Befehl()
    {
        Assert.Null(KuehlerWorker.LetzterBefehl(_einstellungen, _zelt.Id));
    }

    [Fact]
    public void Der_letzte_Befehl_ueberlebt_in_der_Datenbank()
    {
        _einstellungen.SetValue($"{KuehlerWorker.BefehlKey}:{_zelt.Id}", "off");
        Assert.False(KuehlerWorker.LetzterBefehl(_einstellungen, _zelt.Id));

        _einstellungen.SetValue($"{KuehlerWorker.BefehlKey}:{_zelt.Id}", "on");
        Assert.True(KuehlerWorker.LetzterBefehl(_einstellungen, _zelt.Id));

        // Frisch aus der Ablage gelesen, nicht aus dem Speicher: die Sperre
        // muss ein Update des Add-ons überleben.
        Assert.True(KuehlerWorker.LetzterBefehl(new AppSettingsRepository(_paths), _zelt.Id));
    }

    /* ---------------- Absichtlich aus oder ausgefallen ---------------- */

    [Fact]
    public void Selbst_abgeschaltet_ist_kein_Ausfall_und_zwar_ohne_Zeitfenster()
    {
        // <b>Der Fall, der vorher nach zwanzig Minuten kippte.</b> Eine kühle
        // Nacht lässt den Kühler stundenlang stehen; die erste Fassung meldete
        // ab Minute 21 „kritisch: Der Kühler ist seit 21 Minuten aus" samt Push.
        Assert.True(KuehlerService.IstAbsichtlichAus(_zelt, letzterBefehl: false));
    }

    [Fact]
    public void Ein_befohlen_und_trotzdem_aus_bleibt_ein_Ausfall()
    {
        Assert.False(KuehlerService.IstAbsichtlichAus(_zelt, letzterBefehl: true));
    }

    [Fact]
    public void Ohne_eigene_Steuerung_gilt_die_alte_Beurteilung()
    {
        var fremd = new Tent { Id = 9, Name = "Ohne Steuerung", ChillerControlEnabled = false };
        Assert.False(KuehlerService.IstAbsichtlichAus(fremd, letzterBefehl: false));

        var ohneSteckdose = new Tent
        {
            Id = 10, Name = "Ohne Steckdose", ChillerControlEnabled = true, ChillerSwitchEntityId = null,
        };
        Assert.False(KuehlerService.IstAbsichtlichAus(ohneSteckdose, letzterBefehl: false));

        // Und: hat der Regler noch nie geschaltet, ist „aus" nicht seine Schuld.
        Assert.False(KuehlerService.IstAbsichtlichAus(_zelt, letzterBefehl: null));
    }
}
