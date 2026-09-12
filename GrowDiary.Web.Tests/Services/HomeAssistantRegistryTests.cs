using System.Text.Json;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.22): Die Auswertung der beiden HA-Register.
/// </summary>
/// <remarks>
/// <para>Die Nutzlast unten ist gekürzt, aber im Aufbau echt — so antwortet
/// <c>config/entity_registry/list</c> und <c>config/device_registry/list</c> in
/// Brus Anlage. Geprüft wird das, was danach still falsch wäre: dass der vom
/// Nutzer vergebene Gerätename gewinnt, dass eine Entität ohne Gerät nicht
/// verloren geht, und dass fehlende Register nichts zum Absturz bringen.</para>
/// </remarks>
public sealed class HomeAssistantRegistryTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private const string Entitaeten = """
        [
          { "entity_id": "binary_sensor.big_port_5_zustand", "device_id": "e3a70d", "original_name": "Zustand",
            "unique_id": "ac_infinity_34CDB02C4C16_port_5_loadState", "platform": "ac_infinity" },
          { "entity_id": "sensor.bluelab_guardian_ph", "device_id": "8efd96", "original_name": "Ph",
            "unique_id": "6bf89330-2cb9-11f0-b22b-63543a698b74_ph", "platform": "bluelab_guardian" },
          { "entity_id": "input_number.co2_zielwert", "device_id": null, "original_name": "CO2 Zielwert",
            "unique_id": null, "platform": "input_number" }
        ]
        """;

    private const string Geraete = """
        [
          { "id": "372e54", "name": "RDWC", "name_by_user": null, "via_device_id": null },
          { "id": "e3a70d", "name": "RDWC Port 5", "name_by_user": "CO2-Ventil", "via_device_id": "372e54" },
          { "id": "8efd96", "name": "Bluelab Guardian", "name_by_user": null, "via_device_id": null }
        ]
        """;

    [Fact]
    public void DerNameDesNutzersGewinntVorDemNamenDerIntegration()
    {
        var herkunft = HomeAssistantRegistryService.Zusammenfuehren(Json(Entitaeten), Json(Geraete));

        Assert.Equal("CO2-Ventil", herkunft["binary_sensor.big_port_5_zustand"].DeviceName);
        Assert.Equal("Bluelab Guardian", herkunft["sensor.bluelab_guardian_ph"].DeviceName);
    }

    [Fact]
    public void ViaDeviceNenntDenControllerSamtNamen()
    {
        // Home Assistant modelliert die Kette selbst: jedes Port-Geraet zeigt auf
        // seinen Controller. Das ist die Wahrheit, nicht die MAC-Vermutung.
        var eintrag = HomeAssistantRegistryService.Zusammenfuehren(Json(Entitaeten), Json(Geraete))["binary_sensor.big_port_5_zustand"];

        Assert.Equal("372e54", eintrag.ViaDeviceId);
        Assert.Equal("RDWC", eintrag.ViaDeviceName);
    }

    [Fact]
    public void EinControllerOhneViaDeviceHaengtAnNichts()
        => Assert.Null(HomeAssistantRegistryService.Zusammenfuehren(Json(Entitaeten), Json(Geraete))["sensor.bluelab_guardian_ph"].ViaDeviceId);

    [Fact]
    public void DeviceIdUndUniqueIdKommenMit()
    {
        var eintrag = HomeAssistantRegistryService.Zusammenfuehren(Json(Entitaeten), Json(Geraete))["binary_sensor.big_port_5_zustand"];

        Assert.Equal("e3a70d", eintrag.DeviceId);
        Assert.Equal("ac_infinity_34CDB02C4C16_port_5_loadState", eintrag.UniqueId);
    }

    [Fact]
    public void EntitaetenOhneGeraetBleibenInDerListe()
    {
        // Helfer (input_number, template) hängen an keinem Gerät. Sie fallen sonst
        // aus der Zuordnung und die Geräteseite zeigt sie nirgends.
        var eintrag = HomeAssistantRegistryService.Zusammenfuehren(Json(Entitaeten), Json(Geraete))["input_number.co2_zielwert"];

        Assert.Null(eintrag.DeviceId);
        Assert.Equal("CO2 Zielwert", eintrag.DeviceName);
    }

    [Fact]
    public void FehlendeRegisterGebenEineLeereListeStattEinesFehlers()
    {
        Assert.Empty(HomeAssistantRegistryService.Zusammenfuehren(null, null));
        Assert.Empty(HomeAssistantRegistryService.Zusammenfuehren(Json("{}"), Json("[]")));
    }

    [Theory]
    [InlineData("AVM", "FRITZ!Smart Energy 210", "AVM FRITZ!Smart Energy 210")]
    [InlineData("FRITZ!", "FRITZ!Box 7590 (UI)", "FRITZ!Box 7590 (UI)")]
    [InlineData("AC Infinity", "UIS Controller AI+ (CTR89Q)", "AC Infinity UIS Controller AI+ (CTR89Q)")]
    [InlineData("Bluelab", null, "Bluelab")]
    [InlineData(null, null, null)]
    public void DieModellzeileDoppeltDenHerstellerNicht(string? hersteller, string? modell, string? erwartet)
        => Assert.Equal(erwartet, HomeAssistantRegistryService.Modellzeile(hersteller, modell));

    [Theory]
    [InlineData("http://supervisor/core", "ws://supervisor/core/api/websocket")]
    [InlineData("https://smarthome.k9d.world/", "wss://smarthome.k9d.world/api/websocket")]
    [InlineData("http://homeassistant.local:8123", "ws://homeassistant.local:8123/api/websocket")]
    public void DieSocketAdresseFolgtDemSchemaDerBasis(string basis, string erwartet)
        => Assert.Equal(erwartet, HomeAssistantRegistryService.SocketAdresse(basis).ToString());
}
