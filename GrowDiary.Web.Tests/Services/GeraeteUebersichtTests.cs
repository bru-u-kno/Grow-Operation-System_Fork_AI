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

    [Fact]
    public void DieVergleichstabelleKenntNurEchteRollenUndDerenVorgabe()
    {
        // Der Abgleich meldet nur etwas, wenn die Rolle vom YAML abweicht. Passt die
        // Tabelle nicht mehr zu den Rollen (umbenannt, Vorgabe geaendert), meldete er
        // still nichts — deshalb haelt der Test beides zusammen.
        foreach (var (rolle, entity) in Co2SteuerungService.AutomationVerdrahtet)
        {
            var definition = SteuerungGeraeteRollen.Finden(Co2SteuerungService.Modul, rolle);
            Assert.True(definition is not null, $"Rolle {rolle} gibt es nicht mehr.");
            Assert.Equal(definition!.BisherigeVorgabe, entity);
        }
    }

    [Fact]
    public void EineAbweichendeRolleWirdGemeldet()
    {
        var rollen = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["co2_sensor"] = "sensor.ein_anderer_fuehler",
            ["licht"] = "binary_sensor.klein_abluft_zustand",
        };

        var meldung = Assert.Single(Co2SteuerungService.Abweichungen(rollen));
        Assert.Contains("sensor.ein_anderer_fuehler", meldung);
        Assert.Contains("sensor.big_co2_light_sensor_co2", meldung);
    }

    [Fact]
    public void PassendeUndLeereRollenMeldenNichts()
    {
        var rollen = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["co2_sensor"] = "sensor.big_co2_light_sensor_co2",
            ["abluft_stufe"] = null,
        };

        Assert.Empty(Co2SteuerungService.Abweichungen(rollen));
    }

    [Fact]
    public void EntitaetenOhneGeraetWerdenNieAusDemNamenZuEinemGeraet()
    {
        // Frueher wurde aus den ersten beiden Wortteilen ein „Geraet": aus
        // script.edenic_set_alarm das Geraet „Edenic", aus drei Bluelab-Werten
        // „Bluelab Guardian". Beides war geraten. Heute gibt es dafuer nur das
        // Sammelfach „Nicht zugeordnet".
        var geraete = Bauen(Verwendungen(
            ("sensor.bluelab_guardian_ph", "Messgröße ReservoirPh"),
            ("script.edenic_set_alarm", "Steuerung BLUELAB · Grenze setzen")));

        var fach = Assert.Single(geraete);
        Assert.Equal(GeraeteSchluessel.Unzugeordnet, fach.Schluessel);
        Assert.Equal("Nicht zugeordnet", fach.Name);
        Assert.True(fach.IstUnzugeordnet);
        Assert.True(fach.IstRubrik);
        Assert.Equal(2, fach.Entitaeten.Count);
    }

    [Fact]
    public void DasSammelfachHaengtAnKeinemInventarEintrag()
    {
        // Am laufenden Stand gefunden: ein Inventar-Eintrag mit Entitaet im
        // Sammelfach gab dem Fach Zelt und Inventar-Id eines fremden Geraets.
        var hardware = new[] { new HardwareItem { Id = 4, Name = "Entfeuchter", HaEntityId = "switch.demo", TentId = 1 } };

        var fach = Assert.Single(Bauen(Verwendungen(("switch.demo", "Steuerung X · y")), hardware));

        Assert.True(fach.IstUnzugeordnet);
        Assert.Null(fach.HardwareItemId);
        Assert.Null(fach.TentId);
    }

    [Fact]
    public void KeinGeraetEntstehtOhneBelegteQuelle()
    {
        // Jedes Geraet ausser dem Sammelfach und Rubriken hat einen Schluessel aus
        // einer belegten Quelle: HA-Register, Inventar oder Zuordnung des Nutzers.
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["sensor.bluelab_guardian_ph"] = new("sensor.bluelab_guardian_ph", "8efd96", "Bluelab Guardian", "6bf89330_ph"),
        };

        var geraete = Bauen(
            Verwendungen(("sensor.bluelab_guardian_ph", "Messgröße ReservoirPh"),
                         ("script.edenic_set_alarm", "Steuerung BLUELAB · Grenze setzen"),
                         ("input_boolean.irgendwas_an", "Steuerung X · y")),
            herkunft: herkunft);

        foreach (var g in geraete.Where(g => !g.IstRubrik))
        {
            Assert.StartsWith("ha:", g.Schluessel);
        }

        Assert.Equal(2, Assert.Single(geraete, g => g.IstUnzugeordnet).Entitaeten.Count);
    }

    [Fact]
    public void DerVorschlagKommtAusDerSteuerungUndNurWennEindeutig()
    {
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["number.bluelab_guardian_ph_high_alarm"] = new("number.bluelab_guardian_ph_high_alarm", "8efd96", "Bluelab Guardian", "g_ph"),
            ["switch.a"] = new("switch.a", "dev_a", "Gerät A", "a"),
            ["switch.b"] = new("switch.b", "dev_b", "Gerät B", "b"),
        };
        var verwendungen = new Dictionary<string, List<GeraetVerwendung>>(StringComparer.OrdinalIgnoreCase)
        {
            ["number.bluelab_guardian_ph_high_alarm"] = new() { new("Steuerung BLUELAB · pH · oben", GeraetQuellen.Steuerung) },
            ["script.edenic_set_alarm"] = new() { new("Steuerung BLUELAB · Grenze setzen · Skript", GeraetQuellen.Steuerung) },
            ["switch.a"] = new() { new("Steuerung MEHR · eins", GeraetQuellen.Steuerung) },
            ["switch.b"] = new() { new("Steuerung MEHR · zwei", GeraetQuellen.Steuerung) },
            ["script.mehr_skript"] = new() { new("Steuerung MEHR · drei", GeraetQuellen.Steuerung) },
            ["script.ohne_rolle"] = new() { new("Messgröße Irgendwas", GeraetQuellen.Messgroesse) },
        };

        var hinweise = GeraeteUebersichtService.Zuordnungshinweise(
            GeraeteUebersichtService.Zusammenfassen(
                verwendungen,
                herkunft,
                Array.Empty<HardwareItem>(),
                new Dictionary<string, GespeichertesGeraet>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

        Assert.Equal(3, hinweise.Count);
        Assert.Contains(hinweise, h => h.StartsWith("script.edenic_set_alarm", StringComparison.Ordinal)
            && h.Contains("Bluelab Guardian", StringComparison.Ordinal));
        // Zwei Geraete im Modul: der Fork waehlt nicht aus.
        Assert.Contains(hinweise, h => h.StartsWith("script.mehr_skript", StringComparison.Ordinal)
            && h.Contains("bitte einem Gerät zuweisen", StringComparison.Ordinal));
        Assert.Contains(hinweise, h => h.StartsWith("script.ohne_rolle", StringComparison.Ordinal)
            && h.Contains("bitte einem Gerät zuweisen", StringComparison.Ordinal));
    }

    [Fact]
    public void EinControllerMitEigenerMacBleibtEinEintragUndTraegtSeinePorts()
    {
        // Der AC-Infinity-Controller „RDWC": seine eigenen Sensoren tragen
        // …_sensor_7_… in der unique_id und sahen wie ein Kind der MAC aus. Er hing
        // als „Fühler 7" unter einem Platzhalter „Controller 4C16" — derselbe
        // Controller zweimal. Seine Ports kennen ihn per via_device.
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["sensor.big_controller_temperatur"] = new("sensor.big_controller_temperatur", "372e54", "RDWC",
                "ac_infinity_34CDB02C4C16_sensor_7_controllerTemperature"),
            ["binary_sensor.big_port_5_zustand"] = new("binary_sensor.big_port_5_zustand", "e3a70d", "RDWC CO2",
                "ac_infinity_34CDB02C4C16_port_5_loadState", "372e54", "RDWC"),
        };

        var geraete = Bauen(
            Verwendungen(("sensor.big_controller_temperatur", "Steuerung ZULUFT · Kellerfühler"),
                         ("binary_sensor.big_port_5_zustand", "CO₂-Ventil")),
            herkunft: herkunft);

        Assert.DoesNotContain(geraete, g => g.Schluessel.StartsWith("mac:", StringComparison.Ordinal));
        Assert.Equal(2, geraete.Count);

        var controller = Assert.Single(geraete, g => g.Name == "RDWC");
        Assert.True(controller.IstController);
        Assert.Null(controller.ElternSchluessel);
        Assert.Null(controller.Anschluss);

        var ventil = Assert.Single(geraete, g => g.Name == "RDWC CO2");
        Assert.Equal(controller.Schluessel, ventil.ElternSchluessel);
        Assert.Equal("Port 5", ventil.Anschluss);
    }

    [Fact]
    public void EinPortOhneViaDeviceHaengtAmHaControllerMitGleicherMac()
    {
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["sensor.big_controller_temperatur"] = new("sensor.big_controller_temperatur", "372e54", "RDWC",
                "ac_infinity_34CDB02C4C16_sensor_7_controllerTemperature"),
            ["binary_sensor.big_port_5_zustand"] = new("binary_sensor.big_port_5_zustand", "e3a70d", "RDWC CO2",
                "ac_infinity_34CDB02C4C16_port_5_loadState", "372e54", "RDWC"),
            ["number.rdwc_venti_einschaltleistung"] = new("number.rdwc_venti_einschaltleistung", "ffcb63", "RDWC Venti",
                "ac_infinity_34CDB02C4C16_port_1_onSelfSpead"),
        };

        var geraete = Bauen(
            Verwendungen(("sensor.big_controller_temperatur", "Steuerung ZULUFT · Kellerfühler"),
                         ("binary_sensor.big_port_5_zustand", "CO₂-Ventil"),
                         ("number.rdwc_venti_einschaltleistung", "Abluft Stufe")),
            herkunft: herkunft);

        Assert.DoesNotContain(geraete, g => g.Schluessel.StartsWith("mac:", StringComparison.Ordinal));
        var controller = Assert.Single(geraete, g => g.Name == "RDWC");
        Assert.Equal(controller.Schluessel, Assert.Single(geraete, g => g.Name == "RDWC Venti").ElternSchluessel);
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
        Assert.Null(geraet.ElternSchluessel);
    }

    [Fact]
    public void EinLeererElternSchluesselHaengtDasGeraetAus()
    {
        // Die Reolink-Kameras haengen in Home Assistant an der FRITZ!Box, weil sie
        // ueber sie gemeldet werden. Der Nutzer muss sie loesen koennen — und ein
        // leeres Feld darf nicht als 'nichts eingetragen' durchrutschen, sonst
        // erbte das Geraet den Controller sofort wieder.
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["camera.rdwc_overview"] = new("camera.rdwc_overview", "cam1", "RDWC Overview", "reolink_cam1", "fritz", "FRITZ!Box"),
        };
        var gespeichert = new Dictionary<string, GespeichertesGeraet>(StringComparer.OrdinalIgnoreCase)
        {
            [GeraeteUebersichtService.HaSchluessel("cam1")] = new()
            {
                Schluessel = GeraeteUebersichtService.HaSchluessel("cam1"),
                Name = "RDWC Overview",
                ElternSchluessel = string.Empty,
            },
        };

        var ohne = Bauen(Verwendungen(("camera.rdwc_overview", "Kamera")), herkunft: herkunft);
        var mit = Bauen(Verwendungen(("camera.rdwc_overview", "Kamera")), gespeichert: gespeichert, herkunft: herkunft);

        Assert.NotNull(Assert.Single(ohne, g => !g.IstController).ElternSchluessel);
        Assert.Null(Assert.Single(mit, g => !g.IstController).ElternSchluessel);
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
    public void DieZuordnungDesNutzersStichtDasRegister()
    {
        // Zwei Entitäten ohne gemeinsames HA-Gerät — der Nutzer weiß, dass sie zusammengehören.
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
    }

    [Fact]
    public void EinGespeicherterNameStichtInventarUndRegister()
    {
        var hardware = new[]
        {
            new HardwareItem { Id = 3, Name = "Aus dem Inventar", HaEntityId = "sensor.big_probe_sensor_sonden_temperatur" },
        };
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["sensor.big_probe_sensor_sonden_temperatur"] = new("sensor.big_probe_sensor_sonden_temperatur", "c3737f", "RDWC Probe Sensor", "ac_infinity_34CDB02C4C16_sensor_2_probeTemperature"),
        };
        var gespeichert = new Dictionary<string, GespeichertesGeraet>(StringComparer.OrdinalIgnoreCase)
        {
            ["ha:c3737f"] = new() { Schluessel = "ha:c3737f", Name = "Big Probe Sensor" },
        };

        var geraete = Bauen(Verwendungen(("sensor.big_probe_sensor_sonden_temperatur", "Messgröße AirTemperature")), hardware, gespeichert, herkunft: herkunft);

        Assert.Equal("Big Probe Sensor", Assert.Single(geraete, g => g.Schluessel == "ha:c3737f").Name);
    }

    [Fact]
    public void EinLeerGespeicherterNameLaesstDenNamenAusHomeAssistantGelten()
    {
        // Aushaengen speichert eine Korrektur, aber keinen Namen. Zaehlte der leere
        // Name als Korrektur, kaeme ein spaeteres Umbenennen in Home Assistant nie an.
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["camera.rdwc_overview"] = new("camera.rdwc_overview", "cam1", "RDWC Overview", "reolink_1", "fritz", "FRITZ!Box"),
        };
        var gespeichert = new Dictionary<string, GespeichertesGeraet>(StringComparer.OrdinalIgnoreCase)
        {
            ["ha:cam1"] = new() { Schluessel = "ha:cam1", Name = string.Empty, ElternSchluessel = string.Empty },
        };

        var geraete = Bauen(Verwendungen(("camera.rdwc_overview", "Kamera")), gespeichert: gespeichert, herkunft: herkunft);

        var kamera = Assert.Single(geraete, g => g.Schluessel == "ha:cam1");
        Assert.Equal("RDWC Overview", kamera.Name);
        Assert.Null(kamera.ElternSchluessel);
    }

    [Fact]
    public void EineRubrikStehtInDerListeUndTraegtIhreGeraete()
    {
        var gespeichert = new Dictionary<string, GespeichertesGeraet>(StringComparer.OrdinalIgnoreCase)
        {
            ["rubrik:kameras"] = new() { Schluessel = "rubrik:kameras", Name = "Kameras", IstRubrik = true },
            ["ha:cam1"] = new() { Schluessel = "ha:cam1", Name = string.Empty, ElternSchluessel = "rubrik:kameras" },
        };
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["camera.pro"] = new("camera.pro", "cam1", "Pro", "reolink_1", "fritz", "FRITZ!Box"),
        };

        var geraete = Bauen(Verwendungen(("camera.pro", "Kamera")), gespeichert: gespeichert, herkunft: herkunft);

        var rubrik = Assert.Single(geraete, g => g.Schluessel == "rubrik:kameras");
        Assert.True(rubrik.IstRubrik);
        Assert.Empty(rubrik.Entitaeten);

        var kamera = Assert.Single(geraete, g => g.Schluessel == "ha:cam1");
        Assert.Equal("rubrik:kameras", kamera.ElternSchluessel);
        Assert.Equal("Pro", kamera.Name);
    }

    [Fact]
    public void EinEltenSchluesselInsLeereWirdFallenGelassen()
    {
        // Nach dem Loeschen einer Rubrik zeigt das Kind auf einen Geist. Bliebe der
        // Verweis stehen, verschwaende das Geraet aus der Liste — die zeigt nur
        // Wurzeln und deren Kinder.
        var gespeichert = new Dictionary<string, GespeichertesGeraet>(StringComparer.OrdinalIgnoreCase)
        {
            ["ha:cam1"] = new() { Schluessel = "ha:cam1", Name = string.Empty, ElternSchluessel = "rubrik:geloescht" },
        };
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["camera.pro"] = new("camera.pro", "cam1", "Pro", "reolink_1"),
        };

        var kamera = Assert.Single(Bauen(Verwendungen(("camera.pro", "Kamera")), gespeichert: gespeichert, herkunft: herkunft));
        Assert.Null(kamera.ElternSchluessel);
    }

    [Fact]
    public void EineZugewanderteEntitaetBenenntIhrNeuesGeraetNichtUm()
    {
        // Bru schob den CO2-Fuehler versehentlich in eine Kamera — und die Karte
        // hiess danach „RDWC CO2 + Light Sensor". Der Name kam von der
        // zugewanderten Entitaet; gefunden hat er sie so nicht wieder.
        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["camera.rdwc_neu"] = new("camera.rdwc_neu", "cam2", "RDWC neu", "reolink_2"),
            ["sensor.big_co2_light_sensor_co2"] = new("sensor.big_co2_light_sensor_co2", "c17ef", "RDWC CO2 + Light Sensor", "ac_infinity_34CDB02C4C16_sensor_1_co2Sensor"),
        };
        var zuordnungen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sensor.big_co2_light_sensor_co2"] = "ha:cam2",
        };

        var geraete = Bauen(
            Verwendungen(("camera.rdwc_neu", "Kamera"), ("sensor.big_co2_light_sensor_co2", "Messgröße Co2")),
            herkunft: herkunft, zuordnungen: zuordnungen);

        var kamera = Assert.Single(geraete, g => g.Schluessel == "ha:cam2");
        Assert.Equal("RDWC neu", kamera.Name);

        var zugewandert = Assert.Single(kamera.Entitaeten, e => e.EntityId == "sensor.big_co2_light_sensor_co2");
        Assert.True(zugewandert.Verschoben);
        Assert.Equal("RDWC CO2 + Light Sensor", zugewandert.HerkunftName);
    }

    [Fact]
    public void GeraeteOhneEntitaetBleibenInDerListe()
    {
        // Die CO₂-Flasche hat nichts in Home Assistant, trägt aber Wartung und
        // Verschleiß — sie darf nicht herausfallen.
        var hardware = new[] { new HardwareItem { Id = 11, Name = "CO₂-Flasche 10 kg" } };

        var geraete = Bauen(Verwendungen(("sensor.bluelab_guardian_ph", "Messgröße ReservoirPh")), hardware);

        Assert.Equal(2, geraete.Count);
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

        var herkunft = new Dictionary<string, HerkunftEintrag>(StringComparer.OrdinalIgnoreCase)
        {
            ["sensor.bluelab_guardian_temperature"] = new("sensor.bluelab_guardian_temperature", "8efd96", "Bluelab Guardian", "g_temp"),
        };

        var geraet = Assert.Single(Bauen(verwendungen, herkunft: herkunft));
        var entitaet = Assert.Single(geraet.Entitaeten);
        Assert.Equal(2, entitaet.Verwendungen.Count);
    }
}
