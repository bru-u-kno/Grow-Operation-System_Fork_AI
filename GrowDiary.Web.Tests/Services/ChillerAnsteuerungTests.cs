using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (Chiller-Ansteuerung, F-030): Steckdose oder Kühler mit eigenem
/// Thermostat — und die Hysterese, die vorher nur im Fork stand.
/// </summary>
public class ChillerAnsteuerungTests
{
    private static readonly string Ordner = Path.Combine(AppContext.BaseDirectory, "Vorlagen", "chiller");

    private static JsonObject Datei(string name)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Ordner, name)))!;

    private static Dictionary<string, string> Rollen(bool steckdose, bool sollwert)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["wasser_temp"] = "sensor.wasser",
            ["licht_zustand"] = "binary_sensor.licht",
        };
        if (steckdose) d["steckdose"] = "switch.kuehler";
        if (sollwert) d["kuehler_sollwert"] = "climate.kuehler";
        return d;
    }

    [Theory]
    [InlineData("switch.a", null, ChillerAnsteuerung.Steckdose)]
    [InlineData(null, "climate.k", ChillerAnsteuerung.Regelbar)]
    [InlineData("switch.a", "climate.k", ChillerAnsteuerung.Beides)]
    [InlineData(null, null, ChillerAnsteuerung.Keine)]
    [InlineData(" ", "", ChillerAnsteuerung.Keine)]
    public void DieAnsteuerungErgibtSichAusDenRollen(string? steckdose, string? sollwert, string erwartet)
        => Assert.Equal(erwartet, ChillerAnsteuerung.Aus(steckdose, sollwert));

    [Fact]
    public void AutomatikAktivMeintBeiEinemSollwertGeraetDieSollwertAutomation()
    {
        Assert.Equal(ChillerSteuerungService.Entitaeten.Automatik,
            ChillerSteuerungService.AutomatikFuer(ChillerAnsteuerung.Steckdose));
        Assert.Equal(ChillerSteuerungService.Entitaeten.SollwertAutomatik,
            ChillerSteuerungService.AutomatikFuer(ChillerAnsteuerung.Regelbar));
        Assert.Equal(ChillerSteuerungService.Entitaeten.SollwertAutomatik,
            ChillerSteuerungService.AutomatikFuer(ChillerAnsteuerung.Beides));
    }

    [Fact]
    public void NurSteckdose_RegelungJa_SollwertNein()
    {
        var r = Rollen(steckdose: true, sollwert: false);
        Assert.Null(SteuerungAutomationService.UeberfluessigWegen(Datei("regelung.json"), r));
        Assert.NotNull(SteuerungAutomationService.Fuellen(Datei("regelung.json"), r));
        // Ohne Sollwert-Gerät bleibt ein Platzhalter stehen — keine Automation.
        Assert.Null(SteuerungAutomationService.Fuellen(Datei("sollwert.json"), r));
    }

    [Fact]
    public void NurSollwertGeraet_SollwertJa_RegelungNein()
    {
        var r = Rollen(steckdose: false, sollwert: true);
        var sollwert = SteuerungAutomationService.Fuellen(Datei("sollwert.json"), r);
        Assert.NotNull(sollwert);
        Assert.Contains("climate.kuehler", sollwert!.ToJsonString());
        Assert.Null(SteuerungAutomationService.Fuellen(Datei("regelung.json"), r));
    }

    [Fact]
    public void Beides_DieSteckdosenRegelungEntfaelltMitGrund()
    {
        // Regelt das Gerät selbst, würde die Steckdosen-Regelung es bei
        // erreichtem Ziel abwürgen. Die Vorlage entfällt — ausdrücklich, nicht
        // als „fehlendes Gerät".
        var r = Rollen(steckdose: true, sollwert: true);
        Assert.Equal("kuehler_sollwert", SteuerungAutomationService.UeberfluessigWegen(Datei("regelung.json"), r));
        Assert.NotNull(SteuerungAutomationService.Fuellen(Datei("sollwert.json"), r));
    }

    [Theory]
    [InlineData(true, false, "switch.kuehler", "climate.kuehler")]
    [InlineData(false, true, "climate.kuehler", "switch.kuehler")]
    public void DerWaechterSchaltetAbWasDaIst(bool steckdose, bool sollwert, string drin, string nichtDrin)
    {
        var fertig = SteuerungAutomationService.Fuellen(Datei("waechter.json"), Rollen(steckdose, sollwert));
        Assert.NotNull(fertig);
        var text = fertig!.ToJsonString();
        Assert.Contains(drin, text);
        Assert.DoesNotContain(nichtDrin, text);
        Assert.DoesNotContain("\"wenn", text);
    }

    [Fact]
    public void WennNichtImInnerenEntferntNurDiesenBlock()
    {
        var vorlage = (JsonObject)JsonNode.Parse("""
            { "alias": "x", "actions": [
                { "wennNicht": "b", "action": "a.weg" },
                { "action": "a.bleibt", "target": { "entity_id": "[[a]]" } } ] }
            """)!;
        var zuordnung = new Dictionary<string, string> { ["a"] = "switch.a", ["b"] = "climate.b" };

        var fertig = SteuerungAutomationService.Fuellen(vorlage, zuordnung)!.ToJsonString();

        Assert.DoesNotContain("a.weg", fertig);
        Assert.Contains("a.bleibt", fertig);
        Assert.DoesNotContain("wennNicht", fertig);
    }

    [Fact]
    public void DieHystereseLandetInHomeAssistant()
    {
        var liste = ChillerSteuerungService.Schreibliste(new ChillerEinstellungen { HystereseK = 0.8 }, []);
        Assert.Contains(liste, z => z.Entity == ChillerSteuerungService.Entitaeten.Hysterese && z.Wert == 0.8);
    }

    [Fact]
    public void EinAlterStandUebernimmtDieHystereseAusHomeAssistant()
    {
        // Vor F-030 stand 0,3 im Fork, gewirkt hat aber 0,6 in HA. Der alte
        // Wert darf beim ersten Speichern nicht nach HA wandern.
        var alt = new ChillerEinstellungen { HystereseK = 0.3, HystereseGefuehrt = false };
        var zustaende = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ChillerSteuerungService.Entitaeten.Hysterese] = "0.6",
        };

        Assert.Equal(0.6, ChillerSteuerungService.MitHystereseAusHomeAssistant(alt, zustaende).HystereseK);
    }

    [Fact]
    public void EinVomForkGefuehrterWertBleibt()
    {
        var neu = new ChillerEinstellungen { HystereseK = 1.0, HystereseGefuehrt = true };
        var zustaende = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ChillerSteuerungService.Entitaeten.Hysterese] = "0.6",
        };

        Assert.Equal(1.0, ChillerSteuerungService.MitHystereseAusHomeAssistant(neu, zustaende).HystereseK);
    }

    [Fact]
    public void DieHystereseWirdAusHomeAssistantGelesen()
    {
        var e = ChillerSteuerungService.AusHomeAssistant(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ChillerSteuerungService.Entitaeten.Hysterese] = "0.9",
        });
        Assert.Equal(0.9, e.HystereseK);
    }

    [Fact]
    public void DerKuehlbedarfRechnetMitDemHelferUndSchaltetBeimZielAus()
    {
        var bauteil = SteuerungBauteile.Alle.Single(b => b.EntityId == ChillerSteuerungService.Entitaeten.Kuehlbedarf);
        Assert.Contains("input_number.chiller_hysterese", bauteil.Vorlage);
        Assert.Contains("t <= z %", bauteil.Vorlage);
        Assert.DoesNotContain("0.3", bauteil.Vorlage);
        Assert.Contains(SteuerungBauteile.Alle, b => b.EntityId == ChillerSteuerungService.Entitaeten.Hysterese);
    }

    [Fact]
    public void KeineDerBeidenAnsteuerungsRollenIstPflicht()
    {
        Assert.False(SteuerungGeraeteRollen.Finden("chiller", "steckdose")!.Pflicht);
        var sollwert = SteuerungGeraeteRollen.Finden("chiller", "kuehler_sollwert")!;
        Assert.False(sollwert.Pflicht);
        Assert.Equal(new[] { "climate", "number" }, sollwert.Domains);
    }
}

