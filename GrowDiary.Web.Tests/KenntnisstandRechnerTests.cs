using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Fork AI (A-010, Etappe 2, 07.10.2026): Der Kenntnisstand — Zielabgleich, Wirkung, Abdeckung, nächster Lauf.
/// </summary>
public sealed class KenntnisstandRechnerTests
{
    private static readonly DateTime T0 = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
    private static string Titel(string modul) => modul switch { "entfeuchter" => "Entfeuchter", "entfeuchter-zusatz" => "Zusatz-Entfeuchter", "co2" => "CO₂", _ => modul };

    private static List<ZeltMinute> Reihe(bool tag, int minuten, Func<int, double> feuchte, Func<int, double>? temp = null, Func<int, double>? vpd = null)
        => Enumerable.Range(0, minuten)
            .Select(i => new ZeltMinute(T0.AddMinutes(i), feuchte(i), (temp ?? (_ => 22))(i), (vpd ?? (_ => 1.2))(i), tag)).ToList();

    private static readonly Zielbaender Ziele = new(
        Tag: new ZielBand(FeuchteMax: 62, TempMax: 27.5, VpdMin: 0.9, VpdMax: 1.4),
        Nacht: new ZielBand(FeuchteMax: 51, TempMax: 24, VpdMin: 1.4, VpdMax: 1.4));

    // ------------------------------------------------------------- Zielabgleich

    [Fact]
    public void Zielabgleich_AnteilDerZeitImZiel_UndUrteilsGrenzen()
    {
        // Nacht: 100 Minuten, davon 75 unter 51 % → 75 % → „knapp".
        var serie = Reihe(false, 100, i => i < 75 ? 50 : 55);

        var feuchte = KenntnisstandRechner.Zielabgleich(serie, Ziele).Single(z => z.Groesse == "Luftfeuchte");

        Assert.Equal(75, feuchte.Nacht.AnteilProzent);
        Assert.Equal(ZielUrteil.Knapp, feuchte.Nacht.Urteil);
        Assert.Equal("≤ 51 %", feuchte.Nacht.ZielText);
    }

    [Theory]
    [InlineData(90, ZielUrteil.Erreichbar)]
    [InlineData(89, ZielUrteil.Knapp)]
    [InlineData(70, ZielUrteil.Knapp)]
    [InlineData(69, ZielUrteil.Luecke)]
    public void Zielabgleich_Urteil_AnDenSchwellen(int imZielVonHundert, ZielUrteil erwartet)
    {
        var serie = Reihe(false, 100, i => i < imZielVonHundert ? 50 : 60);

        var urteil = KenntnisstandRechner.Zielabgleich(serie, Ziele).Single(z => z.Groesse == "Luftfeuchte").Nacht.Urteil;

        Assert.Equal(erwartet, urteil);
    }

    [Fact]
    public void Zielabgleich_ZuWenigMesszeit_IstUnbekannt_StattEinesUrteils()
    {
        var serie = Reihe(false, 30, _ => 60);

        var phase = KenntnisstandRechner.Zielabgleich(serie, Ziele).Single(z => z.Groesse == "Luftfeuchte").Nacht;

        Assert.Equal(ZielUrteil.Unbekannt, phase.Urteil);
        Assert.Null(phase.AnteilProzent);
    }

    [Fact]
    public void Zielabgleich_TagUndNachtWerdenGetrennt()
    {
        var serie = Reihe(true, 100, _ => 60).Concat(Reihe(false, 100, _ => 60)).ToList();

        var f = KenntnisstandRechner.Zielabgleich(serie, Ziele).Single(z => z.Groesse == "Luftfeuchte");

        Assert.Equal(ZielUrteil.Erreichbar, f.Tag.Urteil);   // 60 ≤ 62
        Assert.Equal(ZielUrteil.Luecke, f.Nacht.Urteil);     // 60 > 51
        Assert.Equal(100, f.Tag.Minuten);
    }

