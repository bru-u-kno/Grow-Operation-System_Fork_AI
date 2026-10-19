using System.Net;

namespace GrowOsAccess;

/// <summary>Cultivate Control antwortet nicht so, wie es soll.</summary>
/// <remarks>
/// Eigene Ausnahme, damit ein Werkzeug den Fehler als lesbaren Satz weitergeben
/// kann statt als Stapelverfolgung. Ein Modell, das „404" liest, rät weiter; eines,
/// das „Grow mit Id 7 existiert nicht" liest, fragt nach.
/// </remarks>
public sealed class GrowOsException(string message, bool nichtGefunden = false) : Exception(message)
{
    /// <summary>
    /// Cultivate Control lief, kannte den Weg aber nicht.
    /// </summary>
    /// <remarks>
    /// Der Unterschied entscheidet, ob ein Werkzeug weitermachen darf. „Zu
    /// diesem Grow gibt es keinen Pheno Hunt" ist eine Antwort und soll die
    /// Pflanzenliste daneben nicht mitreissen. „Cultivate Control ist nicht erreichbar"
    /// dagegen muss durchschlagen — sonst sieht ein halb leeres Ergebnis aus wie
    /// ein vollstaendiges.
    /// </remarks>
    public bool NichtGefunden { get; } = nichtGefunden;
}

/// <summary>
/// Spricht mit Cultivate Control — lesend, und mit einem Fork-Schlüssel auch schreibend.
/// </summary>
/// <remarks>
/// <para>Bis forkai.163 bewusst nur <c>GET</c>: Cultivate Control liess aus dem internen
/// Add-on-Netz nur Lesezugriffe zu. Seit dem Zugriff für KI-Assistenten (A-003)
/// nimmt Cultivate Control auch schreibende Anfragen an — aber nur mit einem Schlüssel,
/// den der Betreiber dort angelegt hat, und nur in den Stufen, die er angehakt
/// hat. Dieser Leser entscheidet darüber nichts: er reicht den Schlüssel der
/// laufenden Anfrage (<see cref="IForkSchluesselQuelle"/>) bei jeder Anfrage als
/// <c>Authorization: Bearer gok_…</c> mit, und Cultivate Control sagt Ja oder Nein.</para>
///
/// <para>Ohne Schlüssel geht keine Kopfzeile mit — der MCP-Schlüssel des Add-ons
/// verlässt dieses Programm nie.</para>
/// </remarks>
public sealed class GrowOsReader(HttpClient http, GrowOsDiscovery discovery, IForkSchluesselQuelle? schluessel = null)
{
    private readonly IForkSchluesselQuelle _schluessel = schluessel ?? KeinForkSchluessel.Instanz;

    /// <summary>Ist die laufende Anfrage mit einem Fork-Schlüssel gekommen?</summary>
    public bool HatForkSchluessel => ForkSchluessel.HatForm(_schluessel.Schluessel);

    /// <summary>Einen Pfad abrufen und den rohen JSON-Text zurückgeben.</summary>
    /// <param name="pfad">Etwa <c>api/grows?archived=false</c>, ohne führenden Schrägstrich.</param>
    public async Task<string> LesenAsync(string pfad, CancellationToken cancellationToken)
    {
        var antwort = await SendenAsync(HttpMethod.Get, pfad, null, cancellationToken);

        if (antwort.Status == (int)HttpStatusCode.NotFound)
        {
            throw new GrowOsException($"Cultivate Control kennt das nicht: {pfad}", nichtGefunden: true);
        }

        if (!antwort.Erfolg)
        {
            // Mit einem Fork-Schlüssel kann auch ein Lesezugriff abgewiesen
            // werden (Zugriff aus, Schlüssel gesperrt). Die Begründung von Cultivate Control
            // ist dann mehr wert als die blosse Zahl.
            throw new GrowOsException(antwort.Status is 401 or 403 or 429
                ? ForkFehler.Text(antwort.Status, antwort.Text, $"lesen {pfad}")
                : $"Cultivate Control antwortete mit {antwort.Status} auf {pfad}.");
        }

        return antwort.Text;
    }

