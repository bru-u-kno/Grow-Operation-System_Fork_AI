using System.Globalization;
using System.Text.Json;
using GrowMcp.Tools;

namespace GrowMcp.Tests;

/// <summary>
/// Fork AI (forkai.247): <c>journal_loeschen</c> und <c>messung_loeschen</c> — erst lesen, nur Vorhandenes löschen, alles nennen.
/// </summary>
public sealed class LoeschWerkzeugeTests
{
    private static Task<string> Aufruf(ForkAttrappe fork, string werkzeug, Dictionary<string, object?> werte)
        => Werkzeugkasten.TextAsync(werkzeug, Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel), werte);

    [Fact]
    public async Task journal_loeschen_liest_loescht_und_nennt_den_vollen_Text()
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Aufruf(fork, "journal_loeschen", new() { ["eintragId"] = 26 });

        Assert.Equal(["GET api/journal/26", "DELETE api/journal/26"], fork.VonWerkzeugen.Select(a => $"{a.Methode} {a.Weg}"));
        // occurredAtUtc 12:30Z in der Ortszeit dieses Rechners — im Add-on ist das die von Home Assistant.
        var wann = new DateTime(2026, 10, 18, 12, 30, 0, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        Assert.Equal(
            $"Journal-Eintrag 26 von Grow 1 vom {wann} gelöscht (Art: Notiz). Titel: „Test\". Text war: „MCP-Schreibtest\"" + Environment.NewLine
            + $"Wieder eintragen: journal_eintragen mit growId 1, zeitpunkt {wann}, art Notiz, titel „Test\", text wie oben.",
            text);
    }

    [Fact]
    public async Task Ein_Meilenstein_wird_nicht_als_wieder_eintragbar_versprochen()
    {
        // journal_eintragen kennt GerminationConfirmed nicht (Prüfer forkai.247) — der Weg zurück ist eine Notiz.
        var fork = ForkAttrappe.MitGrow().Antwort("GET", @"api/journal/\d+", 200,
            """{"id":1,"growId":1,"title":"Gekeimt","body":"Keimung bestätigt","entryType":"GerminationConfirmed","occurredAtUtc":"2026-10-01T10:00:00Z"}""");
        var text = await Aufruf(fork, "journal_loeschen", new() { ["eintragId"] = 1 });

        Assert.Contains("Das war ein Meilenstein (GerminationConfirmed), den Grow OS beim Bestätigen einer Phase selbst schreibt.", text);
        Assert.Contains("Als Notiz: journal_eintragen mit growId 1, zeitpunkt ", text);
        Assert.Contains(", art Notiz, titel „Gekeimt\", text wie oben.", text);
        Assert.DoesNotContain("art GerminationConfirmed", text);
    }

    [Fact]
    public async Task journal_loeschen_ohne_Eintrag_loescht_nichts()
    {
        var fork = ForkAttrappe.MitGrow().Antwort("GET", @"api/journal/\d+", 404, """{"code":"journal_entry_not_found","message":"Journal-Eintrag mit Id 99 existiert nicht."}""");
        var text = await Aufruf(fork, "journal_loeschen", new() { ["eintragId"] = 99 });

        Assert.StartsWith("Nichts gelöscht: Einen Journal-Eintrag mit der Id 99 gibt es nicht.", text);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "DELETE");
    }

    [Fact]
    public async Task messung_loeschen_nennt_jeden_Wert_und_wie_er_wieder_eingetragen_wird()
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Aufruf(fork, "messung_loeschen", new() { ["messungId"] = 42 });

        Assert.Equal(["GET api/measurements/42", "DELETE api/measurements/42"], fork.VonWerkzeugen.Select(a => $"{a.Methode} {a.Weg}"));
        Assert.Equal(
            "Messung 42 von Grow 1 vom 01.10.2026 08:00 (Phase Flower) gelöscht. Werte waren: pH 5,8; EC 1,2 mS/cm; ORP 450 mV; Notiz „alt\"." + Environment.NewLine
            + "Wieder eintragen: messung_eintragen mit growId 1, zeitpunkt 01.10.2026 08:00, phase Flower, ph 5.8, ec 1.2, orpMv 450, notiz „alt\".",
            text);
    }

    [Fact]
    public async Task messung_loeschen_ohne_Messung_loescht_nichts()
    {
        var fork = ForkAttrappe.MitGrow().Antwort("GET", @"api/measurements/\d+", 404, """{"code":"measurement_not_found","message":"Messung mit Id 5 existiert nicht."}""");
        var text = await Aufruf(fork, "messung_loeschen", new() { ["messungId"] = 5 });

        Assert.Equal("Nichts gelöscht: Eine Messung mit der Id 5 gibt es nicht.", text);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "DELETE");
    }

    [Theory]
    [InlineData("journal_loeschen", "eintragId")]
    [InlineData("messung_loeschen", "messungId")]
    public async Task Gibt_Grow_OS_unter_der_Id_etwas_anderes_zurueck_wird_nicht_geloescht(string werkzeug, string parameter)
    {
        // Eine Antwort für eine andere Id (falsche Fassung, falscher Weg) darf kein Löschen auslösen.
        var fork = ForkAttrappe.MitGrow()
            .Antwort("GET", @"api/journal/\d+", 200, """{"id":1,"growId":1,"body":"x","entryType":"Note","occurredAtUtc":"2026-10-18T12:30:00Z"}""")
            .Antwort("GET", @"api/measurements/\d+", 200, """{"id":1,"growId":1,"takenAt":"2026-10-01T08:00:00","stage":"Flower"}""");
        var text = await Aufruf(fork, werkzeug, new() { [parameter] = 8 });

        Assert.StartsWith("Grow OS hat anders geantwortet als erwartet", text);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "DELETE");
    }

    /// <summary>
    /// Die Anzeige der gelöschten Messung deckt jedes Feld, das messung_eintragen schreiben kann — und ihr Parameter schreibt wirklich
    /// in dieses Feld. Sonst ginge beim Wiedereintragen ein Wert verloren, ohne dass es jemand sieht.
    /// </summary>
    [Fact]
    public async Task Jedes_Messfeld_wird_genannt_und_sein_Parameter_schreibt_es()
    {
        var tabelle = Messfelder.Tabelle.Select(t => t.Feld).ToList();
        Assert.True(tabelle.Count >= 20, "Die Zählung sähe ihre Grundmenge nicht.");
        Assert.Equal(tabelle.OrderBy(f => f), SchreibWerkzeuge.MessfelderAnzeige.Select(a => a.Feld).OrderBy(f => f));

        var parameter = Werkzeugkasten.Werkzeug("messung_eintragen").GetParameters().ToDictionary(p => p.Name!);
        foreach (var (feld, name, _, _) in SchreibWerkzeuge.MessfelderAnzeige)
        {
            Assert.True(parameter.TryGetValue(name, out var p), $"messung_eintragen hat keinen Parameter {name}.");
            var typ = Nullable.GetUnderlyingType(p!.ParameterType) ?? p.ParameterType;
            object wert = typ == typeof(double) ? 1.5 : typ == typeof(bool) ? true : feld == "waterFlow" ? "stark" : "Probe";

            var fork = ForkAttrappe.MitGrow();
            await Aufruf(fork, "messung_eintragen", new() { ["growId"] = 1, [name] = wert });
            using var rumpf = JsonDocument.Parse(Assert.Single(fork.VonWerkzeugen, a => a.Methode == "POST").Rumpf!);
            Assert.True(rumpf.RootElement.TryGetProperty(feld, out _), $"{name} schreibt nicht in {feld}.");
        }
    }
}
