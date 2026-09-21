using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (forkai.129, F-023): Die Entfeuchter-Einstellungen — Rundweg,
/// Prüfung, Übernahme vorhandener Helfer und „Temperatur max. = Plan + Abstand".
/// </summary>
/// <remarks>
/// Der allgemeine <c>RundwegVollstaendigTests</c> füllt jedes Feld mit derselben
/// Probe (1). Hier kollidiert das mit den Prüfungen (AUS unter EIN, Wartezeit
/// Außenluft nicht kürzer als die Einschaltverzögerung, Temperatur 15–35 °C) —
/// deshalb fährt dieser Test die Felder mit eigenen, gültigen Werten.
/// </remarks>
public sealed class EntfeuchterEinstellungenTests : IDisposable
{
    private readonly string _wurzel;
    private readonly SteuerungRepository _repo;

    public EntfeuchterEinstellungenTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "EntfeuchterRundweg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        var pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(pfade);
        _repo = new SteuerungRepository(pfade);
    }

    [Fact]
    public void JedesFeldUeberlebtDasSpeichern()
    {
        // Überall andere Werte als die Vorgaben, sonst fiele ein verlorenes Feld nicht auf.
        var gesendet = new EntfeuchterEinstellungen
        {
            VpdRegelung = false,
            HystereseProzent = 6,
            MindestlaufzeitMin = 15,
            EinschaltverzoegerungMin = 7,
            WartezeitAussenluftMin = 30,
            TagbetriebErlauben = false,
            AutomatikAktiv = false,
            TempMaxTagModus = TempMaxModus.Plan,
            TempMaxTagAbstandK = 4.5,
            TempMaxTagFestC = 28,
            TempMaxNachtModus = TempMaxModus.Plan,
            TempMaxNachtAbstandK = 3,
            TempMaxNachtFestC = 24,
            FeuchteEinTag = 58,
            FeuchteAusTag = 54,
            FeuchteEinNacht = 61,
            FeuchteAusNacht = 56,
        };
        Assert.Empty(EntfeuchterSteuerungService.Pruefen(gesendet));

        _repo.SetEinstellungen(EntfeuchterSteuerungService.Modul, gesendet);
        var g = _repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!;

        Assert.False(g.VpdRegelung);
        Assert.Equal(6, g.HystereseProzent);
        Assert.Equal(15, g.MindestlaufzeitMin);
        Assert.Equal(7, g.EinschaltverzoegerungMin);
        Assert.Equal(30, g.WartezeitAussenluftMin);
        Assert.False(g.TagbetriebErlauben);
        Assert.False(g.AutomatikAktiv);
        Assert.Equal(TempMaxModus.Plan, g.TempMaxTagModus);
        Assert.Equal(4.5, g.TempMaxTagAbstandK);
        Assert.Equal(28, g.TempMaxTagFestC);
        Assert.Equal(TempMaxModus.Plan, g.TempMaxNachtModus);
        Assert.Equal(3, g.TempMaxNachtAbstandK);
        Assert.Equal(24, g.TempMaxNachtFestC);
        Assert.Equal(58, g.FeuchteEinTag);
        Assert.Equal(54, g.FeuchteAusTag);
        Assert.Equal(61, g.FeuchteEinNacht);
        Assert.Equal(56, g.FeuchteAusNacht);
    }

    [Fact]
    public void OhneGespeichertenStandGeltenDieVorhandenenHelfer()
    {
        // Brus Stand vom 21.09.2026.
        var e = EntfeuchterSteuerungService.AusHomeAssistant(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EntfeuchterSteuerungService.Entitaeten.VpdRegelung] = "on",
            [EntfeuchterSteuerungService.Entitaeten.Hysterese] = "4.0",
            [EntfeuchterSteuerungService.Entitaeten.Mindestlaufzeit] = "20.0",
            [EntfeuchterSteuerungService.Entitaeten.Einschaltverzoegerung] = "10.0",
            [EntfeuchterSteuerungService.Entitaeten.WartezeitAussenluft] = "25.0",
            [EntfeuchterSteuerungService.Entitaeten.Tagbetrieb] = "on",
            [EntfeuchterSteuerungService.Entitaeten.TempMaxTag] = "29.0",
            [EntfeuchterSteuerungService.Entitaeten.TempMaxNacht] = "25.0",
            [EntfeuchterSteuerungService.Entitaeten.FeuchteEinNacht] = "62.0",
            [EntfeuchterSteuerungService.Entitaeten.FeuchteAusNacht] = "60.0",
            [EntfeuchterSteuerungService.Entitaeten.Automatik] = "on",
        });

        Assert.True(e.VpdRegelung);
        Assert.Equal(4, e.HystereseProzent);
        Assert.Equal(20, e.MindestlaufzeitMin);
        Assert.Equal(10, e.EinschaltverzoegerungMin);
        Assert.Equal(25, e.WartezeitAussenluftMin);
        Assert.Equal(29, e.TempMaxTagFestC);
        Assert.Equal(25, e.TempMaxNachtFestC);
        Assert.Equal(62, e.FeuchteEinNacht);
        Assert.Equal(60, e.FeuchteAusNacht);
        // Ob der Wert einmal aus dem Plan kam, weiß HA nicht — also „Fest".
        Assert.Equal(TempMaxModus.Fest, e.TempMaxTagModus);
        Assert.True(e.AutomatikAktiv);
    }

    [Fact]
    public void EinFehlenderOderUnlesbarerHelferBleibtAufDerVorgabe()
    {
        var vorgabe = new EntfeuchterEinstellungen();
        var e = EntfeuchterSteuerungService.AusHomeAssistant(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EntfeuchterSteuerungService.Entitaeten.Hysterese] = "unavailable",
            [EntfeuchterSteuerungService.Entitaeten.VpdRegelung] = "unknown",
        });
        Assert.Equal(vorgabe.HystereseProzent, e.HystereseProzent);
        Assert.Equal(vorgabe.VpdRegelung, e.VpdRegelung);
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(1, false)]
    [InlineData(10, false)]
    [InlineData(10.5, true)]
    public void DieHystereseHatGrenzen(double wert, bool abgelehnt)
    {
        var f = EntfeuchterSteuerungService.Pruefen(new EntfeuchterEinstellungen { HystereseProzent = wert });
        Assert.Equal(abgelehnt, f.ContainsKey(nameof(EntfeuchterEinstellungen.HystereseProzent)));
    }

    [Fact]
    public void DieWartezeitAufAussenluftIstNieKuerzerAlsDieEinschaltverzoegerung()
    {
        var f = EntfeuchterSteuerungService.Pruefen(new EntfeuchterEinstellungen { EinschaltverzoegerungMin = 20, WartezeitAussenluftMin = 10 });
        Assert.Contains(nameof(EntfeuchterEinstellungen.WartezeitAussenluftMin), f.Keys);
    }

    [Fact]
    public void AusMussUnterEinLiegen()
    {
        var f = EntfeuchterSteuerungService.Pruefen(new EntfeuchterEinstellungen { FeuchteEinTag = 55, FeuchteAusTag = 55 });
        Assert.Contains(nameof(EntfeuchterEinstellungen.FeuchteAusTag), f.Keys);
    }

    [Fact]
    public void EinUnbekannterModusWirdAbgelehnt()
    {
        var f = EntfeuchterSteuerungService.Pruefen(new EntfeuchterEinstellungen { TempMaxNachtModus = "auto" });
        Assert.Contains(nameof(EntfeuchterEinstellungen.TempMaxNachtModus), f.Keys);
    }

    [Fact]
    public void PlanPlusAbstandErgibtDieTemperaturMax()
    {
        // Blüte W5: Plan-Luft Tag 24 °C, Nacht 20 °C → + 5 K = heutige 29/25 °C.
        Assert.Equal(29, EntfeuchterSteuerungService.TempMax(TempMaxModus.Plan, 5, 27, 24));
        Assert.Equal(25, EntfeuchterSteuerungService.TempMax(TempMaxModus.Plan, 5, 22, 20));
    }

    [Fact]
    public void FestIgnoriertDenPlan()
        => Assert.Equal(27.5, EntfeuchterSteuerungService.TempMax(TempMaxModus.Fest, 5, 27.5, 24));

    [Fact]
    public void OhnePlanGiltDerFesteWert()
    {
        // Kein laufender Grow mit Wochen-Zielen: lieber der feste Wert als keiner —
        // ohne Grenze liefe der Entfeuchter auch in der Hitze weiter.
        Assert.Equal(28, EntfeuchterSteuerungService.TempMax(TempMaxModus.Plan, 5, 28, null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_wurzel)) Directory.Delete(_wurzel, recursive: true);
    }
}
