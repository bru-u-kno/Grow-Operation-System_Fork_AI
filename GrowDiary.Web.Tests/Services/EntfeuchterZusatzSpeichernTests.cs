using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-009): Speichern schreibt NUR, was geändert wurde — gegen Brus Stand vom 06.10.2026.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Am 06.10.2026 hat ein Speichern der Seite unbemerkt die Tag-Grenze
/// von 26,5 auf 29 °C zurückgesetzt: die Seite schickte alle Felder, und die gemeinsamen
/// <c>TempMax*</c>-Felder standen dabei auf dem Werkswert. Diese Fälle fahren den ganzen Weg
/// (Dienst, Datenbank, die Aufrufe an Home Assistant), nicht nur die Rechnung.</para>
/// <para>Jeder Fall stellt vorher fest, dass er etwas sieht (Mengenwächter): ein Vergleich
/// „nichts hat sich geändert" ist auch bei einem Test grün, der nie etwas geschrieben hat.</para>
/// </remarks>
public sealed class EntfeuchterZusatzSpeichernTests : IDisposable
{
    private readonly ZusatzPruefstand _stand = new();

    public void Dispose() => _stand.Dispose();

    private const string FolgeAbstand = EntfeuchterZusatzSteuerungService.Entitaeten.FolgeAbstand;

    [Fact]
    public async Task ErsterAufruf_UebernimmtDieHelferUndSchreibtNichtsNachHomeAssistant()
    {
        var dienst = _stand.Zusatz();
        Assert.Null(dienst.Gespeichert);

        var e = await dienst.EinstellungenAsync(CancellationToken.None);

        // Die Werte kommen aus Brus Helfern, nicht aus den Werkseinstellungen.
        Assert.Equal(15, e.MindestlaufzeitMin);
        Assert.Equal(10, e.MindestpauseMin);
        Assert.Equal(1.0, e.FolgeAbstandK);
        Assert.Equal(26.5, e.TempMaxTagFestC);
        Assert.Equal(25.0, e.TempMaxNachtFestC);
        Assert.True(e.AutomatikAktiv);
        Assert.Equal(EntfeuchterZusatzHilfe.Normal, EntfeuchterZusatzHilfe.Erkennen(e));
        Assert.NotNull(dienst.Gespeichert);
        Assert.Empty(_stand.Schreibaufrufe());
    }

    [Fact]
    public async Task NurEinFeld_EinFeldAendertSich_EinHelferWirdGeschrieben_SonstNichts()
    {
        var dienst = _stand.Zusatz();
        var vorher = await dienst.EinstellungenAsync(CancellationToken.None);
        var entfeuchterVorher = _stand.Repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!;
        _stand.Vergessen();

        var (nachher, fehler, erreicht) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { FolgeAbstandK = 2 }, CancellationToken.None);

