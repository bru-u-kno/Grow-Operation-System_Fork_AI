using System.Reflection;
using System.Text.Json;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-009): Die Rechnung des Zusatz-Entfeuchters — Voreinstellungen, Teil-Speichern,
/// Grenzen, Livebild. Rein, ohne Datenbank und Netz; der Weg durch Dienst und Home Assistant
/// steht in <see cref="EntfeuchterZusatzSpeichernTests"/>.
/// </summary>
public sealed class EntfeuchterZusatzTests
{
    // ----------------------------------------------------------- Voreinstellungen

    [Fact]
    public void Voreinstellungen_SindDieTabelleAusDenEntscheidungen()
    {
        // ENTSCHEIDUNGEN A-009, Punkt 3: sparsam {1,5; 1; 0,25; 20; 15} · normal {1; 1; 0,15; 10; 10} · kräftig {0,5; 0,5; 0,10; 5; 10}.
        Assert.Equal(3, EntfeuchterZusatzHilfe.Alle.Count);

        var sparsam = EntfeuchterZusatzHilfe.Finden("sparsam")!;
        Assert.Equal((1.5, 1.0, 0.25, 20, 15),
            (sparsam.FolgeAbstandK, sparsam.WiederEinAbstandK, sparsam.VpdHystereseKpa, sparsam.ZuschaltVerzoegerungMin, sparsam.MindestpauseMin));

        var normal = EntfeuchterZusatzHilfe.Finden("normal")!;
        Assert.Equal((1.0, 1.0, 0.15, 10, 10),
            (normal.FolgeAbstandK, normal.WiederEinAbstandK, normal.VpdHystereseKpa, normal.ZuschaltVerzoegerungMin, normal.MindestpauseMin));

        var kraeftig = EntfeuchterZusatzHilfe.Finden("kraeftig")!;
        Assert.Equal((0.5, 0.5, 0.10, 5, 10),
            (kraeftig.FolgeAbstandK, kraeftig.WiederEinAbstandK, kraeftig.VpdHystereseKpa, kraeftig.ZuschaltVerzoegerungMin, kraeftig.MindestpauseMin));
    }

    [Fact]
    public void JedeVoreinstellung_LiegtInDenGrenzenDerEinzelwerte()
    {
        // Sonst nähme Speichern eine Voreinstellung an, die ihre eigenen Einzelwerte ablehnen würden.
        Assert.True(EntfeuchterZusatzHilfe.Alle.Count >= 3);
        foreach (var v in EntfeuchterZusatzHilfe.Alle)
        {
            var e = Anwenden(new EntfeuchterZusatzAenderung { Hilfe = v.Name });
            Assert.Empty(EntfeuchterZusatzSteuerungService.Pruefen(EntfeuchterZusatzSteuerungService.AlsAenderung(e)));
        }
    }

    [Fact]
    public void DieWerkseinstellungSindDieNormaleHilfsstaerke()
    {
        var werk = new EntfeuchterZusatzEinstellungen();
        Assert.Equal(EntfeuchterZusatzHilfe.Normal, EntfeuchterZusatzHilfe.Erkennen(werk));
    }

    [Fact]
    public void Erkennen_FindetDieVoreinstellung_ErkenntEigeneWerte_UndMerktAus()
    {
        foreach (var v in EntfeuchterZusatzHilfe.Alle)
        {
            Assert.Equal(v.Name, EntfeuchterZusatzHilfe.Erkennen(Anwenden(new EntfeuchterZusatzAenderung { Hilfe = v.Name })));
        }

        // Ein Einzelwert, der zu keiner Voreinstellung passt → „eigene".
        Assert.Equal(EntfeuchterZusatzHilfe.Eigene, EntfeuchterZusatzHilfe.Erkennen(Anwenden(new EntfeuchterZusatzAenderung { FolgeAbstandK = 2 })));
        Assert.Equal(EntfeuchterZusatzHilfe.Eigene, EntfeuchterZusatzHilfe.Erkennen(Anwenden(new EntfeuchterZusatzAenderung { MindestpauseMin = 11 })));

        // Die Mindestlaufzeit gehört nicht zu den Voreinstellungen — sie macht aus „normal" nicht „eigene".
        Assert.Equal(EntfeuchterZusatzHilfe.Normal, EntfeuchterZusatzHilfe.Erkennen(Anwenden(new EntfeuchterZusatzAenderung { MindestlaufzeitMin = 30 })));

        Assert.Equal(EntfeuchterZusatzHilfe.Aus, EntfeuchterZusatzHilfe.Erkennen(Anwenden(new EntfeuchterZusatzAenderung { Hilfe = "aus" })));
    }

    // ------------------------------------------------------------- Teil-Speichern

    /// <summary>Ein Stand, in dem jedes Feld einen anderen Wert hat als die Werkseinstellung — sonst fiele ein zurückgesetztes Feld nicht auf.</summary>
    private static EntfeuchterZusatzEinstellungen Ausgangsstand() => new()
    {
        Hilfe = EntfeuchterZusatzHilfe.Eigene,
        AutomatikAktiv = false,
        TagbetriebErlauben = false,
        NachtDurchlaufen = false,
        VpdHystereseKpa = 0.3,
        ZuschaltVerzoegerungMin = 17,
        FolgeAbstandK = 2,
        WiederEinAbstandK = 1.5,
        MindestlaufzeitMin = 33,
        MindestpauseMin = 44,
        Meldung = new EntfeuchterZusatzMeldung { Aktiv = false, GrenzeW = 77, DauerMin = 9, WiederholungH = 6 },
        Ablauf = EntfeuchterZusatzAblauf.Schlauch,
        TempMaxTagModus = TempMaxModus.Plan,
        TempMaxTagAbstandK = 6.5,
        TempMaxTagFestC = 26.5,
        TempMaxNachtModus = TempMaxModus.Plan,
        TempMaxNachtAbstandK = 9,
        TempMaxNachtFestC = 25,
    };

    private static EntfeuchterZusatzEinstellungen Anwenden(EntfeuchterZusatzAenderung a, EntfeuchterZusatzEinstellungen? stand = null)
        => EntfeuchterZusatzSteuerungService.Anwenden(stand ?? new EntfeuchterZusatzEinstellungen(), a);

