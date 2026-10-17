using System.Text.Json;
using GrowMcp.Tools;
using GrowOsAccess;

namespace GrowMcp.Tests;

/// <summary>
/// Fork AI (17.10.2026): <c>aufgabe_anlegen</c> — eine Aufgabe in die Aufgabenliste legen, ohne dass der Schlüssel im Chat steht.
/// </summary>
public sealed class AufgabeAnlegenTests
{
    private static ForkAttrappe Fork(string aufgabenListe = "[]")
        => ForkAttrappe.MitGrow()
            .Antwort("GET", @"api/grows/\d+/tasks", 200, aufgabenListe)
            .Antwort("POST", @"api/grows/\d+/tasks", 201, """{"id":14,"growId":1,"title":"Filter reinigen","status":"Open"}""");

    private static Task<string> AnlegenAsync(ForkAttrappe fork, Dictionary<string, object?> werte, string? schluessel = Werkzeugkasten.ForkSchluessel)
        => Werkzeugkasten.TextAsync("aufgabe_anlegen", Werkzeugkasten.Leser(fork, schluessel), werte);

    [Fact]
    public async Task Legt_die_Aufgabe_mit_allen_Feldern_und_dem_Schluessel_des_Aufrufers_an()
    {
        var fork = Fork();
        var text = await AnlegenAsync(fork, new()
        {
            ["growId"] = 1, ["titel"] = "  Filter reinigen ", ["notiz"] = "Vorfilter zuerst", ["faellig"] = "18.10.2026 10:00", ["prioritaet"] = "wichtig",
        });

        var post = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "POST");
        Assert.Equal("api/grows/1/tasks", post.Weg);
        Assert.Equal("Bearer " + Werkzeugkasten.ForkSchluessel, post.Authorization);
        using var rumpf = JsonDocument.Parse(post.Rumpf!);
        Assert.Equal("Filter reinigen", rumpf.RootElement.GetProperty("title").GetString());
        Assert.Equal("Vorfilter zuerst", rumpf.RootElement.GetProperty("notes").GetString());
        Assert.Equal("High", rumpf.RootElement.GetProperty("priority").GetString());
        Assert.StartsWith("2026-10-18T10:00", rumpf.RootElement.GetProperty("dueAtLocal").GetString());
        Assert.Contains("Aufgabe 14", text);
    }

    [Fact]
    public async Task Nur_der_Titel_reicht_und_die_Prioritaet_bleibt_der_Vorgabe_von_Grow_OS_ueberlassen()
    {
        var fork = Fork();
        await AnlegenAsync(fork, new() { ["growId"] = 1, ["titel"] = "Filter reinigen" });

        using var rumpf = JsonDocument.Parse(Assert.Single(fork.VonWerkzeugen, a => a.Methode == "POST").Rumpf!);
        Assert.False(rumpf.RootElement.TryGetProperty("priority", out _));
        Assert.False(rumpf.RootElement.TryGetProperty("dueAtLocal", out _));
        Assert.False(rumpf.RootElement.TryGetProperty("notes", out _));
    }

    [Fact]
    public async Task Eine_offene_Aufgabe_mit_demselben_Titel_wird_nicht_noch_einmal_angelegt()
    {
        var fork = Fork("""[{"id":9,"title":"filter reinigen","status":"Open"}]""");
        var text = await AnlegenAsync(fork, new() { ["growId"] = 1, ["titel"] = "Filter reinigen" });

        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "POST");
        Assert.Contains("Id 9", text);
    }

    [Fact]
    public async Task Eine_erledigte_Aufgabe_gleichen_Titels_haelt_eine_neue_nicht_auf()
    {
        var fork = Fork("""[{"id":9,"title":"Filter reinigen","status":"Done"}]""");
        await AnlegenAsync(fork, new() { ["growId"] = 1, ["titel"] = "Filter reinigen" });

        Assert.Single(fork.VonWerkzeugen, a => a.Methode == "POST");
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("Filter", "sofort", null)]
    [InlineData("Filter", null, "irgendwann")]
    public async Task Ohne_Titel_mit_unbekannter_Prioritaet_oder_unlesbarer_Zeit_geht_nichts_raus(string titel, string? prioritaet, string? faellig)
    {
        var fork = Fork();
        var text = await AnlegenAsync(fork, new() { ["growId"] = 1, ["titel"] = titel, ["prioritaet"] = prioritaet, ["faellig"] = faellig });

        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "POST");
        Assert.StartsWith("Nichts angelegt", text);
    }

    [Fact]
    public async Task Mit_dem_MCP_Schluessel_wird_nichts_angelegt_und_Grow_OS_nicht_gefragt()
    {
        var fork = Fork();
        var text = await AnlegenAsync(fork, new() { ["growId"] = 1, ["titel"] = "Filter reinigen" }, schluessel: null);

        Assert.Empty(fork.VonWerkzeugen);
        Assert.Equal(ForkFehler.SchluesselNoetig, text);
    }

    [Fact]
    public void Die_Prioritaeten_sind_genau_TaskPriority_von_Grow_OS()
        => Assert.Equal(
            Enum.GetNames(GrowOsBau.Typ("GrowDiary.Web.Models.TaskPriority")),
            SchreibWerkzeuge.AufgabenPrioritaet.Select(p => p.Wert).ToArray());
}
