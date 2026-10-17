using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.238): Der Zustand der Zuluft — warum der Lüfter läuft oder nicht.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (17.10.2026).</b> Die Kachel sagte „bereit", obwohl die Differenz bei 1,43 g/m³ lag und der
/// Lüfter nicht lief. „bereit" hieß nur: kein Bedarf, keine Sperre, keine Kälte-Pause — es sagte nicht, dass die
/// Einschaltschwelle (1,5) noch nicht erreicht war. Jeder Fall unten ist ein Grund, der einen anderen ausschließt;
/// die Reihenfolge der Prüfung steht im Dienst (<see cref="ZuluftSteuerungService.Zustand"/>).</para>
/// </remarks>
public sealed class ZuluftZustandTests
{
    private static ZuluftEinstellungen Einstellungen(double schwelle = 1.5, double abstand = 0.5, double frost = 2) => new()
    {
        MindestDifferenzGm3 = schwelle,
        AbschaltAbstandGm3 = abstand,
        AussentemperaturMinC = frost,
    };

    /// <summary>Ein ruhiger Ausgangsstand: Automatik an, Port online und aus, kein Bedarf, Differenz 1,43.</summary>
    private static ZuluftLive Live(
        double? diff = 1.43, bool? bedarf = false, bool? portAn = false, bool? automatik = true, bool? online = true,
        int? ist = null, int? ziel = 6, int? sperre = 0, double? zelt = 21.8, bool? kalt = false, double? aussen = 12.8) =>
        new(
            HaErreichbar: true, DifferenzGm3: diff, AussenAbsolutGm3: 10.2, KellerAbsolutGm3: 11.6, AussenTempC: aussen,
            AussenRhProzent: 90, KellerTempC: 19, KellerRhProzent: 71, Bedarf: bedarf, Zielstufe: ziel, IstStufe: ist,
            PortAn: portAn, PortOnline: online, AutomatikAn: automatik, SperreRestMin: sperre, LetzterWechsel: null,
            ZeltTempC: zelt, PauseZeltKalt: kalt, DifferenzRegelGm3: diff);

    private static ZuluftZustand Z(ZuluftLive live, ZuluftEinstellungen? e = null, double? zeltMin = 17)
        => ZuluftSteuerungService.Zustand(live, e ?? Einstellungen(), zeltMin);

    [Fact]
    public void Unter_der_Schwelle_sagt_wie_viel_fehlt_und_gibt_das_Band()
    {
        // Der Fall, der den Anlass gab: 1,43 von 1,5, Lüfter aus.
        var z = Z(Live(diff: 1.43));

        Assert.Equal("zu-wenig", z.Kennung);
        Assert.Equal("aus", z.Wort);
        Assert.Equal("zu wenig Unterschied · noch 0,07 bis 1,5", z.Satz);
        Assert.Equal("neutral", z.Ton);
        Assert.Equal(new ZuluftBalken(1.43, 1.0, 1.5), z.Balken);
    }

    [Fact]
    public void Der_Satz_rechnet_mit_der_Regeldifferenz_nicht_mit_der_rohen()
    {
        // Roh 1,55 (über der Schwelle), geglättet 1,43: die Regelung entscheidet nach 1,43.
        var live = Live(diff: 1.55) with { DifferenzRegelGm3 = 1.43 };

        Assert.Equal("zu-wenig", Z(live).Kennung);
        Assert.Equal("zu wenig Unterschied · noch 0,07 bis 1,5", Z(live).Satz);
    }

    [Fact]
    public void Ohne_geglaettete_Differenz_gilt_die_rohe()
    {
        var live = Live(diff: 1.2) with { DifferenzRegelGm3 = null };

        Assert.Equal("zu wenig Unterschied · noch 0,30 bis 1,5", Z(live).Satz);
    }

    [Fact]
    public void Aussenluft_feuchter_als_der_Keller()
    {
        var z = Z(Live(diff: -0.4));

        Assert.Equal("aussen-feuchter", z.Kennung);
        Assert.Equal("Außenluft ist feuchter als der Keller", z.Satz);
        Assert.Null(z.Balken);
    }

    [Fact]
    public void Differenz_null_ist_nicht_feuchter_sondern_unbekannt()
    {
        var z = Z(Live(diff: null) with { DifferenzRegelGm3 = null });

        Assert.Equal("differenz-unbekannt", z.Kennung);
    }

    [Fact]
    public void Der_Luefter_laeuft_im_Haltebereich_und_nennt_die_Abschaltgrenze()
    {
        // Schwelle 1,2, Abstand 0,2 → aus unter 1,0; Differenz 1,12 liegt dazwischen.
        var z = Z(Live(diff: 1.12, bedarf: true, portAn: true, ist: 6), Einstellungen(1.2, 0.2));

        Assert.Equal("saugt", z.Kennung);
        Assert.Equal("saugt", z.Wort);
        Assert.Equal("Stufe 6 · hält bis unter 1,0", z.Satz);
        Assert.Equal("ok", z.Ton);
    }

