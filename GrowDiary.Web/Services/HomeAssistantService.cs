using GrowDiary.Web.Infrastructure;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public sealed class HomeAssistantService
{
    private static readonly TimeSpan BackoffWindow = TimeSpan.FromSeconds(20);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HydroSetupRepository? _hydroSetups;
    private readonly ILogger<HomeAssistantService> _logger;
    private long _circuitOpenUntilTicks;

    /// <summary>Frist für lesende Abrufe — Zustände, Entitätenliste, Kamerabild.</summary>
    /// <remarks>
    /// Kurz, weil die Live-Seiten darauf warten: ein hängendes Home Assistant
    /// soll die Kacheln nicht für eine Viertelminute einfrieren.
    /// </remarks>
    public TimeSpan Lesefrist { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Frist für Aufrufe, die etwas schalten oder schreiben.</summary>
    /// <remarks>
    /// <para><b>Der Anlass (02.10.2026).</b> Vorher galten auch hier die 4 s
    /// der Lesefrist. Home Assistants Dienst-Endpunkt wartet aber selbst bis zu
    /// 10 s auf die Integration, und Wolken-Integrationen wie AC Infinity
    /// brauchen oft länger. Der Aufruf brach ab, während das Gerät schaltete:
    /// die Seite meldete „nicht angenommen" (beim Preset blieb deshalb der
    /// Modus ungeschrieben), und die Stell-Antwort machte daraus einen 502 —
    /// „502, aber es schaltet".</para>
    /// <para>15 s liegen über den 10 s von Home Assistant, damit dessen eigene
    /// Antwort (auch eine Ablehnung) noch ankommt. Faustregel, keine
    /// dokumentierte Herstellerangabe.</para>
    /// </remarks>
    public TimeSpan Dienstfrist { get; init; } = TimeSpan.FromSeconds(15);

    public HomeAssistantService(
        IHttpClientFactory httpClientFactory,
        ILogger<HomeAssistantService> logger,
        HydroSetupRepository? hydroSetups = null)
    {
        _httpClientFactory = httpClientFactory;
        _hydroSetups = hydroSetups;
        _logger = logger;
    }

    public async Task<Dictionary<string, HomeAssistantState>> GetStatesAsync(
        HomeAssistantSettings settings,
        Tent tent,
        CancellationToken cancellationToken = default)
    {
        // Testdatenmodus: erfundene, bewegte Werte statt eines Abrufs. Vor der
        // Sensorpruefung, weil auf einem frischen Entwicklungsrechner nichts
        // zugeordnet ist — und dann bliebe der Bildschirm leer.
        if (DemoData.IsEnabled)
        {
            // Auch die Testdaten laufen durch die Umrechnung. Sonst verhielte
            // sich der Vorfuehrmodus anders als der Betrieb, und genau dort
            // schaut man hin, bevor man etwas anschliesst.
            var demo = DemoData.StatesFor(DateTime.UtcNow, DemoData.LageFuer(tent));
            AddLitersFromCentimeters(demo, tent);
            WassersondenNullbild.AufZustaendeAnwenden(demo);
            return demo;
        }

        if (!settings.IsConfigured || tent.Sensors.Count == 0)
        {
            return new Dictionary<string, HomeAssistantState>();
        }

        if (IsCircuitOpen())
        {
            return new Dictionary<string, HomeAssistantState>();
        }

        var sensors = tent.Sensors
            .Where(sensor => sensor.IsActive && !string.IsNullOrWhiteSpace(sensor.HaEntityId))
            .GroupBy(sensor => TentSensorMetricKeyMap.Resolve(sensor.MetricType))
            .Select(group => group.Last())
            .ToList();

        if (sensors.Count == 0)
        {
            return new Dictionary<string, HomeAssistantState>();
        }

        try
        {
            var client = CreateClient(settings);

            var results = await Task.WhenAll(sensors.Select(sensor =>
                FetchStateAsync(
                    client,
                    TentSensorMetricKeyMap.Resolve(sensor.MetricType),
                    sensor.HaEntityId,
                    cancellationToken)));

            var states = results
                .Where(result => result.State is not null)
                .ToDictionary(result => result.Key, result => result.State!);

            // Zentimeter in Liter, sobald das System kalibriert ist — und zwar
            // HIER, an der Quelle. Dann sehen Kacheln, Verlauf, Alarme und der
            // Dosier-Faktor alle dasselbe, und niemand muss den Sonderfall
            // „cm-Sensor" kennen. Genau daran scheiterte der Volumenfaktor
            // vorher: er las nur `reservoir-level` in Litern.
            AddLitersFromCentimeters(states, tent);

            // Ebenfalls an der Quelle: meldet die Wassersonde pH 0 und EC 0
            // zugleich, ist das ein Platzhalter der Integration und kein
            // Messwert (Begründung in WassersondenNullbild). Ohne diese Zeile
            // landete er als 0 in Rohwerten, Tageswerten, Kacheln und Alarmen.
            var nullbild = WassersondenNullbild.AufZustaendeAnwenden(states);
            if (nullbild.Count > 0)
            {
                _logger.LogInformation(
                    "Wassersonde in Zelt {TentId} meldet pH 0 und EC 0 zugleich — als nicht verfügbar behandelt: {Groessen}.",
                    tent.Id, string.Join(", ", nullbild));
            }

            if (results.Any(result => result.TransportFailure))
            {
                if (TryOpenCircuit())
                {
                    _logger.LogWarning(
                        "Home Assistant Statusabfragen fuer Zelt {TentId} hatten Transportfehler. Weitere Abfragen sind fuer {BackoffSeconds} Sekunden pausiert.",
                        tent.Id,
                        (int)BackoffWindow.TotalSeconds);
                }
            }
            else
            {
                ResetCircuit();
            }

            return states;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new Dictionary<string, HomeAssistantState>();
        }
        catch (Exception ex)
        {
            if (TryOpenCircuit())
            {
                _logger.LogWarning(
                    ex,
                    "Home Assistant Statusabfragen fuer Zelt {TentId} sind fehlgeschlagen. Weitere Abfragen sind fuer {BackoffSeconds} Sekunden pausiert.",
                    tent.Id,
                    (int)BackoffWindow.TotalSeconds);
            }
            else
            {
                _logger.LogDebug(ex, "Home Assistant Statusabfragen fuer Zelt {TentId} sind fehlgeschlagen.", tent.Id);
            }

            return new Dictionary<string, HomeAssistantState>();
        }
    }

    private async Task<(string Key, HomeAssistantState? State, bool TransportFailure)> FetchStateAsync(
        HttpClient client,
        string key,
        string entityId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync($"api/states/{entityId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Home Assistant state {EntityId} fuer Metrik {MetricKey} konnte nicht geladen werden: HTTP {StatusCode}.",
                    entityId,
                    key,
                    (int)response.StatusCode);
                return (key, null, false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            var state = new HomeAssistantState
            {
                EntityId = root.TryGetProperty("entity_id", out var eid) ? eid.GetString() ?? entityId : entityId,
                State = root.TryGetProperty("state", out var stateEl) ? stateEl.GetString() ?? string.Empty : string.Empty,
                LastChanged = root.TryGetProperty("last_changed", out var changedEl) && DateTime.TryParse(changedEl.GetString(), out var changed)
                    ? changed.ToUniversalTime()
                    : null,
                // last_updated, NICHT last_changed: Letzteres rueckt nur vor,
                // wenn sich der Zustandstext aendert. Eine Wassertemperatur,
                // die zwoelf Minuten lang „19.0" meldet — der Normalfall am
                // Sollwert —, waere sonst „zwoelf Minuten alt", und der
                // Kuehler-Regler haette genau dann aufgehoert zu regeln,
                // wenn er sein Ziel erreicht hat.
                LastUpdated = root.TryGetProperty("last_updated", out var updatedEl) && DateTime.TryParse(updatedEl.GetString(), out var updated)
                    ? updated.ToUniversalTime()
                    : null,
            };

            if (root.TryGetProperty("attributes", out var attrs))
            {
                if (attrs.TryGetProperty("friendly_name", out var friendly)) state.FriendlyName = friendly.GetString();
                if (attrs.TryGetProperty("unit_of_measurement", out var unit)) state.UnitOfMeasurement = unit.GetString();
                if (attrs.TryGetProperty("temperature", out var soll) && soll.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    state.AttributTemperatur = soll.GetDouble();
                }
                // Die Spanne eines input_number (siehe HomeAssistantState.AttributMin).
                if (attrs.TryGetProperty("min", out var min) && min.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    state.AttributMin = min.GetDouble();
                }
                if (attrs.TryGetProperty("max", out var max) && max.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    state.AttributMax = max.GetDouble();
                }
            }

            if (Zahlenlesen.Maschine(state.State) is { } numeric)
            {
                state.NumericValue = numeric;
            }

            return (key, state, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (key, null, false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Home Assistant state {EntityId} fuer Metrik {MetricKey} konnte nicht geladen werden.", entityId, key);
            return (key, null, true);
        }
    }



    /// <summary>
    /// Den Zustand EINER Entität holen, die keine Zelt-Messgröße ist.
    /// </summary>
    /// <remarks>
    /// <b>Warum das eine eigene Methode braucht.</b>
    /// <see cref="GetStatesAsync"/> liefert ein Wörterbuch, dessen Schlüssel
    /// <b>Metrik-Kennungen</b> sind (<c>chiller</c>, <c>reservoir-temp</c>, …) —
    /// nie Entitäts-Kennungen. Wer dort mit <c>switch.kuehler</c> nachschlägt,
    /// findet grundsätzlich nichts. Genau das ist beim Kühler-Regler passiert,
    /// und es fiel nicht auf, weil der Testbestand diesen einen Schlüssel
    /// zusätzlich einträgt: die Demo-Daten haben den Fehler verdeckt.
    /// </remarks>
    public async Task<HomeAssistantState?> GetEntityStateAsync(
        HomeAssistantSettings settings, string entityId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return null;

        if (DemoData.IsEnabled)
        {
            // Auch hier durch dieselbe Quelle wie der Betrieb.
            return DemoData.EntityState(entityId, DateTime.UtcNow);
        }

        if (!settings.IsConfigured || IsCircuitOpen()) return null;

        var client = CreateClient(settings);
        var (_, zustand, _) = await FetchStateAsync(client, entityId, entityId, cancellationToken);
        return zustand;
    }

    public async Task<(byte[] Bytes, string ContentType)?> GetCameraSnapshotAsync(HomeAssistantSettings settings, string entityId, CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        // Testdatenmodus: ein gezeichnetes Bild statt eines Abrufs. Ohne diesen
        // Zweig ging der Aufruf wirklich raus, scheiterte, und der Schutzschalter
        // meldete danach „Home Assistant antwortet nicht" — direkt unter dem
        // Streifen, der sagt, dass gar kein Home Assistant im Spiel ist.
        if (DemoData.IsEnabled)
        {
            return (DemoData.CameraImage(entityId, DateTime.Now), "image/svg+xml");
        }

        if (IsCircuitOpen())
        {
            return null;
        }

        try
        {
            var client = CreateClient(settings);

            using var response = await client.GetAsync($"api/camera_proxy/{entityId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Home Assistant Kamera {EntityId} konnte nicht geladen werden: HTTP {StatusCode}.",
                    entityId,
                    (int)response.StatusCode);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            ResetCircuit();
            return (bytes, contentType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            if (TryOpenCircuit())
            {
                _logger.LogWarning(
                    ex,
                    "Home Assistant Kamera {EntityId} konnte nicht geladen werden. Weitere Abfragen sind für {BackoffSeconds} Sekunden pausiert.",
                    entityId,
                    (int)BackoffWindow.TotalSeconds);
            }
            else
            {
                _logger.LogDebug(ex, "Home Assistant Kamera {EntityId} konnte nicht geladen werden.", entityId);
            }

            return null;
        }
    }

    /// <summary>
    /// Lists all Home Assistant entities (<c>GET /api/states</c>) so the UI can offer
    /// a searchable sensor picker instead of asking the user to type entity IDs.
    /// Returns an empty list when HA is unreachable or unconfigured.
    /// </summary>
    public async Task<IReadOnlyList<HomeAssistantEntity>> GetEntitiesAsync(
        HomeAssistantSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (DemoData.IsEnabled)
        {
            return DemoData.Entities(DateTime.UtcNow);
        }

        if (!settings.IsConfigured || IsCircuitOpen())
        {
            return Array.Empty<HomeAssistantEntity>();
        }

        try
        {
            var client = CreateClient(settings);
            using var response = await client.GetAsync("api/states", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Home Assistant Entity-Liste konnte nicht geladen werden: HTTP {StatusCode}.", (int)response.StatusCode);
                return Array.Empty<HomeAssistantEntity>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var entities = new List<HomeAssistantEntity>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var entityId = element.TryGetProperty("entity_id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(entityId))
                {
                    continue;
                }

                string? friendlyName = null, unit = null, deviceClass = null, konfigKennung = null;
                if (element.TryGetProperty("attributes", out var attrs))
                {
                    if (attrs.TryGetProperty("friendly_name", out var f)) friendlyName = f.GetString();
                    if (attrs.TryGetProperty("unit_of_measurement", out var u)) unit = u.GetString();
                    if (attrs.TryGetProperty("device_class", out var d)) deviceClass = d.GetString();
                    // Nur bei Automationen: dort ist "id" die Kennung der Konfiguration.
                    if (entityId.StartsWith("automation.", StringComparison.Ordinal)
                        && attrs.TryGetProperty("id", out var k) && k.ValueKind == JsonValueKind.String)
                    {
                        konfigKennung = k.GetString();
                    }
                }

                entities.Add(new HomeAssistantEntity
                {
                    EntityId = entityId,
                    FriendlyName = friendlyName,
                    State = element.TryGetProperty("state", out var stateEl) ? stateEl.GetString() : null,
                    UnitOfMeasurement = unit,
                    DeviceClass = deviceClass,
                    KonfigKennung = konfigKennung,
                    LastChangedUtc = element.TryGetProperty("last_changed", out var geaendertEl)
                                     && geaendertEl.ValueKind == JsonValueKind.String
                                     && DateTime.TryParse(geaendertEl.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                                         System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var geaendert)
                        ? DateTime.SpecifyKind(geaendert, DateTimeKind.Utc)
                        : null,
                    Domain = entityId.Split('.', 2)[0],
                });
            }

            ResetCircuit();
            return entities;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<HomeAssistantEntity>();
        }
        catch (Exception ex)
        {
            TryOpenCircuit();
            _logger.LogDebug(ex, "Home Assistant Entity-Liste konnte nicht geladen werden.");
            return Array.Empty<HomeAssistantEntity>();
        }
    }

    /// <summary>
    /// Fork AI (A-003 Etappe B, 03.10.2026): Die Bereiche (Areas) von Home Assistant
    /// samt ihrer Entitäten — für <c>GET /api/ki-ha/bereiche</c> und den Bereichsfilter
    /// der Zustände.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum über die Vorlagen-Schnittstelle.</b> Die REST-Schnittstelle kennt
    /// keine Bereiche; das Register gibt es sonst nur über den WebSocket
    /// (<see cref="HomeAssistantRegistryService"/>). <c>POST /api/template</c> geht
    /// denselben Weg wie jeder andere Aufruf hier — gleiche Adresse, gleiches Token,
    /// gleicher Schutzschalter — und liefert mit <c>areas()</c>, <c>area_name()</c>
    /// und <c>area_entities()</c> alles in einem Aufruf. <c>area_entities</c> nimmt
    /// auch die Entitäten der Geräte im Bereich mit.</para>
    /// <para><c>null</c> heisst: nicht eingerichtet, nicht erreichbar oder nicht
    /// lesbar — anders als eine leere Liste, die heisst „keine Bereiche angelegt".
    /// Im Testbetrieb gibt es keine Bereiche.</para>
    /// </remarks>
    public async Task<IReadOnlyList<HaBereich>?> GetBereicheAsync(
        HomeAssistantSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (DemoData.IsEnabled) return Array.Empty<HaBereich>();
        if (!settings.IsConfigured || IsCircuitOpen()) return null;

        try
        {
            var client = CreateClient(settings);
            var payload = JsonSerializer.Serialize(new { template = BereichsVorlage });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("api/template", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Home Assistant Bereiche konnten nicht geladen werden: HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "[]" : text);
            var bereiche = new List<HaBereich>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var id = element.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = element.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString() : null;
                var entitaeten = element.TryGetProperty("entitaeten", out var entEl) && entEl.ValueKind == JsonValueKind.Array
                    ? entEl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                    : new List<string>();
                bereiche.Add(new HaBereich(id, string.IsNullOrWhiteSpace(name) ? id : name, entitaeten));
            }

            ResetCircuit();
            return bereiche;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Die Vorlage lief, aber ihre Antwort ist kein Feld von Bereichen — kein
            // Grund, den Schutzschalter für alle anderen Abrufe zu öffnen.
            _logger.LogDebug(ex, "Home Assistant Bereiche: Antwort der Vorlage nicht lesbar.");
            return null;
        }
        catch (Exception ex)
        {
            TryOpenCircuit();
            _logger.LogDebug(ex, "Home Assistant Bereiche konnten nicht geladen werden.");
            return null;
        }
    }

    /// <summary>Die Vorlage für <see cref="GetBereicheAsync"/>: ein JSON-Feld mit einem Eintrag je Bereich.</summary>
    public const string BereichsVorlage =
        "[{% for a in areas() %}{{ {'id': a, 'name': area_name(a), 'entitaeten': area_entities(a)} | to_json }}"
        + "{% if not loop.last %},{% endif %}{% endfor %}]";

    /// <summary>
    /// Fork AI (A-003 Etappe B, 03.10.2026): Der Verlauf EINER Entität über
    /// <c>GET /api/history/period</c>.
    /// </summary>
    /// <remarks>
    /// Mit <c>minimal_response</c> und <c>no_attributes</c> — nur Zustand und
    /// Zeitpunkt, so schnell, wie Home Assistant ihn liefern kann. <c>null</c>
    /// heisst: nicht eingerichtet oder nicht erreichbar. Im Testbetrieb gibt es
    /// keinen Verlauf (leere Liste).
    /// </remarks>
    public async Task<IReadOnlyList<HaVerlaufsPunkt>?> GetVerlaufAsync(
        HomeAssistantSettings settings,
        string entityId,
        DateTime vonUtc,
        DateTime bisUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return null;
        if (DemoData.IsEnabled) return Array.Empty<HaVerlaufsPunkt>();
        if (!settings.IsConfigured || IsCircuitOpen()) return null;

        static string Zeit(DateTime t) => Uri.EscapeDataString(
            DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            var client = CreateClient(settings);
            var pfad = $"api/history/period/{Zeit(vonUtc)}?filter_entity_id={Uri.EscapeDataString(entityId)}"
                       + $"&end_time={Zeit(bisUtc)}&minimal_response&no_attributes";
            using var response = await client.GetAsync(pfad, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Home Assistant Verlauf {EntityId} konnte nicht geladen werden: HTTP {StatusCode}.", entityId, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var punkte = new List<HaVerlaufsPunkt>();
            foreach (var reihe in document.RootElement.EnumerateArray())
            {
                if (reihe.ValueKind != JsonValueKind.Array) continue;
                foreach (var element in reihe.EnumerateArray())
                {
                    var zustand = element.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                    if (zustand is null) continue;
                    if (!element.TryGetProperty("last_changed", out var z) || z.ValueKind != JsonValueKind.String
                        || !DateTime.TryParse(z.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var zeit))
                    {
                        continue;
                    }
                    punkte.Add(new HaVerlaufsPunkt(DateTime.SpecifyKind(zeit, DateTimeKind.Utc), zustand));
                }
            }

            ResetCircuit();
            return punkte.OrderBy(p => p.ZeitUtc).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            TryOpenCircuit();
            _logger.LogDebug(ex, "Home Assistant Verlauf {EntityId} konnte nicht geladen werden.", entityId);
            return null;
        }
    }

    /// <summary>
    /// Ruft einen beliebigen Home-Assistant-Dienst für eine Entität auf, etwa
    /// <c>switch.turn_on</c> für <c>switch.dosier_ph_minus</c>.
    /// </summary>
    /// <remarks>
    /// Grow OS hat selbst keine Anschlüsse — alles Schaltbare hängt an Home
    /// Assistant. Dieselbe Strecke, die schon die Push-Nachrichten geht, nur mit
    /// <c>entity_id</c> statt Titel und Text.
    /// </remarks>
    /// <summary>
    /// Ruft einen Home-Assistant-Dienst OHNE Entitaet — etwa ein pyscript, das seine
    /// Ziele selbst kennt. <see cref="CallEntityServiceAsync"/> steigt bei leerer
    /// entity_id aus, und das ist dort auch richtig: ein Schalter ohne Entitaet waere
    /// ein Tippfehler. Hier ist die fehlende Entitaet der Normalfall.
    /// </summary>
    public async Task<bool> CallServiceAsync(
        HomeAssistantSettings settings,
        string domain,
        string service,
        IReadOnlyDictionary<string, object>? daten = null,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(service))
        {
            return false;
        }

        if (DemoData.IsEnabled)
        {
            _logger.LogInformation("Testdaten: {Domain}.{Service} ohne Entitaet — nicht ausgefuehrt.", domain, service);
            return true;
        }

        try
        {
            var client = CreateClient(settings, Dienstfrist);
            var payload = JsonSerializer.Serialize(daten ?? new Dictionary<string, object>());
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"api/services/{domain}/{service}", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Home Assistant {Domain}.{Service} schlug fehl: HTTP {StatusCode}.",
                    domain, service, (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Home Assistant {Domain}.{Service} schlug fehl.", domain, service);
            return false;
        }
    }

    /// <remarks>
    /// <c>true</c> nur, wenn Home Assistant den Aufruf bestätigt hat. Eine
    /// Zeitüberschreitung ist hier <c>false</c> — wer unterscheiden muss, ob
    /// gar nichts gesendet wurde oder nur die Antwort ausblieb, nimmt
    /// <see cref="RufeEntitaetsDienstAsync"/>.
    /// </remarks>
    public async Task<bool> CallEntityServiceAsync(
        HomeAssistantSettings settings,
        string domain,
        string service,
        string entityId,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, object>? daten = null)
        => await RufeEntitaetsDienstAsync(settings, domain, service, entityId, cancellationToken, daten)
            == HaDienstAntwort.Angenommen;

    /// <summary>
    /// Wie <see cref="CallEntityServiceAsync"/>, aber mit dem Unterschied
    /// zwischen „abgelehnt" und „Antwort blieb aus".
    /// </summary>
    /// <remarks>
    /// Bei <see cref="HaDienstAntwort.Unbestaetigt"/> war der Auftrag
    /// unterwegs — ob er wirkte, sagt nur ein Blick auf den Zustand danach.
    /// Raten wäre in beide Richtungen falsch: „nicht angenommen" verschweigt
    /// ein geschaltetes Gerät, „geschaltet" ein verworfenes.
    /// </remarks>
    public async Task<HaDienstAntwort> RufeEntitaetsDienstAsync(
        HomeAssistantSettings settings,
        string domain,
        string service,
        string entityId,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, object>? daten = null)
    {
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(domain)
            || string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(entityId))
        {
            return HaDienstAntwort.Abgelehnt;
        }

        // Im Testdatenmodus geht kein Aufruf ins Netz — aber er wird
        // FESTGEHALTEN. Vorher meldete dieser Zweig blanken Erfolg und
        // veraenderte nichts; damit war alles, was nach dem Schalten kommt, im
        // Testbestand nicht pruefbar (siehe Demoschaltbrett).
        if (DemoData.IsEnabled)
        {
            // Eine Entitaet, die es im Testbestand nicht gibt, laesst sich auch
            // nicht schalten — sonst verdeckt der Testbetrieb jeden Tippfehler
            // in einer Kennung. Genau das ist beim Kuehler schon passiert.
            if (!DemoData.KennstEntitaet(entityId))
            {
                _logger.LogWarning(
                    "Testdaten: {Entity} gibt es nicht — {Domain}.{Service} wird nicht ausgefuehrt.",
                    entityId, domain, service);
                return HaDienstAntwort.Abgelehnt;
            }

            var verstanden = Demoschaltbrett.Schalten(domain, service, entityId, daten);
            _logger.LogInformation(
                "Testdaten: {Domain}.{Service} fuer {Entity} — {Ergebnis}.",
                domain, service, entityId,
                verstanden ? "im Schaltbrett vermerkt" : "unbekannter Dienst, nicht vermerkt");
            return verstanden ? HaDienstAntwort.Angenommen : HaDienstAntwort.Abgelehnt;
        }

        try
        {
            var client = CreateClient(settings, Dienstfrist);
            // Manche Dienste brauchen mehr als die Entitaet: ein Thermostat will
            // `temperature`, ein Zahlenfeld `value`. Deshalb ein Woerterbuch statt
            // eines festen Objekts.
            var felder = new Dictionary<string, object> { ["entity_id"] = entityId };
            if (daten is not null)
            {
                foreach (var (schluessel, wert) in daten) felder[schluessel] = wert;
            }
            var payload = JsonSerializer.Serialize(felder);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"api/services/{domain}/{service}", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Home Assistant {Domain}.{Service} für {Entity} schlug fehl: HTTP {StatusCode}.",
                    domain, service, entityId, (int)response.StatusCode);
                return HaDienstAntwort.Abgelehnt;
            }

            return HaDienstAntwort.Angenommen;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HaDienstAntwort.Abgelehnt;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Die Frist des Clients lief ab, nicht die des Aufrufers: der
            // Auftrag war unterwegs, nur die Antwort fehlt.
            _logger.LogWarning(
                "Home Assistant {Domain}.{Service} für {Entity}: keine Antwort binnen {Sekunden} s — unbestätigt, nicht abgelehnt.",
                domain, service, entityId, (int)Dienstfrist.TotalSeconds);
            return HaDienstAntwort.Unbestaetigt;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Home Assistant {Domain}.{Service} für {Entity} schlug fehl.", domain, service, entityId);
            return HaDienstAntwort.Abgelehnt;
        }
    }

    /// <summary>
    /// Calls a Home Assistant notify service (e.g. <c>notify.mobile_app_pixel</c>) to push a
    /// message to the user's device. Returns false when HA is unreachable or the call fails.
    /// </summary>
    /// <param name="clickPath">
    /// Wohin der Tipp auf die Meldung fuehren soll, als HA-interner Pfad
    /// (z. B. <c>/local_grow_os/aufgaben</c>). Leer = kein Ziel, dann oeffnet
    /// die App wie bisher ihre Startseite.
    /// </param>
    public async Task<bool> SendNotificationAsync(
        HomeAssistantSettings settings,
        string notifyService,
        string title,
        string message,
        CancellationToken cancellationToken = default,
        string? clickPath = null)
    {
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(notifyService))
        {
            return false;
        }

        var (domain, service) = SplitService(notifyService);
        try
        {
            var client = CreateClient(settings, Dienstfrist);
            // Ohne Ziel-Pfad ist das Payload byte-gleich wie frueher. Mit Pfad
            // bekommt die Companion-App ein Ziel: `clickAction` liest Android,
            // `url` liest iOS — die jeweils fremde Taste wird ignoriert, also
            // koennen beide gesetzt werden. Vorher landete jeder Tipp auf der
            // HA-Startseite, und der Nutzer musste sich selbst zur Warnung
            // durchklicken.
            var payload = string.IsNullOrWhiteSpace(clickPath)
                ? JsonSerializer.Serialize(new { title, message })
                : JsonSerializer.Serialize(new { title, message, data = new { clickAction = clickPath, url = clickPath } });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"api/services/{domain}/{service}", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Home Assistant notify {Service} schlug fehl: HTTP {StatusCode}.",
                    notifyService,
                    (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Home Assistant notify {Service} schlug fehl.", notifyService);
            return false;
        }
    }

    /// <summary>
    /// Lists the available Home Assistant notify services (<c>GET /api/services</c>, domain
    /// <c>notify</c>) as fully-qualified ids like <c>notify.mobile_app_pixel</c>, so the UI can
    /// offer a dropdown. Returns an empty list when HA is unreachable.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetNotifyServicesAsync(
        HomeAssistantSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || IsCircuitOpen())
        {
            return Array.Empty<string>();
        }

        try
        {
            var client = CreateClient(settings);
            using var response = await client.GetAsync("api/services", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<string>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var services = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("domain", out var domainEl)
                    || !string.Equals(domainEl.GetString(), "notify", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (element.TryGetProperty("services", out var servicesEl) && servicesEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var service in servicesEl.EnumerateObject())
                    {
                        services.Add($"notify.{service.Name}");
                    }
                }
            }

            ResetCircuit();
            return services.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Home Assistant notify-Services konnten nicht geladen werden.");
            return Array.Empty<string>();
        }
    }

    private static (string Domain, string Service) SplitService(string value)
    {
        var trimmed = value.Trim();
        var separator = trimmed.IndexOf('.');
        return separator > 0
            ? (trimmed[..separator], trimmed[(separator + 1)..])
            : ("notify", trimmed);
    }

    /// <summary>
    /// Ergaenzt einen Liter-Zustand aus dem cm-Sensor, wenn das Hydro-System
    /// des Zelts kalibriert ist.
    /// </summary>
    /// <remarks>
    /// Ein vorhandener echter Liter-Sensor gewinnt: wer beides hat, misst
    /// direkt und braucht keine Gerade.
    /// </remarks>
    private void AddLitersFromCentimeters(Dictionary<string, HomeAssistantState> states, Tent tent)
    {
        if (_hydroSetups is null) return;
        if (states.ContainsKey("reservoir-level")) return;
        if (!states.TryGetValue("reservoir-level-cm", out var cm) || cm.NumericValue is not { } wert) return;

        var system = _hydroSetups.GetHydroSetupsByTent(tent.Id).FirstOrDefault(
            setup => ReservoirVolume.IsCalibrated(setup.LevelSensorEmptyRaw, setup.LevelSensorFullRaw, setup.LevelSensorFullLiters));
        if (system is null) return;

        if (ReservoirVolume.Liters(wert, system.LevelSensorEmptyRaw, system.LevelSensorFullRaw, system.LevelSensorFullLiters) is not { } liter)
        {
            return;
        }

        states["reservoir-level"] = new HomeAssistantState
        {
            EntityId = cm.EntityId,
            State = liter.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            NumericValue = liter,
            UnitOfMeasurement = "L",
            FriendlyName = cm.FriendlyName is { } name ? $"{name} (aus cm gerechnet)" : "Wasserstand (aus cm gerechnet)",
            LastChanged = cm.LastChanged,
        };
    }

    /// <summary>
    /// Ein angemeldeter Client für Home Assistant.
    /// </summary>
    /// <remarks>
    /// Fork AI (forkai.69): öffentlich, weil das Anlegen von Rechenwerten über
    /// den Einrichtungsdialog läuft — den gibt es nur als REST, und er besteht
    /// aus drei Aufrufen, die sich dieselbe Adresse und dasselbe Token teilen
    /// müssen. Ein zweiter Aufbau daneben würde die Feinheit mit dem
    /// Schrägstrich am Ende verlieren, die den Add-on-Pfad rettet.
    /// <para>Ohne Angabe gilt die kurze <see cref="Lesefrist"/>. Wer schreibt —
    /// Dienste, Automations-Konfiguration, Einstellungsdialoge —, gibt
    /// <see cref="Dienstfrist"/> mit.</para>
    /// </remarks>
    public HttpClient CreateClient(HomeAssistantSettings settings) => CreateClient(settings, Lesefrist);

    /// <summary>Ein angemeldeter Client mit eigener Frist.</summary>
    public HttpClient CreateClient(HomeAssistantSettings settings, TimeSpan frist)
    {
        var client = _httpClientFactory.CreateClient(nameof(HomeAssistantService));
        // Trailing slash + relative request paths (no leading slash) so a base with a
        // path segment survives — e.g. the add-on's http://supervisor/core, where a
        // leading-slash path would otherwise drop "/core" and hit the wrong endpoint.
        client.BaseAddress = new Uri(NormalizeBaseUrl(settings.BaseUrl!) + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        client.Timeout = frist;
        return client;
    }

    /// <summary>
    /// When the breaker is open, we stopped calling Home Assistant because the last
    /// attempts failed. Returns the moment we will try again, or <c>null</c> while
    /// calls are going through.
    ///
    /// Exposed so the UI can say so once, at the top of the page, instead of every
    /// tile inventing its own way to look broken. The grow keeps running when Home
    /// Assistant does not — the values just stop being fresh.
    /// </summary>
    public DateTime? UnreachableUntilUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _circuitOpenUntilTicks);
            return ticks > DateTime.UtcNow.Ticks ? new DateTime(ticks, DateTimeKind.Utc) : null;
        }
    }

    private bool IsCircuitOpen()
        => Interlocked.Read(ref _circuitOpenUntilTicks) > DateTime.UtcNow.Ticks;

    private bool TryOpenCircuit()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var openUntilTicks = DateTime.UtcNow.Add(BackoffWindow).Ticks;

        while (true)
        {
            var current = Interlocked.Read(ref _circuitOpenUntilTicks);
            if (current > nowTicks)
            {
                return false;
            }

            var observed = Interlocked.CompareExchange(ref _circuitOpenUntilTicks, openUntilTicks, current);
            if (observed == current)
            {
                return true;
            }
        }
    }

    private void ResetCircuit()
        => Interlocked.Exchange(ref _circuitOpenUntilTicks, 0);

    private static string NormalizeBaseUrl(string value)
        => value.Trim().TrimEnd('/');
}

