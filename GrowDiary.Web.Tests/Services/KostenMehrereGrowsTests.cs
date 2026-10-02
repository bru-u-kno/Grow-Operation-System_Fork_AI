using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Kosten bei mehreren gleichzeitigen Grows — Zähler statt Grow.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (02.10.2026).</b> Bru fährt einen Grow zur Zeit; andere
/// Anwender des Forks haben mehrere Zelte und Grows parallel. Für sie bekam der
/// zweite laufende Grow nie Stromkosten (jeder Stand trug die Id des ältesten),
/// der Import übersprang seine Tage oder löschte fremde Stände, und die
/// Durchgänge-Tabelle widersprach der Summe darüber.</para>
///
/// <para>Alle Stände stehen in <b>Ortszeit</b> um Mitternacht: die Aufteilung
/// rechnet in Ortstagen, und so gilt jede Erwartung in jeder Zeitzone.</para>
/// </remarks>
public sealed class KostenMehrereGrowsTests
{
    private const string Gemeinsam = "sensor.steckdose_gesamt_energy";
    private const string Zelt2Zaehler = "sensor.steckdose_zelt2_energy";

    private static readonly DateTime Anfang = new(2026, 6, 1);

    /// <summary>Mittags an Tag 31 — Tag 1 bis 30 sind vollständig belegt, Tag 31 läuft noch.</summary>
    private static DateTime JetztUtc => Lokal(Anfang.AddDays(30)).AddHours(12);

    private static DateTime Lokal(DateTime ortszeit) => DateTime.SpecifyKind(ortszeit, DateTimeKind.Local).ToUniversalTime();

    private static GrowRun Grow(int id, int tentId, int startTag, int? endTag, string name) => new()
    {
        Id = id,
        TentId = tentId,
        Name = name,
        Status = endTag is null ? GrowStatus.Running : GrowStatus.Completed,
        StartDate = Anfang.AddDays(startTag - 1),
        EndDate = endTag is { } e ? Anfang.AddDays(e - 1) : null,
        PlantCount = 4,
    };

    /// <summary>Tägliche Stände um Mitternacht Ortszeit: Tag 1 bis Tag 31 (Beginn), <paramref name="kwhJeTag"/> je Tag.</summary>
    private static List<Zaehlerstand> Reihe(string? zaehler, double kwhJeTag, int idAb, int? growIdAmStand = null, double start = 1000)
        => Enumerable.Range(0, 31)
            .Select(i => new Zaehlerstand
            {
                Id = idAb + i,
                ZeitpunktUtc = Lokal(Anfang.AddDays(i)),
                Kwh = start + i * kwhJeTag,
                Anlass = ZaehlerAnlass.Tag,
                GrowId = growIdAmStand,
                ZaehlerEntityId = zaehler,
            })
            .ToList();

    private static KostenSeite Seite(GrowRun gezeigt, IReadOnlyList<GrowRun> alle, StromQuelle quelle, IReadOnlyList<Zaehlerstand> staende,
        IReadOnlyList<Verbrauchsartikel>? artikel = null, IReadOnlyList<Nachfuellung>? fuellungen = null, IReadOnlyList<Verbrauch>? verbraeuche = null)
        => KostenSeiteService.Berechnen(gezeigt, alle, quelle, 30, null, staende,
            artikel ?? [], fuellungen ?? [], [], JetztUtc, null, verbraeuche ?? []);

    // ------------------------------------------------------------- Strom

