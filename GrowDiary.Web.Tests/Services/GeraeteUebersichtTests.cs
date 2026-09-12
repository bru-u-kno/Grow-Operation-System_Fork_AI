using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.22): Die Gerätesicht über die fünf Entity-Quellen.
/// </summary>
/// <remarks>
/// <para><b>Was geprüft wird.</b> Die Zusammenfassung ist reine Rechnung —
/// Verwendungen rein, Geräte raus. Sie entscheidet, ob drei Bluelab-Werte EIN
/// Gerät werden oder drei, und ob eine Korrektur des Nutzers die Vermutung
/// sticht. Beides ist still falsch, wenn es kippt: die Liste sieht weiter
/// plausibel aus, nur eben mit dem falschen Gerätezuschnitt.</para>
/// </remarks>
public sealed class GeraeteUebersichtTests
{
    private static Dictionary<string, List<GeraetVerwendung>> Verwendungen(params (string Entity, string Zweck)[] eintraege)
        => eintraege.ToDictionary(
            e => e.Entity,
            e => new List<GeraetVerwendung> { new(e.Zweck, GeraetQuellen.Messgroesse) },
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<Geraet> Bauen(
        Dictionary<string, List<GeraetVerwendung>> verwendungen,
        IReadOnlyList<HardwareItem>? hardware = null,
        Dictionary<string, GespeichertesGeraet>? gespeichert = null,
        Dictionary<string, string>? zuordnungen = null)
        => GeraeteUebersichtService.Zusammenfassen(
            verwendungen,
            hardware ?? Array.Empty<HardwareItem>(),
            gespeichert ?? new Dictionary<string, GespeichertesGeraet>(StringComparer.OrdinalIgnoreCase),
            zuordnungen ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    [Theory]
    [InlineData("sensor.bluelab_guardian_ph", "bluelab_guardian")]
    [InlineData("sensor.bluelab_guardian_electrical_conductivity", "bluelab_guardian")]
    [InlineData("number.klein_abluft_eingeschaltete_leistung", "klein_abluft")]
    [InlineData("switch.pumpe", "pumpe")]
    [InlineData("binary_sensor.big_port_5_zustand", "big_port")]
    public void SchluesselNimmtDieErstenBeidenAbschnitte(string entityId, string erwartet)
        => Assert.Equal(erwartet, GeraeteSchluessel.AusEntity(entityId));

    [Fact]
    public void EntitaetenMitGleichemStammWerdenEinGeraet()
    {
        var geraete = Bauen(Verwendungen(
            ("sensor.bluelab_guardian_ph", "Messgröße ReservoirPh"),
            ("sensor.bluelab_guardian_electrical_conductivity", "Messgröße ReservoirEc"),
            ("sensor.bluelab_guardian_temperature", "Messgröße ReservoirWaterTemp")));

        var geraet = Assert.Single(geraete);
        Assert.Equal("Bluelab Guardian", geraet.Name);
        Assert.Equal(3, geraet.Entitaeten.Count);
        Assert.False(geraet.Bestaetigt);
    }

    [Fact]
    public void EinInventarEintragIstDasGeraetUndMachtEsBestaetigt()
    {
        var hardware = new[]
        {
            new HardwareItem { Id = 7, Name = "Bluelab Guardian Monitor", HaEntityId = "sensor.bluelab_guardian_ph", TentId = 2 },
        };

        var geraete = Bauen(Verwendungen(("sensor.bluelab_guardian_ph", "Messgröße ReservoirPh")), hardware);

        var geraet = Assert.Single(geraete);
        Assert.Equal("Bluelab Guardian Monitor", geraet.Name);
        Assert.Equal(7, geraet.HardwareItemId);
        Assert.Equal(2, geraet.TentId);
        Assert.True(geraet.Bestaetigt);
    }

    [Fact]
    public void DieZuordnungDesNutzersStichtDieVermutung()
    {
        // Zwei Entitäten, die nach Namen NICHT zusammengehören — der Nutzer weiß es besser.
        var zuordnungen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["switch.zuluft_keller"] = "lueftung_keller",
            ["sensor.air_zuluft_temperatur"] = "lueftung_keller",
        };

        var geraete = Bauen(
            Verwendungen(("switch.zuluft_keller", "Zuluft"), ("sensor.air_zuluft_temperatur", "Außenluft")),
            zuordnungen: zuordnungen);

        var geraet = Assert.Single(geraete);
        Assert.Equal("lueftung_keller", geraet.Schluessel);
        Assert.Equal(2, geraet.Entitaeten.Count);
        Assert.True(geraet.Bestaetigt);
    }

    [Fact]
    public void EinGespeicherterNameStichtInventarUndVermutung()
    {
        var hardware = new[]
        {
            new HardwareItem { Id = 3, Name = "Aus dem Inventar", HaEntityId = "sensor.big_probe_sensor_sonden_temperatur" },
        };
        var gespeichert = new Dictionary<string, GespeichertesGeraet>(StringComparer.OrdinalIgnoreCase)
        {
            [GeraeteUebersichtService.HardwareSchluessel(3)] = new() { Schluessel = GeraeteUebersichtService.HardwareSchluessel(3), Name = "Big Probe Sensor" },
        };

        var geraete = Bauen(Verwendungen(("sensor.big_probe_sensor_sonden_temperatur", "Messgröße AirTemperature")), hardware, gespeichert);

        Assert.Equal("Big Probe Sensor", Assert.Single(geraete).Name);
    }

    [Fact]
    public void GeraeteOhneEntitaetBleibenInDerListe()
    {
        // Die CO₂-Flasche hat nichts in Home Assistant, trägt aber Wartung und
        // Verschleiß — sie darf nicht herausfallen.
        var hardware = new[] { new HardwareItem { Id = 11, Name = "CO₂-Flasche 10 kg" } };

        var geraete = Bauen(Verwendungen(("sensor.bluelab_guardian_ph", "Messgröße ReservoirPh")), hardware);

        var flasche = Assert.Single(geraete, g => g.Name == "CO₂-Flasche 10 kg");
        Assert.Empty(flasche.Entitaeten);
        Assert.Equal(11, flasche.HardwareItemId);
    }

    [Fact]
    public void EineEntitaetMitMehrerenVerwendungenBleibtEineZeile()
    {
        var verwendungen = new Dictionary<string, List<GeraetVerwendung>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sensor.bluelab_guardian_temperature"] = new()
            {
                new("Messgröße ReservoirWaterTemp", GeraetQuellen.Messgroesse),
                new("Steuerung CHILLER · Wassertemperatur", GeraetQuellen.Steuerung),
            },
        };

        var geraet = Assert.Single(Bauen(verwendungen));
        var entitaet = Assert.Single(geraet.Entitaeten);
        Assert.Equal(2, entitaet.Verwendungen.Count);
    }
}
