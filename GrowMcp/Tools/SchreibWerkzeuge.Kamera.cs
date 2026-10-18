using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Fork AI (Paket C2, 18.10.2026): <c>kamera_foto_speichern</c> — das aktuelle Kamerabild als Foto in den Grow.
/// </summary>
/// <remarks>
/// Der Grow MCP lädt keine Dateien hoch: Grow OS holt das Bild selbst von der Kamera des Zelts
/// (<c>POST /api/grows/{id}/kamerafoto</c>) und legt es ab wie die Auto-Messung. Ob die Kamera zum Zelt gehört und der
/// Grow läuft, prüft Grow OS.
/// </remarks>
public sealed partial class SchreibWerkzeuge
{
    private static readonly (string Wert, string[] Woerter)[] Motive =
    [
        ("Overview", ["übersicht", "uebersicht", "gesamt", "overview"]),
        ("Canopy", ["kronendach", "canopy", "oben"]),
        ("Leaf", ["blatt", "blätter", "blaetter", "leaf"]),
        ("Root", ["wurzel", "wurzeln", "root"]),
        ("Training", ["training", "lst", "schnitt"]),
        ("Flower", ["blüte", "bluete", "buds", "flower"]),
        ("Problem", ["problem", "schaden", "mangel"]),
        ("Comparison", ["vergleich", "comparison"]),
        ("Other", ["sonstiges", "anderes", "other"]),
    ];

    [McpServerTool(Name = "kamera_foto_speichern")]
    [Description($"Legt das aktuelle Bild der Zelt-Kamera als Foto im Grow ab (erscheint bei den Fotos und im Tagebuch) — „mach ein Foto fürs Tagebuch\". Motiv (übersicht, kronendach, blatt, wurzel, training, blüte, problem, vergleich, sonstiges) und Bildunterschrift wahlweise. Nur in laufende Grows und nur mit einer Kamera des Zelts. Ansehen danach mit foto_ansehen. {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> KameraFotoSpeichernAsync(
        [Description("Die Id des Grows")] int growId,
        [Description("Wahlweise die Kamera (Entity) des Zelts; weglassen = die erste")] string? kamera = null,
        [Description("Motiv: übersicht, kronendach, blatt, wurzel, training, blüte, problem, vergleich, sonstiges")] string? motiv = null,
        [Description("Bildunterschrift, höchstens 500 Zeichen")] string? bildunterschrift = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var rumpf = new JsonObject();
        if (!string.IsNullOrWhiteSpace(kamera)) rumpf["kamera"] = kamera.Trim();
        if (!string.IsNullOrWhiteSpace(motiv))
        {
            if (Auswahl(Motive, motiv) is not { } wert) return $"Nichts gespeichert: „{motiv}\" ist kein Motiv. Möglich: übersicht, kronendach, blatt, wurzel, training, blüte, problem, vergleich, sonstiges.";
            rumpf["motiv"] = wert;
        }
        if (!string.IsNullOrWhiteSpace(bildunterschrift))
        {
            if (bildunterschrift.Trim().Length > 500) return "Nichts gespeichert: die Bildunterschrift hat mehr als 500 Zeichen.";
            rumpf["bildunterschrift"] = bildunterschrift.Trim();
        }

        return await SchreibenAsync(HttpMethod.Post, $"api/grows/{growId}/kamerafoto", rumpf, "Kamerafoto speichern",
            json => $"Kamerabild als Foto {(json.TryGetProperty("id", out var id) ? id.GetRawText() : "?")} in Grow {growId} gespeichert"
                    + $" (Motiv {Text(json, "tag") ?? "Overview"}, „{Text(json, "caption")}\"). Ansehen mit foto_ansehen.", cancellationToken, mitRumpf: false);
    }
}
