using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.22): Holt Geräte- und Entitätsregister von Home Assistant.
/// </summary>
/// <remarks>
/// <para><b>Warum WebSocket.</b> Die REST-Schnittstelle kennt nur Zustände
/// (<c>/api/states</c>) — Name und Wert, sonst nichts. Welche Entität zu welchem
/// Gerät gehört und welche <c>unique_id</c> sie trägt, steht allein in den
/// Registern, und die gibt es nur über <c>/api/websocket</c>. Ohne sie muss der
/// Fork aus Namen raten, und das ging in einer echten Anlage daneben.</para>
///
/// <para><b>Fehler sind hier normal.</b> Ein altes Home Assistant, ein Token ohne
/// Rechte, eine Anlage, die den Socket nicht durchlässt: dann kommt eine leere
/// Liste zurück und die Ableitung fällt auf die Namensvermutung. Der Aufrufer soll
/// nicht abbrechen — gröber ist besser als nichts.</para>
/// </remarks>
public sealed class HomeAssistantRegistryService
{
    private static readonly TimeSpan Zeitlimit = TimeSpan.FromSeconds(15);
    private const int PuffergroesseBytes = 64 * 1024;

    private readonly ILogger<HomeAssistantRegistryService> _log;

    public HomeAssistantRegistryService(ILogger<HomeAssistantRegistryService> log) => _log = log;