    /// <summary>
    /// Eine Anfrage schicken und die Antwort zurückgeben, wie sie ist — auch eine Absage.
    /// </summary>
    /// <remarks>
    /// Wirft nur, wenn Cultivate Control gar nicht erreichbar ist. Ein 403 ist eine
    /// Antwort, die der Aufrufer in einen Satz übersetzt (<see cref="ForkFehler"/>).
    /// </remarks>
    /// <param name="methode">GET, POST, PUT, PATCH …</param>
    /// <param name="pfad">Ohne führenden Schrägstrich.</param>
    /// <param name="json">Der Rumpf als JSON, oder <c>null</c> für keinen.</param>
    public async Task<ForkAntwort> SendenAsync(HttpMethod methode, string pfad, string? json, CancellationToken cancellationToken)
    {
        var verbindung = await discovery.FindenAsync(cancellationToken);
        if (!verbindung.Erreichbar || verbindung.Basis is null)
        {
            throw new GrowOsException(verbindung.Meldung);
        }

        using var anfrage = Anfrage(methode, $"{verbindung.Basis}/{pfad.TrimStart('/')}");
        if (json is not null)
        {
            anfrage.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        using var antwort = await http.SendAsync(anfrage, cancellationToken);
        return new ForkAntwort((int)antwort.StatusCode, await antwort.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// Fork AI (Paket D, 18.10.2026): Wie <see cref="SendenAsync"/>, aber vorsichtig mit der Antwort.
    /// </summary>
    /// <remarks>
    /// <para>Für den allgemeinen Weg (<c>fork_ausfuehren</c>), der jede Funktion von Cultivate Control rufen kann. Der Rumpf wird
    /// erst gelesen, wenn der Inhaltstyp Text oder JSON ist — ein Bild oder eine Sicherungsdatei kommt nie in den
    /// Speicher, nur ihr Typ. Text wird nach <paramref name="maxBytes"/> abgeschnitten (<see cref="ForkAntwort.Gekuerzt"/>).</para>
    /// </remarks>
    public async Task<ForkAntwort> SendenBegrenztAsync(HttpMethod methode, string pfad, string? json, int maxBytes, CancellationToken cancellationToken)
    {
        var verbindung = await discovery.FindenAsync(cancellationToken);
        if (!verbindung.Erreichbar || verbindung.Basis is null)
        {
            throw new GrowOsException(verbindung.Meldung);
        }

        using var anfrage = Anfrage(methode, $"{verbindung.Basis}/{pfad.TrimStart('/')}");
        if (json is not null)
        {
            anfrage.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        using var antwort = await http.SendAsync(anfrage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var typ = antwort.Content.Headers.ContentType?.MediaType;
        var leer = new ForkAntwort((int)antwort.StatusCode, string.Empty, typ);
        if (!leer.IstText) return leer;

        await using var strom = await antwort.Content.ReadAsStreamAsync(cancellationToken);
        var puffer = new byte[maxBytes + 1];
        var gelesen = 0;
        while (gelesen < puffer.Length)
        {
            var n = await strom.ReadAsync(puffer.AsMemory(gelesen, puffer.Length - gelesen), cancellationToken);
            if (n == 0) break;
            gelesen += n;
        }

        var gekuerzt = gelesen > maxBytes;
        var text = System.Text.Encoding.UTF8.GetString(puffer, 0, Math.Min(gelesen, maxBytes));
        return new ForkAntwort((int)antwort.StatusCode, text, typ, gekuerzt);
    }

    /// <summary>
    /// Eine Anfrage bauen — mit dem Fork-Schlüssel der laufenden Anfrage, falls es einen gibt.
    /// </summary>
    /// <remarks>
    /// Die EINE Stelle, an der der Schlüssel in eine Anfrage kommt. Er wird hier
    /// gelesen, eingesetzt und vergessen; kein Feld, kein Protokoll.
    /// </remarks>
    private HttpRequestMessage Anfrage(HttpMethod methode, string adresse)
    {
        var anfrage = new HttpRequestMessage(methode, adresse);
        if (_schluessel.Schluessel is { } wert && ForkSchluessel.HatForm(wert))
        {
            anfrage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", wert);
        }
        return anfrage;
    }

    /// <summary>Eine Datei abrufen — Bytes samt Medientyp.</summary>
    /// <remarks>
    /// <para>Für Fotos: die JSON-Wege liefern nur den Pfad, das Bild selbst
    /// liegt unter <c>/uploads/…</c>. Ein Modell, das die Pflanze wirklich
    /// ansehen soll, braucht die Bytes, nicht den Dateinamen.</para>
    ///
    /// <para><paramref name="maxBytes"/> ist eine harte Grenze: ein Foto aus
    /// einer modernen Kamera kann zweistellige Megabyte haben, und base64
    /// bläht das nochmal um ein Drittel auf. Was zu groß ist, wird abgelehnt
    /// statt die Antwort des Modells zu sprengen.</para>
    /// </remarks>
    public async Task<(byte[] Bytes, string MedienTyp)> DateiLesenAsync(
        string pfad,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var bild = await BildLesenAsync(pfad, maxBytes, cancellationToken);
        return (bild.Bytes, bild.MedienTyp);
    }

    /// <summary>Ein Bild samt Kopfzeilen der Antwort (etwa <c>X-Camera-Live</c>).</summary>
    /// <remarks>
    /// Fork AI (Paket C1, 18.10.2026): Sagt Cultivate Control Nein, steht der Grund im JSON-Rumpf
    /// („Für dieses Zelt ist keine Kamera-Entity hinterlegt") — der kommt in die Ausnahme,
    /// statt nur die Zahl.
    /// </remarks>
    public async Task<GrowOsBild> BildLesenAsync(
        string pfad,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var verbindung = await discovery.FindenAsync(cancellationToken);
        if (!verbindung.Erreichbar || verbindung.Basis is null)
        {
            throw new GrowOsException(verbindung.Meldung);
        }

        using var anfrage = Anfrage(HttpMethod.Get, $"{verbindung.Basis}/{pfad.TrimStart('/')}");
        using var antwort = await http.SendAsync(anfrage, cancellationToken);

        if (!antwort.IsSuccessStatusCode)
        {
            var grund = await GrundAusRumpfAsync(antwort, cancellationToken);
            if (antwort.StatusCode == HttpStatusCode.NotFound)
            {
                throw new GrowOsException(grund ?? $"Cultivate Control kennt das nicht: {pfad}", nichtGefunden: true);
            }
            throw new GrowOsException(grund is null
                ? $"Cultivate Control antwortete mit {(int)antwort.StatusCode} auf {pfad}."
                : $"Cultivate Control antwortete mit {(int)antwort.StatusCode}: {grund}");
        }

        // Erst die angekuendigte Groesse pruefen, dann erst laden: sonst liegt
        // das zu grosse Bild schon im Speicher, wenn die Grenze greift.
        if (antwort.Content.Headers.ContentLength is { } laenge && laenge > maxBytes)
        {
            throw new GrowOsException(
                $"Das Bild ist {laenge / 1024 / 1024} MB gross, die Grenze liegt bei {maxBytes / 1024 / 1024} MB.");
        }

        var bytes = await antwort.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.LongLength > maxBytes)
        {
            throw new GrowOsException(
                $"Das Bild ist {bytes.LongLength / 1024 / 1024} MB gross, die Grenze liegt bei {maxBytes / 1024 / 1024} MB.");
        }

        var typ = antwort.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(typ) || !typ.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            // Aus der Endung ableiten: der statische Dateidienst setzt den Typ
            // zwar, aber ein falscher Typ waere fuer das Modell schlimmer als
            // eine ehrliche Ablehnung.
            typ = Path.GetExtension(pfad).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => throw new GrowOsException($"Das ist kein Bild, das ich weitergeben kann: {pfad}"),
            };
        }

        var kopf = antwort.Headers.Concat(antwort.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        return new GrowOsBild(bytes, typ, kopf);
    }

    /// <summary>Die Meldung aus einer JSON-Absage (<c>message</c> oder <c>meldung</c>) — oder null.</summary>
    private static async Task<string?> GrundAusRumpfAsync(HttpResponseMessage antwort, CancellationToken cancellationToken)
    {
        if (antwort.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true) return null;
        try
        {
            var text = await antwort.Content.ReadAsStringAsync(cancellationToken);
            if (text.Length > 64 * 1024) return null;
            using var json = System.Text.Json.JsonDocument.Parse(text);
            foreach (var name in new[] { "meldung", "message" })
            {
                if (json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && json.RootElement.TryGetProperty(name, out var wert)
                    && wert.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return wert.GetString();
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }
        return null;
    }
}

/// <summary>Fork AI (Paket C1): Ein Bild aus Cultivate Control mit den Kopfzeilen der Antwort.</summary>
public sealed record GrowOsBild(byte[] Bytes, string MedienTyp, IReadOnlyDictionary<string, string> Kopf);
