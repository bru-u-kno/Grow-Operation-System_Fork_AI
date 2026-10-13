using GrowDiary.Web.Api.Controllers;

namespace GrowDiary.Web.Tests.Api;

/// <summary>Fork AI (Schritt 4): welche HA-Automationen als Wächter erscheinen.</summary>
public sealed class HaWaechterTests
{
    private static readonly (string, string?, string?)[] Beispiel =
    [
        ("automation.water_chiller_wachter", "Water Chiller Wächter", "on"),
        ("automation.co2_wachter_rdwc_port_5", "CO2 Wächter (RDWC Port 5)", "off"),
        ("automation.dosier_waechter", "Dosier-Waechter", "on"),
        ("automation.eg_kammer_trockner_fertig", "EG Kammer Trockner fertig", "on"),
        ("sensor.wachter_temperatur", "Wächter Temperatur", "21"),
        ("automation.water_chiller_regelung", "Water Chiller Regelung", "on"),
        ("automation.grow_uv_c_verbindungs_wachter", "Grow UV-C – Verbindungs-Wächter", "on"),
        ("automation.zusatz_entfeuchter_zieht_nichts", "Zusatz-Entfeuchter zieht nichts", "on"),
    ];

    [Fact]
    public void NimmtNurAutomationenMitWaechterImNamen()
    {
        var liste = MeldungenApiController.Auswahl(Beispiel);
        Assert.Equal(
            ["automation.co2_wachter_rdwc_port_5", "automation.water_chiller_wachter", "automation.zusatz_entfeuchter_zieht_nichts", "automation.dosier_waechter"],
            liste.Select(w => w.EntityId));
    }

    [Fact]
    public void BekannteTragenIhrenZweckUndDenZustand()
    {
        var liste = MeldungenApiController.Auswahl(Beispiel);
        var chiller = liste.Single(w => w.EntityId == "automation.water_chiller_wachter");
        Assert.True(chiller.Bekannt);
        Assert.True(chiller.Aktiv);
        Assert.Contains("24 °C", chiller.Zweck);
        Assert.False(liste.Single(w => w.EntityId == "automation.co2_wachter_rdwc_port_5").Aktiv);
        Assert.Null(liste.Single(w => w.EntityId == "automation.dosier_waechter").Zweck);
    }

    /// <summary>
    /// A-021: Die Meldung „Zusatz-Entfeuchter zieht nichts" meldet aufs Handy, hat aber „Wächter" nicht im Namen —
    /// sie stand deshalb nie in der Liste der Absender. Sie ist jetzt bekannt, mit einem Satz dazu.
    /// </summary>
    [Fact]
    public void DieMeldungDesZusatzEntfeuchtersStehtInDerListe()
    {
        var eintrag = MeldungenApiController.Auswahl(Beispiel).Single(w => w.EntityId == "automation.zusatz_entfeuchter_zieht_nichts");
        Assert.True(eintrag.Bekannt);
        Assert.Contains("Tank voll", eintrag.Zweck);
    }
}
