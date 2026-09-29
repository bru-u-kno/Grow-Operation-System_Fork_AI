using System.Text.Json.Nodes;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Im Zweifel zu: die Vorlagen der Steuerung bleiben bei einem schweigenden
/// Fühler nicht offen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (29.09.2026).</b> Drei Stellen der CO₂-Vorlagen
/// liefen bei <c>unavailable</c> in die offene Richtung:</para>
/// <list type="bullet">
/// <item>Die Schleife der Dosierung endete nur bei Zustand <c>off</c>. Ein
/// <c>unavailable</c> an Licht, Klima oder Bedarf ist nicht <c>off</c> — sie
/// lief bis zur Höchstzahl der Impulse weiter.</item>
/// <item><c>co2_bedarf</c> hielt bei schweigendem Fühler fünf Minuten lang
/// „an", und <c>co2_impuls_bedarf</c> rechnete dann mit Ist 0: der längste
/// Impuls.</item>
/// <item>Der Wächter löste nur beim Wechsel nach „an" aus. Stand der Port
/// schon vor einem Neustart offen, wechselte danach nichts mehr.</item>
/// </list>
/// <para>Die erste Stelle prüft eine Zählung über ALLE Vorlagen — eine neue
/// Vorlage mit demselben Muster fällt hier auf, ohne dass jemand sie in eine
/// Liste einträgt.</para>
/// <para>Die Jinja-Vorschriften selbst lassen sich hier nicht ausführen. Ihr
/// Verhalten wurde am 29.09.2026 in der Template-Engine einer echten Home-
/// Assistant-Instanz durchgespielt (Fühler fehlt/unavailable/unknown/0 →
/// Bedarf aus, Impuls 0 s; 500/850/950 ppm → wie vorher); die Tests unten
/// halten die Form fest, die dieses Verhalten trägt.</para>
/// </remarks>
public sealed class VorlagenSchliessenImZweifelTests
{
    private static string VorlagenWurzel => Path.Combine(AppContext.BaseDirectory, "Vorlagen");

    private static IEnumerable<JsonNode> AlleKnoten(JsonNode? knoten)
    {
        if (knoten is null) yield break;
        yield return knoten;
        var kinder = knoten switch
        {
            JsonObject o => o.Select(p => p.Value),
            JsonArray a => a.AsEnumerable(),
            _ => [],
        };
        foreach (var kind in kinder)
        foreach (var k in AlleKnoten(kind)) yield return k;
    }

    private static List<(string Datei, JsonArray Until)> AlleUntil()
    {
        var gefunden = new List<(string, JsonArray)>();
        foreach (var pfad in Directory.GetFiles(VorlagenWurzel, "*.json", SearchOption.AllDirectories))
        {
            var datei = Path.GetRelativePath(VorlagenWurzel, pfad).Replace('\\', '/');
            foreach (var knoten in AlleKnoten(JsonNode.Parse(File.ReadAllText(pfad))))
            {
                if (knoten is JsonObject o && o["until"] is JsonArray until) gefunden.Add((datei, until));
            }
        }
        return gefunden;
    }

    /// <summary>Alle Zustands-Bedingungen, die direkt (nicht unter einem <c>not</c>) in einem Abbruch stehen.</summary>
    private static IEnumerable<JsonObject> DirekteZustandsBedingungen(JsonNode? knoten)
    {
        if (knoten is JsonArray liste)
        {
            foreach (var k in liste)
            foreach (var z in DirekteZustandsBedingungen(k)) yield return z;
            yield break;
        }
        if (knoten is not JsonObject o) yield break;

        var art = o["condition"]?.GetValue<string>();
        if (art == "state") { yield return o; yield break; }
        if (art == "not") yield break; // „nicht an" ist genau die gewollte Form
        foreach (var z in DirekteZustandsBedingungen(o["conditions"])) yield return z;
    }

    [Fact]
    public void Selbsttest_DieZaehlungSiehtDieSchleifeDerCo2Dosierung()
    {
        Assert.True(Directory.Exists(VorlagenWurzel), $"Vorlagen fehlen unter {VorlagenWurzel}.");
        var until = AlleUntil();
        Assert.True(until.Count >= 1, "Keine einzige until-Bedingung gefunden — die Zählung prüft nichts.");
        Assert.Contains(until, u => u.Datei == "co2/dosierung.json");
        Assert.True(DirekteZustandsBedingungen(until.First(u => u.Datei == "co2/dosierung.json").Until).Any()
            || AlleKnoten(until.First(u => u.Datei == "co2/dosierung.json").Until).OfType<JsonObject>()
                .Any(o => o["condition"]?.ToString() == "not"),
            "Die Zählung erkennt in der CO₂-Schleife keine einzige Zustands-Bedingung.");
    }

