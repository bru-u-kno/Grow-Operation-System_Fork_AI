using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-009): Ein Home Assistant aus Papier und der Zusatz-Entfeuchter davor —
/// echte Dienste, echte Datenbank, nur das Netz ist eine Attrappe, die jeden Aufruf mitschreibt.
/// </summary>
/// <remarks>
/// <para>Die Zustände sind <b>Brus Stand vom 06.10.2026</b> (Helfer, Plan-Sensoren, Shelly):
/// Tag-Grenze 26,5 fest, Nacht 25, Folge-Abstand 1 K. Damit prüft jeder Fall gegen die Zahlen,
/// die am 06.10. beim Speichern verloren gingen — nicht gegen Werkseinstellungen.</para>
/// </remarks>
public sealed class ZusatzPruefstand : IDisposable
{
    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly RecordingHttpHandler _ha;

    /// <summary>Was Home Assistant auf <c>GET /api/states</c> antwortet: Entity-ID auf (Zustand, Name, Einheit, Konfig-Kennung).</summary>
    public Dictionary<string, (string State, string? Name, string? Unit, string? Kennung)> Zustaende { get; } = new(StringComparer.Ordinal);

    /// <summary>Was Home Assistant als Verlauf liefert (Rohtext der Antwort); leer = „[]".</summary>
    public string Verlauf { get; set; } = "[]";