/// <summary>Wie Home Assistant auf einen Dienstaufruf reagiert hat.</summary>
public enum HaDienstAntwort
{
    /// <summary>Home Assistant hat den Aufruf bestätigt (HTTP 2xx).</summary>
    Angenommen,

    /// <summary>
    /// Abgelehnt oder gar nicht gesendet — Verbindung, Anmeldung, Entität oder
    /// Daten stimmen nicht. Geschaltet wurde nichts.
    /// </summary>
    Abgelehnt,

    /// <summary>
    /// Gesendet, aber die Antwort kam nicht binnen der Frist. Ob das Gerät
    /// geschaltet hat, sagt nur der Zustand danach.
    /// </summary>
    Unbestaetigt,
}

/// <summary>Fork AI (A-003 Etappe B, 03.10.2026): Ein Bereich (Area) in Home Assistant.</summary>
/// <param name="Entitaeten">Die Entitäten im Bereich, auch die über ein Gerät zugeordneten.</param>
public sealed record HaBereich(string Id, string Name, IReadOnlyList<string> Entitaeten);

/// <summary>Fork AI (A-003 Etappe B, 03.10.2026): Ein Zustand im Verlauf einer Entität.</summary>
public sealed record HaVerlaufsPunkt(DateTime ZeitUtc, string Zustand);