/// <summary>Fork AI (forkai.136): Einmalige Übernahme aus Crop Steering.</summary>
public class CropSteeringUebernahmeTests
{
    [Fact]
    public void SteckdoseUndSollwertGeraetWerdenZuRollen()
    {
        var r = ChillerSteuerungService.UebernahmeAus(false,
            new[] { null, "switch.kuehler" }, new[] { "climate.kuehler" });
        Assert.Equal("switch.kuehler", r["steckdose"]);
        Assert.Equal("climate.kuehler", r["kuehler_sollwert"]);
    }

    [Fact]
    public void EinHelferAlsZielgeraetWirdNichtUebernommen()
    {
        var r = ChillerSteuerungService.UebernahmeAus(false,
            Array.Empty<string?>(), new[] { "input_number.chiller_zieltemperatur_nacht" });
        Assert.Empty(r);
    }

    [Fact]
    public void WerSchonZugeordnetHatBehaeltSeineRollen()
        => Assert.Empty(ChillerSteuerungService.UebernahmeAus(true,
            new[] { "switch.kuehler" }, new[] { "climate.kuehler" }));
}

/// <summary>Fork AI (F-032): Die Geräte-Zählung der Chiller-Seite.</summary>
public class ChillerGeraeteZaehlenTests
{
    private static Dictionary<string, string?> Basis() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["wasser_temp"] = "sensor.w",
        ["licht_zustand"] = "binary_sensor.l",
        ["steckdose_zustand"] = null,
        ["leistung"] = null,
    };

    [Fact]
    public void NurSteckdose_DieLeereSollwertRolleIstKeineLuecke()
    {
        var g = Basis(); g["steckdose"] = "switch.k"; g["kuehler_sollwert"] = null;
        Assert.Equal((3, 3), ChillerSteuerungService.GeraeteZaehlen(g));
    }

    [Fact]
    public void NurSollwertGeraet_DieLeereSteckdoseIstKeineLuecke()
    {
        var g = Basis(); g["steckdose"] = null; g["kuehler_sollwert"] = "climate.k";
        Assert.Equal((3, 3), ChillerSteuerungService.GeraeteZaehlen(g));
    }

    [Fact]
    public void OhneBeides_FehltEineStelle()
    {
        var g = Basis(); g["steckdose"] = null; g["kuehler_sollwert"] = null;
        var (zu, ges) = ChillerSteuerungService.GeraeteZaehlen(g);
        Assert.True(zu < ges);
    }
}

