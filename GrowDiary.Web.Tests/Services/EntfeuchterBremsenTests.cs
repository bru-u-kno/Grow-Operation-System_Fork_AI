using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-022): „Was bremst gerade?" — jede Meldung nur, wenn ihre Bedingung belegt ist, mit den Zahlen dahinter.
/// Ausgangslage ist der Befund vom 14.10.2026: Plan-Wert 53 %, gemessen 57,9 %, Entfeuchter läuft seit Stunden.
/// </summary>
public sealed class EntfeuchterBremsenTests
{
    private static readonly DateTime Jetzt = new(2026, 10, 14, 4, 0, 0, DateTimeKind.Utc);

    private static BremsEingabe Eingabe(
        double? feuchte = 57.9, double? grenze = 53, double? ein = 48, double? temp = 22, double? tempMax = 23, bool? tag = true,
        bool tagbetrieb = true, bool? automatik = true, bool? portAn = true, bool? vorrang = false, bool? drossel = false,
        int einschalt = 1, int aussenluft = 5, int? ueberSeit = null, int? laeuftSeit = null,
        bool? zusatzAn = null, double? wiederEin = null, bool? zusatzAutomatik = null, bool nachVpd = false)
        => new(feuchte, grenze, ein, temp, tempMax, tag, tagbetrieb, automatik, portAn, vorrang, drossel, einschalt, aussenluft, ueberSeit, laeuftSeit,
            zusatzAn, wiederEin, zusatzAutomatik, nachVpd);

