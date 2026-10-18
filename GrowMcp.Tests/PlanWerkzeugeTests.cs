using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GrowMcp.Tools;

namespace GrowMcp.Tests;

/// <summary>
/// Fork AI (forkai.247): <c>grenzwert_setzen</c> und <c>lichtplan_setzen</c> — genau ein Feld, der Rest per Prüfsumme belegt.
/// </summary>
/// <remarks>
/// Die Attrappen verhalten sich wie Grow OS: das PUT ersetzt alles mit dem, was kommt. Die Varianten <c>verfaelscht</c> spielen einen
/// Server nach, der dabei noch etwas anderes ändert — davor soll die Prüfsumme schützen (und zurückschreiben).
/// </remarks>
public sealed class PlanWerkzeugeTests
{
    private const string Regeln = """
        [{"metricKey":"temperature","minValue":18,"maxValue":30,"notifyService":"","enabled":true,"cooldownMinutes":30,"quelle":"Fest","toleranz":2,"nightMinValue":16,"nightMaxValue":null},
         {"metricKey":"humidity","minValue":null,"maxValue":null,"notifyService":"","enabled":true,"cooldownMinutes":45,"quelle":"Plan","toleranz":5,"nightMinValue":null,"nightMaxValue":null},
         {"metricKey":"power","minValue":null,"maxValue":900,"notifyService":"","enabled":false,"cooldownMinutes":30,"quelle":"Fest","toleranz":null,"nightMinValue":null,"nightMaxValue":null}]
        """;

    private static ForkAttrappe Grenzen(bool verfaelscht = false)
    {
        var stand = JsonNode.Parse(Regeln)!.AsArray();
        return new ForkAttrappe()
            .Antwort("GET", "api/messgroessen", 200, ForkAttrappe.Katalog)
            .Antwort("GET", @"api/alerts/tents/\d+", 404, "")
            .Antwort("GET", "api/alerts/tents/1", _ => (200, new JsonObject { ["tentId"] = 1, ["rules"] = stand.DeepClone() }.ToJsonString()))
            .Antwort("PUT", "api/alerts/tents/1", a =>
            {
                stand = JsonNode.Parse(a.Rumpf!)!["rules"]!.AsArray();
                if (verfaelscht && stand.Count > 0) stand[0]!["cooldownMinutes"] = 5;
                return (200, new JsonObject { ["tentId"] = 1, ["rules"] = stand.DeepClone() }.ToJsonString());
            });
    }

