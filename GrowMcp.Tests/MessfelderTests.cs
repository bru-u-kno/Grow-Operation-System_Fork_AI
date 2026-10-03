using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrowMcp.Tools;
using GrowOsAccess;

namespace GrowMcp.Tests;

/// <summary>
/// Was die Schreib-Werkzeuge schicken, gegen die ECHTEN Typen von Grow OS.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026). Keine abgetippte Liste: die Typen kommen
/// per Reflexion aus der gebauten <c>GrowDiary.Web.dll</c> (<see cref="GrowOsBau"/>).
/// Ein falscher Feldname wäre still — ASP.NET übergeht unbekannte Felder, Grow
/// OS speichert eine Messung ohne den Wert, und der Assistent meldet
/// „eingetragen".</para>
/// </remarks>
public sealed class MessfelderTests
{
    private const string Vertraege = "GrowDiary.Web.Api.Contracts.";
    private const string Modelle = "GrowDiary.Web.Models.";

    /// <summary>So liest ASP.NET in Grow OS einen Rumpf (Program.cs: Web-Vorgaben, Enums als Text).</summary>
    private static readonly JsonSerializerOptions WieGrowOs = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        // Strenger als Grow OS: ein Feld, das der Typ nicht kennt, ist hier ein Fehler.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Eigenschaften von <c>MeasurementUpsertRequest</c>, die das Werkzeug mit Absicht nicht setzt.
    /// </summary>
    private static readonly Dictionary<string, string> Ausnahmen = new()
    {
        ["Source"] = "Bleibt auf der Vorgabe Manual: diktiert ist von Hand gemessen. HomeAssistant/Imported/Derived "
                     + "setzt Grow OS selbst, ein Assistent soll keine Herkunft vortäuschen.",
    };

    /// <summary>Jedes Werkzeug-Argument mit einem eigenen, wiedererkennbaren Wert.</summary>
    private static Dictionary<string, object?> AlleWerte()
    {
        var werte = new Dictionary<string, object?>
        {
            ["growId"] = 1,
            ["zeitpunkt"] = "03.10.2026 14:30",
            ["phase"] = "Blüte",
            ["stroemung"] = "mittel",
            ["wasserwechsel"] = true,
            ["notiz"] = "2 ml pH-Minus, 300 ml Pyrolyt",
        };
        var zahl = 1.25;
        foreach (var p in Werkzeugkasten.Werkzeug("messung_eintragen").GetParameters().Where(p => p.ParameterType == typeof(double?)))
        {
            werte[p.Name!] = zahl;
            zahl += 1;
        }
        return werte;
    }