    /// <summary>
    /// Zwei überlappende Grows an einem gemeinsamen Zähler: an den gemeinsamen
    /// Tagen bekommt jeder die Hälfte, und zusammen ergibt es den ganzen Zähler.
    /// </summary>
    /// <remarks>
    /// Die Stände tragen die Id von Grow 1 — so hat der Worker sie bisher
    /// geschrieben (der älteste laufende). Genau damit zeigte Grow 2 dauerhaft
    /// „Noch kein Zählerstand".
    /// </remarks>
    [Fact]
    public void ZweiUeberlappendeGrowsTeilenDenGemeinsamenZaehler()
    {
        var a = Grow(1, 1, startTag: 1, endTag: null, "Grow A");
        var b = Grow(2, 2, startTag: 11, endTag: null, "Grow B");
        var quelle = new StromQuelle { ZaehlerEntityId = Gemeinsam };
        // Altstände ohne Zählerangabe: sie gehören zum gemeinsamen Zähler.
        var staende = Reihe(null, 10, 1, growIdAmStand: 1);

        var seiteB = Seite(b, [a, b], quelle, staende);
        var seiteA = Seite(a, [a, b], quelle, staende);

        // Tag 11 bis 30 geteilt: 20 Tage × 10 kWh ÷ 2.
        Assert.Equal(100, seiteB.Strom.KwhSeitStart!.Value, precision: 6);
        Assert.Equal(30.00, seiteB.Strom.EurSeitStart!.Value, precision: 2);
        // Tag 1 bis 10 allein (100), Tag 11 bis 30 halb (100).
        Assert.Equal(200, seiteA.Strom.KwhSeitStart!.Value, precision: 6);
        // Nichts verloren, nichts doppelt: 30 Tage × 10 kWh.
        Assert.Equal(300, seiteA.Strom.KwhSeitStart!.Value + seiteB.Strom.KwhSeitStart!.Value, precision: 6);

        Assert.Equal(20, seiteB.Strom.GeteiltTage);
        var mit = Assert.Single(seiteB.Strom.GeteiltMit!);
        Assert.Equal(1, mit.GrowId);
        Assert.Equal(20, mit.Tage);
        Assert.Equal("Zähler geteilt mit „Grow A“ an 20 Tagen — an diesen Tagen ist sein Verbrauch zu gleichen Teilen auf die Grows verteilt, die dort liefen.",
            seiteB.Strom.TeilungHinweis);
        Assert.False(seiteB.Strom.EigenerZaehler);

        // Die Durchgänge-Tabelle rechnet dasselbe wie die Summe darüber.
        Assert.Equal(seiteB.Strom.EurSeitStart, seiteA.Durchgaenge.Single(d => d.GrowId == 2).StromEur);
        Assert.Equal(seiteA.Strom.EurSeitStart, seiteB.Durchgaenge.Single(d => d.GrowId == 1).StromEur);
        Assert.Equal(20, seiteA.Durchgaenge.Single(d => d.GrowId == 1).StromGeteiltTage);
    }

    /// <summary>Zwei Zelte mit je eigenem Zähler: nichts wird geteilt, jeder trägt seinen Zähler.</summary>
    [Fact]
    public void ZweiZelteMitEigenenZaehlernTeilenNichts()
    {
        var a = Grow(1, 1, startTag: 1, endTag: null, "Grow A");
        var b = Grow(2, 2, startTag: 11, endTag: null, "Grow B");
        var quelle = new StromQuelle
        {
            ZaehlerEntityId = Gemeinsam,
            Zelte = [new ZeltZaehler { TentId = 2, ZaehlerEntityId = Zelt2Zaehler }],
        };
        var staende = Reihe(Gemeinsam, 10, 1).Concat(Reihe(Zelt2Zaehler, 4, 100, start: 50)).ToList();

        var seiteA = Seite(a, [a, b], quelle, staende);
        var seiteB = Seite(b, [a, b], quelle, staende);

        Assert.Equal(300, seiteA.Strom.KwhSeitStart!.Value, precision: 6); // ganzer gemeinsamer Zähler
        Assert.Equal(80, seiteB.Strom.KwhSeitStart!.Value, precision: 6);  // Tag 11–30 × 4 kWh am eigenen
        Assert.Equal(0, seiteA.Strom.GeteiltTage);
        Assert.Null(seiteB.Strom.TeilungHinweis);
        Assert.True(seiteB.Strom.EigenerZaehler);
        Assert.False(seiteA.Strom.EigenerZaehler);
        Assert.Equal(Zelt2Zaehler, seiteB.Strom.ZaehlerEntityId);
        Assert.Equal(Gemeinsam, seiteA.Strom.ZaehlerEntityId);
        // Der eigene Zähler beginnt am Start von B, nicht am Anfang der Reihe.
        Assert.Equal(50 + 10 * 4, seiteB.Strom.ZaehlerStart!.Value, precision: 6);
    }

    /// <summary>
    /// Ein Grow, ein Zähler: dieselbe Summe wie die Differenz der Stände in
    /// seiner Laufzeit — das bisherige Ergebnis.
    /// </summary>
    [Fact]
    public void EinGrowRechnetWieBisher()
    {
        var a = Grow(1, 1, startTag: 1, endTag: null, "Grow A");
        var quelle = new StromQuelle { ZaehlerEntityId = Gemeinsam };
        var staende = Reihe(null, 10, 1, growIdAmStand: 1);

        var seite = Seite(a, [a], quelle, staende);

        Assert.Equal(KostenSeiteService.KwhZwischen(staende, 0, staende.Count - 1), seite.Strom.KwhSeitStart!.Value, precision: 6);
        Assert.Equal(300, seite.Strom.KwhSeitStart!.Value, precision: 6);
        Assert.Equal(10, seite.Strom.KwhProTag!.Value, precision: 6);
        Assert.Equal(0, seite.Strom.GeteiltTage);
        Assert.Null(seite.Strom.TeilungHinweis);
        Assert.Equal("Gemessen am kWh-Zähler, Preis aus den Kosten-Einstellungen.", seite.Strom.Hinweis);
    }

