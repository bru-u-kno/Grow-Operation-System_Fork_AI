using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Fork AI (A-010, 07.10.2026): Der Eingriff eines Probelaufs — Regelung pausieren, Gerät aus, alles zurück.
/// </summary>
public sealed class ProbelaufEingriffTests
{
    private static (FakeProbelaufHa Ha, FakeChillerRegler Chiller, ProbelaufEingriff Eingriff) Anlage()
    {
        var ha = new FakeProbelaufHa();
        ha.Regelungen.Add("automation.entfeuchter_regelung");
        ha.Zustaende["automation.entfeuchter_regelung"] = "on";
        ha.Geraete["port_schalter"] = "select.rdwc_dehumi_aktiver_modus";
        ha.Zustaende["select.rdwc_dehumi_aktiver_modus"] = "Auto";
        var chiller = new FakeChillerRegler();
        return (ha, chiller, new ProbelaufEingriff(ha, chiller));
    }

    [Fact]
    public async Task Ausgangszustand_FragtNurNachRegelungenNichtNachWaechtern()
    {
        var (ha, _, eingriff) = Anlage();

        await eingriff.AusgangszustandLesenAsync("entfeuchter", default);

        Assert.All(ha.AngefragteRollen, r => Assert.Equal(ProbelaufRolle.Pausieren, r));
        Assert.NotEmpty(ha.AngefragteRollen);
    }

    [Fact]
    public async Task Eingreifen_SchaltetErstDieRegelungAusDannDasGeraet()
    {
        var (ha, _, eingriff) = Anlage();
        var ausgang = await eingriff.AusgangszustandLesenAsync("entfeuchter", default);

        var (ok, _) = await eingriff.EingreifenAsync("entfeuchter", ausgang, default);

        Assert.True(ok);
        Assert.Equal(["automation aus automation.entfeuchter_regelung", "aus select.rdwc_dehumi_aktiver_modus"], ha.Protokoll);
    }

    [Fact]
    public async Task Eingreifen_LaesstDasGeraetAn_WennDieRegelungNichtAusgehtDennSieSchaltetEsZurueck()
    {
        var (ha, _, eingriff) = Anlage();
        ha.Haengt.Add("automation.entfeuchter_regelung");
        var ausgang = await eingriff.AusgangszustandLesenAsync("entfeuchter", default);

        var (ok, _) = await eingriff.EingreifenAsync("entfeuchter", ausgang, default);

        Assert.False(ok);
        Assert.DoesNotContain(ha.Protokoll, p => p.StartsWith("aus "));
    }

    [Fact]
    public async Task Zurueckstellen_ErstDasGeraetDannDieRegelung_AufDenFruehherenZustand()
    {
        var (ha, _, eingriff) = Anlage();
        var ausgang = await eingriff.AusgangszustandLesenAsync("entfeuchter", default);
        var (_, geaendert) = await eingriff.EingreifenAsync("entfeuchter", ausgang, default);
        ha.Protokoll.Clear();

        var ok = await eingriff.ZurueckstellenAsync("entfeuchter", geaendert, default);

        Assert.True(ok);
        Assert.Equal(["herstellen select.rdwc_dehumi_aktiver_modus=Auto", "automation an automation.entfeuchter_regelung"], ha.Protokoll);
        Assert.Equal("Auto", ha.Zustaende["select.rdwc_dehumi_aktiver_modus"]);
        Assert.Equal("on", ha.Zustaende["automation.entfeuchter_regelung"]);
    }

    [Fact]
    public async Task Zurueckstellen_NutztDieGespeichertenIds_AuchWennDieZuordnungSichInzwischenAendert()
    {
        var (ha, _, eingriff) = Anlage();
        var ausgang = await eingriff.AusgangszustandLesenAsync("entfeuchter", default);
        var (_, geaendert) = await eingriff.EingreifenAsync("entfeuchter", ausgang, default);
        ha.Geraete["port_schalter"] = "select.ein_anderes_geraet"; // Bru tauscht das Gerät mitten im Lauf
        ha.Protokoll.Clear();

        await eingriff.ZurueckstellenAsync("entfeuchter", geaendert, default);

        Assert.Contains("herstellen select.rdwc_dehumi_aktiver_modus=Auto", ha.Protokoll);
        Assert.DoesNotContain(ha.Protokoll, p => p.Contains("ein_anderes_geraet"));
    }