    public ZusatzPruefstand(bool bruStand = true)
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "ZusatzPruefstand_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        TestDatabase.InitializeWithDefaultTent(_pfade);
        new HomeAssistantSettingsRepository(_pfade).SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true,
        });

        _ha = new RecordingHttpHandler((anfrage, _) =>
        {
            var pfad = anfrage.RequestUri!.AbsolutePath;
            if (anfrage.Method == HttpMethod.Get && pfad.EndsWith("/api/states", StringComparison.Ordinal))
                return RecordingHttpHandler.Json(StatesJson());
            if (anfrage.Method == HttpMethod.Get && pfad.Contains("/api/history/period", StringComparison.Ordinal))
                return RecordingHttpHandler.Json(Verlauf);
            if (anfrage.Method == HttpMethod.Post && pfad.Contains("/api/services/", StringComparison.Ordinal))
                return RecordingHttpHandler.Json("[]");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        // Die Zuordnung der Geräte (kein Rückfall auf Vorgaben ohne App-Einstellungen).
        var steuerung = new SteuerungRepository(_pfade);
        foreach (var (modul, rolle, entitaet) in new[]
                 {
                     ("entfeuchter", "zelt_rh", "sensor.big_probe_sensor_sonden_luftfeuchtigkeit"),
                     ("entfeuchter", "zelt_temp", "sensor.big_probe_sensor_sonden_temperatur"),
                     ("entfeuchter", "zelt_vpd", "sensor.big_probe_sensor_sonden_vpd"),
                     ("entfeuchter", "port_schalter", "select.rdwc_dehumi_aktiver_modus"),
                     ("entfeuchter", "port_zustand", "binary_sensor.big_port_7_zustand"),
                     ("entfeuchter", "licht_zustand", "binary_sensor.klein_abluft_zustand"),
                     (EntfeuchterZusatzSteuerungService.Modul, "zusatz_schalter", "switch.grow_dehumi_tent"),
                     (EntfeuchterZusatzSteuerungService.Modul, "zusatz_leistung", "sensor.grow_dehumi_tent_leistung"),
                     (EntfeuchterZusatzSteuerungService.Modul, "zusatz_energie", "sensor.grow_dehumi_tent_energie"),
                     (EntfeuchterZusatzSteuerungService.Modul, "fuehrung_zustand", "select.rdwc_dehumi_aktiver_modus"),
                 })
        {
            steuerung.SetGeraet(modul, rolle, entitaet);
        }

        if (bruStand) BruStand();
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Dateisystem haelt manchmal fest */ }
    }

    public SteuerungRepository Repo => new(_pfade);

    /// <summary>Alle Aufrufe, die Home Assistant bekommen hat — ohne die Lesezugriffe.</summary>
    public List<(string Dienst, string Entitaet, JsonObject Daten)> Schreibaufrufe()
        => _ha.Requests
            .Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("/api/services/", StringComparison.Ordinal))
            .Select(r =>
            {
                var daten = (JsonObject)JsonNode.Parse(r.Body!)!;
                var dienst = r.Uri.AbsolutePath[(r.Uri.AbsolutePath.IndexOf("/api/services/", StringComparison.Ordinal) + "/api/services/".Length)..];
                return (dienst, daten["entity_id"]!.GetValue<string>(), daten);
            })
            .ToList();

    public void Vergessen() => _ha.Requests.Clear();

    public EntfeuchterSteuerungService Entfeuchter()
        => new(Repo, Ha(), new HomeAssistantSettingsRepository(_pfade), new SteuerungGeraeteService(Repo), Wochenplan(),
            NullLogger<EntfeuchterSteuerungService>.Instance);

    public EntfeuchterZusatzSteuerungService Zusatz()
        => new(Repo, Ha(), new HomeAssistantSettingsRepository(_pfade), new SteuerungGeraeteService(Repo), Wochenplan(),
            Entfeuchter(), NullLogger<EntfeuchterZusatzSteuerungService>.Instance);

    private HomeAssistantService Ha()
        => new(new StubHttpClientFactory(_ha), NullLogger<HomeAssistantService>.Instance);

    private WochenplanSyncService Wochenplan()
    {
        var grows = new GrowRepository(_pfade);
        var wissen = new GrowDiary.Web.Services.Knowledge.KnowledgeBaseLoader(
            _pfade, NullLogger<GrowDiary.Web.Services.Knowledge.KnowledgeBaseLoader>.Instance);
        return new WochenplanSyncService(grows, wissen, Repo, new AlertRuleRepository(_pfade), Ha(),
            new HomeAssistantSettingsRepository(_pfade), NullLogger<WochenplanSyncService>.Instance);
    }

    public void Setze(string entitaet, string zustand, string? name = null, string? einheit = null, string? kennung = null)
        => Zustaende[entitaet] = (zustand, name, einheit, kennung);

    private string StatesJson()
    {
        var sb = new StringBuilder("[");
        var erster = true;
        foreach (var (id, (state, name, unit, kennung)) in Zustaende)
        {
            if (!erster) sb.Append(',');
            erster = false;
            var attribute = new JsonObject();
            if (name is not null) attribute["friendly_name"] = name;
            if (unit is not null) attribute["unit_of_measurement"] = unit;
            if (kennung is not null) attribute["id"] = kennung;
            sb.Append(new JsonObject
            {
                ["entity_id"] = id,
                ["state"] = state,
                ["last_changed"] = "2026-10-06T20:00:00+00:00",
                ["attributes"] = attribute,
            }.ToJsonString());
        }
        return sb.Append(']').ToString();
    }

    /// <summary>Brus Anlage am Abend des 06.10.2026.</summary>
    private void BruStand()
    {
        // Zusatz-Helfer (bestehen bei Bru)
        Setze(EntfeuchterZusatzSteuerungService.Entitaeten.VpdHysterese, "0.15");
        Setze(EntfeuchterZusatzSteuerungService.Entitaeten.Mindestlaufzeit, "15");
        Setze(EntfeuchterZusatzSteuerungService.Entitaeten.Mindestpause, "10");
        Setze(EntfeuchterZusatzSteuerungService.Entitaeten.FolgeAbstand, "1.0");
        Setze(EntfeuchterZusatzSteuerungService.Entitaeten.WiederEinAbstand, "1.0");
        Setze(EntfeuchterZusatzSteuerungService.Entitaeten.ZuschaltVerzoegerung, "10");

        // Entfeuchter-Helfer (gemeinsame Quelle der Höchsttemperatur): Tag 26,5, Nacht 25
        var e = EntfeuchterSteuerungService.Entitaeten.TempMaxTag;
        Setze(e, "26.5");
        Setze(EntfeuchterSteuerungService.Entitaeten.TempMaxNacht, "25.0");
        // Bewusst NICHT die Werkseinstellungen: ein Test, in dem der Rest des Entfeuchters genau
        // dem Werkswert gleicht, bemerkt es nicht, wenn Speichern den Rest auf den Werkswert zurücksetzt.
        Setze(EntfeuchterSteuerungService.Entitaeten.Hysterese, "6");
        Setze(EntfeuchterSteuerungService.Entitaeten.Mindestlaufzeit, "25");
        Setze(EntfeuchterSteuerungService.Entitaeten.Einschaltverzoegerung, "7");
        Setze(EntfeuchterSteuerungService.Entitaeten.WartezeitAussenluft, "30");
        Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteEinTag, "58");
        Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteAusTag, "54");
        Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteEinNacht, "61");
        Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteAusNacht, "56");
        Setze(EntfeuchterSteuerungService.Entitaeten.VpdRegelung, "off");
        Setze(EntfeuchterSteuerungService.Entitaeten.Tagbetrieb, "off");
        Setze(EntfeuchterSteuerungService.Entitaeten.Automatik, "on", kennung: "entf-auto");

        // Plan-Sensoren
        Setze(EntfeuchterSteuerungService.Entitaeten.VpdUnten, "1.4");
        Setze(EntfeuchterSteuerungService.Entitaeten.VpdOben, "1.4");
        Setze(EntfeuchterSteuerungService.Entitaeten.EinAktiv, "39.0");
        Setze(EntfeuchterSteuerungService.Entitaeten.AusAktiv, "35.0");

        // Geräte
        Setze("sensor.big_probe_sensor_sonden_temperatur", "25.1", "Sonde Temperatur", "°C");
        Setze("sensor.big_probe_sensor_sonden_luftfeuchtigkeit", "55.2", "Sonde Luftfeuchte", "%");
        Setze("sensor.big_probe_sensor_sonden_vpd", "1.31", "Sonde VPD", "kPa");
        Setze("binary_sensor.klein_abluft_zustand", "on", "Light LED Top Zustand");
        Setze("select.rdwc_dehumi_aktiver_modus", "On", "RDWC Dehumi Aktiver Modus");
        Setze("switch.grow_dehumi_tent", "on", "Dehumi RDWC Tent");
        Setze("sensor.grow_dehumi_tent_leistung", "313.0", "Dehumi RDWC Tent Leistung", "W");
        Setze("sensor.grow_dehumi_tent_energie", "7.27", "Dehumi RDWC Tent Energie", "kWh");

        // Die handgebaute Regelung: läuft.
        Setze("automation.rdwc_trotec_zelt_shelly_plan_regelung", "on", "RDWC Trotec Zelt (Shelly) Plan-Regelung", kennung: "1791297367181");
    }

    // ---- Vergleichshilfen -------------------------------------------------

    /// <summary>Ein Objekt als flache Liste Pfad → Wert, damit „was hat sich verändert" ablesbar ist.</summary>
    public static Dictionary<string, string> Flach<T>(T objekt)
    {
        var ergebnis = new Dictionary<string, string>(StringComparer.Ordinal);
        void Lauf(string pfad, JsonNode? knoten)
        {
            switch (knoten)
            {
                case JsonObject o:
                    foreach (var (k, v) in o) Lauf(pfad.Length == 0 ? k : pfad + "." + k, v);
                    break;
                default:
                    ergebnis[pfad] = knoten?.ToJsonString() ?? "null";
                    break;
            }
        }
        Lauf("", JsonSerializer.SerializeToNode(objekt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return ergebnis;
    }

    /// <summary>Die Pfade, in denen sich zwei Objekte unterscheiden.</summary>
    public static List<string> Unterschiede<T>(T vorher, T nachher)
    {
        var a = Flach(vorher);
        var b = Flach(nachher);
        return a.Keys.Union(b.Keys).Where(k => a.GetValueOrDefault(k) != b.GetValueOrDefault(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }
}