    private static async Task<string> RumpfAsync()
    {
        var fork = ForkAttrappe.MitGrow();
        await Werkzeugkasten.TextAsync("messung_eintragen", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel), AlleWerte());
        return Assert.Single(fork.VonWerkzeugen, a => a.Methode == "POST").Rumpf!;
    }

    [Fact]
    public async Task messung_eintragen_schickt_genau_die_Felder_von_MeasurementUpsertRequest()
    {
        var typ = GrowOsBau.Typ(Vertraege + "MeasurementUpsertRequest");
        var rumpf = await RumpfAsync();

        // Wirft bei jedem Feld, das Grow OS nicht kennt.
        var gelesen = JsonSerializer.Deserialize(rumpf, typ, WieGrowOs);
        Assert.NotNull(gelesen);

        // Und jeder Wert kommt dort an, wo er hingehört — nicht nur „irgendwo".
        using var doc = JsonDocument.Parse(rumpf);
        var felder = doc.RootElement.EnumerateObject().ToList();
        Assert.True(felder.Count >= 25, $"Nur {felder.Count} Felder im Rumpf — das Werkzeug setzt nicht alles.");
        foreach (var feld in felder)
        {
            var eigenschaft = typ.GetProperty(feld.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
            var wert = eigenschaft.GetValue(gelesen);
            switch (feld.Value.ValueKind)
            {
                case JsonValueKind.Number:
                    Assert.Equal(feld.Value.GetDouble(), Convert.ToDouble(wert));
                    break;
                case JsonValueKind.True or JsonValueKind.False:
                    Assert.Equal(feld.Value.GetBoolean(), wert);
                    break;
                default:
                    Assert.Equal(feld.Value.GetString(), wert?.ToString());
                    break;
            }
        }

        Assert.Equal("Flower", typ.GetProperty("Stage")!.GetValue(gelesen)!.ToString());
        Assert.Equal("2026-10-03T14:30", typ.GetProperty("TakenAtLocal")!.GetValue(gelesen));
        Assert.Equal("Moderate", typ.GetProperty("WaterFlow")!.GetValue(gelesen));
        Assert.Equal(true, typ.GetProperty("SolutionChange")!.GetValue(gelesen));
    }

    [Fact]
    public async Task Jede_Eigenschaft_von_MeasurementUpsertRequest_wird_gesetzt_oder_hat_einen_Grund()
    {
        var typ = GrowOsBau.Typ(Vertraege + "MeasurementUpsertRequest");
        var eigenschaften = typ.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        Assert.True(eigenschaften.Count >= 25, $"Nur {eigenschaften.Count} Eigenschaften — die Reflexion greift ins Leere.");

        using var doc = JsonDocument.Parse(await RumpfAsync());
        var gesetzt = doc.RootElement.EnumerateObject().Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offen = eigenschaften.Where(name => !gesetzt.Contains(name) && !Ausnahmen.ContainsKey(name)).ToList();
        Assert.True(offen.Count == 0,
            $"Diese Felder kann messung_eintragen nicht setzen: {string.Join(", ", offen)}. Ins Werkzeug aufnehmen oder als Ausnahme mit Grund eintragen.");

        // Eine Ausnahme für ein Feld, das es nicht gibt, griffe nie.
        Assert.All(Ausnahmen.Keys, name => Assert.Contains(name, eigenschaften));
    }

    [Fact]
    public async Task messung_aendern_schickt_ebenfalls_nur_Felder_von_MeasurementUpsertRequest()
    {
        // Die Antwort auf GET api/measurements/{id} wird zum Rumpf des PUT — also
        // muss das, was nach dem Entfernen von id, growId und takenAt übrig bleibt,
        // ein gültiger MeasurementUpsertRequest sein. Geprüft mit dem echten
        // MeasurementDto: dessen Felder kommen per Reflexion in die Attrappe.
        var dto = GrowOsBau.Typ(Vertraege + "MeasurementDto");
        var antwort = new Dictionary<string, object?>();
        foreach (var p in dto.GetProperties())
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            antwort[name] = p.Name switch
            {
                "TakenAt" => "2026-10-01T08:00:00",
                "Stage" => "Flower",
                "Source" => "Manual",
                "WaterFlow" => "Weak",
                "Notes" => "alt",
                _ when p.PropertyType == typeof(bool) => false,
                _ when p.PropertyType == typeof(int) => 7,
                _ => 1.5,
            };
        }
        Assert.True(antwort.Count >= 25);

        var fork = ForkAttrappe.MitGrow().Antwort("GET", @"api/measurements/\d+", 200, JsonSerializer.Serialize(antwort));
        await Werkzeugkasten.TextAsync("messung_aendern", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?> { ["messungId"] = 7, ["ph"] = 6.1, ["zeitpunkt"] = "2026-10-01T09:15" });

        var put = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT");
        var gelesen = JsonSerializer.Deserialize(put.Rumpf!, GrowOsBau.Typ(Vertraege + "MeasurementUpsertRequest"), WieGrowOs);
        Assert.NotNull(gelesen);
    }

    // ------------------------------------------------------------ Auswahllisten gegen die Enums

    private static string[] EnumNamen(string typ) => Enum.GetNames(GrowOsBau.Typ(Modelle + typ));

    [Fact]
    public void Die_Phasen_sind_genau_die_GrowStage_von_Grow_OS()
        => Assert.Equal(EnumNamen("GrowStage"), Messfelder.Phasen.Select(p => p.Wert).ToArray());

    [Fact]
    public void Die_Stroemungen_sind_genau_WaterFlowLevel_von_Grow_OS()
        => Assert.Equal(EnumNamen("WaterFlowLevel"), Messfelder.Stroemungen.Select(p => p.Wert).ToArray());

    [Fact]
    public void Die_Aufgabenzustaende_sind_genau_GrowTaskStatus_von_Grow_OS()
        => Assert.Equal(EnumNamen("GrowTaskStatus"), SchreibWerkzeuge.AufgabenStatus.Select(p => p.Wert).ToArray());

    [Fact]
    public void Die_Journalarten_gibt_es_in_Grow_OS_und_was_fehlt_hat_einen_Grund()
    {
        var alle = EnumNamen("JournalEntryType");
        Assert.True(alle.Length >= 10);
        var angeboten = SchreibWerkzeuge.JournalArten.Select(a => a.Wert).ToList();
        Assert.All(angeboten, art => Assert.Contains(art, alle));

        // Die Meilensteine schreibt Grow OS selbst, wenn phase_bestaetigen sie bestätigt.
        var meilensteine = new[] { "GerminationConfirmed", "CloneRooted", "VegStarted", "FlipToFlower", "FinishStarted" };
        Assert.All(meilensteine, art => Assert.Contains(art, alle));
        Assert.Equal(alle.OrderBy(n => n), angeboten.Concat(meilensteine).OrderBy(n => n));
    }

    [Fact]
    public void Die_Stufen_sind_genau_KiStufe_von_Grow_OS_in_der_Reihenfolge_der_Bits()
    {
        var typ = GrowOsBau.Typ("GrowDiary.Web.Infrastructure.KiZugriff.KiStufe");
        var echte = Enum.GetValues(typ).Cast<object>()
            .Select(w => (Name: w.ToString()!, Bit: Convert.ToInt64(w)))
            .Where(s => s.Bit != 0)
            .OrderBy(s => s.Bit)
            .Select(s => s.Name)
            .ToArray();

        Assert.Equal(echte, Stufen.Alle.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Die_Anzeigenamen_der_Stufen_sind_die_von_Grow_OS()
    {
        var dienst = GrowOsBau.Typ("GrowDiary.Web.Infrastructure.KiZugriff.KiZugriffDienst");
        var stufe = GrowOsBau.Typ("GrowDiary.Web.Infrastructure.KiZugriff.KiStufe");
        var anzeigename = dienst.GetMethod("Anzeigename", BindingFlags.Public | BindingFlags.Static)!;

        foreach (var (name, text) in Stufen.Alle)
        {
            Assert.Equal(anzeigename.Invoke(null, [Enum.Parse(stufe, name)]), text);
        }
    }

    [Fact]
    public void Die_Vorsilbe_des_Schluessels_ist_die_von_Grow_OS()
    {
        var feld = GrowOsBau.Typ("GrowDiary.Web.Infrastructure.KiZugriff.KiZugriffDienst")
            .GetField("Vorsilbe", BindingFlags.Public | BindingFlags.Static)!;
        Assert.Equal(feld.GetRawConstantValue(), ForkSchluessel.Vorsilbe);
    }

    [Fact]
    public void Das_Zeitformat_ist_das_von_Grow_OS()
    {
        var feld = GrowOsBau.Typ("GrowDiary.Web.Api.Controllers.MeasurementsApiController")
            .GetField("ZeitFormat", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(feld.GetRawConstantValue(), Messfelder.OrtszeitFormat);
    }
}