    /// <summary>
    /// Herkunft je Entity-ID. Leer, wenn Home Assistant nicht eingerichtet ist oder
    /// der Socket nichts hergibt — dann greift die Namensvermutung.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, HerkunftEintrag>> HerkunftAsync(
        HomeAssistantSettings settings,
        CancellationToken ct)
    {
        if (!settings.IsConfigured) return Leer();

        try
        {
            using var abbruch = CancellationTokenSource.CreateLinkedTokenSource(ct);
            abbruch.CancelAfter(Zeitlimit);

            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(SocketAdresse(settings.BaseUrl!), abbruch.Token);

            // Handshake: HA schickt auth_required, wir antworten mit dem Token.
            await LesenAsync(socket, abbruch.Token);
            await SendenAsync(socket, new { type = "auth", access_token = settings.AccessToken }, abbruch.Token);
            var antwort = await LesenAsync(socket, abbruch.Token);
            if (Typ(antwort) != "auth_ok")
            {
                _log.LogInformation("Home Assistant nimmt das Token am WebSocket nicht an — Geräte werden geraten.");
                return Leer();
            }

            await SendenAsync(socket, new { id = 1, type = "config/entity_registry/list" }, abbruch.Token);
            var entitaeten = await ErgebnisAsync(socket, 1, abbruch.Token);

            await SendenAsync(socket, new { id = 2, type = "config/device_registry/list" }, abbruch.Token);
            var geraete = await ErgebnisAsync(socket, 2, abbruch.Token);

            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

            return Zusammenfuehren(entitaeten, geraete);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException or UriFormatException)
        {
            _log.LogInformation(ex, "Register von Home Assistant nicht erreichbar — Geräte werden aus Namen geraten.");
            return Leer();
        }
    }

    /// <summary>
    /// Aus beiden Registern die Herkunft je Entität bauen. Rein rechnend, damit der
    /// Test echte HA-Antworten durchschieben kann.
    /// </summary>
    public static IReadOnlyDictionary<string, HerkunftEintrag> Zusammenfuehren(
        JsonElement? entitaeten,
        JsonElement? geraete)
    {
        var namen = new Dictionary<string, string>(StringComparer.Ordinal);
        var modelle = new Dictionary<string, string>(StringComparer.Ordinal);
        var eltern = new Dictionary<string, string>(StringComparer.Ordinal);
        if (geraete is { ValueKind: JsonValueKind.Array } geraeteliste)
        {
            foreach (var geraet in geraeteliste.EnumerateArray())
            {
                var id = Text(geraet, "id");
                if (id is null) continue;
                // name_by_user ist der Name, den der Nutzer vergeben hat — der gilt.
                namen[id] = Text(geraet, "name_by_user") ?? Text(geraet, "name") ?? id;
                // via_device_id: der Controller, an dem dieses Gerät hängt.
                if (Text(geraet, "via_device_id") is { } via) eltern[id] = via;

                // Hersteller und Modell — die Unterzeile der Geräteliste.
                var modell = string.Join(' ', new[] { Text(geraet, "manufacturer"), Text(geraet, "model") }
                    .Where(teil => !string.IsNullOrWhiteSpace(teil)));
                if (modell.Length > 0) modelle[id] = modell;
            }
        }

        var treffer = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase);
        if (entitaeten is { ValueKind: JsonValueKind.Array } entitaetenliste)
        {
            foreach (var eintrag in entitaetenliste.EnumerateArray())
            {
                var entityId = Text(eintrag, "entity_id");
                if (entityId is null) continue;

                var deviceId = Text(eintrag, "device_id");
                var geraetename = deviceId is not null && namen.TryGetValue(deviceId, out var name) ? name : null;

                string? viaId = null;
                string? viaName = null;
                string? viaModell = null;
                if (deviceId is not null && eltern.TryGetValue(deviceId, out var via))
                {
                    viaId = via;
                    viaName = namen.TryGetValue(via, out var name2) ? name2 : null;
                    viaModell = modelle.TryGetValue(via, out var modell2) ? modell2 : null;
                }

                treffer[entityId] = new HerkunftEintrag(
                    entityId,
                    deviceId,
                    // Der Name der Entität ist der bessere Rückfall als die rohe Id.
                    geraetename ?? Text(eintrag, "name") ?? Text(eintrag, "original_name"),
                    Text(eintrag, "unique_id"),
                    viaId,
                    viaName,
                    deviceId is not null && modelle.TryGetValue(deviceId, out var eigenes) ? eigenes : null,
                    viaModell);
            }
        }

        return treffer;
    }

    // ---------------------------------------------------------------- Kleinkram

    private static Dictionary<string, HerkunftEintrag> Leer() => new(StringComparer.OrdinalIgnoreCase);

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var wert) && wert.ValueKind == JsonValueKind.String
            ? wert.GetString()
            : null;

    private static string? Typ(JsonElement element) => Text(element, "type");

    /// <summary>Die WebSocket-Adresse zur eingestellten Basis — öffentlich, weil der Test sie prüft.</summary>
    public static Uri SocketAdresse(string baseUrl)
    {
        var basis = baseUrl.Trim().TrimEnd('/');
        var schema = basis.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "wss://" : "ws://";
        var ohneSchema = basis
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
        return new Uri($"{schema}{ohneSchema}/api/websocket");
    }

    private static async Task SendenAsync(ClientWebSocket socket, object nachricht, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(nachricht);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task<JsonElement> LesenAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var puffer = new byte[PuffergroesseBytes];
        using var speicher = new MemoryStream();

        WebSocketReceiveResult ergebnis;
        do
        {
            ergebnis = await socket.ReceiveAsync(puffer, ct);
            speicher.Write(puffer, 0, ergebnis.Count);
        }
        while (!ergebnis.EndOfMessage);

        return JsonDocument.Parse(Encoding.UTF8.GetString(speicher.ToArray())).RootElement.Clone();
    }

    /// <summary>Auf die Antwort zu einer Anfrage warten; Zwischenrufe von HA werden übersprungen.</summary>
    private static async Task<JsonElement?> ErgebnisAsync(ClientWebSocket socket, int id, CancellationToken ct)
    {
        for (var versuch = 0; versuch < 20; versuch++)
        {
            var nachricht = await LesenAsync(socket, ct);
            if (Typ(nachricht) != "result") continue;
            if (!nachricht.TryGetProperty("id", out var kennung) || kennung.GetInt32() != id) continue;

            return nachricht.TryGetProperty("result", out var ergebnis) ? ergebnis.Clone() : null;
        }

        return null;
    }
}
