using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using GrowOsAccess;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Fork AI (Paket C1, 18.10.2026): <c>kamera_bild</c> — das Livebild der Zelt-Kamera als Bild.
/// </summary>
/// <remarks>
/// Derselbe Weg wie die Oberfläche (<c>GET /api/live/tents/{id}/camera</c>): Grow OS holt das Bild über seine eigene
/// Verbindung zu Home Assistant und fällt auf das letzte gültige Bild zurück, wenn gerade keines kommt. Ob es frisch
/// ist, steht in den Kopfzeilen (<c>X-Camera-Live</c>, <c>X-Camera-Captured-At</c>) — und im Text neben dem Bild.
/// </remarks>
public sealed partial class GrowTools
{
    [McpServerTool(Name = "kamera_bild")]
    [Description("Holt das aktuelle Livebild der Zelt-Kamera eines Grows als Bild, damit du mitschauen kannst („wie sieht das Zelt gerade aus?\"). Ohne kamera die erste Kamera des Zelts; eine andere nur, wenn sie dem Zelt zugeordnet ist. Kommt gerade kein frisches Bild, liefert Grow OS das letzte gültige und sagt, von wann. Ältere Tage: tagesbilder.")]
    public async Task<IEnumerable<ContentBlock>> KameraBildAsync(
        [Description("Die Id des Grows (aus grows_auflisten) — das Bild kommt von der Kamera seines Zelts")] int growId,
        [Description("Wahlweise die Entity der Kamera (etwa camera.zelt_oben), wenn das Zelt mehrere hat")] string? kamera = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (Zahl(await DetailAsync(growId, cancellationToken), "tentId") is not { } zeltId)
            {
                return [new TextContentBlock { Text = $"Der Grow {growId} hängt an keinem Zelt, deshalb gibt es keine Kamera dazu." }];
            }

            var pfad = $"api/live/tents/{zeltId}/camera"
                       + (string.IsNullOrWhiteSpace(kamera) ? string.Empty : $"?entity={Uri.EscapeDataString(kamera.Trim())}");
            var bild = await reader.BildLesenAsync(pfad, MaxBildBytes, cancellationToken);
            return
            [
                ImageContentBlock.FromBytes(bild.Bytes, bild.MedienTyp),
                new TextContentBlock { Text = KameraNotiz(growId, zeltId, kamera, bild.Kopf, DateTimeOffset.UtcNow) },
            ];
        }
        catch (GrowOsException ex)
        {
            return [new TextContentBlock { Text = ex.Message }];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return [new TextContentBlock { Text = SchreibWerkzeuge.AndersAlsErwartet }];
        }
    }

    /// <summary>Der Satz neben dem Bild: welches Zelt, welche Kamera, wie frisch.</summary>
    public static string KameraNotiz(int growId, int zeltId, string? kamera, IReadOnlyDictionary<string, string> kopf, DateTimeOffset jetzt)
    {
        var teile = new List<string> { $"Livebild Zelt {zeltId} (Grow {growId})" };
        if (!string.IsNullOrWhiteSpace(kamera)) teile.Add($"Kamera {kamera.Trim()}");

        var live = kopf.TryGetValue("X-Camera-Live", out var l) && string.Equals(l, "true", StringComparison.OrdinalIgnoreCase);
        if (kopf.TryGetValue("X-Camera-Captured-At", out var zeit)
            && DateTimeOffset.TryParse(zeit, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var aufgenommen))
        {
            var ortszeit = aufgenommen.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"));
            var minuten = (int)Math.Round((jetzt - aufgenommen).TotalMinutes);
            teile.Add(live
                ? $"frisch aufgenommen ({ortszeit})"
                : $"KEIN frisches Bild — das letzte gültige von {ortszeit} (vor {minuten} min)");
        }
        else if (!live)
        {
            teile.Add("ob das Bild frisch ist, sagt Grow OS nicht");
        }

        return string.Join(", ", teile) + ".";
    }
}