    [Fact]
    public void Zielabgleich_VpdBandDerBreiteNull_HatToleranzUndHaeltNichtNullProzent()
    {
        // Brus Plan am 07.10.2026: VPD-Ziel Nacht 1,4 bis 1,4. Ohne Toleranz wäre jeder Wert „außerhalb".
        var serie = Reihe(false, 100, _ => 50, vpd: i => i < 80 ? 1.35 : 1.0);

        var vpd = KenntnisstandRechner.Zielabgleich(serie, Ziele).Single(z => z.Groesse == "VPD").Nacht;

        Assert.Equal(80, vpd.AnteilProzent);
        Assert.Equal("≈ 1,4 kPa", vpd.ZielText);
    }

    [Fact]
    public void Zielabgleich_OhneZiel_BleibtUnbekanntOhneText()
    {
        var ohne = new Zielbaender(new ZielBand(null, null, null, null), new ZielBand(null, null, null, null));

        var z = KenntnisstandRechner.Zielabgleich(Reihe(true, 100, _ => 50), ohne);

        Assert.All(z, zeile => Assert.Null(zeile.Tag.ZielText));
        Assert.All(z, zeile => Assert.Equal(ZielUrteil.Unbekannt, zeile.Tag.Urteil));
    }

    [Fact]
    public void Zielabgleich_MinutenOhneWertOderLichtphaseZaehlenNicht()
    {
        var serie = Reihe(false, 100, _ => 50);
        serie.AddRange(Enumerable.Range(100, 50).Select(i => new ZeltMinute(T0.AddMinutes(i), null, null, null, false)));   // Fühler ohne Wert
        serie.AddRange(Enumerable.Range(150, 50).Select(i => new ZeltMinute(T0.AddMinutes(i), 99, 99, 9, null)));          // Lichtphase unbekannt

        var f = KenntnisstandRechner.Zielabgleich(serie, Ziele).Single(z => z.Groesse == "Luftfeuchte");

        Assert.Equal(100, f.Nacht.Minuten);
        Assert.Equal(100, f.Nacht.AnteilProzent);
    }

    // ------------------------------------------------------------------ Wirkung

    private static ProbelaufLauf Lauf(string modul, bool? tag, ProbelaufStatus status, double feuchteProMinute, double minuten = 10, double? erholung = 8)
        => new()
        {
            Modul = modul, Status = status, TagPhaseBeiStart = tag,
            StartUtc = T0, EingriffEndeUtc = T0.AddMinutes(minuten),
            Auswertung = new ProbelaufAuswertung(
                [new ProbelaufKennzahl("Feuchte", 50, 55, 55, feuchteProMinute, erholung)], [], tag, tag),
        };

    [Fact]
    public void Wirkung_MitteltUeberDieLaeufeJeGeraetUndLichtphase()
    {
        var laeufe = new[]
        {
            Lauf("entfeuchter-zusatz", false, ProbelaufStatus.Fertig, 0.55),
            Lauf("entfeuchter-zusatz", false, ProbelaufStatus.Abgebrochen, 1.05),
            Lauf("entfeuchter-zusatz", true, ProbelaufStatus.Fertig, 0.2),
        };

        var w = KenntnisstandRechner.Wirkung(laeufe, Titel);

        var nacht = w.Single(x => x.Modul == "entfeuchter-zusatz" && !x.Tag);
        Assert.Equal(2, nacht.Laeufe);
        Assert.Equal(0.8, nacht.Werte.Single().ProMinute, 3);
        Assert.Equal("Zusatz-Entfeuchter", nacht.Titel);
        Assert.Equal(1, w.Single(x => x.Tag).Laeufe);
    }

    [Fact]
    public void Wirkung_IgnoriertZuKurzeUndUnfertigeLaeufeOhneLichtphase()
    {
        var laeufe = new[]
        {
            Lauf("entfeuchter", false, ProbelaufStatus.Fertig, 9, minuten: 0.2),      // 14 Sekunden: nichts wert
            Lauf("entfeuchter", false, ProbelaufStatus.Laeuft, 9),                    // noch nicht fertig
            Lauf("entfeuchter", null, ProbelaufStatus.Fertig, 9),                     // Lichtphase unbekannt
            Lauf("entfeuchter", false, ProbelaufStatus.RueckstellungOffen, 9),
        };

        Assert.Empty(KenntnisstandRechner.Wirkung(laeufe, Titel));
    }

    // --------------------------------------------------------- Abdeckung, nächster Lauf

    private static readonly string[] Module = ["entfeuchter", "entfeuchter-zusatz", "co2"];