    /// <summary>
    /// Nacheinander statt gleichzeitig: der Vorgänger endet an Tag 10, der
    /// Nachfolger beginnt an Tag 11. Kein Tag ist geteilt, keiner fehlt.
    /// </summary>
    [Fact]
    public void NacheinanderLaufendeGrowsTeilenKeinenTag()
    {
        var a = Grow(1, 1, startTag: 1, endTag: 10, "Grow A");
        var b = Grow(2, 1, startTag: 11, endTag: null, "Grow B");
        var quelle = new StromQuelle { ZaehlerEntityId = Gemeinsam };
        var staende = Reihe(Gemeinsam, 10, 1);

        var seite = Seite(b, [a, b], quelle, staende);

        Assert.Equal(200, seite.Strom.KwhSeitStart!.Value, precision: 6);
        Assert.Equal(100, seite.Durchgaenge.Single(d => d.GrowId == 1).StromEur!.Value / 0.30, precision: 6);
        Assert.Equal(0, seite.Strom.GeteiltTage);
    }

    /// <summary>Ein Grow in Planung zieht keinen Strom — und nimmt dem laufenden nichts weg.</summary>
    [Fact]
    public void EinGeplanterGrowBekommtKeinenAnteil()
    {
        var a = Grow(1, 1, startTag: 1, endTag: null, "Grow A");
        var geplant = Grow(2, 1, startTag: 5, endTag: null, "Geplant");
        geplant.Status = GrowStatus.Planning;
        var quelle = new StromQuelle { ZaehlerEntityId = Gemeinsam };

        var seite = Seite(a, [a, geplant], quelle, Reihe(Gemeinsam, 10, 1));

        Assert.Equal(300, seite.Strom.KwhSeitStart!.Value, precision: 6);
        Assert.Null(seite.Durchgaenge.Single(d => d.GrowId == 2).StromEur);
    }

    /// <summary>Eine Lücke über drei Tage verteilt sich gleichmäßig auf diese Tage.</summary>
    [Fact]
    public void EineLueckeVerteiltSichNachDerZeit()
    {
        var stuecke = StromAufteilung.NachTagen(Lokal(new DateTime(2026, 6, 1)), Lokal(new DateTime(2026, 6, 4))).ToList();

        Assert.Equal(3, stuecke.Count);
        Assert.Equal(new DateTime(2026, 6, 1), stuecke[0].Tag);
        Assert.Equal(new DateTime(2026, 6, 3), stuecke[2].Tag);
        Assert.All(stuecke, s => Assert.Equal(1.0 / 3, s.Anteil, precision: 9));
    }

    // ------------------------------------------------------------- Artikel

    /// <summary>
    /// Ein 50-€-Kanister, auf den Grow gebucht nach Verbrauch: 10 € verbraucht.
    /// Summe, Artikelzeile und Durchgänge-Tabelle sagen alle 10 €.
    /// </summary>
    [Fact]
    public void ArtikelkostenSindInSummeUndDurchgaengenGleich()
    {
        var a = Grow(1, 1, startTag: 1, endTag: null, "Grow A");
        var kanister = new Verbrauchsartikel { Id = 1, Name = "Purolyt", Einheit = "ml", AufGrowBuchen = true };
        var fuellung = new Nachfuellung { Id = 1, ArtikelId = 1, Menge = 10000, KostenEur = 50, GrowId = 1, ZeitpunktUtc = Lokal(Anfang) };
        var verbrauch = new Verbrauch { Id = 1, ArtikelId = 1, GrowId = 1, Menge = 2000, ZeitpunktUtc = Lokal(Anfang.AddDays(3)) };

        var seite = Seite(a, [a], new StromQuelle(), [], [kanister], [fuellung], [verbrauch]);

        Assert.Equal(10, seite.Summe.ArtikelEur, precision: 6);
        Assert.Equal(10, seite.Artikel.Single().SummeEurImGrow, precision: 6);
        Assert.Equal(10, seite.Durchgaenge.Single().ArtikelEur, precision: 6);
        Assert.Equal(seite.Summe.GesamtEur, seite.Durchgaenge.Single().GesamtEur!.Value, precision: 6);
    }

    // ------------------------------------------------------------- Import