/// <summary>Fork AI (F-034): Frühere Vorgaben werden nur übernommen, wo es sie gibt.</summary>
public class RollenVorgabenUebernahmeTests
{
    private static readonly GeraeteRolle[] Rollen =
    {
        new("chiller", "wasser_temp", "Wasserfühler", "messen", "sensor.w", new[] { "sensor" }),
        new("chiller", "steckdose", "Kühler · schalten", "schalten", "switch.k", new[] { "switch" }, Pflicht: false),
        new("chiller", "kuehler_sollwert", "Kühler · Sollwert", "schalten", "", new[] { "climate" }, Pflicht: false),
    };

    [Fact]
    public void BeiBruWirdAllesUebernommen()
    {
        var r = RollenVorgabenUebernahme.Uebernehmen(Rollen, _ => Array.Empty<string>(),
            new HashSet<string>(new[] { "sensor.w", "switch.k" })).ToList();
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void FremdeKennungenBleibenDraussen()
        => Assert.Empty(RollenVorgabenUebernahme.Uebernehmen(Rollen, _ => Array.Empty<string>(),
            new HashSet<string>(new[] { "sensor.anderes" })));

    [Fact]
    public void EigeneZuordnungenWerdenNichtUeberschrieben()
    {
        var r = RollenVorgabenUebernahme.Uebernehmen(Rollen, _ => new[] { "wasser_temp" },
            new HashSet<string>(new[] { "sensor.w", "switch.k" })).ToList();
        Assert.Single(r);
        Assert.Equal("steckdose", r[0].Rolle);
    }

    [Fact]
    public void EsGibtKeineWerksvorgabeMehr()
        => Assert.All(SteuerungGeraeteRollen.Alle, rolle => Assert.Equal(string.Empty, rolle.Vorgabe));
}