    [Fact]
    public void NichtsBremst_dieListeIstLeer()
    {
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 47.2)));
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: null, grenze: null, temp: null, tempMax: null, tag: null, automatik: null, portAn: null, vorrang: null, drossel: null)));
    }

    [Fact]
    public void Kapazitaet_BrauchtBeideDauernUndDieFeuchteDarueber()
    {
        var b = Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(ueberSeit: 90, laeuftSeit: 130)));
        Assert.Equal("kapazitaet", b.Art);
        Assert.Equal("kritisch", b.Stufe);
        Assert.Contains("2 h 10 Min.", b.Text);
        Assert.Contains("1 h 30 Min.", b.Text);
        Assert.Contains("57,9 statt 53", b.Text);

        // Zu kurz, nicht belegt oder Feuchte im Ziel: keine Aussage.
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(ueberSeit: 59, laeuftSeit: 130)));
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(ueberSeit: 90, laeuftSeit: 59)));
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(ueberSeit: null, laeuftSeit: 130)));
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 52, ueberSeit: 90, laeuftSeit: 130)));
    }

    [Fact]
    public void Uebertemperatur_NenntBeideTemperaturen()
    {
        var b = Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 50, temp: 23.4, tempMax: 23)));
        Assert.Equal("uebertemperatur", b.Art);
        Assert.Equal("info", b.Stufe);
        Assert.Contains("23,4 °C über 23 °C", b.Titel);

        // Über der Plan-Grenze zugleich: eine Warnung, kein bloßer Hinweis.
        Assert.Equal("warnung", Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(temp: 23.4, tempMax: 23))).Stufe);
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 50, temp: 23, tempMax: 23)));
    }

    [Fact]
    public void Aussenluft_NurWennSieWirklichLaenger_warten_laesst()
    {
        var b = Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 50, ein: 48, portAn: false, vorrang: true)));
        Assert.Equal("aussenluft", b.Art);
        Assert.Contains("5 statt 1 Minuten", b.Text);

        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 50, ein: 48, portAn: true, vorrang: true)));   // läuft schon
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 47, ein: 48, portAn: false, vorrang: true)));  // noch unter EIN
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 50, ein: 48, portAn: false, vorrang: true, einschalt: 5, aussenluft: 5))); // gleiche Wartezeit
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 50, ein: 48, portAn: false, vorrang: false)));
    }

    [Fact]
    public void TagbetriebAus_nurTagsUeberDerGrenze()
    {
        Assert.Equal("tagbetrieb", Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(tagbetrieb: false))).Art);
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(tagbetrieb: false, tag: false)));
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(tagbetrieb: false, feuchte: 50)));
    }

    [Fact]
    public void AutomatikAus_undDrosselung()
    {
        Assert.Equal("automatik", Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 50, automatik: false))).Art);
        Assert.Equal("drosselung", Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(drossel: true))).Art);
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 50, drossel: true)));
    }

    [Fact]
    public void DrosselungWirkt_nurBeiLichtUndTatsaechlichGedrosselterStufe()
    {
        Assert.True(EntfeuchterBremsen.DrosselungWirkt(true, true, 6, 7));
        Assert.False(EntfeuchterBremsen.DrosselungWirkt(true, false, 6, 7)); // nachts
        Assert.False(EntfeuchterBremsen.DrosselungWirkt(true, true, 7, 7)); // Port steht auf normal
        Assert.False(EntfeuchterBremsen.DrosselungWirkt(false, true, 6, 7));
        Assert.True(EntfeuchterBremsen.DrosselungWirkt(true, true, null, 7)); // Stufe unlesbar: Schalter + Licht entscheiden
        Assert.True(EntfeuchterBremsen.DrosselungWirkt(true, null, 5, 7));
        Assert.Null(EntfeuchterBremsen.DrosselungWirkt(null, true, 6, 7));
    }

    [Fact]
    public void DieKapazitaetStehtVorDenUebrigenGruenden()
    {
        var arten = EntfeuchterBremsen.Pruefen(Eingabe(ueberSeit: 90, laeuftSeit: 130, drossel: true, temp: 23.4)).Select(b => b.Art).ToList();
        Assert.Equal("kapazitaet", arten[0]);
        Assert.Contains("uebertemperatur", arten);
        Assert.Contains("drosselung", arten);
    }

    // ---- Dauer aus dem Verlauf ---------------------------------------------------------------------------------------

    private static HaVerlaufsPunkt P(int minutenVorJetzt, string zustand) => new(Jetzt.AddMinutes(-minutenVorJetzt), zustand);

    [Fact]
    public void UeberGrenzeSeit_ZaehltRueckwaertsBisZumLetztenPunktDarunter()
    {
        var verlauf = new[] { P(180, "51"), P(150, "52.5"), P(120, "54"), P(90, "55"), P(30, "57.9") };
        Assert.Equal(120, EntfeuchterBremsen.UeberGrenzeSeitMin(verlauf, 53, Jetzt));
    }

    [Fact]
    public void UeberGrenzeSeit_NullWennJetztDarunterOderLeer()
    {
        Assert.Null(EntfeuchterBremsen.UeberGrenzeSeitMin(new[] { P(60, "57"), P(5, "52") }, 53, Jetzt));
        Assert.Null(EntfeuchterBremsen.UeberGrenzeSeitMin(Array.Empty<HaVerlaufsPunkt>(), 53, Jetzt));
        Assert.Null(EntfeuchterBremsen.UeberGrenzeSeitMin(null, 53, Jetzt));
    }

    [Fact]
    public void UnbekannteZustaendeBeendenDieReiheNicht()
    {
        var verlauf = new[] { P(100, "56"), P(70, "unavailable"), P(40, "unknown"), P(10, "58") };
        Assert.Equal(100, EntfeuchterBremsen.UeberGrenzeSeitMin(verlauf, 53, Jetzt));
    }

    [Fact]
    public void LaeuftSeit_LiestDenSchalterRueckwaerts()
    {
        Assert.Equal(75, EntfeuchterBremsen.LaeuftSeitMin(new[] { P(200, "Off"), P(75, "On"), P(20, "On") }, Jetzt));
        Assert.Null(EntfeuchterBremsen.LaeuftSeitMin(new[] { P(200, "On"), P(20, "Off") }, Jetzt));
        Assert.Equal(180, EntfeuchterBremsen.LaeuftSeitMin(new[] { P(180, "on") }, Jetzt));
    }

    // --- Zusatz-Sperre (Wieder-ein-Temperatur), Nachprüfung 15.10.2026: Zelt 22,1 °C, Wieder-ein 22,0 °C, Zusatz aus, Feuchte 56 %.

    [Fact]
    public void ZusatzSperre_ZusatzAusZeltUeberWiederEin_nenntTemperaturUndSchwelle()
    {
        var b = Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 56.2, temp: 22.4, tempMax: 24, zusatzAn: false, wiederEin: 22, zusatzAutomatik: true)));
        Assert.Equal("zusatzsperre", b.Art);
        Assert.Equal("warnung", b.Stufe);
        Assert.Equal("Der Zusatz-Entfeuchter wartet auf Abkühlung", b.Titel);
        Assert.Contains("22,4 °C", b.Text);
        Assert.Contains("unter 22 °C wieder ein", b.Text);
        Assert.Contains("Solange läuft nur der Hauptentfeuchter", b.Text);
        Assert.Contains("56,2 %", b.Text);
        Assert.Contains("(53 %)", b.Text);
    }

    [Fact]
    public void ZusatzSperre_Grenzfall_genauAufWiederEinSperrt_darunterNicht()
    {
        Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(temp: 22, tempMax: 24, zusatzAn: false, wiederEin: 22)));
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(temp: 21.9, tempMax: 24, zusatzAn: false, wiederEin: 22)));
    }

    [Fact]
    public void ZusatzSperre_LaeuftDerZusatzOderIstEsUnbekannt_schweigtSie()
    {
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(temp: 22.4, tempMax: 24, zusatzAn: true, wiederEin: 22)));
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(temp: 22.4, tempMax: 24, zusatzAn: null, wiederEin: 22)));
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(temp: 22.4, tempMax: 24, zusatzAn: false, wiederEin: null)));
    }

    [Fact]
    public void ZusatzSperre_ZusatzAutomatikAus_schweigtSie_weilDannNichtDieTemperaturDasWartenErklaert()
    {
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(temp: 22.4, tempMax: 24, zusatzAn: false, wiederEin: 22, zusatzAutomatik: false)));
    }

    [Fact]
    public void ZusatzSperre_FeuchteImPlan_schweigtSie()
    {
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(feuchte: 50, temp: 22.4, tempMax: 24, zusatzAn: false, wiederEin: 22)));
    }

    [Fact]
    public void ZusatzSperre_UeberDerHoechsttemperatur_sagtNurZuWarm()
    {
        var b = Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(temp: 24.5, tempMax: 24, zusatzAn: false, wiederEin: 22)));
        Assert.Equal("uebertemperatur", b.Art);
    }

    [Fact]
    public void ZusatzSperre_HauptgeraetAus_lassenDenSatzUeberDasHauptgeraetWeg()
    {
        var b = Assert.Single(EntfeuchterBremsen.Pruefen(Eingabe(temp: 22.4, tempMax: 24, portAn: false, zusatzAn: false, wiederEin: 22)));
        Assert.DoesNotContain("Hauptentfeuchter", b.Text);
        Assert.StartsWith("Das Zelt hat", b.Text);
    }

    [Fact]
    public void ZusatzSperre_TagsNachVpdGeregelt_schweigtSie_weilDannNichtDieFeuchteDenZusatzSteuert()
    {
        Assert.Empty(EntfeuchterBremsen.Pruefen(Eingabe(temp: 22.4, tempMax: 24, zusatzAn: false, wiederEin: 22, nachVpd: true)));
    }

    [Fact]
    public void ZusatzSperre_TagbetriebNichtErlaubt_schweigtSie_unddieBremseTagbetriebSprichtStattdessen()
    {
        var liste = EntfeuchterBremsen.Pruefen(Eingabe(temp: 22.4, tempMax: 24, tag: true, tagbetrieb: false, zusatzAn: false, wiederEin: 22));
        Assert.DoesNotContain(liste, b => b.Art == "zusatzsperre");
        Assert.Contains(liste, b => b.Art == "tagbetrieb");
        // Nachts gilt der Tagbetrieb nicht: dort sperrt die Temperatur wie sonst.
        Assert.Contains(EntfeuchterBremsen.Pruefen(Eingabe(temp: 22.4, tempMax: 24, tag: false, tagbetrieb: false, zusatzAn: false, wiederEin: 22)), b => b.Art == "zusatzsperre");
    }
}
