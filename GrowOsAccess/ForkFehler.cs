using System.Text;
using System.Text.Json;

namespace GrowOsAccess;

/// <summary>Was Grow OS auf eine Anfrage geantwortet hat — auch wenn es Nein war.</summary>
/// <param name="Status">Der HTTP-Status.</param>
/// <param name="Text">Der Rumpf der Antwort, roh.</param>
public sealed record ForkAntwort(int Status, string Text)
{
    public bool Erfolg => Status is >= 200 and < 300;
}

/// <summary>
/// Eine Absage von Grow OS als Satz, den ein Assistent weitergeben kann.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026): Grow OS antwortet im festen Fehlerformat
/// (<c>ApiError</c>: <c>code</c>, <c>message</c>, <c>fieldErrors</c>), und die
/// Meldungen des Schlüsselzugriffs sind dort schon deutsch geschrieben —
/// „Dafür fehlt die Freigabe für Stufe ‚Grow planen'". Sie werden deshalb
/// weitergereicht, nicht neu erfunden; dazu kommt ein Satz, was jetzt zu tun ist.
/// Eine zweite Formulierung hier wäre eine zweite Wahrheit.</para>
///
/// <para>Keine Ausnahme verlässt diese Klasse: ein Assistent, der statt einer
/// Antwort eine Stapelverfolgung bekommt, rät — und rät beim Dosieren falsch.</para>
/// </remarks>
public static class ForkFehler
{
    /// <summary>Der Hinweis, wenn ein Werkzeug einen Fork-Schlüssel braucht und keiner da ist.</summary>
    public const string SchluesselNoetig =
        "Zum Eintragen braucht es einen Schlüssel aus Grow OS: Einrichtung → KI-Assistent → Zugriff & Schlüssel. "
        + "Diesen statt des MCP-Schlüssels in den Connector eintragen.";

    /// <summary>Eine Absage in einen Satz übersetzen.</summary>
    /// <param name="status">HTTP-Status der Antwort.</param>
    /// <param name="rumpf">Der Rumpf, wie er kam — darf leer oder kaputt sein.</param>
    /// <param name="was">Was versucht wurde, etwa „Messung eintragen".</param>
    public static string Text(int status, string? rumpf, string was)
    {
        var (code, meldung, felder) = Lesen(rumpf);
        var text = new StringBuilder();
        text.Append($"Nicht ausgeführt ({was}): ");

        switch (status)
        {
            case 401 when code == "ki_schluessel_fehlt":
                text.Append(SchluesselNoetig);
                return text.ToString();

            case 401:
                text.Append(meldung ?? "Grow OS hat den Schlüssel nicht angenommen.");
                text.Append(" Der Betreiber kann in Grow OS unter Einrichtung → KI-Assistent → Zugriff & Schlüssel "
                            + "nachsehen, ob der Schlüssel noch besteht, und sonst einen neuen anlegen.");
                return text.ToString();

            case 403 when code == "admin_access_required":
                // Die Absage, die ein Nachbar-Add-on bisher bei jedem Schreiben
                // bekam. Mit einem Schlüssel kommt sie nur, wenn Grow OS den
                // Schlüsselzugriff noch nicht kennt.
                text.Append("Grow OS lässt aus dem Add-on-Netz nur lesen. Den Zugriff für KI-Assistenten gibt es "
                            + "erst ab Grow OS Fork AI 2.0.0-forkai.163 — bitte Grow OS aktualisieren.");
                return text.ToString();

            case 404 when code == "endpoint_not_found":
                text.Append("Diesen Weg kennt Grow OS nicht. Vermutlich ist Grow OS älter als dieser Grow MCP — bitte Grow OS Fork AI aktualisieren.");
                return text.ToString();

            case 404:
                text.Append(meldung ?? "Grow OS kennt diesen Weg nicht. Vermutlich ist Grow OS älter als dieser Grow MCP — bitte beide aktualisieren.");
                return text.ToString();

            case 400 or 422 when felder.Count > 0:
                text.Append(meldung ?? "Grow OS hat die Angaben nicht angenommen.");
                text.Append(' ').Append(string.Join(" ", felder));
                return text.ToString();
        }

        if (meldung is not null)
        {
            text.Append(meldung);
            if (status == 429) text.Append(" Bitte später noch einmal versuchen.");
            return text.ToString();
        }

        text.Append($"Grow OS antwortete mit {status}.");
        return text.ToString();
    }

    /// <summary>Code, Meldung und Feldfehler aus dem Fehlerformat — was fehlt, bleibt leer.</summary>
    private static (string? Code, string? Meldung, List<string> Felder) Lesen(string? rumpf)
    {
        var felder = new List<string>();
        if (string.IsNullOrWhiteSpace(rumpf)) return (null, null, felder);

        try
        {
            using var json = JsonDocument.Parse(rumpf);
            var wurzel = json.RootElement;
            if (wurzel.ValueKind != JsonValueKind.Object) return (null, null, felder);

            string? Feld(string name)
                => wurzel.TryGetProperty(name, out var wert) && wert.ValueKind == JsonValueKind.String
                    ? wert.GetString()
                    : null;

            // ASP.NETs eigenes Format (ValidationProblemDetails) heisst "errors",
            // das von Grow OS "fieldErrors". Beide lesen: eine Absage, die vor
            // dem Controller entsteht, kommt im ersten.
            foreach (var name in new[] { "fieldErrors", "errors" })
            {
                if (!wurzel.TryGetProperty(name, out var liste) || liste.ValueKind != JsonValueKind.Object) continue;
                foreach (var feld in liste.EnumerateObject())
                {
                    if (feld.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var eintrag in feld.Value.EnumerateArray())
                    {
                        if (eintrag.ValueKind == JsonValueKind.String) felder.Add($"{feld.Name}: {eintrag.GetString()}");
                    }
                }
            }

            return (Feld("code"), Feld("message") ?? Feld("title"), felder);
        }
        catch (JsonException)
        {
            return (null, null, felder);
        }
    }
}
