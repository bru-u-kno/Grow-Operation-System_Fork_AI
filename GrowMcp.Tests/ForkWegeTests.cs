using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrowMcp.Tools;

namespace GrowMcp.Tests;

/// <summary>
/// Jeder Weg, den ein Werkzeug bei Grow OS aufruft, steht dort an einem Controller.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026): „Bezeichner nie aus dem Kopf" — auch nicht
/// Routen. Alle Werkzeuge laufen gegen die Attrappe; jeder Weg, der dabei
/// angefragt wird, muss in der gebauten <c>GrowDiary.Web.dll</c> mit derselben
/// Methode als Route stehen. Ein erfundener Weg gäbe in Grow OS 404 — und das
/// Werkzeug wäre tot, ohne dass ein anderer Test es merkt.</para>
/// </remarks>
public sealed class ForkWegeTests
{
    /// <summary>
    /// Wege aus einem Vertrag, die es in diesem Stand noch nicht gibt.
    /// </summary>
    /// <remarks>
    /// Gilt NUR, solange Grow OS unter diesem Vorsatz gar keinen Weg hat. Sobald
    /// es einen gibt, wird auch hier verlangt, dass jeder angefragte Weg passt —
    /// die Ausnahme erlischt von selbst, statt still einen Tippfehler zu decken.
    /// </remarks>
    private static readonly Dictionary<string, string> NochNichtGebaut = new()
    {
        ["api/ki-ha/"] = "Home Assistant über den Fork (A-003 Etappe B) entsteht parallel auf dem Branch "
                         + "ki-ha-schnittstelle; bis zum Zusammenführen gilt der Vertrag aus dem Auftrag.",
    };

    private static readonly Dictionary<string, object?> EinWert = new() { ["ph"] = 5.8 };

    private static async Task<List<Angekommen>> AlleAnfragenAsync()
    {
        var alle = new List<Angekommen>();
        foreach (var methode in Werkzeugkasten.Werkzeugmethoden())
        {
            var fork = ForkAttrappe.MitGrow();
            await Werkzeugkasten.AufrufenAsync(methode, Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel), EinWert);
            alle.AddRange(fork.VonWerkzeugen);
        }

        // Jeder Schritt von phase_bestaetigen, nicht nur der Beispielwert.
        foreach (var (schritt, _, _) in SchreibWerkzeuge.PhasenSchritte)
        {
            var fork = ForkAttrappe.MitGrow();
            await Werkzeugkasten.TextAsync("phase_bestaetigen", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
                new Dictionary<string, object?> { ["schritt"] = schritt });
            alle.AddRange(fork.VonWerkzeugen);
        }

        return alle;
    }

    [Fact]
    public void Die_Wege_von_Grow_OS_sind_lesbar()
    {
        var wege = GrowOsBau.Wege();
        Assert.True(wege.Count >= 150, $"Nur {wege.Count} Wege in Grow OS gefunden — die Reflexion greift ins Leere.");
        // Selbsttest gegen bekannte Wege: Vorsatz am Controller, absolute Route an der Aktion.
        Assert.Contains(wege, w => w is { Methode: "POST", Vorlage: "api/grows/{growId:int}/measurements" });
        Assert.Contains(wege, w => w is { Methode: "GET", Vorlage: "api/ki-zugriff/ich" });
    }

    [Fact]
    public async Task Jeder_angefragte_Weg_steht_in_Grow_OS()
    {
        var wege = GrowOsBau.Wege();
        var anfragen = await AlleAnfragenAsync();
        Assert.True(anfragen.Count(a => a.Methode != "GET") >= 15, "Zu wenige schreibende Anfragen — laufen die Werkzeuge bis Grow OS durch?");

        var fehlend = new List<string>();
        foreach (var anfrage in anfragen.DistinctBy(a => (a.Methode, a.Weg)))
        {
            var passt = wege.Any(w => w.Methode == anfrage.Methode && w.Muster.IsMatch(anfrage.Weg));
            if (passt) continue;

            var ausnahme = NochNichtGebaut.Keys.FirstOrDefault(v => anfrage.Weg.StartsWith(v, StringComparison.Ordinal));
            var vorsatzGebaut = ausnahme is not null && wege.Any(w => w.Vorlage.StartsWith(ausnahme, StringComparison.Ordinal));
            if (ausnahme is not null && !vorsatzGebaut) continue;

            fehlend.Add($"{anfrage.Methode} {anfrage.Weg}");
        }

        Assert.True(fehlend.Count == 0, "Diese Wege gibt es in Grow OS nicht: " + string.Join("; ", fehlend));
    }

    [Fact]
    public async Task Die_Ausnahmen_decken_nur_Wege_die_wirklich_angefragt_werden()
    {
        var anfragen = await AlleAnfragenAsync();
        Assert.All(NochNichtGebaut.Keys, vorsatz => Assert.Contains(anfragen, a => a.Weg.StartsWith(vorsatz, StringComparison.Ordinal)));
    }

    [Fact]
    public void Die_Schritte_von_phase_bestaetigen_sind_alle_Bestaetigungen_von_Grow_OS()
    {
        // Andersherum: kommt in Grow OS eine Bestätigung dazu, soll sie hier nicht fehlen.
        var aktionen = GrowOsBau.Wege()
            .Where(w => w.Methode == "POST" && w.Vorlage.StartsWith("api/grows/{id:int}/actions/", StringComparison.Ordinal))
            .Select(w => w.Vorlage["api/grows/{id:int}/actions/".Length..])
            .OrderBy(n => n)
            .ToList();
        Assert.True(aktionen.Count >= 5);
        Assert.Equal(aktionen, SchreibWerkzeuge.PhasenSchritte.Select(s => s.Weg).OrderBy(n => n));
    }

    [Fact]
    public async Task ha_dienst_schickt_die_Felder_des_Vertrags()
    {
        var fork = ForkAttrappe.MitGrow();
        await Werkzeugkasten.TextAsync("ha_dienst", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?>
            {
                ["domain"] = "light", ["dienst"] = "turn_on", ["entityId"] = "light.zelt", ["daten"] = """{"brightness_pct": 60}""",
            });
        var rumpf = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "POST").Rumpf!;

        // Gibt es den Vertrag in Grow OS schon, wird gegen ihn gelesen — streng.
        // Sonst gegen die Felder aus dem Auftrag (NochNichtGebaut).
        var typ = GrowOsBau.Assembly.GetType("GrowDiary.Web.Api.Contracts.KiHaDienstRequest");
        if (typ is not null)
        {
            var gelesen = JsonSerializer.Deserialize(rumpf, typ, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            });
            Assert.Equal("light.zelt", typ.GetProperty("EntityId")!.GetValue(gelesen));
            Assert.Equal("turn_on", typ.GetProperty("Dienst")!.GetValue(gelesen));
        }
        else
        {
            using var doc = JsonDocument.Parse(rumpf);
            Assert.Equal(["domain", "dienst", "entityId", "daten"], doc.RootElement.EnumerateObject().Select(f => f.Name).ToArray());
        }
    }
}