    private static ZaehlerstandImportService.Tageswert Endstand(int tag, double kwh)
        => new(Lokal(Anfang.AddDays(tag)), kwh); // Ende von Tag <tag> = Mitternacht vor Tag <tag + 1>

    /// <summary>
    /// Der zweite Grow bekommt seine Tage aus der Statistik, obwohl der Worker
    /// davor schon Stände ohne Grow geschrieben hat.
    /// </summary>
    /// <remarks>
    /// Vorher galt „nur VOR dem ersten vorhandenen Stand nachtragen", und dieser
    /// erste Stand durfte jeder ohne Grow sein, aus jeder Zeit. Beim zweiten Grow
    /// lag er Wochen zurück — jeder Tageswert wurde übersprungen.
    /// </remarks>
    [Fact]
    public void DerImportFuelltDieTageDesZweitenGrows()
    {
        var a = Grow(1, 1, startTag: 1, endTag: 5, "Grow A");
        var b = Grow(2, 1, startTag: 11, endTag: 20, "Grow B");
        var quelle = new StromQuelle { ZaehlerEntityId = Gemeinsam };
        // Tag 1 bis 8: Worker-Stände, die ersten mit Grow A, danach ohne Grow.
        var vorhanden = Reihe(Gemeinsam, 10, 1).Take(8).ToList();
        vorhanden.ForEach(s => s.GrowId = s.ZeitpunktUtc < Lokal(Anfang.AddDays(5)) ? 1 : null);
        // HA liefert Tag 10 (Vortag des Starts) bis Tag 20.
        var werte = Enumerable.Range(10, 11).Select(t => Endstand(t, 1000 + t * 10)).ToList();

        var plan = ZaehlerstandImportService.Planen(b, Gemeinsam, quelle, vorhanden, werte, neuAufbauen: false, JetztUtc);

        // Tag 11 bis 20 brauchen elf Stände: Beginn von Tag 11 bis Ende von Tag 20.
        Assert.Equal(11, plan.Neu.Count);
        Assert.Equal(0, plan.Uebersprungen);
        Assert.Equal(0, plan.Unstimmig);
        Assert.All(plan.Neu, s => Assert.Equal(Gemeinsam, s.ZaehlerEntityId));
        Assert.Equal(Lokal(Anfang.AddDays(10)), plan.Neu[0].ZeitpunktUtc);

        // Und die Seite rechnet daraus den Strom von B: zehn Tage × 10 kWh.
        var strom = KostenSeiteService.StromBerechnen(b, [a, b], quelle, 30, null, vorhanden.Concat(plan.Neu).ToList(), JetztUtc);
        Assert.Equal(100, strom.KwhSeitStart!.Value, precision: 6);
    }

    /// <summary>
    /// Der Neuaufbau verwirft nur die Stände dieses Zählers in der Laufzeit
    /// dieses Grows — nicht den Endstand des Vorgängers, nicht den anderen Zähler.
    /// </summary>
    [Fact]
    public void DerNeuaufbauLoeschtNurDenEigenenZaehlerImEigenenZeitraum()
    {
        var a = Grow(1, 1, startTag: 1, endTag: 10, "Grow A");
        var b = Grow(2, 1, startTag: 11, endTag: 20, "Grow B");
        var quelle = new StromQuelle
        {
            ZaehlerEntityId = Gemeinsam,
            Zelte = [new ZeltZaehler { TentId = 2, ZaehlerEntityId = Zelt2Zaehler }],
        };
        var gemeinsam = Reihe(Gemeinsam, 10, 1);
        var anderer = Reihe(Zelt2Zaehler, 4, 100);
        var werte = Enumerable.Range(10, 11).Select(t => Endstand(t, 1000 + t * 10)).ToList();

        var plan = ZaehlerstandImportService.Planen(b, Gemeinsam, quelle, gemeinsam.Concat(anderer).ToList(), werte, neuAufbauen: true, JetztUtc);

        var geloescht = plan.Loeschen.ToHashSet();
        // Beginn von Tag 11 bis Beginn von Tag 21: elf Stände des gemeinsamen Zählers.
        Assert.Equal(11, geloescht.Count);
        Assert.DoesNotContain(geloescht, id => anderer.Any(s => s.Id == id));
        // Tag 10 (Beginn) gehört nur noch A — er bleibt.
        Assert.DoesNotContain(gemeinsam.Single(s => s.ZeitpunktUtc == Lokal(Anfang.AddDays(9))).Id, geloescht);
        // Und alles Gelöschte kommt aus der Statistik zurück.
        Assert.Equal(11, plan.Neu.Count);
    }

