using System.Text.Json.Nodes;
using GrowMcp.Tools;
using ModelContextProtocol.Protocol;

namespace GrowMcp.Tests;

/// <summary>Fork AI (Paket C2, 18.10.2026): <c>tagesbilder</c> und <c>kamera_foto_speichern</c>.</summary>
public sealed class KameraWerkzeugeTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F'];

    private const string Liste = """
        {"zeltId":1,"kameras":["camera.zelt","camera.seite"],
         "bilder":[{"datum":"2026-10-17","bytes":10,"pfad":"/api/tents/1/tagesbilder/2026-10-17"},{"datum":"2026-10-12","bytes":10,"pfad":"/api/tents/1/tagesbilder/2026-10-12"}]}
        """;

    private static async Task<List<ContentBlock>> Tagesbilder(ForkAttrappe fork, string? datum)
        => ((IEnumerable<ContentBlock>)(await Werkzeugkasten.AufrufenAsync(Werkzeugkasten.Werkzeug("tagesbilder"),
            Werkzeugkasten.Leser(fork, null), new Dictionary<string, object?> { ["growId"] = 1, ["datum"] = datum }))!).ToList();

    private static ForkAttrappe Fork() => ForkAttrappe.MitGrow().Antwort("GET", "api/tents/1/tagesbilder", 200, Liste)
        .Datei("api/tents/1/tagesbilder/2026-10-12", "image/jpeg", Jpeg);

    [Fact]
    public async Task Ohne_Datum_kommen_die_Tage_und_die_Kameras()
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(await Tagesbilder(Fork(), null))).Text;

        Assert.Contains("2 Tage, neueste zuerst: 17.10.2026, 12.10.2026.", text);
        Assert.Contains("Kameras des Zelts: camera.zelt, camera.seite (die erste macht die Tagesbilder).", text);
    }

    [Theory]
    [InlineData("12.10.2026")]
    [InlineData("2026-10-12")]
    public async Task Mit_Datum_kommt_das_Bild_des_Tages(string datum)
    {
        var fork = Fork();
        var bloecke = await Tagesbilder(fork, datum);

        Assert.Equal("image/jpeg", Assert.IsType<ImageContentBlock>(bloecke[0]).MimeType);
        Assert.Contains("vom 12.10.2026", Assert.IsType<TextContentBlock>(bloecke[1]).Text);
        Assert.Contains(fork.VonWerkzeugen, a => a.Weg == "api/tents/1/tagesbilder/2026-10-12");
    }

    [Theory]
    [InlineData("13.10.2026", "Für den 13.10.2026 gibt es kein Tagesbild von Zelt 1. Vorhanden sind: 17.10.2026, 12.10.2026.")]
    [InlineData("letzten Sonntag", "kann ich nicht als Tag lesen")]
    [InlineData("../2026-10-12", "kann ich nicht als Tag lesen")]
    public async Task Ein_fehlender_oder_unlesbarer_Tag_holt_kein_Bild(string datum, string erwartet)
    {
        var fork = Fork();
        Assert.Contains(erwartet, Assert.IsType<TextContentBlock>(Assert.Single(await Tagesbilder(fork, datum))).Text);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Weg.StartsWith("api/tents/1/tagesbilder/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task kamera_foto_speichern_uebersetzt_das_Motiv_und_schickt_die_Unterschrift()
    {
        var fork = ForkAttrappe.MitGrow().Antwort("POST", @"api/grows/1/kamerafoto", 201,
            """{"id":88,"growId":1,"relativePath":"/uploads/1/x.jpg","caption":"Blätter oben hell","tag":"Leaf","source":"HomeAssistant"}""");
        var text = await Werkzeugkasten.TextAsync("kamera_foto_speichern", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?> { ["growId"] = 1, ["motiv"] = "Blätter", ["bildunterschrift"] = " Blätter oben hell " });

        Assert.Equal("Kamerabild als Foto 88 in Grow 1 gespeichert (Motiv Leaf, „Blätter oben hell\"). Ansehen mit foto_ansehen.", text);
        var rumpf = JsonNode.Parse(Assert.Single(fork.VonWerkzeugen, a => a.Methode == "POST").Rumpf!)!.AsObject();
        Assert.Equal("Leaf", rumpf["motiv"]!.GetValue<string>());
        Assert.Equal("Blätter oben hell", rumpf["bildunterschrift"]!.GetValue<string>());
        Assert.False(rumpf.ContainsKey("kamera"));
    }

    [Fact]
    public async Task Ein_unbekanntes_Motiv_und_die_Absage_von_Grow_OS_werden_Saetze()
    {
        var fork = ForkAttrappe.MitGrow().Antwort("POST", @"api/grows/1/kamerafoto", 400,
            """{"code":"camera_not_in_tent","message":"Diese Kamera gehört nicht zum Zelt. Möglich: camera.zelt."}""");
        var leser = Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel);

        Assert.Contains("ist kein Motiv", await Werkzeugkasten.TextAsync("kamera_foto_speichern", leser,
            new Dictionary<string, object?> { ["growId"] = 1, ["motiv"] = "Selfie" }));
        Assert.Empty(fork.VonWerkzeugen);

        var text = await Werkzeugkasten.TextAsync("kamera_foto_speichern", leser,
            new Dictionary<string, object?> { ["growId"] = 1, ["kamera"] = "camera.nachbar" });
        Assert.StartsWith("Nicht ausgeführt (Kamerafoto speichern): Diese Kamera gehört nicht zum Zelt.", text);
    }
}