    [Fact]
    public async Task Zurueckstellen_MeldetFalse_WennEinSchrittNichtBestaetigtIst_UndMacheDennochDenRest()
    {
        var (ha, _, eingriff) = Anlage();
        var ausgang = await eingriff.AusgangszustandLesenAsync("entfeuchter", default);
        var (_, geaendert) = await eingriff.EingreifenAsync("entfeuchter", ausgang, default);
        ha.Haengt.Add("select.rdwc_dehumi_aktiver_modus");
        ha.Protokoll.Clear();

        var ok = await eingriff.ZurueckstellenAsync("entfeuchter", geaendert, default);

        Assert.False(ok);
        Assert.Contains("automation an automation.entfeuchter_regelung", ha.Protokoll);
    }

    [Fact]
    public async Task Eingreifen_FasstEineSchonAusgeschalteteRegelungNichtAn_UndSchaltetSieDanachNichtEin()
    {
        var (ha, _, eingriff) = Anlage();
        ha.Zustaende["automation.entfeuchter_regelung"] = "off";
        var ausgang = await eingriff.AusgangszustandLesenAsync("entfeuchter", default);

        var (_, geaendert) = await eingriff.EingreifenAsync("entfeuchter", ausgang, default);
        ha.Protokoll.Clear();
        await eingriff.ZurueckstellenAsync("entfeuchter", geaendert, default);

        Assert.DoesNotContain(ha.Protokoll, p => p.StartsWith("automation"));
    }

    [Fact]
    public async Task Eingreifen_LaesstEinNichtErreichbaresGeraetUnangetastet()
    {
        var (ha, _, eingriff) = Anlage();
        ha.Zustaende["select.rdwc_dehumi_aktiver_modus"] = "unavailable";
        var ausgang = await eingriff.AusgangszustandLesenAsync("entfeuchter", default);

        await eingriff.EingreifenAsync("entfeuchter", ausgang, default);

        Assert.DoesNotContain(ha.Protokoll, p => p.StartsWith("aus "));
    }

    [Fact]
    public async Task Chiller_SchaltetDenForkReglerAusUndDanachWiederAn()
    {
        var (ha, chiller, eingriff) = Anlage();
        ha.Geraete.Clear();
        ha.Geraete["steckdose"] = "switch.chiller";
        ha.Zustaende["switch.chiller"] = "on";
        var ausgang = await eingriff.AusgangszustandLesenAsync("chiller", default);

        var (ok, geaendert) = await eingriff.EingreifenAsync("chiller", ausgang, default);
        await eingriff.ZurueckstellenAsync("chiller", geaendert, default);

        Assert.True(ok);
        Assert.Equal(["regler aus", "regler an 1"], chiller.Protokoll);
        Assert.Equal("on", ha.Zustaende["switch.chiller"]);
    }

    [Fact]
    public async Task EinAnderesModulAlsChillerRuehrtDenForkReglerNichtAn()
    {
        var (_, chiller, eingriff) = Anlage();
        var ausgang = await eingriff.AusgangszustandLesenAsync("entfeuchter", default);

        var (_, geaendert) = await eingriff.EingreifenAsync("entfeuchter", ausgang, default);
        await eingriff.ZurueckstellenAsync("entfeuchter", geaendert, default);

        Assert.Empty(chiller.Protokoll);
    }

    [Fact]
    public async Task UnbekanntesModulHatKeinenProbelauf()
    {
        var (_, _, eingriff) = Anlage();

        await Assert.ThrowsAsync<ArgumentException>(() => eingriff.AusgangszustandLesenAsync("bluelab", default));
        Assert.False(ProbelaufEingriff.KenntModul("licht"));
    }

    [Fact]
    public async Task ZurueckstellenMitKaputtemAusgangswirftNicht()
    {
        var (_, _, eingriff) = Anlage();

        Assert.False(await eingriff.ZurueckstellenAsync("entfeuchter", "{kaputt", default));
    }

    [Fact]
    public void AlleRegelungsAutomationenSindAusdruecklichEingestuft_UndWaechterBleibenAn()
    {
        var automationen = SteuerungBauteile.Alle.Where(b => b.Art == BauteilArt.Automation).ToList();

        // Keine Automation ohne Entscheidung — sonst wüsste niemand, ob der Probelauf sie anfasst.
        Assert.All(automationen, b => Assert.NotNull(b.Probelauf));
        Assert.All(automationen.Where(b => b.EntityId.Contains("wachter") || b.EntityId.Contains("waechter") || b.EntityId.Contains("zieht_nichts")),
            b => Assert.Equal(ProbelaufRolle.Laufenlassen, b.Probelauf));
        // Es gibt wirklich etwas zu pausieren, in jedem Modul mit Probelauf.
        foreach (var modul in ProbelaufEingriff.Module)
            Assert.Contains(automationen, b => string.Equals(b.Modul, modul, StringComparison.OrdinalIgnoreCase) && b.Probelauf == ProbelaufRolle.Pausieren);
    }
}
