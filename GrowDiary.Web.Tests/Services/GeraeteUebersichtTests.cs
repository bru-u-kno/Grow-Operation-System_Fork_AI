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
        Dictionary<string, string>? zuordnungen = null,
        Dictionary<string, HerkunftEintrag>? herkunft = null)
        => GeraeteUebersichtService.Zusammenfassen(
            verwendungen,
            herkunft ?? new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase),
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
    public void DasHaGeraetSchlaegtDasInventarUndFasstSeineEintraegeZusammen()
    {
        // Das Inventar legt je Messgröße einen Eintrag an — „pH", „EC",
        // „Wassertemperatur" sind EIN Bluelab, nicht drei Geräte. Vor dieser
        // Änderung standen sie dreifach in der Liste.
        var hardware = new[]
        {
            new HardwareItem { Id = 11, Name = "pH", HaEntityId = "sensor.bluelab_guardian_ph", TentId = 2 },
            new HardwareItem { Id = 12, Name = "EC", HaEntityId = "sensor.bluelab_guardian_electrical_conductivity" },
        };
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["sensor.bluelab_guardian_ph"] = new("sensor.bluelab_guardian_ph", "8efd96", "Bluelab Guardian", "6bf89330_ph"),
            ["sensor.bluelab_guardian_electrical_conductivity"] = new("sensor.bluelab_guardian_electrical_conductivity", "8efd96", "Bluelab Guardian", "6bf89330_ec"),
        };

        var geraete = Bauen(
            Verwendungen(("sensor.bluelab_guardian_ph", "Messgröße ReservoirPh"),
                         ("sensor.bluelab_guardian_electrical_conductivity", "Messgröße ReservoirEc")),
            hardware,
            herkunft: herkunft);

        var geraet = Assert.Single(geraete);
        Assert.Equal("Bluelab Guardian", geraet.Name);
        Assert.Equal(2, geraet.Entitaeten.Count);
        Assert.True(geraet.Bestaetigt);
        Assert.Null(geraet.ElternSchluessel);
    }

    [Fact]
    public void ViaDeviceSchlaegtDieMacVermutung()
    {
        // Der Controller heisst in HA "RDWC" — der Fork soll diesen Namen zeigen und
        // nicht "Controller 4C16" aus der MAC bauen.
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["binary_sensor.big_port_5_zustand"] = new("binary_sensor.big_port_5_zustand", "e3a70d", "RDWC CO2",
                "ac_infinity_34CDB02C4C16_port_5_loadState", "372e54", "RDWC"),
        };

        var geraete = Bauen(Verwendungen(("binary_sensor.big_port_5_zustand", "CO₂-Ventil")), herkunft: herkunft);

        var ventil = Assert.Single(geraete, g => !g.IstController);
        var controller = Assert.Single(geraete, g => g.IstController);

        Assert.Equal("RDWC CO2", ventil.Name);
        Assert.Equal("Port 5", ventil.Anschluss);
        Assert.Equal(controller.Schluessel, ventil.ElternSchluessel);
        Assert.Equal("RDWC", controller.Name);
    }

    [Fact]
    public void PortsEinesControllersHaengenAnEinemGeraet()
    {
        // AC Infinity legt je Port ein eigenes HA-Gerät an. Die MAC in der
        // unique_id klammert sie zum Controller; der Port wird zur Steckstelle.
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["binary_sensor.big_port_5_zustand"] = new("binary_sensor.big_port_5_zustand", "e3a70d", "Port 5",
                "ac_infinity_34CDB02C4C16_port_5_loadState"),
            ["number.rdwc_venti_einschaltleistung"] = new("number.rdwc_venti_einschaltleistung", "ffcb63", "RDWC Venti",
                "ac_infinity_34CDB02C4C16_port_1_onSelfSpead"),
            ["binary_sensor.klein_abluft_zustand"] = new("binary_sensor.klein_abluft_zustand", "1abfe0", "Klein Abluft",
                "ac_infinity_4827E289FA8E_port_1_loadState"),
        };

        var geraete = Bauen(
            Verwendungen(("binary_sensor.big_port_5_zustand", "CO₂-Ventil"),
                         ("number.rdwc_venti_einschaltleistung", "Abluft Stufe"),
                         ("binary_sensor.klein_abluft_zustand", "Licht-Status")),
            herkunft: herkunft);

        var controller = geraete.Where(g => g.IstController).ToList();
        Assert.Equal(2, controller.Count);

        var ventil = Assert.Single(geraete, g => g.Entitaeten.Any(e => e.EntityId == "binary_sensor.big_port_5_zustand"));
        var abluft = Assert.Single(geraete, g => g.Entitaeten.Any(e => e.EntityId == "number.rdwc_venti_einschaltleistung"));
        var licht = Assert.Single(geraete, g => g.Entitaeten.Any(e => e.EntityId == "binary_sensor.klein_abluft_zustand"));

        // Gleicher Controller, verschiedene Ports.
        Assert.Equal(ventil.ElternSchluessel, abluft.ElternSchluessel);
        Assert.Equal("Port 5", ventil.Anschluss);
        Assert.Equal("Port 1", abluft.Anschluss);

        // Anderer Controller.
        Assert.NotEqual(ventil.ElternSchluessel, licht.ElternSchluessel);
    }

    [Theory]
    [InlineData("ac_infinity_34CDB02C4C16_port_5_loadState", "34CDB02C4C16", "Port 5")]
    [InlineData("ac_infinity_34CDB02C4C16_sensor_2_probeTemperature", "34CDB02C4C16", "Fühler 2")]
    [InlineData("6bf89330-2cb9-11f0-b22b-63543a698b74_ph", null, null)]
    public void MacUndSteckstelleKommenAusDerUniqueId(string uniqueId, string? mac, string? anschluss)
    {
        var (gelesen, stelle) = GeraeteHerkunft.Lesen(uniqueId);
        Assert.Equal(mac, gelesen);
        Assert.Equal(anschluss, stelle);
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
            ["big_probe"] = new() { Schluessel = "big_probe", Name = "Big Probe Sensor" },
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