    /// <summary>Je Feld der Änderung: wie es gesetzt wird, und welcher Pfad sich dadurch ändert.</summary>
    private static readonly (string Name, Action<EntfeuchterZusatzAenderung> Setzen, string Pfad)[] EinzelFaelle =
    [
        ("AutomatikAktiv", a => a.AutomatikAktiv = true, "automatikAktiv"),
        ("TagbetriebErlauben", a => a.TagbetriebErlauben = true, "tagbetriebErlauben"),
        ("NachtDurchlaufen", a => a.NachtDurchlaufen = true, "nachtDurchlaufen"),
        ("VpdHystereseKpa", a => a.VpdHystereseKpa = 0.45, "vpdHystereseKpa"),
        ("ZuschaltVerzoegerungMin", a => a.ZuschaltVerzoegerungMin = 31, "zuschaltVerzoegerungMin"),
        ("FolgeAbstandK", a => a.FolgeAbstandK = 2.5, "folgeAbstandK"),
        ("WiederEinAbstandK", a => a.WiederEinAbstandK = 2.5, "wiederEinAbstandK"),
        ("MindestlaufzeitMin", a => a.MindestlaufzeitMin = 41, "mindestlaufzeitMin"),
        ("MindestpauseMin", a => a.MindestpauseMin = 51, "mindestpauseMin"),
        ("Ablauf", a => a.Ablauf = EntfeuchterZusatzAblauf.Tank, "ablauf"),
        ("Meldung.Aktiv", a => a.Meldung = new() { Aktiv = true }, "meldung.aktiv"),
        ("Meldung.GrenzeW", a => a.Meldung = new() { GrenzeW = 101 }, "meldung.grenzeW"),
        ("Meldung.DauerMin", a => a.Meldung = new() { DauerMin = 11 }, "meldung.dauerMin"),
        ("Meldung.WiederholungH", a => a.Meldung = new() { WiederholungH = 12 }, "meldung.wiederholungH"),
    ];

    public static IEnumerable<object[]> EinzelFallNamen() => EinzelFaelle.Select(f => new object[] { f.Name });

    [Theory]
    [MemberData(nameof(EinzelFallNamen))]
    public void NurEinFeld_NurDiesesFeldAendertSich(string name)
    {
        var fall = EinzelFaelle.Single(f => f.Name == name);
        var vorher = Ausgangsstand();
        var a = new EntfeuchterZusatzAenderung();
        fall.Setzen(a);

        var nachher = Anwenden(a, vorher);

        Assert.Equal([fall.Pfad], ZusatzPruefstand.Unterschiede(vorher, nachher));
        // Und der übergebene Stand blieb unberührt (Kopie, kein Durchschreiben).
        Assert.Empty(ZusatzPruefstand.Unterschiede(Ausgangsstand(), vorher));
    }

    [Fact]
    public void DieEinzelfaelleDeckenJedesFeldDerAenderungAb()
    {
        // Zählung statt Liste: kommt ein Feld dazu, fehlt es hier — und ein Feld, das beim Speichern
        // unbemerkt andere mitreißt, fiele sonst nie auf.
        var felder = typeof(EntfeuchterZusatzAenderung).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is null)
            .Select(p => p.Name)
            .Where(n => !n.StartsWith("TempMax", StringComparison.Ordinal) && n is not ("Meldung" or "Hilfe"))
            .Concat(typeof(EntfeuchterZusatzMeldungAenderung).GetProperties().Select(p => "Meldung." + p.Name))
            .ToList();

