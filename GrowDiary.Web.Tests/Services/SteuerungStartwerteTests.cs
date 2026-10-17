using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Neuling-Test 17.10.2026: Frisch angelegte Helfer bekommen die Werte des Fork
/// (<see cref="SteuerungStartwerteService"/>). Der Schreibweg selbst (Home Assistant) ist am laufenden Stand geprüft;
/// hier steht die Zählung gegen die Grundmenge — ein Tippfehler in der Liste ließe einen Helfer stumm auf seinem Minimum.
/// </summary>
public sealed class SteuerungStartwerteTests
{
    private static IReadOnlyList<Bauteil> EntfeuchterHelfer()
        => SteuerungBauteile.FuerModul(EntfeuchterSteuerungService.Modul)
            .Where(b => b.Art is BauteilArt.Zahl or BauteilArt.Schalter)
            .ToList();

    private static string Id(Bauteil b) => Kennungen.Id(b.EntityId);

    [Fact]
    public void DieStartwerteGehoerenAlleZuHelfernDesKatalogs()
    {
        var helfer = EntfeuchterHelfer().Select(Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var startwerte = EntfeuchterSteuerungService.Entitaeten.Startwerte;

        // Mengenwächter: ohne Grundmenge liefe die Prüfung null Mal und wäre grün.
        Assert.True(helfer.Count >= 12, $"Der Katalog kennt {helfer.Count} Zahl-/Schalter-Helfer des Entfeuchters.");
        Assert.True(startwerte.Count >= 12);
        Assert.Equal(startwerte.Count, startwerte.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(startwerte, id => Assert.Contains(id, helfer));
    }

    [Fact]
    public void JederHelferDesEntfeuchtersBekommtEinenStartwertOderHatEinenAnderenSchreiber()
    {
        // Was NICHT in der Startwerte-Liste steht, schreibt jemand anderes — sonst bliebe der Helfer auf dem Minimum von
        // Home Assistant. Neuer Helfer im Katalog → entweder in Startwerte aufnehmen oder hier mit Grund eintragen.
        var andererSchreiber = new[]
        {
            "input_number.vpd_ziel_unten",        // schreibt der Wochenplan (WochenplanSyncService)
            "input_number.vpd_ziel_abschaltung",  // schreibt der Wochenplan
            "input_number.vpd_blatt_offset",      // Blatt-Offset aus dem Zelt
        };

        var ohneStartwert = EntfeuchterHelfer()
            .Where(b => !EntfeuchterSteuerungService.Entitaeten.Startwerte.Contains(Id(b), StringComparer.OrdinalIgnoreCase))
            .Select(b => b.EntityId)
            .Order()
            .ToList();

        Assert.Equal(andererSchreiber.Order(), ohneStartwert);
    }

    [Theory]
    [InlineData("entfeuchter", true)]
    [InlineData("Entfeuchter", true)]
    [InlineData("chiller", true)]
    [InlineData("zuluft", true)]
    [InlineData("co2", true)]
    [InlineData("entfeuchter-zusatz", false)] // schreibt nur geänderte Felder gegen einen alten Stand — noch kein Weg
    [InlineData("licht", false)]              // hat keine Zahl-Helfer
    public void WelcheSteuerungenEinenStartwertWegHaben(string modul, bool kann)
        => Assert.Equal(kann, SteuerungStartwerteService.Kann(modul));

    [Fact]
    public void DieListeDerModuleIstGenauDieDerDienste()
    {
        // Mengenwächter: Wer IStartwertDienst umsetzt, steht in der Liste (und umgekehrt) — sonst bliebe ein Dienst ohne Aufruf
        // oder die Liste verspräche einen Weg, den es nicht gibt. Die Registrierung im Container (Program.cs) belegt der Lauf der App.
        var dienste = typeof(IStartwertDienst).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IStartwertDienst).IsAssignableFrom(t))
            .ToList();
        Assert.True(dienste.Count >= 4, $"Gefunden: {dienste.Count} Dienste.");

        var module = dienste
            .Select(t => (string)t.GetField("Modul")!.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(SteuerungStartwerteService.Module.Order(StringComparer.Ordinal), module);
    }

    private sealed class DienstAttrappe(string modul) : IStartwertDienst
    {
        public string StartModul => modul;
        public Task<object> StandFesthaltenAsync(CancellationToken ct) => Task.FromResult<object>(modul);
        public Task<(int Geschrieben, bool Alles)> StartwerteSchreibenAsync(object stand, IReadOnlySet<string> neuAngelegt, CancellationToken ct)
            => Task.FromResult((0, true));
    }

    [Fact]
    public void EinFehlenderDienstScheitertLautStattStillZuUebergehen()
    {
        // Eine vergessene Registrierung hieße: Anlegen ohne Startwerte, ohne jede Meldung — der alte 4-°C-Fehler.
        var alle = SteuerungStartwerteService.Module.ToDictionary(m => m, m => (IStartwertDienst)new DienstAttrappe(m), StringComparer.OrdinalIgnoreCase);
        Assert.True(alle.Count >= 4);

        foreach (var modul in SteuerungStartwerteService.Module)
        {
            Assert.Same(alle[modul], SteuerungStartwerteService.Waehlen(modul, alle));

            var ohne = alle.Where(p => !string.Equals(p.Key, modul, StringComparison.OrdinalIgnoreCase)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            Assert.Throws<InvalidOperationException>(() => SteuerungStartwerteService.Waehlen(modul, ohne));
        }
    }

    [Theory]
    [InlineData("licht")]
    [InlineData("entfeuchter-zusatz")]
    [InlineData("gibt-es-nicht")]
    public void EineSteuerungOhneWegBekommtKeinenDienstUndKeinenFehler(string modul)
        => Assert.Null(SteuerungStartwerteService.Waehlen(modul, new Dictionary<string, IStartwertDienst>()));

    [Fact]
    public void NurFilterBehaeltNurDieGenanntenHelfer()
    {
        IReadOnlyList<(string Entity, double Wert)> liste = [("a", 1), ("b", 2), ("c", 3)];

        Assert.Equal(3, Startwerte.Nur(liste, null).Count);
        Assert.Equal(["b"], Startwerte.Nur(liste, new HashSet<string> { "b" }).Select(z => z.Entity).ToList());
        Assert.Empty(Startwerte.Nur(liste, new HashSet<string>()));
        Assert.Empty(Startwerte.Nur(liste, new HashSet<string> { "x" }));
    }

    // ------------------------------------------------------------------ Grundmenge je Steuerung

    private static IReadOnlyList<string> KatalogHelfer(string modul)
        => SteuerungBauteile.FuerModul(modul)
            .Where(b => b.Art is BauteilArt.Zahl or BauteilArt.Schalter)
            .Select(b => b.EntityId)
            .ToList();

    private static List<string> OhneSchreiber(string modul, IEnumerable<string> geschrieben)
    {
        var menge = geschrieben.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return KatalogHelfer(modul).Where(id => !menge.Contains(Kennungen.Id(id)) && !menge.Contains(id)).Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void ChillerJederHelferBekommtEinenStartwert()
    {
        Assert.True(KatalogHelfer("chiller").Count >= 5);
        var geschrieben = ChillerSteuerungService.Schreibliste(new ChillerEinstellungen(), []).Select(z => z.Entity).ToList();

        Assert.True(geschrieben.Count >= 5, "Die Schreibliste ist leer — die Prüfung sähe nichts.");
        Assert.Empty(OhneSchreiber("chiller", geschrieben));
    }

    [Fact]
    public void ZuluftJederHelferBekommtEinenStartwert()
    {
        Assert.True(KatalogHelfer("zuluft").Count >= 7);
        // Mit gesetzter Zelt-Mindesttemperatur — ohne sie schreibt der Dienst diesen Helfer bewusst nicht.
        var e = new ZuluftEinstellungen { ZeltTemperaturMinC = 20 };
        var geschrieben = ZuluftSteuerungService.Schreibliste(e).Select(z => z.Entity).ToList();

        Assert.True(geschrieben.Count >= 7, "Die Schreibliste ist leer — die Prüfung sähe nichts.");
        Assert.Empty(OhneSchreiber("zuluft", geschrieben));
    }

    [Fact]
    public void Co2JederHelferBekommtEinenStartwertOderHatEinenAnderenSchreiber()
    {
        Assert.True(KatalogHelfer("co2").Count >= 20);
        // Alle wahlfreien Werte gesetzt — ohne sie schreibt der Dienst den Helfer bewusst nicht (sein Helfer behält, was in HA steht).
        var e = new Co2Einstellungen { KlimaToleranzMinuten = 5, T6StufeKlima = 4 };
        var zahlen = Co2SteuerungService.Schreibliste(e, 1000, 900, 800, []).Select(z => z.Entity);
        var schalter = Co2SteuerungService.Schalterliste(e, mitAutomatik: false).Select(s => s.Entity);
        // Der Durchfluss bekommt beim NEU Anlegen einen sicheren Startwert (ZusatzStartwerte), er ist keine Einstellung.
        var alleNeu = KatalogHelfer("co2").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var zusatz = Co2SteuerungService.ZusatzStartwerte(alleNeu).Select(z => z.Entity);
        var geschrieben = zahlen.Concat(schalter).Concat(zusatz).ToList();

        Assert.True(geschrieben.Count >= 18, "Die Schreibliste ist leer — die Prüfung sähe nichts.");

        // Diese drei schreibt der Fork nicht — und ein Startwert wäre geraten (Neuling-Test 17.10.2026):
        var anderer = new[]
        {
            "input_number.co2_flasche_rest",         // Rechenstand der Flasche, springt beim Wechsel
            "input_number.co2_g_s_letzte_messung",   // Rohwert der letzten Durchfluss-Messung (Autokalibrierung)
            "input_number.co2_rh_notbremse",         // wahlfrei (Pflicht: false); nur bei gesetztem Wert oder Plan-Modus
        };
        Assert.Equal(anderer.Order(StringComparer.Ordinal), OhneSchreiber("co2", geschrieben));
    }

    // ------------------------------------------------------------------ CO₂-Durchfluss

    private const string Durchfluss = "input_number.co2_gramm_pro_sekunde";

    [Fact]
    public void DerDurchflussBekommtNurBeimNeuAnlegenSeinenStartwert()
    {
        var neu = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Durchfluss, "input_number.co2_hysterese" };
        Assert.Equal([(Durchfluss, Co2SteuerungService.Entitaeten.GrammProSekundeStart)], Co2SteuerungService.ZusatzStartwerte(neu).ToList());

        // Ein bestehender Durchfluss ist kalibriert und bleibt: er steht dann nicht unter den neu angelegten.
        Assert.Empty(Co2SteuerungService.ZusatzStartwerte(new HashSet<string> { "input_number.co2_hysterese" }));
        Assert.Empty(Co2SteuerungService.ZusatzStartwerte(new HashSet<string>()));
    }

    [Fact]
    public void DerStartwertDesDurchflussesLiegtImBereichDesHelfersUndIstVorsichtig()
    {
        var (min, max) = SteuerungBauteile.Spanne(Durchfluss);
        var start = Co2SteuerungService.Entitaeten.GrammProSekundeStart;

        Assert.InRange(start, min, max);
        // Vorsichtig heißt: nicht kleiner als ein übliches Ventil. Ein zu kleiner Durchfluss überdosiert (jeder Impuls läuft bis zur
        // Höchstdauer), ein zu großer unterdosiert nur. Das HA-Minimum (0,005) war genau der falsche Rand.
        Assert.True(start >= 0.1, $"Startwert {start} g/s ist kleiner als ein übliches Ventil.");
        Assert.True(start > min * 10);
    }

    [Fact]
    public void AlleVorlagenRechnenMitDemselbenRueckfallWieDerStartwert()
    {
        // EINE Wahrheit je Zahl: Die Vorlagen nutzen den Wert als Rückfall (float(0.26)), der Fork schreibt ihn als Startwert.
        // Laufen sie auseinander, rechnet die Automation mit einem anderen Durchfluss als der, den der Helfer beim Anlegen bekam.
        // Gesucht wird in den Vorlagen, die in Home Assistant ankommen: Katalog (Rechenwerte) und die Dosier-Automation.
        var muster = new System.Text.RegularExpressions.Regex(@"co2_gramm_pro_sekunde'\)\s*\|\s*float\(([0-9.,]+)\)");
        var texte = SteuerungBauteile.FuerModul("co2").Select(b => b.Vorlage).Where(t => t is not null).Select(t => t!).ToList();
        texte.Add(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Vorlagen", "co2", "dosierung.json")));

        var treffer = texte.SelectMany(t => muster.Matches(t).Select(m => m.Groups[1].Value)).ToList();

        // Mengenwächter: Katalog (Bedarf) und Dosier-Automation (Flaschenrest, Kalibrierung) nutzen den Rückfall.
        Assert.True(treffer.Count >= 3, $"Gefunden: {treffer.Count} Stellen — die Suche sähe nichts.");
        Assert.All(treffer, wert => Assert.Equal(Co2SteuerungService.Entitaeten.GrammProSekundeStart,
            double.Parse(wert, System.Globalization.CultureInfo.InvariantCulture)));
    }

    // ------------------------------------------------------------------ Ablauf (ohne Home Assistant)

    private static SteuerungHelferService.Ergebnis Neu(string entityId) => new(entityId, entityId, Angelegt: true, Fehler: null);
    private static SteuerungHelferService.Ergebnis Bestehend(string entityId) => new(entityId, entityId, Angelegt: false, Fehler: null);

    private static SteuerungHelferService.Bilanz Bilanz(bool erreichbar, params SteuerungHelferService.Ergebnis[] einzeln)
        => new(erreichbar, einzeln.Count(e => e.Angelegt), einzeln.Count(e => !e.Angelegt), 0, einzeln);

    private static string FeuchteEin => EntfeuchterSteuerungService.Entitaeten.FeuchteEinTag;
    private static string FeuchteAus => EntfeuchterSteuerungService.Entitaeten.FeuchteAusTag;
    private static string Hysterese => EntfeuchterSteuerungService.Entitaeten.Hysterese;

    [Fact]
    public void NurNeuAngelegteHelferBekommenEinenStartwert()
    {
        // Bestehende Helfer dürfen nie überschrieben werden — auch kein von Hand geänderter (Neuling-Test 17.10.2026).
        var ids = SteuerungStartwerteService.ZuSchreiben([Neu(FeuchteEin), Bestehend(FeuchteAus), Neu(Hysterese)]);

        Assert.Equal(new[] { FeuchteEin, Hysterese }.Order(StringComparer.OrdinalIgnoreCase), ids.Order(StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(FeuchteAus, ids, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EinNeuerHelferOhneSchreiberBekommtKeinenStartwert()
    {
        // vpd_ziel_unten schreibt der Wochenplan — der Fork darf ihn hier nicht mit etwas Eigenem belegen. Welche der neu
        // angelegten Helfer wirklich geschrieben werden, entscheidet der Dienst über seine Schreibliste.
        var neu = SteuerungStartwerteService.ZuSchreiben([Neu("input_number.vpd_ziel_unten"), Neu(FeuchteEin)]);
        var umfang = EntfeuchterSteuerungService.Schreibumfang(Soll(), neu);

        Assert.Equal([FeuchteEin], umfang.Zahlen.Select(z => z.Entity).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NichtsNeuAngelegtHeisstNichtsSchreiben()
    {
        Assert.Empty(SteuerungStartwerteService.ZuSchreiben([Bestehend(FeuchteEin), Bestehend(Hysterese)]));
        Assert.Empty(SteuerungStartwerteService.ZuSchreiben([]));
    }

    [Fact]
    public async Task DerStandWirdVorDemAnlegenFestgehaltenUndDanachNurDasNeueGeschrieben()
    {
        var reihenfolge = new List<string>();
        IReadOnlySet<string>? geschrieben = null;

        var bilanz = await SteuerungStartwerteService.AblaufAsync(
            festhalten: () => { reihenfolge.Add("festhalten"); return Task.FromResult("stand"); },
            anlegen: () => { reihenfolge.Add("anlegen"); return Task.FromResult(Bilanz(true, Neu(FeuchteEin), Bestehend(Hysterese))); },
            schreiben: (stand, ids) => { reihenfolge.Add("schreiben:" + stand); geschrieben = ids; return Task.FromResult((ids.Count, true)); });

        // Festhalten muss VOR dem Anlegen liegen: danach stünden die Mindestwerte in Home Assistant, und der Fork
        // übernähme sie beim ersten Lesen als seine eigenen Einstellungen.
        Assert.Equal(["festhalten", "anlegen", "schreiben:stand"], reihenfolge);
        Assert.Equal([FeuchteEin], geschrieben!.ToList(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, bilanz.Startwerte);
        Assert.True(bilanz.StartwerteVollstaendig);
    }

    [Fact]
    public async Task OhneAntwortVonHomeAssistantWirdNichtGeschrieben()
    {
        var geschrieben = false;
        var bilanz = await SteuerungStartwerteService.AblaufAsync(
            festhalten: () => Task.FromResult(0),
            anlegen: () => Task.FromResult(Bilanz(false)),
            schreiben: (_, _) => { geschrieben = true; return Task.FromResult((1, true)); });

        Assert.False(geschrieben);
        Assert.False(bilanz.Erreichbar);
        Assert.Equal(0, bilanz.Startwerte);
    }

    [Fact]
    public async Task WennAllesSchonDaWarWirdNichtGeschriebenUndNichtsAlsOffenGemeldet()
    {
        var geschrieben = false;
        var bilanz = await SteuerungStartwerteService.AblaufAsync(
            festhalten: () => Task.FromResult(0),
            anlegen: () => Task.FromResult(Bilanz(true, Bestehend(FeuchteEin), Bestehend(Hysterese))),
            schreiben: (_, _) => { geschrieben = true; return Task.FromResult((1, true)); });

        Assert.False(geschrieben);
        Assert.Equal(0, bilanz.Startwerte);
        Assert.True(bilanz.StartwerteVollstaendig);
    }

    [Fact]
    public async Task NimmtHomeAssistantNichtAllesAnWirdDasGemeldet()
    {
        var bilanz = await SteuerungStartwerteService.AblaufAsync(
            festhalten: () => Task.FromResult(0),
            anlegen: () => Task.FromResult(Bilanz(true, Neu(FeuchteEin), Neu(Hysterese))),
            schreiben: (_, _) => Task.FromResult((0, false)));

        Assert.Equal(0, bilanz.Startwerte);
        Assert.False(bilanz.StartwerteVollstaendig);
    }

    [Fact]
    public async Task WirftDasSchreibenBleibtDieBilanzDerAngelegtenHelferErhalten()
    {
        // Die Helfer sind angelegt. Ginge die Bilanz verloren, sähe die Oberfläche einen Fehler, ein zweiter Lauf legte
        // nichts mehr an, und die Startwerte würden nie nachgeholt.
        var bilanz = await SteuerungStartwerteService.AblaufAsync<int>(
            festhalten: () => Task.FromResult(0),
            anlegen: () => Task.FromResult(Bilanz(true, Neu(FeuchteEin), Neu(Hysterese))),
            schreiben: (_, _) => throw new HttpRequestException("Home Assistant antwortet nicht"));

        Assert.Equal(2, bilanz.Angelegt);
        Assert.Equal(0, bilanz.Startwerte);
        Assert.False(bilanz.StartwerteVollstaendig);
    }

    [Fact]
    public async Task EinAbbruchDurchDenNutzerWirdNichtVerschluckt()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => SteuerungStartwerteService.AblaufAsync<int>(
            festhalten: () => Task.FromResult(0),
            anlegen: () => Task.FromResult(Bilanz(true, Neu(FeuchteEin))),
            schreiben: (_, _) => throw new OperationCanceledException()));
    }

    // ------------------------------------------------------------------ Schreibumfang

    private static (string Entity, double Wert)[] Soll() =>
    [
        (FeuchteEin, 60), (FeuchteAus, 57), (Hysterese, 4),
    ];

    [Fact]
    public void OhneEinschraenkungWirdAllesGeschriebenUndDieRegelungGeschaltet()
    {
        var umfang = EntfeuchterSteuerungService.Schreibumfang(Soll(), nur: null);

        Assert.Equal(3, umfang.Zahlen.Count);
        Assert.True(umfang.VpdRegelung);
        Assert.True(umfang.Tagbetrieb);
        Assert.True(umfang.RegelungSchalten);
    }

    [Fact]
    public void MitEinschraenkungWirdNurDasGenannteGeschriebenUndDieRegelungBleibtUnberuehrt()
    {
        var umfang = EntfeuchterSteuerungService.Schreibumfang(Soll(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FeuchteEin });

        Assert.Equal([FeuchteEin], umfang.Zahlen.Select(z => z.Entity).ToList(), StringComparer.OrdinalIgnoreCase);
        Assert.False(umfang.VpdRegelung);
        Assert.False(umfang.Tagbetrieb);
        Assert.False(umfang.RegelungSchalten);
    }

    [Fact]
    public void EinSchalterWirdNurGeschriebenWennErGenanntIst()
    {
        var nurTagbetrieb = EntfeuchterSteuerungService.Schreibumfang(Soll(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { EntfeuchterSteuerungService.Entitaeten.Tagbetrieb });

        Assert.Empty(nurTagbetrieb.Zahlen);
        Assert.True(nurTagbetrieb.Tagbetrieb);
        Assert.False(nurTagbetrieb.VpdRegelung);
        Assert.False(nurTagbetrieb.RegelungSchalten);
    }

    [Fact]
    public void EinLeererAuftragSchreibtNichts()
    {
        var umfang = EntfeuchterSteuerungService.Schreibumfang(Soll(), new HashSet<string>());

        Assert.Empty(umfang.Zahlen);
        Assert.False(umfang.VpdRegelung || umfang.Tagbetrieb || umfang.RegelungSchalten);
    }

    // ------------------------------------------------------------------ Beschriftung

    [Fact]
    public void JederStartwertHatEinenAnzeigenamenMitEinheit()
    {
        // Die Abweichungs-Anzeige nennt den Helfer beim Namen; ohne Eintrag stünde dort die rohe Entity-ID.
        foreach (var id in EntfeuchterSteuerungService.Entitaeten.Startwerte.Where(i => i.StartsWith("input_number.", StringComparison.OrdinalIgnoreCase)))
        {
            var (name, einheit) = EntfeuchterSteuerungService.BeschriftungFuer(id);
            Assert.NotEqual(id, name);
            Assert.False(string.IsNullOrWhiteSpace(einheit), $"{id} hat keine Einheit.");
        }

        Assert.Equal(("unbekannt", string.Empty), EntfeuchterSteuerungService.BeschriftungFuer("unbekannt"));
    }
}

