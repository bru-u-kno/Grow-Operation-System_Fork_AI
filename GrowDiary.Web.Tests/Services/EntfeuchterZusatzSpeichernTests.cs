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
    public async Task ZweimalSpeichern_DerStandBleibtGleich_DieGenanntenFelderGehenJedesMalNachHomeAssistant()
    {
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);

        var aenderung = new EntfeuchterZusatzAenderung { FolgeAbstandK = 2, TagbetriebErlauben = false };
        var (erstes, _, _) = await dienst.SpeichernAsync(aenderung, CancellationToken.None);
        var ersteAufrufe = _stand.Schreibaufrufe().Select(a => a.Entitaet).Order().ToList();
        Assert.Equal(2, ersteAufrufe.Count);
        _stand.Vergessen();

        var (zweites, fehler, erreicht) = await dienst.SpeichernAsync(aenderung, CancellationToken.None);

        Assert.Empty(fehler);
        Assert.True(erreicht);
        Assert.Empty(ZusatzPruefstand.Unterschiede(erstes!, zweites!));
        // Der Fork-Stand trägt die Werte schon — Home Assistant kann aber abweichen (jemand hat dort gedreht):
        // was im Body steht, wird deshalb jedes Mal geschrieben, und nur das.
        Assert.Equal(ersteAufrufe, _stand.Schreibaufrufe().Select(a => a.Entitaet).Order().ToList());
    }

    [Fact]
    public async Task EinFeldGleichZumForkStand_WirdTrotzdemGeschrieben_WeilHomeAssistantAbweichenKann()
    {
        var dienst = _stand.Zusatz();
        var vorher = await dienst.EinstellungenAsync(CancellationToken.None);
        _stand.Vergessen();

        // Bru hat den Helfer in Home Assistant von Hand auf 3 gestellt; der Fork-Stand sagt weiter 15.
        var (nachher, _, erreicht) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { MindestlaufzeitMin = vorher.MindestlaufzeitMin }, CancellationToken.None);

        Assert.Empty(ZusatzPruefstand.Unterschiede(vorher, nachher!));
        var einzig = Assert.Single(_stand.Schreibaufrufe());
        Assert.Equal(EntfeuchterZusatzSteuerungService.Entitaeten.Mindestlaufzeit, einzig.Entitaet);
        Assert.Equal(15, einzig.Daten["value"]!.GetValue<double>());
        Assert.True(erreicht);
    }

    [Fact]
    public async Task HaAngenommen_KommtAusDenEchtenSchreibergebnissen()
    {
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);

        // Nichts nach Home Assistant zu schreiben (ein reines Fork-Feld): weder „ja" noch „nein".
        var nichts = await dienst.SpeichernAsync(new EntfeuchterZusatzAenderung { Ablauf = EntfeuchterZusatzAblauf.Schlauch }, CancellationToken.None);
        Assert.Empty(_stand.Schreibaufrufe());
        Assert.Null(nichts.HaErreicht);

        // Geschrieben und angenommen.
        var gut = await dienst.SpeichernAsync(new EntfeuchterZusatzAenderung { MindestpauseMin = 12 }, CancellationToken.None);
        Assert.Single(_stand.Schreibaufrufe());
        Assert.True(gut.HaErreicht);

        // Home Assistant lehnt ab: der Fork-Stand ist gespeichert, aber „angenommen" ist false.
        _stand.DienstAntwort = System.Net.HttpStatusCode.InternalServerError;
        var schlecht = await dienst.SpeichernAsync(new EntfeuchterZusatzAenderung { MindestpauseMin = 13 }, CancellationToken.None);
        Assert.False(schlecht.HaErreicht);
        Assert.Equal(13, dienst.Gespeichert!.MindestpauseMin);

        // Ein einziger abgelehnter Aufruf unter mehreren genügt.
        var gemischt = await dienst.SpeichernAsync(new EntfeuchterZusatzAenderung { Hilfe = "kraeftig" }, CancellationToken.None);
        Assert.False(gemischt.HaErreicht);
    }

    [Fact]
    public async Task TempMax_SchreibtNurDenGenanntenHelfer_KeinAndererEntfeuchterHelferKeinSchalter()
    {
        var dienst = _stand.Zusatz();
        var vorher = await dienst.EinstellungenAsync(CancellationToken.None);
        var entfeuchterVorher = _stand.Repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!;
        // Mengenwächter: der Entfeuchter-Stand ist Brus (Tag 26,5, nicht der Werkswert 29) und der
        // Rest weicht vom Werkswert ab — sonst fiele ein zurückgesetztes Feld nicht auf.
        Assert.Equal(26.5, entfeuchterVorher.TempMaxTagFestC);
        var werk = new EntfeuchterEinstellungen();
        Assert.True(ZusatzPruefstand.Unterschiede(werk, entfeuchterVorher).Count >= 10,
            "Der Entfeuchter-Stand des Tests gleicht der Werkseinstellung — er würde ein Zurücksetzen nicht bemerken.");
        _stand.Vergessen();

        var (nachher, fehler, erreicht) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { TempMaxTagFestC = 27 }, CancellationToken.None);

        Assert.Empty(fehler);
        Assert.True(erreicht);

        // Home Assistant: GENAU ein Aufruf — die Tag-Grenze. Kein anderer Helfer, kein turn_on/turn_off
        // (VPD-Regelung, Tagbetrieb, die Port-7-Automation bleiben, wie sie sind).
        var aufrufe = _stand.Schreibaufrufe();
        var einzig = Assert.Single(aufrufe);
        Assert.Equal(("input_number/set_value", EntfeuchterSteuerungService.Entitaeten.TempMaxTag), (einzig.Dienst, einzig.Entitaet));
        Assert.Equal(27, einzig.Daten["value"]!.GetValue<double>());
        Assert.DoesNotContain(aufrufe, a => a.Dienst.Contains("turn_", StringComparison.Ordinal));

        // Der Entfeuchter-Stand im Fork: GENAU die Tag-Grenze ist anders.
        var entfeuchterNachher = _stand.Repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!;
        Assert.Equal(["tempMaxTagFestC"], ZusatzPruefstand.Unterschiede(entfeuchterVorher, entfeuchterNachher));
        Assert.Equal(27, entfeuchterNachher.TempMaxTagFestC);
        Assert.Equal(entfeuchterVorher.TempMaxNachtFestC, entfeuchterNachher.TempMaxNachtFestC);

        // Der Zusatz-Stand: dieselbe Grenze, sonst nichts.
        Assert.Equal(["tempMaxTagFestC"], ZusatzPruefstand.Unterschiede(vorher, nachher!));
    }

    [Fact]
    public async Task TempMax_NachtUndModus_SchreibenNurDenNachtHelfer_ImPlanModusMitPlanLuft()
    {
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);
        _stand.Vergessen();

        // Ohne laufenden Plan gilt im Modus „plan" der feste Wert — der Helfer bekommt ihn, nichts erfundenes.
        await dienst.SpeichernAsync(new EntfeuchterZusatzAenderung { TempMaxNachtFestC = 24, TempMaxNachtModus = TempMaxModus.Plan }, CancellationToken.None);

        var einzig = Assert.Single(_stand.Schreibaufrufe());
        Assert.Equal(EntfeuchterSteuerungService.Entitaeten.TempMaxNacht, einzig.Entitaet);
        Assert.Equal(24, einzig.Daten["value"]!.GetValue<double>());
        var gespeichert = _stand.Repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!;
        Assert.Equal(TempMaxModus.Plan, gespeichert.TempMaxNachtModus);
        Assert.Equal(26.5, gespeichert.TempMaxTagFestC);
    }

    [Fact]
    public async Task TempMaxZusammenMitEigenenFeldern_SchreibtBeides_UndSonstNichts()
    {
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);
        _stand.Vergessen();

        await dienst.SpeichernAsync(new EntfeuchterZusatzAenderung { TempMaxTagFestC = 27, FolgeAbstandK = 2 }, CancellationToken.None);

        Assert.Equal(
            new[] { EntfeuchterZusatzSteuerungService.Entitaeten.FolgeAbstand, EntfeuchterSteuerungService.Entitaeten.TempMaxTag }.Order(),
            _stand.Schreibaufrufe().Select(a => a.Entitaet).Order());
    }

    [Fact]
    public async Task Voreinstellung_SchreibtIhreFuenfHelfer_SonstNichts()
    {
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);
        _stand.Vergessen();

        var (nachher, fehler, _) = await dienst.SpeichernAsync(
            new EntfeuchterZusatzAenderung { Hilfe = EntfeuchterZusatzHilfe.Sparsam }, CancellationToken.None);

        Assert.Empty(fehler);
        Assert.Equal(EntfeuchterZusatzHilfe.Sparsam, EntfeuchterZusatzHilfe.Erkennen(nachher!));
        // Eine Voreinstellung nennt ihre fünf Werte — alle fünf gehen nach Home Assistant, auch der
        // Wieder-ein-Abstand (1 K), der von „normal" auf „sparsam" gleich bleibt. Sonst nichts.
        var geschrieben = _stand.Schreibaufrufe().Select(a => a.Entitaet).Order().ToList();
        Assert.Equal(new[]
        {
            // Die Stärke steht im Body, also wird auch der Zustand der Regelung (an) geschrieben.
            "automation.rdwc_trotec_zelt_shelly_plan_regelung",
            EntfeuchterZusatzSteuerungService.Entitaeten.FolgeAbstand,
            EntfeuchterZusatzSteuerungService.Entitaeten.Mindestpause,
            EntfeuchterZusatzSteuerungService.Entitaeten.VpdHysterese,
            EntfeuchterZusatzSteuerungService.Entitaeten.WiederEinAbstand,
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
    public async Task HilfeAus_SchaltetNurSwitchOderInputBooleanAus_SonstHinweisUndKeinBefehl()
    {
        var dienst = _stand.Zusatz();
        await dienst.EinstellungenAsync(CancellationToken.None);

        // Ein Schalter, der kein switch ist (ein select kennt „turn_off" nicht): kein Befehl an ihn.
        _stand.Ordne("zusatz_schalter", "select.komischer_schalter");
        _stand.Setze("select.komischer_schalter", "On");
        _stand.Vergessen();

        var ergebnis = await dienst.SpeichernAsync(new EntfeuchterZusatzAenderung { Hilfe = EntfeuchterZusatzHilfe.Aus }, CancellationToken.None);

        var aufrufe = _stand.Schreibaufrufe();
        Assert.DoesNotContain(aufrufe, a => a.Entitaet == "select.komischer_schalter");
        Assert.DoesNotContain(aufrufe, a => a.Dienst.EndsWith("/turn_off", StringComparison.Ordinal) && !a.Dienst.StartsWith("automation/", StringComparison.Ordinal));
        // Die Regelung wird trotzdem angehalten, und der Aufrufer erfährt, was nicht ging.
        Assert.Contains(aufrufe, a => a.Dienst == "automation/turn_off");
        Assert.False(ergebnis.HaErreicht);
        var hinweis = Assert.Single(ergebnis.Hinweise);
        Assert.Contains("select.komischer_schalter", hinweis);
        Assert.NotNull(ergebnis.Gespeichert);

        // Mit einem input_boolean geht es.
        _stand.Ordne("zusatz_schalter", "input_boolean.zusatz");
        _stand.Setze("input_boolean.zusatz", "on");
        _stand.Vergessen();
        var gut = await dienst.SpeichernAsync(new EntfeuchterZusatzAenderung { Hilfe = EntfeuchterZusatzHilfe.Aus }, CancellationToken.None);
        Assert.Contains(_stand.Schreibaufrufe(), a => a is { Dienst: "input_boolean/turn_off", Entitaet: "input_boolean.zusatz" });
        Assert.Empty(gut.Hinweise);
        Assert.True(gut.HaErreicht);
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
    public async Task Livebild_ZiehtNichts_ErstWennDerShellySeitDerEingestelltenDauerAnIst()
    {
        _stand.Setze("sensor.grow_dehumi_tent_leistung", "3.0", "Dehumi RDWC Tent Leistung", "W");

        // Seit drei Stunden an (Standard des Prüfstands), 3 W: zieht nichts.
        var live = await _stand.Zusatz().LiveAsync(CancellationToken.None);
        Assert.True(live.ZiehtNichts);
        Assert.Equal(3.0, live.LeistungW);

        // Erst seit einer Minute an: Anlaufphase, noch keine Meldung (Dauer 5 min).
        _stand.Seit["switch.grow_dehumi_tent"] = DateTime.UtcNow.AddMinutes(-1);
        Assert.False((await _stand.Zusatz().LiveAsync(CancellationToken.None)).ZiehtNichts);

        // Seit sechs Minuten: jetzt ja.
        _stand.Seit["switch.grow_dehumi_tent"] = DateTime.UtcNow.AddMinutes(-6);
        Assert.True((await _stand.Zusatz().LiveAsync(CancellationToken.None)).ZiehtNichts);
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
        Assert.False(live.ZiehtNichts);
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