    /// <summary>
    /// Ein Tageswert, der neben einem echten Stand rückwärts liefe, bleibt
    /// draußen — ein Rücksprung zählt sonst als Zählerwechsel mit vollem Stand.
    /// </summary>
    [Fact]
    public void EinRueckwaertsSpringenderWertWirdNichtEingefuegt()
    {
        var b = Grow(2, 1, startTag: 11, endTag: 20, "Grow B");
        var quelle = new StromQuelle { ZaehlerEntityId = Gemeinsam };
        // Vorhanden: Beginn Tag 11 mit 1100 und Beginn Tag 14 mit 1130.
        var vorhanden = new List<Zaehlerstand>
        {
            new() { Id = 1, ZeitpunktUtc = Lokal(Anfang.AddDays(10)), Kwh = 1100, ZaehlerEntityId = Gemeinsam },
            new() { Id = 2, ZeitpunktUtc = Lokal(Anfang.AddDays(13)), Kwh = 1130, ZaehlerEntityId = Gemeinsam },
        };
        // Ende Tag 11 passt (1110), Ende Tag 12 springt über den späteren Stand (1500).
        var werte = new[] { Endstand(11, 1110), Endstand(12, 1500) };

        var plan = ZaehlerstandImportService.Planen(b, Gemeinsam, quelle, vorhanden, werte, neuAufbauen: false, JetztUtc);

        var neu = Assert.Single(plan.Neu);
        Assert.Equal(1110, neu.Kwh);
        Assert.Equal(1, plan.Unstimmig);
    }

    /// <summary>Der laufende Tag ist nicht zu Ende — sein „Endstand" läge in der Zukunft.</summary>
    [Fact]
    public void DerLaufendeTagWirdNichtImportiert()
    {
        var b = Grow(2, 1, startTag: 11, endTag: null, "Grow B");
        var quelle = new StromQuelle { ZaehlerEntityId = Gemeinsam };
        // JetztUtc ist mittags an Tag 31 (Ortszeit); das Ende von Tag 31 liegt danach.
        var werte = new[] { Endstand(30, 1300), Endstand(31, 1310) };

        var plan = ZaehlerstandImportService.Planen(b, Gemeinsam, quelle, [], werte, neuAufbauen: false, JetztUtc);

        Assert.Equal(Lokal(Anfang.AddDays(30)), Assert.Single(plan.Neu).ZeitpunktUtc);
    }

    // ------------------------------------------------------------- Einstellung

    /// <summary>
    /// Speichern ohne Zelt-Liste lässt die Zelt-Zähler stehen — ältere Aufrufer
    /// kennen das Feld nicht. Eine leere Liste entfernt sie.
    /// </summary>
    [Fact]
    public void ZeltZaehlerBleibenBeimSpeichernOhneListe()
    {
        var wurzel = Path.Combine(Path.GetTempPath(), "grow-os-strom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(wurzel);
        try
        {
            var pfade = new AppPaths(wurzel);
            TestDatabase.Initialize(pfade);
            var settings = new AppSettingsRepository(pfade);

            KostenSeiteService.StromQuelleSchreiben(settings, new StromQuelle
            {
                ZaehlerEntityId = Gemeinsam,
                Zelte = [new ZeltZaehler { TentId = 2, ZaehlerEntityId = $" {Zelt2Zaehler} " }, new ZeltZaehler { TentId = 3, ZaehlerEntityId = "  " }],
            });
            var gelesen = KostenSeiteService.StromQuelleLesen(settings);
            var zelt = Assert.Single(gelesen.Zelte!);
            Assert.Equal(Zelt2Zaehler, zelt.ZaehlerEntityId);
            Assert.Equal(Zelt2Zaehler, gelesen.ZaehlerFuerZelt(2));
            Assert.Equal(Gemeinsam, gelesen.ZaehlerFuerZelt(3));
            Assert.Equal([Gemeinsam, Zelt2Zaehler], gelesen.AlleZaehler());

            KostenSeiteService.StromQuelleSchreiben(settings, new StromQuelle { ZaehlerEntityId = "sensor.anderer" });
            Assert.Equal(Zelt2Zaehler, KostenSeiteService.StromQuelleLesen(settings).ZaehlerFuerZelt(2));

            KostenSeiteService.StromQuelleSchreiben(settings, new StromQuelle { ZaehlerEntityId = Gemeinsam, Zelte = [] });
            Assert.Equal(Gemeinsam, KostenSeiteService.StromQuelleLesen(settings).ZaehlerFuerZelt(2));
        }
        finally
        {
            try { Directory.Delete(wurzel, recursive: true); } catch (IOException) { }
        }
    }
}