    private static Task<string> Aufruf(ForkAttrappe fork, string werkzeug, Dictionary<string, object?> werte)
        => Werkzeugkasten.TextAsync(werkzeug, Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel), werte);

    private static Dictionary<string, object?> Grenze(string messgroesse, string grenze, double wert)
        => new() { ["zeltId"] = 1, ["messgroesse"] = messgroesse, ["grenze"] = grenze, ["wert"] = wert };

    // ------------------------------------------------------------------ grenzwert_setzen

    [Fact]
    public async Task Nur_die_eine_Grenze_aendert_sich()
    {
        var fork = Grenzen();
        var text = await Aufruf(fork, "grenzwert_setzen", Grenze("Lufttemperatur", "Obergrenze", 28));

        var put = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT");
        var neu = JsonNode.Parse(put.Rumpf!)!["rules"]!.AsArray();
        var alt = JsonNode.Parse(Regeln)!.AsArray();
        Assert.Equal(alt.Count, neu.Count);
        alt[0]!["maxValue"] = 28.0;
        Assert.True(JsonNode.DeepEquals(alt, neu), neu.ToJsonString());

        Assert.StartsWith("Lufttemp. max. an Zelt 1: vorher 30 °C, jetzt 28 °C. Alle anderen Grenzen und Einstellungen sind unverändert (Prüfsumme ", text);
        Assert.Contains("vorher und nachher gleich, 3 Regeln)", text);
    }

    [Fact]
    public async Task Aendert_der_Fork_noch_etwas_wird_gemeldet_und_zurueckgeschrieben()
    {
        var fork = Grenzen(verfaelscht: true);
        var text = await Aufruf(fork, "grenzwert_setzen", Grenze("temperature", "max", 28));

        Assert.StartsWith("ACHTUNG", text);
        var puts = fork.VonWerkzeugen.Where(a => a.Methode == "PUT").ToList();
        Assert.Equal(2, puts.Count);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Regeln), JsonNode.Parse(puts[1].Rumpf!)!["rules"]), "Zurückgeschrieben wird der Stand von vorher.");
    }

    [Fact]
    public async Task Ohne_Regel_entsteht_eine_neue_mit_nur_dieser_Grenze()
    {
        var fork = Grenzen();
        var text = await Aufruf(fork, "grenzwert_setzen", Grenze("Außen Temp.", "min", 12));

        var neu = JsonNode.Parse(Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT").Rumpf!)!["rules"]!.AsArray();
        Assert.Equal(4, neu.Count);
        var regel = neu[3]!;
        Assert.Equal("outside-temperature", regel["metricKey"]!.GetValue<string>());
        Assert.Equal(12, regel["minValue"]!.GetValue<double>());
        Assert.Null(regel["maxValue"]);
        Assert.Contains("vorher keine Regel, jetzt 12 °C (neue Regel, Meldungen an, Abstand 30 Minuten)", text);
    }

    [Fact]
    public async Task Eine_Grenze_ausserhalb_der_Liste_laesst_sich_ueber_ihren_Schluessel_aendern()
    {
        var fork = Grenzen();
        var text = await Aufruf(fork, "grenzwert_setzen", Grenze("power", "max", 800));
        Assert.Contains("power max.: ", text.Replace(" an Zelt 1:", ":"));
        Assert.Contains("vorher 900, jetzt 800", text);
    }

    [Theory]
    [InlineData("Luftfeuchte", "max", 70, "folgen dem Wochenplan")]
    [InlineData("Lufttemp.", "max", 30, "steht schon auf 30 °C")]
    [InlineData("Lufttemp.", "irgendwo", 30, "„irgendwo\" ist keine Grenze")]
    [InlineData("Aussenluft", "max", 30, "Messgröße „Aussenluft\" gibt es nicht")]
    // Grow OS verwirft eine feste Regel ohne Tag-Grenze still (Prüfer forkai.247).
    [InlineData("Außen Temp.", "nacht max", 22, "Eine Nachtgrenze gilt nur zusammen mit einer Tag-Grenze")]
    public async Task Was_nicht_geht_aendert_nichts(string messgroesse, string grenze, double wert, string erwartet)
    {
        var fork = Grenzen();
        var text = await Aufruf(fork, "grenzwert_setzen", Grenze(messgroesse, grenze, wert));

        Assert.StartsWith("Nichts geändert", text);
        Assert.Contains(erwartet, text);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "PUT");
    }

    [Fact]
    public async Task Die_Physik_prueft_Grow_OS_und_sein_Satz_kommt_zurueck()
    {
        var fork = Grenzen().Antwort("PUT", "api/alerts/tents/1", 400,
            """{"code":"validation_failed","message":"Die Grenzwerte lassen sich so nicht speichern.","fieldErrors":{"MaxValue":["Die Obergrenze (temperature) liegt außerhalb dessen, was physikalisch vorkommt (−10 bis 60)."]}}""");
        var text = await Aufruf(fork, "grenzwert_setzen", Grenze("Lufttemp.", "max", 600));

        Assert.StartsWith("Nicht ausgeführt (Meldegrenze setzen): Die Grenzwerte lassen sich so nicht speichern.", text);
        Assert.Contains("physikalisch", text);
    }

    [Fact]
    public async Task Ein_unbekanntes_Zelt_aendert_nichts()
        => Assert.Equal("Nichts geändert: Es gibt kein Zelt mit der Id 9.",
            await Aufruf(Grenzen(), "grenzwert_setzen", new() { ["zeltId"] = 9, ["messgroesse"] = "pH", ["grenze"] = "min", ["wert"] = 5.5 }));

    [Fact]
    public async Task Der_Rumpf_passt_zum_Vertrag_in_Grow_OS()
    {
        var fork = Grenzen();
        await Aufruf(fork, "grenzwert_setzen", Grenze("Lufttemp.", "nacht max", 26));
        var rumpf = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT").Rumpf!;
        var typ = GrowOsBau.Typ("GrowDiary.Web.Api.Contracts.SaveTentAlertRulesRequest");
        var gelesen = JsonSerializer.Deserialize(rumpf, typ, Streng);
        var regeln = (System.Collections.IEnumerable)typ.GetProperty("Rules")!.GetValue(gelesen)!;
        Assert.Equal(3, regeln.Cast<object>().Count());
        Assert.Equal(26.0, JsonNode.Parse(rumpf)!["rules"]![0]!["nightMaxValue"]!.GetValue<double>());
    }

    [Fact]
    public void Die_Pruefsumme_sieht_jede_andere_Aenderung_aber_nicht_das_eine_Feld()
    {
        var a = JsonNode.Parse(Regeln)!.AsArray();
        var b = (JsonArray)a.DeepClone();
        b[0]!["maxValue"] = 28;
        Assert.Equal(SchreibWerkzeuge.RegelPruefsumme(a, "temperature", "maxValue", false), SchreibWerkzeuge.RegelPruefsumme(b, "temperature", "maxValue", false));
        b[0]!["minValue"] = 17;
        Assert.NotEqual(SchreibWerkzeuge.RegelPruefsumme(a, "temperature", "maxValue", false), SchreibWerkzeuge.RegelPruefsumme(b, "temperature", "maxValue", false));
    }

    // ------------------------------------------------------------------ lichtplan_setzen

    private static ForkAttrappe Licht(bool verfaelscht = false, bool aktiv = true)
    {
        var plan = JsonNode.Parse(ForkAttrappe.Lichtplan)!.AsObject();
        plan["isActive"] = aktiv;
        plan["timeZoneId"] = "Europe/Berlin";
        return new ForkAttrappe()
            .Antwort("GET", "api/light-schedules", _ => (200, new JsonArray(plan.DeepClone()).ToJsonString()))
            .Antwort("GET", @"api/light-schedules/\d+", _ => (200, plan.ToJsonString()))
            .Antwort("PUT", @"api/light-schedules/\d+", a =>
            {
                foreach (var (name, wert) in JsonNode.Parse(a.Rumpf!)!.AsObject()) plan[name] = wert?.DeepClone();
                plan["updatedAtUtc"] = "2026-10-18T13:00:00Z";
                if (verfaelscht) plan["name"] = "Veg";
                return (200, plan.ToJsonString());
            });
    }

    private static Dictionary<string, object?> Zeiten(string an, string aus) => new() { ["zeltId"] = 1, ["an"] = an, ["aus"] = aus };

    [Fact]
    public async Task Nur_die_Zeiten_aendern_sich_und_die_Antwort_sagt_was_nicht_passiert()
    {
        var fork = Licht();
        var text = await Aufruf(fork, "lichtplan_setzen", Zeiten("7", "19 Uhr"));

        var put = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT");
        Assert.Equal("api/light-schedules/3", put.Weg);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"name":"Blüte 12/12","isActive":true,"lightsOnTime":"07:00","lightsOffTime":"19:00","timeZoneId":"Europe/Berlin","source":"Manual"}"""),
            JsonNode.Parse(put.Rumpf!)), put.Rumpf);

        Assert.StartsWith("Lichtplan „Blüte 12/12\" (Id 3) an Zelt 1: vorher 06:00–18:00 (12 h Licht), jetzt 07:00–19:00 (12 h Licht). "
                          + "Die Zeiten gelten als Uhrzeit im Zelt (Zeitzone des Plans: Europe/Berlin). Name, aktiv, Quelle und Zeitzone sind unverändert (Prüfsumme ", text);
        Assert.EndsWith("Nach Home Assistant oder an den Controller schreibt Grow OS diese Zeiten nicht — was das Licht tatsächlich schaltet, bleibt, wie es ist.", text);
    }

    [Fact]
    public async Task Aendert_der_Fork_mehr_als_die_Zeiten_wird_zurueckgeschrieben()
    {
        var fork = Licht(verfaelscht: true);
        var text = await Aufruf(fork, "lichtplan_setzen", Zeiten("07:00", "19:00"));

        Assert.StartsWith("ACHTUNG", text);
        var puts = fork.VonWerkzeugen.Where(a => a.Methode == "PUT").ToList();
        Assert.Equal(2, puts.Count);
        Assert.Equal("06:00", JsonNode.Parse(puts[1].Rumpf!)!["lightsOnTime"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("06:00", "18:00", "läuft schon 06:00–18:00")]
    [InlineData("25:00", "18:00", "„25:00\" ist keine Uhrzeit")]
    [InlineData("früh", "18:00", "„früh\" ist keine Uhrzeit")]
    public async Task Was_nicht_geht_aendert_den_Plan_nicht(string an, string aus, string erwartet)
    {
        var fork = Licht();
        var text = await Aufruf(fork, "lichtplan_setzen", Zeiten(an, aus));
        Assert.StartsWith("Nichts geändert", text);
        Assert.Contains(erwartet, text);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "PUT");
    }

    [Fact]
    public async Task Ohne_aktiven_Plan_wird_nach_der_planId_gefragt()
    {
        var fork = Licht(aktiv: false);
        var text = await Aufruf(fork, "lichtplan_setzen", Zeiten("07:00", "19:00"));
        Assert.Equal("Nichts geändert: Zelt 1 hat keinen aktiven Lichtplan. Pläne: 3 „Blüte 12/12\" — bitte planId angeben.", text);

        var mitId = await Aufruf(Licht(aktiv: false), "lichtplan_setzen", new() { ["zeltId"] = 1, ["an"] = "07:00", ["aus"] = "19:00", ["planId"] = 3 });
        Assert.StartsWith("Lichtplan „Blüte 12/12\" (Id 3)", mitId);
    }

    [Theory]
    [InlineData("7", "07:00")]
    [InlineData("7:30", "07:30")]
    [InlineData("07.30 Uhr", "07:30")]
    [InlineData("23:59", "23:59")]
    [InlineData("24:00", null)]
    [InlineData("7:5", null)]
    [InlineData("", null)]
    public void Uhrzeiten_werden_gelesen(string gesagt, string? erwartet) => Assert.Equal(erwartet, SchreibWerkzeuge.Uhrzeit(gesagt));

    [Fact]
    public async Task Der_Lichtplan_Rumpf_passt_zum_Vertrag_in_Grow_OS()
    {
        var fork = Licht();
        await Aufruf(fork, "lichtplan_setzen", Zeiten("07:00", "19:00"));
        var typ = GrowOsBau.Typ("GrowDiary.Web.Api.Contracts.UpdateLightScheduleRequest");
        var gelesen = JsonSerializer.Deserialize(Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT").Rumpf!, typ, Streng);
        Assert.Equal("07:00", typ.GetProperty("LightsOnTime")!.GetValue(gelesen));
        Assert.Equal("Europe/Berlin", typ.GetProperty("TimeZoneId")!.GetValue(gelesen));
    }

    private static readonly JsonSerializerOptions Streng = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };
}
