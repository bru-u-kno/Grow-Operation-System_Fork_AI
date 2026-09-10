using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Alarmgrenzen, die dem Wochenplan folgen.
/// </summary>
/// <remarks>
/// <b>Der Anlass (10.09.2026).</b> Die Alarme lasen nur die von Hand
/// eingetragenen Zahlen. Ein Lauf nach SKX-Chart bewegt sein EC-Ziel aber
/// woechentlich (Bluete W3: 1,2 — W6: 1,6). Eine feste Obergrenze von 1,2
/// haette ab W4 taeglich gemeldet, obwohl planmaessig gefuettert wurde.
/// </remarks>
public class PlanzielgrenzenTests
{
    private static HydroTargetValues Band(double ecMin = 1.1, double ecMax = 1.3) => new(
        PhMin: 6.0, PhMax: 6.0,
        EcMin: ecMin, EcMax: ecMax,
        OrpMin: 400, OrpMax: 450,
        WaterTempDayC: 20, WaterTempNightC: 18,
        VpdMin: 1.0, VpdMax: 1.2,
        PpfdMin: 800, PpfdMax: 1000,
        Co2Min: 1200, Co2Max: 1400);

    private static TentAlertRule Regel(
        string metricKey = "reservoir-ec",
        Grenzwertquelle quelle = Grenzwertquelle.Plan,
        double? toleranz = null) => new()
        {
            Id = 7,
            TentId = 1,
            MetricKey = metricKey,
            Quelle = quelle,
            Toleranz = toleranz,
            CooldownMinutes = 10,
            Enabled = true,
        };

    [Fact]
    public void Plan_Regel_nimmt_das_Zielband_plus_Toleranz()
    {
        var wirksam = Planzielgrenzen.Wirksam(Regel(toleranz: 0.2), Band(), rampenBodenC: null);

        Assert.NotNull(wirksam);
        Assert.Equal(0.9, wirksam!.MinValue!.Value, 3);
        Assert.Equal(1.5, wirksam.MaxValue!.Value, 3);
    }

    [Fact]
    public void Ohne_eigene_Toleranz_gilt_der_Standard_der_Messgroesse()
    {
        var wirksam = Planzielgrenzen.Wirksam(Regel(), Band(), rampenBodenC: null);

        Assert.Equal(Planzielgrenzen.StandardToleranz("reservoir-ec"), wirksam!.Toleranz);
        Assert.Equal(0.9, wirksam.MinValue!.Value, 3);
    }

    [Fact]
    public void Das_Band_wandert_mit_der_Woche()
    {
        // Dieselbe Regel, zwei Wochen: W3 (EC-Ziel 1,2) und W6 (EC-Ziel 1,6).
        // Genau hier haette die feste Obergrenze 1,2 ab W4 dauernd gemeldet.
        var w3 = Planzielgrenzen.Wirksam(Regel(toleranz: 0.2), Band(1.1, 1.3), null);
        var w6 = Planzielgrenzen.Wirksam(Regel(toleranz: 0.2), Band(1.5, 1.7), null);

        Assert.Equal(1.5, w3!.MaxValue!.Value, 3);
        Assert.Equal(1.9, w6!.MaxValue!.Value, 3);
    }

    [Fact]
    public void Ohne_Zielband_schweigt_die_Regel()
    {
        // Kein aktiver Grow im Zelt: es gibt kein Ziel, also darf auch nichts
        // gemeldet werden. Null heisst „diese Regel ueberspringen".
        Assert.Null(Planzielgrenzen.Wirksam(Regel(), band: null, rampenBodenC: null));
    }

    [Fact]
    public void Messgroessen_ohne_Planwert_bleiben_stumm()
    {
        // Luftfeuchte steht in keinem Sollwertprofil und in keinem Feed-Chart.
        Assert.False(Planzielgrenzen.KenntPlanziel("humidity"));
        Assert.Null(Planzielgrenzen.Wirksam(Regel("humidity"), Band(), null));
    }

    [Fact]
    public void Eine_feste_Regel_bleibt_unangetastet()
    {
        var fest = Regel(quelle: Grenzwertquelle.Fest);
        fest.MinValue = 0.7;
        fest.MaxValue = 1.2;

        var wirksam = Planzielgrenzen.Wirksam(fest, Band(), null);

        Assert.Same(fest, wirksam);
        Assert.Equal(1.2, wirksam!.MaxValue);
    }

    [Fact]
    public void Der_pH_bekommt_ein_Band_statt_eines_Punktes()
    {
        // Das SKX-Chart nennt je Woche EINEN pH-Wert; phMin und phMax sind dann
        // gleich. Ohne Toleranz waere jede Messung eine Ueberschreitung.
        var wirksam = Planzielgrenzen.Wirksam(Regel("reservoir-ph"), Band(), null);

        Assert.NotNull(wirksam);
        Assert.True(wirksam!.MaxValue!.Value - wirksam.MinValue!.Value > 0.3,
            "Ein null breites pH-Band haette rund um die Uhr gemeldet.");
    }

    [Fact]
    public void Die_Nachtabsenkung_zieht_die_Untergrenze_mit()
    {
        // Faehrt die Rampe planmaessig auf 16 °C, darf der Alarm das nicht als
        // Abweichung melden — sonst klingelt jede Nacht die eigene Regelung.
        var ohne = Planzielgrenzen.Wirksam(Regel("reservoir-temp"), Band(), rampenBodenC: null);
        var mit = Planzielgrenzen.Wirksam(Regel("reservoir-temp"), Band(), rampenBodenC: 16);

        Assert.True(mit!.MinValue <= ohne!.MinValue);
    }
}