    [Fact]
    public void Ueber_der_Einschaltgrenze_haelt_er_nicht_er_saugt_einfach()
    {
        var z = Z(Live(diff: 1.6, bedarf: true, portAn: true, ist: 6), Einstellungen(1.2, 0.2));

        Assert.Equal("Stufe 6", z.Satz);
    }

    [Fact]
    public void Die_Abschaltgrenze_zeigt_zwei_Stellen_wenn_sie_welche_hat()
    {
        var z = Z(Live(diff: 1.3, bedarf: true, portAn: true, ist: 4), Einstellungen(1.5, 0.25));

        Assert.Equal("Stufe 4 · hält bis unter 1,25", z.Satz);
    }

    [Fact]
    public void Laeuft_ohne_Stufenangabe_steht_kein_erfundenes_Stufenwort()
    {
        var z = Z(Live(diff: 1.6, bedarf: true, portAn: true, ist: null));

        Assert.Equal("Lüfter läuft", z.Satz);
    }

    [Fact]
    public void Bedarf_da_aber_Sperre_aktiv_nennt_die_Restzeit_und_die_Zielstufe()
    {
        var z = Z(Live(diff: 1.44, bedarf: true, sperre: 8, ziel: 6));

        Assert.Equal("sperre", z.Kennung);
        Assert.Equal("wartet", z.Wort);
        Assert.Equal("Mindestpause noch 8 min, dann Stufe 6", z.Satz);
        Assert.Equal("warn", z.Ton);
    }

    [Fact]
    public void Bedarf_da_und_Sperre_frei_heisst_er_schaltet_gleich_ein()
    {
        var z = Z(Live(diff: 1.6, bedarf: true, sperre: 0));

        Assert.Equal("startet", z.Kennung);
    }

    [Fact]
    public void Zelt_zu_kalt_nennt_Temperatur_und_Freigabegrenze()
    {
        var z = Z(Live(diff: 1.6, kalt: true, zelt: 16.5), zeltMin: 17);

        Assert.Equal("zelt-kalt", z.Kennung);
        Assert.Equal("pausiert", z.Wort);
        Assert.Equal("Zelt 16,5 °C · ab 18 °C wieder frei", z.Satz);
        Assert.Equal("warn", z.Ton);
    }

    [Fact]
    public void Zelt_zu_kalt_ohne_Messwert_bleibt_beim_Grund_ohne_Zahlen()
    {
        var z = Z(Live(diff: 1.6, kalt: true, zelt: null));

        Assert.Equal("Zelt zu kalt", z.Satz);
    }

    [Fact]
    public void Automatik_aus_geht_allem_voran_auch_dem_Laufen()
    {
        var z = Z(Live(diff: 1.6, bedarf: true, portAn: true, automatik: false, ist: 6));

        Assert.Equal("automatik-aus", z.Kennung);
        Assert.Equal("Automatik aus", z.Wort);
        Assert.Equal("Lüfter bleibt, wie er steht", z.Satz);
    }

    [Fact]
    public void Port_offline_geht_dem_Rest_voran()
    {
        var z = Z(Live(diff: 1.6, bedarf: true, online: false));

        Assert.Equal("offline", z.Kennung);
        Assert.Equal("warn", z.Ton);
    }

    [Fact]
    public void Frostschutz_haelt_zurueck_wenn_die_Differenz_reicht_und_der_Bedarf_trotzdem_aus_ist()
    {
        var z = Z(Live(diff: 1.8, aussen: 1.5), Einstellungen(frost: 2));

        Assert.Equal("frost", z.Kennung);
        Assert.Equal("Außen 1,5 °C · Frostschutz bis 2 °C", z.Satz);
    }

    [Fact]
    public void Reicht_die_Differenz_und_nichts_haelt_zurueck_wird_der_fehlende_Bedarf_gesagt()
    {
        var z = Z(Live(diff: 1.8, bedarf: false));

        Assert.Equal("bedarf-fehlt", z.Kennung);
        Assert.Equal("Bedarf noch nicht gemeldet", z.Satz);
    }

    [Fact]
    public void Ohne_Bedarfszustand_steht_unbekannt_nicht_noch_nicht_gemeldet()
    {
        var z = Z(Live(diff: 1.8, bedarf: null));

        Assert.Equal("Bedarf unbekannt", z.Satz);
    }

    [Fact]
    public void Genau_auf_der_Schwelle_ist_es_nicht_mehr_zu_wenig()
    {
        // Die Regelung schaltet bei d >= Schwelle ein — 1,5 ist also genug.
        var z = Z(Live(diff: 1.5));

        Assert.NotEqual("zu-wenig", z.Kennung);
    }

