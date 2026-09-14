using GrowDiary.Web.Infrastructure;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public sealed class HomeAssistantService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan BackoffWindow = TimeSpan.FromSeconds(20);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HydroSetupRepository? _hydroSetups;
    private readonly ILogger<HomeAssistantService> _logger;
    private long _circuitOpenUntilTicks;

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
            var demo = DemoData.StatesFor(DateTime.UtcNow);
            AddLitersFromCentimeters(demo, tent);
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
            }

            if (double.TryParse(state.State, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var numeric))
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

                string? friendlyName = null, unit = null, deviceClass = null;
                if (element.TryGetProperty("attributes", out var attrs))
                {
                    if (attrs.TryGetProperty("friendly_name", out var f)) friendlyName = f.GetString();
                    if (attrs.TryGetProperty("unit_of_measurement", out var u)) unit = u.GetString();
                    if (attrs.TryGetProperty("device_class", out var d)) deviceClass = d.GetString();
                }

                entities.Add(new HomeAssistantEntity
                {
                    EntityId = entityId,
                    FriendlyName = friendlyName,
                    State = element.TryGetProperty("state", out var stateEl) ? stateEl.GetString() : null,
                    UnitOfMeasurement = unit,
                    DeviceClass = deviceClass,
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
            var client = CreateClient(settings);
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

    public async Task<bool> CallEntityServiceAsync(
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
            return false;
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
                return false;
            }

            var verstanden = Demoschaltbrett.Schalten(domain, service, entityId, daten);
            _logger.LogInformation(
                "Testdaten: {Domain}.{Service} fuer {Entity} — {Ergebnis}.",
                domain, service, entityId,
                verstanden ? "im Schaltbrett vermerkt" : "unbekannter Dienst, nicht vermerkt");
            return verstanden;
        }

        try
        {
            var client = CreateClient(settings);
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
            _logger.LogWarning(ex, "Home Assistant {Domain}.{Service} für {Entity} schlug fehl.", domain, service, entityId);
            return false;
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
            var client = CreateClient(settings);
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
    /// </remarks>
    public HttpClient CreateClient(HomeAssistantSettings settings)
    {
        var client = _httpClientFactory.CreateClient(nameof(HomeAssistantService));
        // Trailing slash + relative request paths (no leading slash) so a base with a
        // path segment survives — e.g. the add-on's http://supervisor/core, where a
        // leading-slash path would otherwise drop "/core" and hit the wrong endpoint.
        client.BaseAddress = new Uri(NormalizeBaseUrl(settings.BaseUrl!) + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        client.Timeout = RequestTimeout;
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