    [Fact]
    public void KeinAbbruchWartetAufOff_UnavailableMussDieSchleifeEbenfallsBeenden()
    {
        var falsch = AlleUntil()
            .SelectMany(u => DirekteZustandsBedingungen(u.Until)
                .Where(b => string.Equals(b["state"]?.ToString(), "off", StringComparison.OrdinalIgnoreCase))
                .Select(b => $"{u.Datei}: {b["entity_id"]} == off"))
            .ToList();

        Assert.True(falsch.Count == 0,
            "Diese Abbrüche greifen bei unavailable nicht — als „nicht on“ schreiben:\n" + string.Join("\n", falsch));
    }

    [Fact]
    public void Dosierung_OeffnetNichtOhneGueltigenImpulsBedarf()
    {
        var vorlage = JsonNode.Parse(File.ReadAllText(Path.Combine(VorlagenWurzel, "co2", "dosierung.json")))!;
        var schritte = vorlage["actions"]![0]!["repeat"]!["sequence"]!.AsArray().ToList();

        var impuls = schritte[0]!["variables"]!["impuls"]!.GetValue<string>();
        Assert.EndsWith("| int(0) }}", impuls);

        var oeffnen = schritte.FindIndex(s => s?["data"]?["option"]?.ToString() == "On");
        var sperre = schritte.FindIndex(s =>
            s?["condition"]?.ToString() == "template"
            && s["value_template"]!.ToString().Contains("impuls | int(0) > 0", StringComparison.Ordinal));
        Assert.True(oeffnen >= 0, "Kein Öffnen in der Schleife gefunden — der Test sieht die Vorlage nicht.");
        Assert.True(sperre >= 0, "Keine Sperre gegen einen Impuls von 0 s.");
        Assert.True(sperre < oeffnen, "Die Sperre steht erst hinter dem Öffnen.");
    }

    [Fact]
    public void Waechter_SiehtAuchNachDemStartUndImTaktNach()
    {
        var vorlage = JsonNode.Parse(File.ReadAllText(Path.Combine(VorlagenWurzel, "co2", "waechter.json")))!;
        var ausloeser = vorlage["triggers"]!.AsArray();

        Assert.Contains(ausloeser, t => t?["trigger"]?.ToString() == "homeassistant" && t["event"]?.ToString() == "start");
        Assert.Contains(ausloeser, t => t?["trigger"]?.ToString() == "time_pattern");

        // Jeder Auslöser muss auch im Schließ-Zweig ankommen, sonst löst er nur aus.
        var schliessZweig = vorlage["actions"]![0]!["choose"]![0]!["conditions"]!.ToJsonString();
        foreach (var id in new[] { "port_lang", "start", "takt" })
        {
            Assert.Contains($"\"{id}\"", schliessZweig);
        }
    }

    private static string Vorschrift(string entityId)
        => SteuerungBauteile.Alle.Single(b => b.EntityId == entityId).Vorlage!;

    [Fact]
    public void Co2Bedarf_HaeltBeiSchweigendemFuehlerNichtAn()
    {
        var bedarf = Vorschrift("binary_sensor.co2_bedarf");

        Assert.DoesNotContain("seit > 300", bedarf);
        Assert.StartsWith(
            "{% set co2_s = states.[[co2_sensor]] %}{% set ist = (co2_s.state if co2_s is not none else 'unavailable') | float(-1) %}",
            bedarf);
        Assert.Contains("{% if ist <= 0 or ziel <= 0 %}false", bedarf);
    }

    [Fact]
    public void Co2ImpulsBedarf_IstOhneMesswertNull()
    {
        var impuls = Vorschrift("sensor.co2_impuls_bedarf");

        Assert.Contains("{% set ist = states('[[co2_sensor]]') | float(-1) %}{% if ist <= 0 or ziel <= 0 %}0{% else %}", impuls);
    }
}
