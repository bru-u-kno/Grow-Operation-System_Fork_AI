using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.58): Eine offene WebSocket-Sitzung zu Home Assistant.
/// </summary>
/// <remarks>
/// <para><b>Warum das hier steht.</b> Die Handshake- und Lese-Logik lag privat
/// in <see cref="HomeAssistantRegistryService"/>. Das Anlegen von Helfern
/// braucht dieselben vier Schritte — verbinden, Token schicken, Befehl senden,
/// auf das Ergebnis mit der passenden Nummer warten. Eine zweite Abschrift
/// daneben wäre die nächste Stelle, die bei einer Änderung vergessen wird.</para>
/// <para><b>Warum WebSocket und nicht REST.</b> Helfer entstehen über
/// <c>input_number/create</c> und Geschwister — Befehle, die es nur am Socket
/// gibt. Die REST-Schnittstelle kennt nur Zustände und Dienste.</para>
/// <para><b>Was sie nicht tut.</b> Sie hält nichts offen und versucht nichts
/// erneut. Wer sie benutzt, macht seine Arbeit und schließt. Eine dauerhafte
/// Verbindung wäre eine eigene Entscheidung mit eigenem Aufräumproblem.</para>
/// </remarks>
public sealed class HomeAssistantSocket : IAsyncDisposable
{
    private const int PuffergroesseBytes = 64 * 1024;

    private readonly ClientWebSocket _socket;
    private int _naechsteNummer = 1;

    private HomeAssistantSocket(ClientWebSocket socket) => _socket = socket;

    /// <summary>Verbinden und anmelden. Gibt null zurück, wenn beides nicht klappt.</summary>
    public static async Task<HomeAssistantSocket?> OeffnenAsync(
        HomeAssistantSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return null;
        }

        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(HomeAssistantRegistryService.SocketAdresse(settings.BaseUrl), ct);

            // HA meldet sich mit auth_required, wir antworten mit dem Token.
            await LesenAsync(socket, ct);
            await SendenAsync(socket, new { type = "auth", access_token = settings.AccessToken }, ct);

            var antwort = await LesenAsync(socket, ct);
            if (Text(antwort, "type") != "auth_ok")
            {
                socket.Dispose();
                return null;
            }

            return new HomeAssistantSocket(socket);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException or UriFormatException)
        {
            socket.Dispose();
            return null;
        }
    }

    /// <summary>Das Ergebnis eines Befehls — mit Erfolg und, wenn es schiefging, dem Grund.</summary>
    public sealed record Antwort(bool Erfolg, JsonElement? Ergebnis, string? Fehler);

    /// <summary>Einen Befehl schicken und auf seine Antwort warten.</summary>
    /// <param name="typ">Der Befehlsname, etwa <c>input_number/create</c>.</param>
    /// <param name="felder">Die weiteren Felder des Befehls.</param>
    public async Task<Antwort> BefehlAsync(
        string typ, IReadOnlyDictionary<string, object?> felder, CancellationToken ct)
    {
        var nummer = _naechsteNummer++;
        var nachricht = new Dictionary<string, object?>(felder, StringComparer.Ordinal)
        {
            ["id"] = nummer,
            ["type"] = typ,
        };

        try
        {
            await SendenAsync(_socket, nachricht, ct);

            for (var versuch = 0; versuch < 30; versuch++)
            {
                var antwort = await LesenAsync(_socket, ct);
                if (Text(antwort, "type") != "result") continue;
                if (!antwort.TryGetProperty("id", out var kennung) || kennung.GetInt32() != nummer) continue;

                var erfolg = antwort.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
                if (erfolg)
                {
                    return new Antwort(true, antwort.TryGetProperty("result", out var e) ? e.Clone() : null, null);
                }

                var grund = antwort.TryGetProperty("error", out var fehler)
                    ? Text(fehler, "message") ?? Text(fehler, "code")
                    : null;
                return new Antwort(false, null, grund ?? "Home Assistant hat den Befehl abgelehnt.");
            }

            return new Antwort(false, null, "Keine Antwort von Home Assistant.");
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException)
        {
            return new Antwort(false, null, ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // Beim Schliessen ist ein Fehler folgenlos.
        }
        finally
        {
            _socket.Dispose();
        }
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

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var wert) && wert.ValueKind == JsonValueKind.String
            ? wert.GetString()
            : null;
}
