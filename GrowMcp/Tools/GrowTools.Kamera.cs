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
/// Derselbe Weg wie die Oberfläche (<c>GET /api/live/tents/{id}/camera</c>): Cultivate Control holt das Bild über seine eigene
/// Verbindung zu Home Assistant und fällt auf das letzte gültige Bild zurück, wenn gerade keines kommt. Ob es frisch
/// ist, steht in den Kopfzeilen (<c>X-Camera-Live</c>, <c>X-Camera-Captured-At</c>) — und im Text neben dem Bild.
/// </remarks>
public sealed partial class GrowTools
{
    [McpServerTool(Name = "kamera_bild")]
    [Description("Holt das aktuelle Livebild der Zelt-Kamera eines Grows als Bild, damit du mitschauen kannst („wie sieht das Zelt gerade aus?\"). Ohne kamera die erste Kamera des Zelts; eine andere nur, wenn sie dem Zelt zugeordnet ist. Kommt gerade kein frisches Bild, liefert Cultivate Control das letzte gültige und sagt, von wann. Welche Kameras das Zelt hat und ältere Tage: tagesbilder.")]
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

    [McpServerTool(Name = "tagesbilder")]
    [Description("Die täglichen Kamerabilder eines Zelts (Cultivate Control speichert einmal am Tag ab 12 Uhr eines der ersten Kamera): ohne datum die Liste der vorhandenen Tage und die Kameras des Zelts, mit datum (2026-10-12 oder 12.10.2026) das Bild dieses Tages zum Ansehen — „wie sah das Zelt letzten Sonntag aus?\".")]
    public async Task<IEnumerable<ContentBlock>> TagesbilderAsync(
        [Description("Die Id des Grows — die Bilder kommen vom Zelt dieses Grows")] int growId,
        [Description("Wahlweise ein Tag: 2026-10-12 oder 12.10.2026")] string? datum = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (Zahl(await DetailAsync(growId, cancellationToken), "tentId") is not { } zeltId)
            {
                return [new TextContentBlock { Text = $"Der Grow {growId} hängt an keinem Zelt, deshalb gibt es keine Tagesbilder dazu." }];
            }

            using var liste = JsonDocument.Parse(await reader.LesenAsync($"api/tents/{zeltId}/tagesbilder", cancellationToken));
            var tage = liste.RootElement.GetProperty("bilder").EnumerateArray().Select(b => b.GetProperty("datum").GetString()!).ToList();
            var kameras = liste.RootElement.GetProperty("kameras").EnumerateArray().Select(k => k.GetString()!).ToList();

            if (string.IsNullOrWhiteSpace(datum))
            {
                var kameraText = kameras.Count == 0 ? "Das Zelt hat keine Kamera." : $"Kameras des Zelts: {string.Join(", ", kameras)} (die erste macht die Tagesbilder).";
                return [new TextContentBlock { Text = tage.Count == 0
                    ? $"Zelt {zeltId} (Grow {growId}) hat noch keine Tagesbilder. {kameraText}"
                    : $"Tagesbilder von Zelt {zeltId} (Grow {growId}), {tage.Count} Tage, neueste zuerst: {string.Join(", ", tage.Select(TagText))}. {kameraText} Ein Bild mit datum ansehen." }];
            }

            if (TagLesen(datum) is not { } tag)
            {
                return [new TextContentBlock { Text = $"„{datum}\" kann ich nicht als Tag lesen. Bitte so: 2026-10-12 oder 12.10.2026." }];
            }
            if (!tage.Contains(tag))
            {
                return [new TextContentBlock { Text = $"Für den {TagText(tag)} gibt es kein Tagesbild von Zelt {zeltId}."
                    + (tage.Count > 0 ? $" Vorhanden sind: {string.Join(", ", tage.Take(14).Select(TagText))}{(tage.Count > 14 ? " …" : string.Empty)}." : " Es gibt noch keine.") }];
            }

            var bild = await reader.BildLesenAsync($"api/tents/{zeltId}/tagesbilder/{tag}", MaxBildBytes, cancellationToken);
            return
            [
                ImageContentBlock.FromBytes(bild.Bytes, bild.MedienTyp),
                new TextContentBlock { Text = $"Tagesbild Zelt {zeltId} (Grow {growId}) vom {TagText(tag)}, aufgenommen ab 12 Uhr{(kameras.Count > 0 ? $" mit {kameras[0]}" : string.Empty)}." },
            ];
        }
        catch (GrowOsException ex)
        {
            return [new TextContentBlock { Text = ex.Message }];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return [new TextContentBlock { Text = SchreibWerkzeuge.AndersAlsErwartet }];
        }
    }

    /// <summary>Ein Tag als yyyy-MM-dd — aus 2026-10-12 oder 12.10.2026; sonst null.</summary>
    public static string? TagLesen(string text)
        => DateOnly.TryParseExact(text.Trim(), ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag)
            ? tag.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    private static string TagText(string tag)
        => DateOnly.TryParseExact(tag, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : tag;

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
            teile.Add("ob das Bild frisch ist, sagt Cultivate Control nicht");
        }

        return string.Join(", ", teile) + ".";
    }
}