        Assert.Empty(fehler);
        Assert.True(erreicht);
        // Der Stand: genau EIN Pfad ist anders.
        // Die Hilfsstärke ist abgeleitet: 2 K passen zu keiner Voreinstellung, also „eigene". Sonst ändert sich nichts.
        Assert.Equal(["folgeAbstandK", "hilfe"], ZusatzPruefstand.Unterschiede(vorher, nachher!));
        Assert.Equal(EntfeuchterZusatzHilfe.Normal, vorher.Hilfe);
        Assert.Equal(EntfeuchterZusatzHilfe.Eigene, nachher!.Hilfe);
        // Und er steht auch so in der Datenbank.
        Assert.Equal(2, dienst.Gespeichert!.FolgeAbstandK);
        // Home Assistant: genau ein Aufruf, der Folge-Abstand.
        var aufrufe = _stand.Schreibaufrufe();
        var einzig = Assert.Single(aufrufe);
        Assert.Equal("input_number/set_value", einzig.Dienst);
        Assert.Equal(FolgeAbstand, einzig.Entitaet);
        Assert.Equal(2, einzig.Daten["value"]!.GetValue<double>());
        // Der Entfeuchter wurde nicht angefasst.
        Assert.Empty(ZusatzPruefstand.Unterschiede(entfeuchterVorher,
            _stand.Repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!));
    }

    [Fact]
    public async Task ZweimalSpeichern_DasZweiteMalSchreibtNichtsUndAendertNichts()
    {
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);

        var aenderung = new EntfeuchterZusatzAenderung { FolgeAbstandK = 2, TagbetriebErlauben = false };
        var (erstes, _, _) = await dienst.SpeichernAsync(aenderung, CancellationToken.None);
        Assert.True(_stand.Schreibaufrufe().Count >= 2, "Das erste Speichern hat nichts geschrieben — der Test sieht es nicht.");
        _stand.Vergessen();

        var (zweites, fehler, erreicht) = await dienst.SpeichernAsync(aenderung, CancellationToken.None);

        Assert.Empty(fehler);
        Assert.True(erreicht);
        Assert.Empty(ZusatzPruefstand.Unterschiede(erstes!, zweites!));
        Assert.Empty(_stand.Schreibaufrufe());
    }

    [Fact]
    public async Task TempMaxLeitetWeiter_AndereEntfeuchterFelderBleibenUnberuehrt()
    {
        var dienst = _stand.Zusatz();
        var vorher = await dienst.EinstellungenAsync(CancellationToken.None);
        var entfeuchterVorher = _stand.Repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!;
        // Mengenwächter: der Entfeuchter-Stand ist Brus (Tag 26,5, nicht der Werkswert 29) und der
        // Rest weicht vom Werkswert ab — sonst fiele ein zurückgesetztes Feld nicht auf.
        Assert.Equal(26.5, entfeuchterVorher.TempMaxTagFestC);
        var werk = new EntfeuchterEinstellungen();
        Assert.Equal(["tempMaxTagFestC"], ZusatzPruefstand.Unterschiede(werk, entfeuchterVorher)
            .Where(p => p.StartsWith("tempMax", StringComparison.Ordinal)).ToList());
        Assert.True(ZusatzPruefstand.Unterschiede(werk, entfeuchterVorher).Count >= 10,
            "Der Entfeuchter-Stand des Tests gleicht der Werkseinstellung — er würde ein Zurücksetzen nicht bemerken.");
        _stand.Vergessen();

        var (nachher, fehler, erreicht) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { TempMaxTagFestC = 27 }, CancellationToken.None);

        Assert.Empty(fehler);
        Assert.True(erreicht);

        // Der Entfeuchter-Stand: GENAU die Tag-Grenze ist anders.
        var entfeuchterNachher = _stand.Repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!;
        Assert.Equal(["tempMaxTagFestC"], ZusatzPruefstand.Unterschiede(entfeuchterVorher, entfeuchterNachher));
        Assert.Equal(27, entfeuchterNachher.TempMaxTagFestC);
        Assert.Equal(25, entfeuchterNachher.TempMaxNachtFestC);

        // Der Zusatz-Stand: dieselbe Grenze, sonst nichts.
        Assert.Equal(["tempMaxTagFestC"], ZusatzPruefstand.Unterschiede(vorher, nachher!));

        // Home Assistant bekommt die Tag-Grenze — und die anderen Entfeuchter-Helfer mit
        // ihren BISHERIGEN Werten (nicht mit Werkseinstellungen).
        var aufrufe = _stand.Schreibaufrufe();
        double Wert(string entitaet) => aufrufe.Last(a => a.Entitaet == entitaet).Daten["value"]!.GetValue<double>();
        Assert.Equal(27, Wert(EntfeuchterSteuerungService.Entitaeten.TempMaxTag));
        Assert.Equal(25, Wert(EntfeuchterSteuerungService.Entitaeten.TempMaxNacht));
        Assert.Equal(6, Wert(EntfeuchterSteuerungService.Entitaeten.Hysterese));
        Assert.Equal(58, Wert(EntfeuchterSteuerungService.Entitaeten.FeuchteEinTag));
        Assert.Equal(61, Wert(EntfeuchterSteuerungService.Entitaeten.FeuchteEinNacht));
        // Die eigenen Helfer des Zusatzes gehen NICHT mit.
        Assert.DoesNotContain(aufrufe, a => a.Entitaet.StartsWith("input_number.trotec_zelt_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Voreinstellung_SchreibtNurDieHelfer_DieSichWirklichAendern()
    {
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);
        _stand.Vergessen();

        var (nachher, fehler, _) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { Hilfe = EntfeuchterZusatzHilfe.Sparsam }, CancellationToken.None);

        Assert.Empty(fehler);
        Assert.Equal(EntfeuchterZusatzHilfe.Sparsam, EntfeuchterZusatzHilfe.Erkennen(nachher!));
        // Von „normal" auf „sparsam": Folge-Abstand, VPD-Abstand, Zuschaltung, Pause ändern sich —
        // der Wieder-ein-Abstand (1 K) ist in beiden gleich und wird nicht geschrieben.
        var geschrieben = _stand.Schreibaufrufe().Select(a => a.Entitaet).Order().ToList();
        Assert.Equal(new[]
        {
            EntfeuchterZusatzSteuerungService.Entitaeten.FolgeAbstand,
            EntfeuchterZusatzSteuerungService.Entitaeten.Mindestpause,
            EntfeuchterZusatzSteuerungService.Entitaeten.VpdHysterese,
            EntfeuchterZusatzSteuerungService.Entitaeten.ZuschaltVerzoegerung,
        }.Order(), geschrieben);
    }

    [Fact]
    public async Task HilfeAus_HaeltAutomationUndShellyAus_UndNormalSchaltetDieAutomationWiederEin()
    {
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);
        _stand.Vergessen();

        var (aus, fehlerAus, _) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { Hilfe = EntfeuchterZusatzHilfe.Aus }, CancellationToken.None);
        Assert.Empty(fehlerAus);
        Assert.Equal(EntfeuchterZusatzHilfe.Aus, EntfeuchterZusatzHilfe.Erkennen(aus!));
        var aufrufe = _stand.Schreibaufrufe();
        Assert.Contains(aufrufe, a => a.Dienst == "automation/turn_off" && a.Entitaet == "automation.rdwc_trotec_zelt_shelly_plan_regelung");
        Assert.Contains(aufrufe, a => a.Dienst == "switch/turn_off" && a.Entitaet == "switch.grow_dehumi_tent");
        // Die Einzelwerte bleiben, wie sie waren.
        Assert.Empty(aufrufe.Where(a => a.Dienst.StartsWith("input_number", StringComparison.Ordinal)));

        // Ein Einzelwert ändert „aus" nicht.
        var (immerNochAus, _, _) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { FolgeAbstandK = 2 }, CancellationToken.None);
        Assert.Equal(EntfeuchterZusatzHilfe.Aus, EntfeuchterZusatzHilfe.Erkennen(immerNochAus!));

        _stand.Vergessen();
        var (wieder, _, _) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { Hilfe = EntfeuchterZusatzHilfe.Normal }, CancellationToken.None);
        Assert.Equal(EntfeuchterZusatzHilfe.Normal, EntfeuchterZusatzHilfe.Erkennen(wieder!));
        Assert.Contains(_stand.Schreibaufrufe(), a => a.Dienst == "automation/turn_on" && a.Entitaet == "automation.rdwc_trotec_zelt_shelly_plan_regelung");
    }

    [Fact]
    public async Task AutomatikAusUndWieder_SchaltetDieHandgebauteUndDieVomForkAngelegte()
    {
        // Eine vom Fork angelegte Regelung heißt anders — sie wird über ihre Konfigurations-Kennung gefunden.
        _stand.Setze("automation.zusatz_entfeuchter_regelung", "on", kennung: "fork_ai_entfeuchter-zusatz_regelung");
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);
        _stand.Vergessen();

        await dienst.SpeichernAsync(new EntfeuchterZusatzAenderung { AutomatikAktiv = false }, CancellationToken.None);

        var geschaltet = _stand.Schreibaufrufe().Where(a => a.Dienst == "automation/turn_off").Select(a => a.Entitaet).Order().ToList();
        Assert.Equal(["automation.rdwc_trotec_zelt_shelly_plan_regelung", "automation.zusatz_entfeuchter_regelung"], geschaltet);
    }

    [Fact]
    public async Task UngueltigeEingabe_SchreibtNichtsUndSpeichertNichts()
    {
        var dienst = _stand.Zusatz();
        var vorher = await dienst.EinstellungenAsync(CancellationToken.None);
        var entfeuchterVorher = _stand.Repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!;
        _stand.Vergessen();

        // Ein gültiges Feld zusammen mit einem ungültigen: auch das gültige wird nicht geschrieben.
        var (g1, f1, _) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { MindestlaufzeitMin = 20, FolgeAbstandK = 9 }, CancellationToken.None);
        Assert.Null(g1);
        Assert.Contains("FolgeAbstandK", f1.Keys);

        // Die gemeinsame Höchsttemperatur ungültig, mit einem gültigen eigenen Feld dabei.
        var (g2, f2, _) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { MindestlaufzeitMin = 20, TempMaxTagFestC = 50 }, CancellationToken.None);
        Assert.Null(g2);
        Assert.Contains("TempMaxTagFestC", f2.Keys);

        Assert.Empty(_stand.Schreibaufrufe());
        Assert.Empty(ZusatzPruefstand.Unterschiede(vorher, await dienst.EinstellungenAsync(CancellationToken.None)));
        Assert.Empty(ZusatzPruefstand.Unterschiede(entfeuchterVorher,
            _stand.Repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!));
    }

    [Fact]
    public async Task Meldung_EinzelnerWertAendertNurDiesenHelfer()
    {
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);
        _stand.Vergessen();

        var (nachher, _, _) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { Meldung = new EntfeuchterZusatzMeldungAenderung { GrenzeW = 80 } }, CancellationToken.None);

        Assert.Equal(80, nachher!.Meldung.GrenzeW);
        Assert.True(nachher.Meldung.Aktiv);
        Assert.Equal(5, nachher.Meldung.DauerMin);
        var einzig = Assert.Single(_stand.Schreibaufrufe());
        Assert.Equal(EntfeuchterZusatzSteuerungService.Entitaeten.MeldeGrenze, einzig.Entitaet);
    }

    // ------------------------------------------------------------------ Livebild

    [Fact]
    public async Task Livebild_RechnetMitBrusWerten()
    {
        var live = await _stand.Zusatz().LiveAsync(CancellationToken.None);

        Assert.True(live.HaErreichbar);
        Assert.Equal(26.5, live.TempMaxTagC);
        Assert.Equal(25.0, live.TempMaxNachtC);
        Assert.Equal(25.5, live.FolgeAusTagC);
        Assert.Equal(24.0, live.FolgeAusNachtC);
        Assert.Equal(24.5, live.WiederEinTagC);
        Assert.Equal(23.0, live.WiederEinNachtC);
        Assert.Equal(EntfeuchterZusatzSchaltgroesse.Vpd, live.Schaltgroesse);
        Assert.Equal(1.25, live.VpdEinSchwelle);
        Assert.Equal(1.55, live.VpdAusSchwelle);
        Assert.Equal(39.0, live.FeuchteEinProzent);
        Assert.Equal(25.1, live.TempC);
        Assert.True(live.ZusatzAn);
        Assert.True(live.ZusatzOnline);
        Assert.True(live.FuehrungAn);
        Assert.Equal(313.0, live.LeistungW);
        Assert.False(live.ZiehtNichts);
        Assert.True(live.AutomatikAn);
        Assert.Equal("Dehumi RDWC Tent", live.ZusatzName);
        Assert.Equal("RDWC Dehumi", live.FuehrungName);
        Assert.False(live.PlanUnvollstaendig);
    }

    [Fact]
    public async Task Livebild_ZiehtNichts_WennDerShellyAnIstUndDieLeistungUnterDerGrenzeLiegt()
    {
        _stand.Setze("sensor.grow_dehumi_tent_leistung", "3.0", "Dehumi RDWC Tent Leistung", "W");

        var live = await _stand.Zusatz().LiveAsync(CancellationToken.None);

        Assert.True(live.ZiehtNichts);
        Assert.Equal(3.0, live.LeistungW);
    }

    [Fact]
    public async Task Livebild_FehlendeHaWerte_SindNull_NieNull()
    {
        // Home Assistant antwortet, kennt aber nichts von dem, was die Seite braucht.
        using var leer = new ZusatzPruefstand(bruStand: false);
        leer.Setze("sensor.irgendwas", "1");

        var live = await leer.Zusatz().LiveAsync(CancellationToken.None);

        Assert.True(live.HaErreichbar);
        Assert.Null(live.TempC);
        Assert.Null(live.FeuchteProzent);
        Assert.Null(live.Vpd);
        Assert.Null(live.TagPhase);
        Assert.Null(live.ZusatzAn);
        Assert.Null(live.ZusatzOnline);
        Assert.Null(live.LeistungW);
        Assert.Null(live.EnergieHeuteKwh);
        Assert.Null(live.FuehrungAn);
        Assert.Null(live.ZiehtNichts);
        Assert.Null(live.AutomatikAn);
        Assert.Null(live.VpdZiel);
        Assert.Null(live.VpdEinSchwelle);
        Assert.Null(live.FeuchteEinProzent);
        Assert.Equal(EntfeuchterZusatzSchaltgroesse.Keine, live.Schaltgroesse);
        Assert.True(live.PlanUnvollstaendig);
        // Die Namen fallen auf die allgemeinen zurück — nicht auf etwas Erfundenes.
        Assert.Equal("Zusatz-Entfeuchter", live.ZusatzName);
        Assert.Equal("Entfeuchter", live.FuehrungName);
    }

    [Fact]
    public async Task Livebild_EnergieHeute_KommtAusDemVerlaufDesZaehlers()
    {
        _stand.Verlauf = """[[{"state":"7.00","last_changed":"2026-10-06T00:00:00+00:00"},{"state":"7.60","last_changed":"2026-10-06T09:00:00+00:00"},{"state":"8.15","last_changed":"2026-10-06T19:00:00+00:00"}]]""";

        var mit = await _stand.Zusatz().LiveAsync(CancellationToken.None);
        var ohne = await _stand.Zusatz().LiveAsync(CancellationToken.None, mitEnergie: false);

        Assert.Equal(1.15, mit.EnergieHeuteKwh);
        Assert.Null(ohne.EnergieHeuteKwh);
    }

    // ------------------------------------------------------------------- Namen

    [Fact]
    public async Task Namen_VorgabeIstDerGeraetenameAusHomeAssistant_UndGespeichertesGewinnt()
    {
        var dienst = _stand.Zusatz();

        var vorgabe = await dienst.NamenAsync(CancellationToken.None);
        Assert.Equal("RDWC Dehumi", vorgabe.Fuehrung.Anzeigename);
        Assert.Equal("RDWC Dehumi", vorgabe.Fuehrung.Vorgabe);
        Assert.Equal("Dehumi RDWC Tent", vorgabe.Zusatz.Anzeigename);

        Assert.Empty(dienst.NamenSpeichern(new EntfeuchterNamenAenderung { Zusatz = "Zelt-Entfeuchter" }));
        var danach = await dienst.NamenAsync(CancellationToken.None);
        Assert.Equal("Zelt-Entfeuchter", danach.Zusatz.Anzeigename);
        Assert.Equal("Dehumi RDWC Tent", danach.Zusatz.Vorgabe);
        // Der andere Name wurde nicht genannt und bleibt.
        Assert.Equal("RDWC Dehumi", danach.Fuehrung.Anzeigename);

        // Der Anzeigename kommt im Livebild an — kein „Port 7" fest im Text.
        Assert.Equal("Zelt-Entfeuchter", (await dienst.LiveAsync(CancellationToken.None)).ZusatzName);
    }

    [Fact]
    public async Task Namen_LeerOderNull_SetztAufDieVorgabe_FehlendLaesstDenNamen()
    {
        var dienst = _stand.Zusatz();
        dienst.NamenSpeichern(new EntfeuchterNamenAenderung { Fuehrung = "Haupt", Zusatz = "Zusatz" });

        // fehlt: bleibt
        dienst.NamenSpeichern(new EntfeuchterNamenAenderung());
        var a = await dienst.NamenAsync(CancellationToken.None);
        Assert.Equal("Haupt", a.Fuehrung.Anzeigename);
        Assert.Equal("Zusatz", a.Zusatz.Anzeigename);

        // genannt als null / leer: Vorgabe
        dienst.NamenSpeichern(new EntfeuchterNamenAenderung { Fuehrung = null, Zusatz = "  " });
        var b = await dienst.NamenAsync(CancellationToken.None);
        Assert.Equal("RDWC Dehumi", b.Fuehrung.Anzeigename);
        Assert.Equal("Dehumi RDWC Tent", b.Zusatz.Anzeigename);
    }
}
