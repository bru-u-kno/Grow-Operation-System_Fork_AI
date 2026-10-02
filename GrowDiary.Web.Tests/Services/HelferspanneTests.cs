using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (02.10.2026): Kein Wert, den der Fork in einen Zahlen-Helfer
/// schreibt, liegt außerhalb dessen Spanne.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> <c>input_number.set_value</c> lehnt jeden Wert
/// außerhalb von min/max ab — bei jedem stündlichen Abgleich wieder, und die
/// Seite zeigte eine Grenze, die nie galt. Drei belegte Wege dorthin: die
/// Feuchte-Obergrenze bis 90 % (Helfer endet bei 80), das Plan-Ziel
/// 800 ppm × 45 % = 360 (Helfer beginnt bei 400) und beim Entfeuchter
/// Plan-Luft + Abstand bis 15 K über das Ende des Helfers (35 °C).</para>
/// <para>Die Grenzen kommen aus dem Katalog (<see cref="SteuerungBauteile"/>),
/// aus dem auch die Helfer angelegt werden — die Tests lesen sie ebenfalls
/// von dort, statt sie abzutippen.</para>
/// </remarks>
public sealed class HelferspanneTests
{
    private static (double Min, double Max) Spanne(string entity) => SteuerungBauteile.Spanne(entity);

    [Fact]
    public void PlanZiel_UnterDemHelfer_WirdAufDessenUntergrenzeGehoben()
    {
        var e = new Co2Einstellungen { ZielQuelle = "plan", AnteilWarmProzent = 45, AnteilMittelProzent = 45, AnteilKuehlProzent = 45 };

        var (warm, mittel, kuehl) = Co2SteuerungService.WirksameZiele(e, planPpm: 800);

        Assert.Equal(Spanne(Co2SteuerungService.Entitaeten.ZielWarm).Min, warm);
        Assert.Equal(Spanne(Co2SteuerungService.Entitaeten.ZielMittel).Min, mittel);
        Assert.Equal(Spanne(Co2SteuerungService.Entitaeten.ZielKuehl).Min, kuehl);
    }

    [Fact]
    public void Selbsttest_PlanZielInDerSpanneBleibtUnveraendert()
    {
        var e = new Co2Einstellungen { ZielQuelle = "plan", AnteilWarmProzent = 100, AnteilMittelProzent = 90, AnteilKuehlProzent = 80 };

        Assert.Equal((1000, 900, 800), Co2SteuerungService.WirksameZiele(e, planPpm: 1000));
    }

    [Fact]
    public void FeuchteObergrenze_UeberDemHelfer_IstEinFeldfehler()
    {
        var max = Spanne(Co2SteuerungService.Entitaeten.RhObergrenze).Max;
        var e = new Co2Einstellungen { RhObergrenzeProzent = max + 5, RhNotbremseFestProzent = 90 };

        Assert.Contains(nameof(Co2Einstellungen.RhObergrenzeProzent), Co2SteuerungService.Pruefen(e).Keys);
    }

    [Fact]
    public void Selbsttest_FeuchteObergrenzeAmEndeDesHelfersIstErlaubt()
    {
        var max = Spanne(Co2SteuerungService.Entitaeten.RhObergrenze).Max;
        var e = new Co2Einstellungen { RhObergrenzeProzent = max, RhNotbremseFestProzent = 90 };

        Assert.DoesNotContain(nameof(Co2Einstellungen.RhObergrenzeProzent), Co2SteuerungService.Pruefen(e).Keys);
    }

    /// <summary>
    /// Die Zählung: jeder Eintrag der Schreibliste — auch bei Werten an den
    /// Rändern und einem Plan, der weit draußen liegt.
    /// </summary>
    [Fact]
    public void Schreibliste_JederWertLiegtInDerSpanneSeinesHelfers()
    {
        var e = new Co2Einstellungen
        {
            CanopyObergrenzeModus = GrenzModus.Plan, CanopyObergrenzeAbstandK = 15,
            RhNotbremseModus = GrenzModus.Plan, RhNotbremseAbstandProzent = 30,
            KlimaToleranzMinuten = 30, T6StufeKlima = 5,
        };

        var liste = Co2SteuerungService.Schreibliste(e, warm: 300, mittel: 2500, kuehl: 390, [],
            rhObergrenzeWirksam: 95, planLuftTagC: 40);

        // Mengenwächter: ohne Einträge liefe die Prüfung null Mal durch.
        Assert.True(liste.Count >= 15, $"Nur {liste.Count} Einträge — die Grundmenge stimmt nicht.");
        var draussen = liste
            .Where(z => SteuerungBauteile.Alle.Any(b => b.EntityId == z.Entity && b.Min is not null))
            .Where(z => z.Wert < Spanne(z.Entity).Min || z.Wert > Spanne(z.Entity).Max)
            .Select(z => $"{z.Entity} = {z.Wert} (Spanne {Spanne(z.Entity).Min}–{Spanne(z.Entity).Max})")
            .ToList();
        Assert.True(draussen.Count == 0, "Außerhalb der Helfer-Spanne:\n  " + string.Join("\n  ", draussen));
    }

    [Fact]
    public void Entfeuchter_PlanLuftPlusAbstand_EndetAmHelfer()
    {
        var max = Spanne(EntfeuchterSteuerungService.Entitaeten.TempMaxTag).Max;

        var wert = EntfeuchterSteuerungService.TempMaxFuer(EntfeuchterSteuerungService.Entitaeten.TempMaxTag,
            TempMaxModus.Plan, abstandK: 15, festC: 27, planLuftC: 30);

        Assert.Equal(max, wert);
    }

    [Fact]
    public void Selbsttest_EntfeuchterImRahmenBleibtDieRechnung()
    {
        Assert.Equal(29, EntfeuchterSteuerungService.TempMaxFuer(EntfeuchterSteuerungService.Entitaeten.TempMaxNacht,
            TempMaxModus.Plan, abstandK: 5, festC: 27, planLuftC: 24));
    }

    /// <summary>
    /// Die Begrenzung sucht den Helfer im Katalog über seine Entitäts-Id —
    /// steht dieselbe Id zweimal mit verschiedener Spanne darin, wäre sie
    /// mehrdeutig.
    /// </summary>
    [Fact]
    public void JederZahlenHelferStehtMitEinerSpanneImKatalog()
    {
        var zahlen = SteuerungBauteile.Alle.Where(b => b.Art == BauteilArt.Zahl).ToList();
        Assert.True(zahlen.Count >= 30, $"Nur {zahlen.Count} Zahlen-Helfer — die Grundmenge stimmt nicht.");

        var mehrdeutig = zahlen.GroupBy(b => b.EntityId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(b => (b.Min, b.Max)).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(mehrdeutig);
        Assert.All(zahlen, b => Assert.True(b.Min is not null && b.Max is not null, $"{b.EntityId} hat keine Spanne."));
    }

    [Fact]
    public void EinUnbekannterHelferScheitertLaut()
        => Assert.Throws<InvalidOperationException>(() => SteuerungBauteile.Spanne("input_number.gibt_es_nicht"));
}