    [Fact]
    public void Eine_fast_erreichte_Schwelle_sagt_nie_noch_0_00()
    {
        // 1,496 rundet auf 1,50 — „noch 0,00 bis 1,5" wäre ein Widerspruch in einem Satz.
        var z = Z(Live(diff: 1.496));

        Assert.Equal("zu wenig Unterschied · noch 0,01 bis 1,5", z.Satz);
    }

    [Fact]
    public void Ohne_Abschalt_Abstand_hat_das_Band_keinen_Haltebereich()
    {
        var z = Z(Live(diff: 1.0), Einstellungen(1.5, 0));

        Assert.Equal(new ZuluftBalken(1.0, 1.5, 1.5), z.Balken);
    }

    [Fact]
    public void Port_offline_geht_auch_dem_Laufen_voran()
    {
        // Der Port meldet „an", ist aber nicht erreichbar: dem Zustand „saugt" ist dann nicht zu trauen.
        var z = Z(Live(diff: 1.6, bedarf: true, portAn: true, online: false, ist: 6));

        Assert.Equal("offline", z.Kennung);
    }

    [Fact]
    public void Die_Kachel_zeigt_die_Regeldifferenz_nicht_die_rohe()
    {
        var live = Live(diff: 1.55) with { DifferenzRegelGm3 = 1.43 };
        live = live with { Zustand = Z(live) };

        var modul = ZuluftKachel.Modul(live, "1,5 g/m³");

        Assert.Equal("1,43 g/m³", modul.Wert);
        Assert.Equal("Ansaugen ab 1,5 g/m³ · Automatik an", modul.Kurz);
    }

    [Fact]
    public void Die_Kachel_gibt_Zustand_und_Balken_ans_DTO_weiter()
    {
        var live = Live(diff: 1.43);
        live = live with { Zustand = Z(live) };

        var modul = ZuluftKachel.Modul(live, "1,5 g/m³");

        Assert.Equal(new SteuerungModulZustandDto("aus", "zu wenig Unterschied · noch 0,07 bis 1,5", "neutral"), modul.Zustand);
        Assert.Equal(new SteuerungModulBalkenDto(1.43, 1.0, 1.5), modul.Balken);
        Assert.Equal("aus · zu wenig Unterschied · noch 0,07 bis 1,5", modul.Unterzeile);
    }

    [Theory]
    [InlineData("zu-wenig", "aus")]
    [InlineData("sperre", "warn")]
    [InlineData("zelt-kalt", "warn")]
    [InlineData("offline", "warn")]
    [InlineData("saugt", "an")]
    [InlineData("automatik-aus", "aus")]
    public void Der_Punkt_der_Kachel_folgt_dem_Zustand(string fall, string erwartet)
    {
        var live = fall switch
        {
            "zu-wenig" => Live(diff: 1.43),
            "sperre" => Live(diff: 1.44, bedarf: true, sperre: 8),
            "zelt-kalt" => Live(diff: 1.6, kalt: true),
            "offline" => Live(diff: 1.6, online: false),
            "saugt" => Live(diff: 1.6, bedarf: true, portAn: true, ist: 6),
            _ => Live(diff: 1.6, automatik: false),
        };
        live = live with { Zustand = Z(live) };

        Assert.Equal(fall, live.Zustand!.Kennung);
        Assert.Equal(erwartet, ZuluftKachel.Modul(live, "1,5 g/m³").Status);
    }

    [Fact]
    public void Ohne_Zustand_bleibt_die_Kachel_ehrlich_und_ohne_Zusatzfelder()
    {
        var modul = ZuluftKachel.Modul(Live(diff: 1.0), "1,5 g/m³");

        Assert.Equal("Zustand unbekannt", modul.Unterzeile);
        Assert.Null(modul.Zustand);
        Assert.Null(modul.Balken);
    }

    [Fact]
    public void Der_Balken_gehoert_nur_zum_Fall_zu_wenig_Unterschied()
    {
        // Selbsttest der Zählung: jede Kennung außer „zu-wenig" kommt ohne Balken aus, und es gibt mehr als einen Fall.
        var faelle = new[]
        {
            Z(Live(diff: -0.4)),
            Z(Live(diff: 1.6, bedarf: true, portAn: true, ist: 6)),
            Z(Live(diff: 1.6, bedarf: true, sperre: 8)),
            Z(Live(diff: 1.6, kalt: true)),
            Z(Live(diff: 1.6, automatik: false)),
            Z(Live(diff: 1.6, online: false)),
        };

        Assert.True(faelle.Length >= 6);
        Assert.All(faelle, f => Assert.Null(f.Balken));
    }
}