    [Fact]
    public void Abdeckung_ZaehltLaeufeJeLichtphase_Co2HatNachtsNichts()
    {
        var a = KenntnisstandRechner.Abdeckung([Lauf("entfeuchter-zusatz", false, ProbelaufStatus.Fertig, 0.5)], Module, Titel);

        Assert.Equal(1, a.Single(z => z.Modul == "entfeuchter-zusatz").LaeufeNacht);
        Assert.Equal(0, a.Single(z => z.Modul == "entfeuchter-zusatz").LaeufeTag);
        Assert.False(a.Single(z => z.Modul == "co2").NachtMoeglich);
    }

    [Fact]
    public void Naechster_SchliesstDieGroessteLueckeUndNenntDenGrund()
    {
        var ziele = KenntnisstandRechner.Zielabgleich(
            Reihe(false, 100, _ => 60).Concat(Reihe(true, 100, _ => 55)).ToList(), Ziele);   // Nacht: Lücke bei Feuchte, Tag: alles gut
        var abdeckung = KenntnisstandRechner.Abdeckung([Lauf("entfeuchter-zusatz", false, ProbelaufStatus.Fertig, 0.5)], Module, Titel);

        var n = KenntnisstandRechner.Naechster(abdeckung, ziele)!;

        // Nacht ist die Lücke; dort fehlt noch der Hauptentfeuchter (der Zusatz ist schon gemessen).
        Assert.Equal("entfeuchter", n.Modul);
        Assert.False(n.Tag);
        Assert.Contains("Lücke", n.Begruendung);
        Assert.Contains("Luftfeuchte", n.Begruendung);
        Assert.Equal(10, n.DauerMinuten);
    }

    [Fact]
    public void Naechster_OhneLuecke_NimmtDieErsteUngemesseneKombination()
    {
        var abdeckung = KenntnisstandRechner.Abdeckung([], Module, Titel);

        var n = KenntnisstandRechner.Naechster(abdeckung, [])!;

        Assert.Contains("noch keine Messung", n.Begruendung);
    }

    [Fact]
    public void Naechster_IstNull_WennAllesGemessenIst()
    {
        var laeufe = Module.SelectMany(m => new[] { true, false }.Where(t => !(m == "co2" && !t)).Select(t => Lauf(m, t, ProbelaufStatus.Fertig, 0.5))).ToList();

        Assert.Null(KenntnisstandRechner.Naechster(KenntnisstandRechner.Abdeckung(laeufe, Module, Titel), []));
    }

    // ----------------------------------------------------------------- Hinweise

    [Fact]
    public void Hinweise_NennenDasGeraetMitDerGroesstenGemessenenWirkung_AlsAnsatzpunkt_NichtAlsEinstellung()
    {
        var ziele = KenntnisstandRechner.Zielabgleich(Reihe(false, 100, i => i < 71 ? 50 : 55), Ziele);   // Nacht-Feuchte: 71 % → knapp
        var wirkung = KenntnisstandRechner.Wirkung(
            [Lauf("entfeuchter-zusatz", false, ProbelaufStatus.Fertig, 0.55), Lauf("entfeuchter", false, ProbelaufStatus.Fertig, 0.2)], Titel);

        var h = KenntnisstandRechner.Hinweise(ziele, wirkung);

        var satz = Assert.Single(h, s => s.StartsWith("Luftfeuchte bei Licht aus"));
        Assert.Contains("71 %", satz);
        Assert.Contains("Zusatz-Entfeuchter", satz);
        Assert.Contains("+0,55", satz);
    }

    [Fact]
    public void Hinweise_OhneLaeufeSagenSie_WasFehlt()
    {
        var ziele = KenntnisstandRechner.Zielabgleich(Reihe(false, 100, _ => 60), Ziele);

        var h = KenntnisstandRechner.Hinweise(ziele, []);

        Assert.Contains(h, s => s.Contains("fehlt noch ein Probelauf"));
    }

    [Fact]
    public void Hinweise_ErreichbareZieleBekommenKeinen()
        => Assert.Empty(KenntnisstandRechner.Hinweise(KenntnisstandRechner.Zielabgleich(Reihe(true, 100, _ => 50), Ziele), []));
}
