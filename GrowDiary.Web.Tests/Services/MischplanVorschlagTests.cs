using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Der Mischplan-Vorschlag für den Wasserwechsel (A-006) — die Regeln, die Bru
/// am 05.10.2026 entschieden hat.
/// </summary>
/// <remarks>
/// Zahlen aus Brus Wechsel vom 04.10.: 160 L, SKX Canna Aqua Blütewoche 7
/// (EC-Ziel 1,2, CalMag 0,5 ml/L, A/B je 2 ml/L, PK 1 ml/L), Leitungswasser mit
/// 500 µS/cm, Calcium 66,7 mg/L, Magnesium 8,1 mg/L.
/// </remarks>
public sealed class MischplanVorschlagTests
{
    private static readonly FeedChartColumn Woche7 = new()
    {
        Id = "bluete-7",
        Label = "Blütewoche 7",
        Stage = "Flower",
        Week = 7,
        EcTarget = 1.2,
        PhMin = 5.8,
        PhMax = 6.2,
        Items =
        [
            new FeedChartItem { Component = "CalMag Agent", MinMlPerLiter = 0.5, MaxMlPerLiter = 0.5 },
            new FeedChartItem { Component = "Aqua Flores A", MinMlPerLiter = 2, MaxMlPerLiter = 2 },
            new FeedChartItem { Component = "Aqua Flores B", MinMlPerLiter = 2, MaxMlPerLiter = 2 },
            new FeedChartItem { Component = "PK 13/14", MinMlPerLiter = 1, MaxMlPerLiter = 1 },
            new FeedChartItem { Component = "Cannaboost", MinMlPerLiter = 1, MaxMlPerLiter = 3 },
        ],
    };

    private static readonly WaterProfile Goerlitz = new()
    {
        SourceLabel = "Stadtwerke Görlitz",
        ConductivityUsCm = 500,
        TotalHardnessDh = 11.2,
        CalciumMgL = 66.7,
        MagnesiumMgL = 8.1,
    };

    private static readonly List<Verbrauchsartikel> Artikel =
    [
        new() { Id = 4, Name = "Canna Aqua Flores A", Einheit = "ml", Aktiv = true },
        new() { Id = 5, Name = "Aqua Flores B", Einheit = "ml", Aktiv = true },
        new() { Id = 8, Name = "CalMag Agent", Einheit = "ml", Aktiv = true },
        new() { Id = 6, Name = "Dünger", Produkt = "PK 13/14", Einheit = "ml", Aktiv = true },
    ];

    private static MischplanVorschlag Rechnen(WaterSource wasser, double? osmose = null, WaterProfile? profil = null, double? eigenerEc = null, double liter = 160)
        => MischplanVorschlagRechnung.Rechnen("SKX Canna Aqua", Woche7, null, 165, profil ?? Goerlitz, Artikel, liter, wasser, osmose, eigenerEc);

    private static MischplanVorschlagZeile Zeile(MischplanVorschlag v, string name) => v.Zeilen.Single(z => z.Komponente == name);

    [Fact]
    public void LeitungswasserBringtSeinenEcUndSeinCalciumMit()
    {
        var v = Rechnen(WaterSource.Tap);

        Assert.Equal(0.5, v.WasserEcVorschlag);
        Assert.Equal(1.7, v.EcZielGesamt);
        Assert.Equal(0, Zeile(v, "CalMag Agent").VorschlagMl);
        Assert.Contains("66,7 mg/L Calcium", v.CalMagHinweis);
        Assert.Contains("8,1 mg/L Magnesium", v.CalMagHinweis);
        Assert.Equal("entfällt — Calcium aus dem Leitungswasser", Zeile(v, "CalMag Agent").Hinweis);
        Assert.Contains("Stadtwerke Görlitz", v.WasserEcQuelle);

        // Die Grunddünger rechnen unabhängig vom Wasser nach Plan × Liter.
        Assert.Equal(320, Zeile(v, "Aqua Flores A").VorschlagMl);
        Assert.Equal(160, Zeile(v, "PK 13/14").VorschlagMl);
    }

    [Fact]
    public void OsmoseIstNullUndCalMagNachPlan()
    {
        var v = Rechnen(WaterSource.RO);

        Assert.Equal(0, v.WasserEcVorschlag);
        Assert.Equal(1.2, v.EcZielGesamt);
        Assert.Equal(80, Zeile(v, "CalMag Agent").VorschlagMl);
    }