        Assert.True(felder.Count >= 14, $"Nur {felder.Count} Felder gefunden — die Reflexion sieht die Änderung nicht.");
        Assert.Equal(felder.Order(), EinzelFaelle.Select(f => f.Name).Order());
    }

    [Fact]
    public void KeinFeld_NichtsAendertSich()
    {
        var vorher = Ausgangsstand();
        Assert.Empty(ZusatzPruefstand.Unterschiede(vorher, Anwenden(new EntfeuchterZusatzAenderung(), vorher)));
        Assert.Empty(ZusatzPruefstand.Unterschiede(vorher, Anwenden(new EntfeuchterZusatzAenderung { Meldung = new() }, vorher)));
    }

    [Fact]
    public void ZweimalAnwenden_ErgibtDasselbe()
    {
        var a = new EntfeuchterZusatzAenderung { Hilfe = "sparsam", MindestlaufzeitMin = 20, Meldung = new() { GrenzeW = 90 } };
        var einmal = Anwenden(a, Ausgangsstand());
        var zweimal = Anwenden(a, einmal);
        Assert.Empty(ZusatzPruefstand.Unterschiede(einmal, zweimal));
    }

    [Fact]
    public void Voreinstellung_SetztNurDieFuenfWerte_UndEinEinzelwertImselbenBodyGewinnt()
    {
        var vorher = Ausgangsstand();

        var sparsam = Anwenden(new EntfeuchterZusatzAenderung { Hilfe = "sparsam" }, vorher);
        Assert.Equal(
            ["folgeAbstandK", "hilfe", "mindestpauseMin", "vpdHystereseKpa", "wiederEinAbstandK", "zuschaltVerzoegerungMin"],
            ZusatzPruefstand.Unterschiede(vorher, sparsam));
        Assert.Equal(1.5, sparsam.FolgeAbstandK);
        Assert.Equal(20, sparsam.ZuschaltVerzoegerungMin);
        Assert.Equal(15, sparsam.MindestpauseMin);
        Assert.Equal(0.25, sparsam.VpdHystereseKpa);
        Assert.Equal(1, sparsam.WiederEinAbstandK);

        var mitEinzelwert = Anwenden(new EntfeuchterZusatzAenderung { Hilfe = "sparsam", FolgeAbstandK = 0.5 }, vorher);
        Assert.Equal(0.5, mitEinzelwert.FolgeAbstandK);
        Assert.Equal(20, mitEinzelwert.ZuschaltVerzoegerungMin);
        Assert.Equal(EntfeuchterZusatzHilfe.Eigene, EntfeuchterZusatzHilfe.Erkennen(mitEinzelwert));
    }

    [Fact]
    public void HilfeAus_BleibtAus_BisEineStaerkeGewaehltWird()
    {
        var aus = Anwenden(new EntfeuchterZusatzAenderung { Hilfe = "aus" }, Ausgangsstand());
        Assert.Equal(EntfeuchterZusatzHilfe.Aus, EntfeuchterZusatzHilfe.Erkennen(aus));
        Assert.False(EntfeuchterZusatzSteuerungService.AutomatikWirksam(aus));

        // Ein Einzelwert wählt keine Stärke.
        var immerAus = Anwenden(new EntfeuchterZusatzAenderung { FolgeAbstandK = 2 }, aus);
        Assert.Equal(EntfeuchterZusatzHilfe.Aus, EntfeuchterZusatzHilfe.Erkennen(immerAus));

        var normal = Anwenden(new EntfeuchterZusatzAenderung { Hilfe = "normal", AutomatikAktiv = true }, immerAus);
        Assert.Equal(EntfeuchterZusatzHilfe.Normal, EntfeuchterZusatzHilfe.Erkennen(normal));
        Assert.True(EntfeuchterZusatzSteuerungService.AutomatikWirksam(normal));
    }

    [Fact]
    public void TempMaxEinarbeiten_AendertNurDieGenanntenFelder_AlleAnderenBleiben()
    {
        // Ein Entfeuchter-Stand, in dem JEDES Feld vom Werkswert abweicht.
        var gespeichert = new EntfeuchterEinstellungen
        {
            VpdRegelung = false, HystereseProzent = 6, MindestlaufzeitMin = 15, EinschaltverzoegerungMin = 7,
            WartezeitAussenluftMin = 30, TagbetriebErlauben = false, AutomatikAktiv = false,
            TempMaxTagModus = TempMaxModus.Fest, TempMaxTagAbstandK = 4.5, TempMaxTagFestC = 26.5,
            TempMaxNachtModus = TempMaxModus.Fest, TempMaxNachtAbstandK = 3, TempMaxNachtFestC = 24,
            FeuchteEinTag = 58, FeuchteAusTag = 54, FeuchteEinNacht = 61, FeuchteAusNacht = 56,
        };
        var alleFelder = typeof(EntfeuchterEinstellungen).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).ToList();
        Assert.True(alleFelder.Count >= 17, $"Nur {alleFelder.Count} Felder — die Reflexion sieht den Entfeuchter nicht.");

        var neu = EntfeuchterZusatzSteuerungService.TempMaxEinarbeiten(gespeichert,
            new EntfeuchterZusatzAenderung { TempMaxTagFestC = 27, TempMaxNachtModus = TempMaxModus.Plan });

        Assert.Equal(["tempMaxNachtModus", "tempMaxTagFestC"], ZusatzPruefstand.Unterschiede(gespeichert, neu));
        Assert.Equal(27, neu.TempMaxTagFestC);
        Assert.Equal(TempMaxModus.Plan, neu.TempMaxNachtModus);
        // Der übergebene Stand ist unberührt.
        Assert.Equal(26.5, gespeichert.TempMaxTagFestC);
    }

    [Fact]
    public void TempMaxEinarbeiten_OhneTempMaxFelder_IstEineKopieOhneAenderung()
    {
        var gespeichert = new EntfeuchterEinstellungen { HystereseProzent = 6, TempMaxTagFestC = 26.5 };
        var neu = EntfeuchterZusatzSteuerungService.TempMaxEinarbeiten(gespeichert, new EntfeuchterZusatzAenderung { FolgeAbstandK = 2 });
        Assert.Empty(ZusatzPruefstand.Unterschiede(gespeichert, neu));
        Assert.NotSame(gespeichert, neu);
    }

    [Fact]
    public void BetrifftTempMax_ErkenntJedesDerSechsFelder()
    {
        var setzer = new Action<EntfeuchterZusatzAenderung>[]
        {
            a => a.TempMaxTagModus = "fest", a => a.TempMaxTagAbstandK = 1, a => a.TempMaxTagFestC = 1,
            a => a.TempMaxNachtModus = "fest", a => a.TempMaxNachtAbstandK = 1, a => a.TempMaxNachtFestC = 1,
        };
        Assert.Equal(6, setzer.Length);
        Assert.False(new EntfeuchterZusatzAenderung { FolgeAbstandK = 2 }.BetrifftTempMax);
        foreach (var s in setzer)
        {
            var a = new EntfeuchterZusatzAenderung();
            s(a);
            Assert.True(a.BetrifftTempMax);
        }
    }

    // ------------------------------------------------------------------ Grenzen

    private static readonly (string Feld, Action<EntfeuchterZusatzAenderung, double> Setzen, double Min, double Max, bool Ganz)[] Grenzen =
    [
        ("FolgeAbstandK", (a, w) => a.FolgeAbstandK = w, 0.5, 3, false),
        ("WiederEinAbstandK", (a, w) => a.WiederEinAbstandK = w, 0.5, 3, false),
        ("ZuschaltVerzoegerungMin", (a, w) => a.ZuschaltVerzoegerungMin = (int)w, 0, 60, true),
        ("MindestlaufzeitMin", (a, w) => a.MindestlaufzeitMin = (int)w, 1, 120, true),
        ("MindestpauseMin", (a, w) => a.MindestpauseMin = (int)w, 1, 120, true),
        ("VpdHystereseKpa", (a, w) => a.VpdHystereseKpa = w, 0.05, 0.6, false),
        ("Meldung.GrenzeW", (a, w) => a.Meldung = new() { GrenzeW = (int)w }, 5, 200, true),
        ("Meldung.DauerMin", (a, w) => a.Meldung = new() { DauerMin = (int)w }, 1, 60, true),
        ("Meldung.WiederholungH", (a, w) => a.Meldung = new() { WiederholungH = (int)w }, 1, 24, true),
    ];

    public static IEnumerable<object[]> GrenzFallNamen() => Grenzen.Select(g => new object[] { g.Feld });

    [Theory]
    [MemberData(nameof(GrenzFallNamen))]
    public void Grenzen_UntereUndObereGrenzeGelten_DarueberUndDarunterNicht(string feld)
    {
        var g = Grenzen.Single(x => x.Feld == feld);
        var schritt = g.Ganz ? 1 : 0.01;

        foreach (var gueltig in new[] { g.Min, g.Max, (g.Min + g.Max) / 2 })
        {
            var a = new EntfeuchterZusatzAenderung();
            g.Setzen(a, g.Ganz ? Math.Floor(gueltig) : gueltig);
            Assert.Empty(EntfeuchterZusatzSteuerungService.Pruefen(a));
        }

        foreach (var ungueltig in new[] { g.Min - schritt, g.Max + schritt })
        {
            var a = new EntfeuchterZusatzAenderung();
            g.Setzen(a, ungueltig);
            var fehler = EntfeuchterZusatzSteuerungService.Pruefen(a);
            var gemeldet = Assert.Single(fehler);
            Assert.Equal(feld, gemeldet.Key);
            Assert.False(string.IsNullOrWhiteSpace(gemeldet.Value));
        }
    }

    [Fact]
    public void DieGrenzenDeckenJedesZahlenfeldAb()
    {
        var zahlenfelder = typeof(EntfeuchterZusatzAenderung).GetProperties()
            .Where(p => !p.Name.StartsWith("TempMax", StringComparison.Ordinal)
                        && (p.PropertyType == typeof(double?) || p.PropertyType == typeof(int?)))
            .Select(p => p.Name)
            .Concat(typeof(EntfeuchterZusatzMeldungAenderung).GetProperties()
                .Where(p => p.PropertyType == typeof(int?)).Select(p => "Meldung." + p.Name))
            .ToList();

        Assert.True(zahlenfelder.Count >= 9, $"Nur {zahlenfelder.Count} Zahlenfelder gefunden.");
        Assert.Equal(zahlenfelder.Order(), Grenzen.Select(g => g.Feld).Order());
    }

    [Fact]
    public void GrenzenStimmenMitDenSpannenDerHelferUeberein()
    {
        // Ein Wert, den Speichern annimmt, darf der Helfer in Home Assistant nicht ablehnen oder
        // still abschneiden (SteuerungBauteile.AufSpanne): beide Spannen sind dieselben.
        var helfer = new Dictionary<string, string>
        {
            ["FolgeAbstandK"] = EntfeuchterZusatzSteuerungService.Entitaeten.FolgeAbstand,
            ["WiederEinAbstandK"] = EntfeuchterZusatzSteuerungService.Entitaeten.WiederEinAbstand,
            ["ZuschaltVerzoegerungMin"] = EntfeuchterZusatzSteuerungService.Entitaeten.ZuschaltVerzoegerung,
            ["MindestlaufzeitMin"] = EntfeuchterZusatzSteuerungService.Entitaeten.Mindestlaufzeit,
            ["MindestpauseMin"] = EntfeuchterZusatzSteuerungService.Entitaeten.Mindestpause,
            ["VpdHystereseKpa"] = EntfeuchterZusatzSteuerungService.Entitaeten.VpdHysterese,
            ["Meldung.GrenzeW"] = EntfeuchterZusatzSteuerungService.Entitaeten.MeldeGrenze,
            ["Meldung.DauerMin"] = EntfeuchterZusatzSteuerungService.Entitaeten.MeldeDauer,
            ["Meldung.WiederholungH"] = EntfeuchterZusatzSteuerungService.Entitaeten.MeldeWiederholung,
        };
        Assert.Equal(Grenzen.Length, helfer.Count);
        foreach (var g in Grenzen)
        {
            var (min, max) = SteuerungBauteile.Spanne(helfer[g.Feld]);
            Assert.True(min == g.Min && max == g.Max, $"{g.Feld}: Prüfung {g.Min}–{g.Max}, Helfer {min}–{max}");
        }
    }

    [Fact]
    public void UnbekannteNamen_WerdenAbgelehnt()
    {
        Assert.Contains("Hilfe", EntfeuchterZusatzSteuerungService.Pruefen(new EntfeuchterZusatzAenderung { Hilfe = "turbo" }).Keys);
        Assert.Contains("Ablauf", EntfeuchterZusatzSteuerungService.Pruefen(new EntfeuchterZusatzAenderung { Ablauf = "eimer" }).Keys);
        foreach (var gut in new[] { "aus", "sparsam", "normal", "kraeftig", "eigene" })
        {
            Assert.Empty(EntfeuchterZusatzSteuerungService.Pruefen(new EntfeuchterZusatzAenderung { Hilfe = gut }));
        }
        foreach (var gut in new[] { "tank", "schlauch" })
        {
            Assert.Empty(EntfeuchterZusatzSteuerungService.Pruefen(new EntfeuchterZusatzAenderung { Ablauf = gut }));
        }
    }

    [Fact]
    public void NichtEndlicheZahlen_WerdenAbgelehnt()
    {
        Assert.Contains("FolgeAbstandK", EntfeuchterZusatzSteuerungService.Pruefen(new EntfeuchterZusatzAenderung { FolgeAbstandK = double.NaN }).Keys);
        Assert.Contains("VpdHystereseKpa", EntfeuchterZusatzSteuerungService.Pruefen(new EntfeuchterZusatzAenderung { VpdHystereseKpa = double.PositiveInfinity }).Keys);
    }

    [Fact]
    public void EineLeereAenderung_IstGueltig_UndDieWerkseinstellungSind()
    {
        Assert.Empty(EntfeuchterZusatzSteuerungService.Pruefen(new EntfeuchterZusatzAenderung()));
        Assert.Empty(EntfeuchterZusatzSteuerungService.Pruefen(EntfeuchterZusatzSteuerungService.AlsAenderung(new EntfeuchterZusatzEinstellungen())));
    }

    // ------------------------------------------------------ Übernahme aus Home Assistant

    [Fact]
    public void AusHomeAssistant_LiestDieHelfer_UndLaesstFehlendesAufDerVorgabe()
    {
        var e = EntfeuchterZusatzSteuerungService.AusHomeAssistant(new Dictionary<string, string?>
        {
            [EntfeuchterZusatzSteuerungService.Entitaeten.VpdHysterese] = "0.25",
            [EntfeuchterZusatzSteuerungService.Entitaeten.Mindestpause] = "15.0",
            [EntfeuchterZusatzSteuerungService.Entitaeten.FolgeAbstand] = "1.5",
            [EntfeuchterZusatzSteuerungService.Entitaeten.ZuschaltVerzoegerung] = "20",
            [EntfeuchterZusatzSteuerungService.Entitaeten.Melden] = "off",
            [EntfeuchterZusatzSteuerungService.Entitaeten.MeldeGrenze] = "unavailable",
        }, automatikAn: false);

        Assert.Equal(0.25, e.VpdHystereseKpa);
        Assert.Equal(15, e.MindestpauseMin);
        Assert.Equal(1.5, e.FolgeAbstandK);
        Assert.Equal(20, e.ZuschaltVerzoegerungMin);
        Assert.False(e.Meldung.Aktiv);
        Assert.False(e.AutomatikAktiv);
        // Nicht gelesen → Vorgabe, nicht 0.
        Assert.Equal(60, e.Meldung.GrenzeW);
        Assert.Equal(15, e.MindestlaufzeitMin);
        // Passt zu „sparsam" (1,5 / 1 / 0,25 / 20 / 15).
        Assert.Equal(EntfeuchterZusatzHilfe.Sparsam, EntfeuchterZusatzHilfe.Erkennen(e));
    }

    // ---------------------------------------------------------------- Livebild

    private static readonly DateTime Jetzt = new(2026, 10, 6, 21, 0, 0, DateTimeKind.Utc);

    private static EntfeuchterZusatzSteuerungService.ZusatzEingang Eingang(
        double? temp = 25.1, double? rh = 55.2, double? vpd = 1.31, bool? tag = true,
        double? vpdUnten = 1.4, double? vpdOben = 1.4, double? feuchteEin = 39, double? feuchteAus = 35,
        string? zusatz = "on", double? leistung = 313, string? fuehrung = "On", bool? automatik = true,
        (string, double, double)? plan = null, bool ha = true, int? anSeitMin = 30, double? rhMax = null)
        => new(ha, "RDWC Dehumi", "Dehumi RDWC Tent", plan, temp, rh, vpd, tag, vpdUnten, vpdOben, feuchteEin, feuchteAus,
            zusatz, leistung, 2.9, fuehrung, automatik,
            ZusatzSeitUtc: anSeitMin is { } m ? Jetzt.AddMinutes(-m) : null, JetztUtc: Jetzt, RhObergrenzeProzent: rhMax);

    private static EntfeuchterZusatzEinstellungen Bru() => new()
    {
        TempMaxTagModus = TempMaxModus.Fest, TempMaxTagFestC = 26.5,
        TempMaxNachtModus = TempMaxModus.Fest, TempMaxNachtFestC = 25,
    };

    [Fact]
    public void Livebild_RechnetSchwellenUndTemperaturen()
    {
        var live = EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang());

        Assert.Equal((26.5, 25.0), (live.TempMaxTagC, live.TempMaxNachtC));
        Assert.Equal((25.5, 24.0), (live.FolgeAusTagC, live.FolgeAusNachtC));
        Assert.Equal((24.5, 23.0), (live.WiederEinTagC, live.WiederEinNachtC));
        Assert.Equal(EntfeuchterZusatzSchaltgroesse.Vpd, live.Schaltgroesse);
        Assert.Equal((1.4, 1.25, 1.55), (live.VpdZiel, live.VpdEinSchwelle, live.VpdAusSchwelle));
        Assert.Equal((39.0, 35.0), (live.FeuchteEinProzent, live.FeuchteAusProzent));
        Assert.False(live.PlanUnvollstaendig);
        Assert.Equal(("RDWC Dehumi", "Dehumi RDWC Tent"), (live.FuehrungName, live.ZusatzName));
    }

    [Fact]
    public void Livebild_ReichtDiePlanObergrenzeDerLuftfeuchteDurch_SonstNull()
    {
        // A-014: Beide Entfeuchter-Seiten messen die Zone der Feuchte am selben Ziel — der Plan-Obergrenze.
        Assert.Equal(51.0, EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(rhMax: 51)).RhObergrenzeProzent);
        Assert.Null(EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang()).RhObergrenzeProzent);
    }

    [Fact]
    public void Livebild_FolgeUndWiederEinFolgenDenAbstaenden()
    {
        var e = Bru();
        e.FolgeAbstandK = 2;
        e.WiederEinAbstandK = 0.5;
        e.VpdHystereseKpa = 0.25;

        var live = EntfeuchterZusatzSteuerungService.Berechnen(e, Eingang());

        Assert.Equal((24.5, 23.0), (live.FolgeAusTagC, live.FolgeAusNachtC));
        Assert.Equal((24.0, 22.5), (live.WiederEinTagC, live.WiederEinNachtC));
        Assert.Equal((1.15, 1.65), (live.VpdEinSchwelle, live.VpdAusSchwelle));
    }

    [Fact]
    public void Livebild_HoechsttemperaturImPlanModus_IstPlanLuftPlusAbstand()
    {
        var e = Bru();
        e.TempMaxTagModus = TempMaxModus.Plan;
        e.TempMaxTagAbstandK = 6.5;
        e.TempMaxNachtModus = TempMaxModus.Plan;
        e.TempMaxNachtAbstandK = 9;

        var live = EntfeuchterZusatzSteuerungService.Berechnen(e, Eingang(plan: ("Blütewoche 7", 20, 16)));

        Assert.Equal((26.5, 25.0), (live.TempMaxTagC, live.TempMaxNachtC));
        Assert.Equal("Blütewoche 7", live.PlanWoche);
        Assert.Equal((20, 16), (live.PlanLuftTagC, live.PlanLuftNachtC));
    }

    [Theory]
    [InlineData(1.4, 1.4, 39.0, 35.0, "vpd", false)]
    [InlineData(1.4, null, 39.0, 35.0, "feuchte", false)]
    [InlineData(null, null, 39.0, 35.0, "feuchte", false)]
    [InlineData(null, null, 39.0, null, "keine", true)]
    [InlineData(null, null, null, null, "keine", true)]
    public void Schaltgroesse_VpdVorFeuchteVorKeine(double? vpdUnten, double? vpdOben, double? ein, double? aus, string erwartet, bool unvollstaendig)
    {
        var live = EntfeuchterZusatzSteuerungService.Berechnen(Bru(),
            Eingang(vpdUnten: vpdUnten, vpdOben: vpdOben, feuchteEin: ein, feuchteAus: aus));

        Assert.Equal(erwartet, live.Schaltgroesse);
        Assert.Equal(unvollstaendig, live.PlanUnvollstaendig);
    }

    [Fact]
    public void Schaltgroesse_NurEineVpdGrenze_ReichtNichtAus()
    {
        var live = EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(vpdUnten: 1.4, vpdOben: null, feuchteEin: null, feuchteAus: null));
        Assert.Equal(EntfeuchterZusatzSchaltgroesse.Keine, live.Schaltgroesse);
        Assert.Null(live.VpdZiel);
        Assert.Null(live.VpdEinSchwelle);
        Assert.Null(live.VpdAusSchwelle);
    }

    [Theory]
    [InlineData("on", 3.0, true)]
    [InlineData("on", 59.9, true)]
    [InlineData("on", 60.0, false)]
    [InlineData("on", 313.0, false)]
    [InlineData("off", 3.0, false)]
    [InlineData("off", 313.0, false)]
    public void ZiehtNichts_NurBeiLaufendemShellyUnterDerGrenze(string zusatz, double leistung, bool erwartet)
    {
        var live = EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(zusatz: zusatz, leistung: leistung));
        Assert.Equal(erwartet, live.ZiehtNichts);
    }

    [Theory]
    [InlineData(1, false)]   // Anlaufphase: erst seit einer Minute an
    [InlineData(4, false)]
    [InlineData(5, true)]    // genau die eingestellte Dauer
    [InlineData(30, true)]
    public void ZiehtNichts_ErstNachDerEingestelltenDauer_NichtInDerAnlaufphase(int anSeitMin, bool erwartet)
    {
        // Ein Kompressor zieht in der ersten Minute nach dem Einschalten oft noch wenig — die Meldung
        // darf nicht schon dann kommen (Dauer = 5 min).
        var live = EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(zusatz: "on", leistung: 3, anSeitMin: anSeitMin));
        Assert.Equal(erwartet, live.ZiehtNichts);
    }

    [Fact]
    public void ZiehtNichts_FolgtDerEingestelltenDauerUndGrenze()
    {
        var e = Bru();
        e.Meldung.GrenzeW = 350;
        e.Meldung.DauerMin = 20;
        Assert.False(EntfeuchterZusatzSteuerungService.Berechnen(e, Eingang(zusatz: "on", leistung: 313, anSeitMin: 19)).ZiehtNichts);
        Assert.True(EntfeuchterZusatzSteuerungService.Berechnen(e, Eingang(zusatz: "on", leistung: 313, anSeitMin: 20)).ZiehtNichts);
    }

    [Fact]
    public void ZiehtNichts_IstFalse_WennDieMeldungAusgeschaltetIst()
    {
        var e = Bru();
        e.Meldung.Aktiv = false;
        Assert.False(EntfeuchterZusatzSteuerungService.Berechnen(e, Eingang(zusatz: "on", leistung: 3)).ZiehtNichts);
    }

    [Theory]
    [InlineData(null, 3.0, 30)]
    [InlineData("unavailable", 3.0, 30)]
    [InlineData("unknown", 3.0, 30)]
    [InlineData("on", null, 30)]
    [InlineData("on", 3.0, null)]   // seit wann er an ist, weiß Home Assistant nicht
    public void ZiehtNichts_OhneMesswertZustandOderZeitpunkt_IstFalse_NieErraten(string? zusatz, double? leistung, int? anSeitMin)
    {
        Assert.False(EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(zusatz: zusatz, leistung: leistung, anSeitMin: anSeitMin)).ZiehtNichts);
    }

    [Fact]
    public void PlanUnvollstaendig_NurMitHomeAssistant_OhneVerbindungWeissNiemandEtwasUeberDenPlan()
    {
        var mitHa = EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(vpdUnten: null, vpdOben: null, feuchteEin: null, feuchteAus: null, ha: true));
        Assert.True(mitHa.PlanUnvollstaendig);
        Assert.Equal(EntfeuchterZusatzSchaltgroesse.Keine, mitHa.Schaltgroesse);

        var ohneHa = EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(vpdUnten: null, vpdOben: null, feuchteEin: null, feuchteAus: null, ha: false));
        Assert.False(ohneHa.PlanUnvollstaendig);

        // Mit Plan-Werten ist er nie unvollständig.
        Assert.False(EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(ha: true)).PlanUnvollstaendig);
        Assert.False(EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(vpdUnten: null, vpdOben: null, ha: true)).PlanUnvollstaendig);
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("On", true)]
    [InlineData("off", false)]
    [InlineData("Off", false)]
    [InlineData("Auto", false)]
    [InlineData("unavailable", null)]
    [InlineData("unknown", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void AnAus_ZustandOderNull(string? zustand, bool? erwartet)
        => Assert.Equal(erwartet, EntfeuchterZusatzSteuerungService.AnAus(zustand));

    [Fact]
    public void ZusatzOnline_UnavailableIstOffline_FehlendIstUnbekannt()
    {
        Assert.False(EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(zusatz: "unavailable")).ZusatzOnline);
        Assert.True(EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(zusatz: "off")).ZusatzOnline);
        Assert.Null(EntfeuchterZusatzSteuerungService.Berechnen(Bru(), Eingang(zusatz: null)).ZusatzOnline);
    }

    // ---------------------------------------------------------- Energie heute

    private static HaVerlaufsPunkt P(string zustand) => new(DateTime.UtcNow, zustand);

    [Fact]
    public void EnergieHeute_SummeDerAnstiege_ResetBeginntBeiNull_OhneVerlaufNull()
    {
        Assert.Null(EntfeuchterZusatzSteuerungService.EnergieHeute(null));
        Assert.Null(EntfeuchterZusatzSteuerungService.EnergieHeute([]));
        Assert.Null(EntfeuchterZusatzSteuerungService.EnergieHeute([P("unavailable"), P("unknown")]));
        Assert.Equal(0.0, EntfeuchterZusatzSteuerungService.EnergieHeute([P("7.0")]));
        Assert.Equal(1.15, EntfeuchterZusatzSteuerungService.EnergieHeute([P("7.00"), P("7.60"), P("unavailable"), P("8.15")]));
        // Zähler zurückgesetzt (Shelly-Neustart): 7,0 → 7,5 (0,5), dann 0,2 (Neuanfang: 0,2), dann 0,5 (0,3).
        Assert.Equal(1.0, EntfeuchterZusatzSteuerungService.EnergieHeute([P("7.0"), P("7.5"), P("0.2"), P("0.5")]));
    }

    // -------------------------------------------------------------------- Namen

    [Fact]
    public void GeraeteName_LoestDenEntitaetsNamenAuf()
    {
        Assert.Equal("RDWC Dehumi", EntfeuchterZusatzSteuerungService.GeraeteName("RDWC Dehumi Aktiver Modus"));
        Assert.Equal("Dehumi RDWC Tent", EntfeuchterZusatzSteuerungService.GeraeteName("Dehumi RDWC Tent"));
        Assert.Equal("Trotec", EntfeuchterZusatzSteuerungService.GeraeteName("Trotec Zustand"));
        // Nicht auf nichts kürzen.
        Assert.Equal("Zustand", EntfeuchterZusatzSteuerungService.GeraeteName("Zustand"));
        Assert.Null(EntfeuchterZusatzSteuerungService.GeraeteName(null));
        Assert.Null(EntfeuchterZusatzSteuerungService.GeraeteName("  "));
    }

    [Fact]
    public void NamenBilden_VorgabeAusHomeAssistant_DanachAllgemein_UndGespeichertesGewinnt()
    {
        var ohne = EntfeuchterZusatzSteuerungService.NamenBilden(null, null, null);
        Assert.Equal(("Entfeuchter", "Entfeuchter"), (ohne.Fuehrung.Anzeigename, ohne.Fuehrung.Vorgabe));
        Assert.Equal(("Zusatz-Entfeuchter", "Zusatz-Entfeuchter"), (ohne.Zusatz.Anzeigename, ohne.Zusatz.Vorgabe));

        var mit = EntfeuchterZusatzSteuerungService.NamenBilden(new EntfeuchterNamen { Zusatz = " Zeltgerät " }, "RDWC Dehumi Aktiver Modus", "Dehumi RDWC Tent");
        Assert.Equal(("RDWC Dehumi", "RDWC Dehumi"), (mit.Fuehrung.Anzeigename, mit.Fuehrung.Vorgabe));
        Assert.Equal(("Zeltgerät", "Dehumi RDWC Tent"), (mit.Zusatz.Anzeigename, mit.Zusatz.Vorgabe));
    }

    [Fact]
    public void NamenAenderung_UnterscheidetFehlendVonNull()
    {
        var optionen = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var fehlt = JsonSerializer.Deserialize<EntfeuchterNamenAenderung>("{}", optionen)!;
        Assert.False(fehlt.FuehrungGesetzt);
        Assert.False(fehlt.ZusatzGesetzt);

        var einer = JsonSerializer.Deserialize<EntfeuchterNamenAenderung>("""{"fuehrung":"Haupt"}""", optionen)!;
        Assert.True(einer.FuehrungGesetzt);
        Assert.False(einer.ZusatzGesetzt);

        var null_ = JsonSerializer.Deserialize<EntfeuchterNamenAenderung>("""{"zusatz":null}""", optionen)!;
        Assert.False(null_.FuehrungGesetzt);
        Assert.True(null_.ZusatzGesetzt);
        Assert.Null(null_.Zusatz);
    }

    // ----------------------------------------------------- Schreibliste, Katalog

    [Fact]
    public void Schreibliste_NurWasSichAendert_AufDieSpanneDesHelfers()
    {
        var alt = Ausgangsstand();
        Assert.Empty(EntfeuchterZusatzSteuerungService.Schreibliste(alt, Ausgangsstand()));

        var neu = Ausgangsstand();
        neu.MindestlaufzeitMin = 20;
        neu.Meldung.Aktiv = true;
        var liste = EntfeuchterZusatzSteuerungService.Schreibliste(alt, neu);

        Assert.Equal(2, liste.Count);
        Assert.Contains(liste, l => l is { Entitaet: EntfeuchterZusatzSteuerungService.Entitaeten.Mindestlaufzeit, Wert: 20, Dienst: "set_value" });
        Assert.Contains(liste, l => l is { Entitaet: EntfeuchterZusatzSteuerungService.Entitaeten.Melden, Dienst: "turn_on", Wert: null });
    }

    [Fact]
    public void JedeEntitaetDesDienstes_StehtImKatalog_MitDerRichtigenDomaene()
    {
        // Eine Kennung aus dem Kopf ist ein Tippfehler, der still ins Leere schreibt (CLAUDE.md, Prüfung 3).
        var konstanten = typeof(EntfeuchterZusatzSteuerungService.Entitaeten)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        Assert.True(konstanten.Count >= 12, $"Nur {konstanten.Count} Kennungen gefunden — die Reflexion sieht sie nicht.");

        var katalog = SteuerungBauteile.FuerModul(EntfeuchterZusatzSteuerungService.Modul).Select(b => b.EntityId).ToHashSet();
        foreach (var id in konstanten)
        {
            Assert.Contains(id, katalog);
        }
    }

    [Fact]
    public void ZusatzRollen_GibtEsMitDenDomaenen_UndDieMitbenutztenAuchImEntfeuchter()
    {
        foreach (var rolle in new[] { "zusatz_schalter", "zusatz_leistung", "zusatz_energie", "fuehrung_zustand" })
        {
            Assert.NotNull(SteuerungGeraeteRollen.Finden(EntfeuchterZusatzSteuerungService.Modul, rolle));
        }

        Assert.Contains("switch", SteuerungGeraeteRollen.Finden(EntfeuchterZusatzSteuerungService.Modul, "zusatz_schalter")!.Domains);
        Assert.Contains("select", SteuerungGeraeteRollen.Finden(EntfeuchterZusatzSteuerungService.Modul, "fuehrung_zustand")!.Domains);
        Assert.True(SteuerungGeraeteRollen.Finden(EntfeuchterZusatzSteuerungService.Modul, "zusatz_schalter")!.Pflicht);
        Assert.False(SteuerungGeraeteRollen.Finden(EntfeuchterZusatzSteuerungService.Modul, "zusatz_leistung")!.Pflicht);

        var mit = SteuerungGeraeteRollen.MitbenutztVon(EntfeuchterZusatzSteuerungService.Modul);
        Assert.Equal(4, mit.Count);
        Assert.Empty(SteuerungGeraeteRollen.MitbenutztVon("entfeuchter"));
        // Keine doppelte Zuordnung: das Modul trägt diese Rollen NICHT noch einmal selbst.
        Assert.DoesNotContain(SteuerungGeraeteRollen.FuerModul(EntfeuchterZusatzSteuerungService.Modul),
            r => mit.Any(m => m.Rolle == r.Schluessel));
        Assert.Equal(8, SteuerungGeraeteRollen.FuerModulMitMitbenutzten(EntfeuchterZusatzSteuerungService.Modul).Count);
    }

    [Fact]
    public void Vorlagen_Fassung_ErkenntAuchEinModulMitBindestrich()
    {
        // „entfeuchter-zusatz" hat einen Bindestrich; das Muster der Herkunftsmarke liess ihn früher nicht zu —
        // die Fassung der Vorlage wäre null gewesen, und eine Erneuerung nie angeboten worden.
        Assert.Equal(3, SteuerungAutomationService.Fassung("... Herkunft: fork-ai/entfeuchter-zusatz/regelung/3."));
        Assert.Equal(1, SteuerungAutomationService.VorlagenFassung("entfeuchter-zusatz", "regelung"));
        Assert.Equal(2, SteuerungAutomationService.VorlagenFassung("entfeuchter-zusatz", "meldung"));
    }

    // ------------------------------------------- genannte Felder immer schreiben

    [Fact]
    public void Schreibliste_EinGenanntesFeld_WirdAuchBeiGleichemWertGeschrieben()
    {
        var stand = Ausgangsstand();
        var a = new EntfeuchterZusatzAenderung { FolgeAbstandK = stand.FolgeAbstandK, Meldung = new() { Aktiv = stand.Meldung.Aktiv } };

        var liste = EntfeuchterZusatzSteuerungService.Schreibliste(stand, Anwenden(a, stand), a);

        Assert.Equal(2, liste.Count);
        Assert.Contains(liste, l => l is { Entitaet: EntfeuchterZusatzSteuerungService.Entitaeten.FolgeAbstand, Wert: 2 });
        Assert.Contains(liste, l => l is { Entitaet: EntfeuchterZusatzSteuerungService.Entitaeten.Melden, Dienst: "turn_off" });
        // Ohne Kenntnis der Änderung bleibt es beim Vergleich: gleich = nichts.
        Assert.Empty(EntfeuchterZusatzSteuerungService.Schreibliste(stand, Anwenden(a, stand)));
    }

    [Fact]
    public void Schreibliste_EineVoreinstellungNenntIhreFuenfWerte()
    {
        var stand = Anwenden(new EntfeuchterZusatzAenderung { Hilfe = "normal" });
        var a = new EntfeuchterZusatzAenderung { Hilfe = "normal" };

        var liste = EntfeuchterZusatzSteuerungService.Schreibliste(stand, Anwenden(a, stand), a);

        Assert.Equal(
            new[] { "folge_abstand", "mindestpause", "vpd_hysterese", "wieder_ein_abstand", "zuschalt_verzogerung" },
            liste.Select(l => l.Entitaet.Replace("input_number.trotec_zelt_", "")).Order());
        // „aus" und „eigene" nennen keinen Helfer.
        Assert.Empty(EntfeuchterZusatzSteuerungService.Schreibliste(stand, stand, new EntfeuchterZusatzAenderung { Hilfe = "aus" }));
        Assert.Empty(EntfeuchterZusatzSteuerungService.Schreibliste(stand, stand, new EntfeuchterZusatzAenderung { Hilfe = "eigene" }));
    }

    [Fact]
    public void TempMaxSchreibliste_NurDieGenanntenHelfer_MitDemGueltigenSollwert()
    {
        var e = new EntfeuchterEinstellungen
        {
            TempMaxTagModus = TempMaxModus.Fest, TempMaxTagFestC = 27,
            TempMaxNachtModus = TempMaxModus.Plan, TempMaxNachtAbstandK = 9,
        };

        var nurTag = EntfeuchterZusatzSteuerungService.TempMaxSchreibliste(e, new() { TempMaxTagFestC = 27 }, ("Woche 7", 20, 16));
        var tag = Assert.Single(nurTag);
        Assert.Equal((EntfeuchterSteuerungService.Entitaeten.TempMaxTag, "set_value", 27.0), (tag.Entitaet, tag.Dienst, tag.Wert));

        // Nacht im Plan-Modus: Plan-Luft Nacht + Abstand.
        var nurNacht = EntfeuchterZusatzSteuerungService.TempMaxSchreibliste(e, new() { TempMaxNachtAbstandK = 9 }, ("Woche 7", 20, 16));
        var nacht = Assert.Single(nurNacht);
        Assert.Equal((EntfeuchterSteuerungService.Entitaeten.TempMaxNacht, 25.0), (nacht.Entitaet, nacht.Wert));

        Assert.Equal(2, EntfeuchterZusatzSteuerungService.TempMaxSchreibliste(e, new() { TempMaxTagModus = "fest", TempMaxNachtModus = "plan" }, null).Count);
        Assert.Empty(EntfeuchterZusatzSteuerungService.TempMaxSchreibliste(e, new() { FolgeAbstandK = 2 }, null));
    }

    // ------------------------------------------------------- Shelly ausschalten

    [Theory]
    [InlineData("switch.grow_dehumi_tent", true)]
    [InlineData("input_boolean.zusatz", true)]
    [InlineData("select.rdwc_dehumi_aktiver_modus", false)]
    [InlineData("light.irgendwas", false)]
    [InlineData("climate.trotec", false)]
    [InlineData("automation.regelung", false)]
    [InlineData("sensor.x", false)]
    [InlineData("kaputt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IstSchaltbar_NurSwitchUndInputBoolean(string? entitaet, bool erwartet)
        => Assert.Equal(erwartet, EntfeuchterZusatzSteuerungService.IstSchaltbar(entitaet));

    // ------------------------------------------------------------ Push-Adresse

    [Theory]
    [InlineData("notify.mobile_app_bruno_smartphone_1", "notify.mobile_app_bruno_smartphone_1")]
    [InlineData("  notify.mobile_app_pixel ", "notify.mobile_app_pixel")]
    [InlineData(null, "notify.notify")]
    [InlineData("", "notify.notify")]
    [InlineData("persistent_notification.create", "notify.notify")]
    [InlineData("notify.x\"; drop", "notify.notify")]
    [InlineData("mobile_app_pixel", "notify.notify")]
    public void PushDienst_DieEingestellteAdresse_SonstDieSammelgruppe(string? eingestellt, string erwartet)
        => Assert.Equal(erwartet, SteuerungAutomationService.PushDienst(eingestellt));
}