    [Fact]
    public void MischungRechnetAnteilig()
    {
        var v = Rechnen(WaterSource.Mixed, osmose: 50);

        Assert.Equal(0.25, v.WasserEcVorschlag);
        Assert.Equal(1.45, v.EcZielGesamt);
        Assert.Equal(40, Zeile(v, "CalMag Agent").VorschlagMl);
        Assert.Contains("50 % Osmose + 50 % Leitung", v.WasserEcQuelle);
    }

    [Fact]
    public void EigenerWasserEcUeberschreibtDenVorschlag()
    {
        var v = Rechnen(WaterSource.Tap, eigenerEc: 0.4);

        Assert.Equal(0.5, v.WasserEcVorschlag);
        Assert.Equal(0.4, v.WasserEc);
        Assert.Equal(1.6, v.EcZielGesamt);
    }

    [Fact]
    public void WeichesLeitungswasserBekommtCalMagNachPlan()
    {
        var weich = new WaterProfile { ConductivityUsCm = 150, TotalHardnessDh = 4.2, CalciumMgL = 22 };
        var v = Rechnen(WaterSource.Tap, profil: weich);

        Assert.Equal(80, Zeile(v, "CalMag Agent").VorschlagMl);
        Assert.Contains("weich", v.CalMagHinweis);
    }

    [Fact]
    public void OhneCalciumImProfilWirdNichtsWeggelassen()
    {
        var v = Rechnen(WaterSource.Tap, profil: new WaterProfile { ConductivityUsCm = 500 });

        Assert.Equal(80, Zeile(v, "CalMag Agent").VorschlagMl);
        Assert.Contains("kein Calcium-Wert", v.CalMagHinweis);
    }

    [Fact]
    public void OhneLeitwertGibtEsKeinGesamtziel()
    {
        var v = Rechnen(WaterSource.Tap, profil: new WaterProfile { CalciumMgL = 60 });

        Assert.Null(v.WasserEcVorschlag);
        Assert.Null(v.EcZielGesamt);
        Assert.Contains("kein Leitwert", v.WasserEcQuelle);
    }

    [Fact]
    public void EineSpanneRechnetMitDerMitte()
    {
        var v = Rechnen(WaterSource.Tap);
        Assert.Equal(320, Zeile(v, "Cannaboost").VorschlagMl);
        Assert.Contains("1–3", Zeile(v, "Cannaboost").Hinweis);
    }

    [Fact]
    public void DieArtikelWerdenGefundenOhneZuRaten()
    {
        var v = Rechnen(WaterSource.Tap);

        Assert.Equal(4, Zeile(v, "Aqua Flores A").ArtikelId); // als Wort im Namen
        Assert.Equal(5, Zeile(v, "Aqua Flores B").ArtikelId); // genau
        Assert.Equal(6, Zeile(v, "PK 13/14").ArtikelId);      // über das Produkt
        Assert.Null(Zeile(v, "Cannaboost").ArtikelId);         // keiner da

        // Zwei passende → keiner; die App rät nicht.
        var doppelt = new List<Verbrauchsartikel>
        {
            new() { Id = 1, Name = "Aqua Flores A (alt)", Aktiv = true },
            new() { Id = 2, Name = "Aqua Flores A (neu)", Aktiv = true },
        };
        Assert.Null(MischplanVorschlagRechnung.ArtikelFuer("Aqua Flores A", doppelt));

        // Ein inaktiver Artikel zählt nicht.
        Assert.Null(MischplanVorschlagRechnung.ArtikelFuer("Aqua Flores B", [new() { Id = 9, Name = "Aqua Flores B", Aktiv = false }]));
    }

    [Theory]
    [InlineData("CalMag Agent", MischplanRolle.CalMagMittel)]
    [InlineData("CaMg", MischplanRolle.CalMagMittel)]
    [InlineData("Aqua Flores A", MischplanRolle.Grundduenger)]
    [InlineData("Bloom B", MischplanRolle.Grundduenger)]
    [InlineData("PK 13/14", MischplanRolle.Grundduenger)]
    [InlineData("PK", MischplanRolle.Grundduenger)]
    [InlineData("Cannaboost", MischplanRolle.ZusatzImPlan)]
    [InlineData("Cleanse", MischplanRolle.ZusatzImPlan)]
    [InlineData("Rhizotonic", MischplanRolle.ZusatzImPlan)]
    public void RollenAusDenNamenImWissen(string komponente, MischplanRolle erwartet)
        => Assert.Equal(erwartet, MischplanVorschlagRechnung.Rolle(komponente));
}
