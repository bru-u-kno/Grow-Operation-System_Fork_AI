using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

// Fork AI (forkai.6): Die Kosten-Seite — Strom aus Zählerständen + Verbrauchsartikel.

public sealed record KostenGrowInfo(int Id, string Name, DateTime StartDate, DateTime? EndDate, int Tag, string Phase, int? Pflanzen);

public sealed record KostenSumme(
    double GesamtEur,
    double? StromEur,
    double ArtikelEur,
    double AnschaffungenEur,
    double? ProTagEur,
    double? ProPflanzeEur,
    double? PrognoseErnteEur,
    string? PrognoseHinweis);

public sealed record KostenPhase(string Phase, string Label, DateTime VonUtc, DateTime BisUtc, double Tage, double Kwh, double? Eur, bool Laeuft);

public sealed record KostenStrom(
    bool Eingerichtet,
    string? ZaehlerEntityId,
    string? LeistungEntityId,
    double? PreisCentProKwh,
    double? LeistungW,
    double? KwhSeitStart,
    double? EurSeitStart,
    double? KwhProTag,
    double? EurProTag,
    double? ZaehlerStart,
    double? ZaehlerAktuell,
    DateTime? ErsterStandUtc,
    DateTime? LetzterStandUtc,
    string Hinweis,
    IReadOnlyList<KostenPhase> Phasen,
    /// <summary>Der Grow misst am eigenen Zähler seines Zelts, nicht am gemeinsamen.</summary>
    bool EigenerZaehler = false,
    /// <summary>An wie vielen Tagen (mit Zählerdaten) der Zähler mit anderen Grows geteilt war.</summary>
    int GeteiltTage = 0,
    /// <summary>Mit wem geteilt wurde, je Grow die Zahl der gemeinsamen Tage.</summary>
    IReadOnlyList<KostenTeilung>? GeteiltMit = null,
    /// <summary>Der Satz dazu für die Seite — null, wenn nie geteilt.</summary>
    string? TeilungHinweis = null);

/// <summary>Ein Grow, der an einigen Tagen am selben Zähler lief.</summary>
public sealed record KostenTeilung(int GrowId, string Name, int Tage);

/// <summary>Ein Zelt für die Zähler-Einstellung.</summary>
public sealed record KostenZelt(int Id, string Name);

public sealed record KostenFuellungAktuell(
    int Id,
    DateTime ZeitpunktUtc,
    double Menge,
    double? KostenEur,
    int Tag,
    double? PrognoseTage,
    DateTime? PrognoseLeerAmUtc,
    double? FuellstandProzent,
    double? EurProTag,
    /// <summary>Was seit dieser Füllung gebucht wurde (Steuerung, Journal) — in der Einheit des Artikels.</summary>
    double VerbrauchtMenge = 0,
    /// <summary><c>gemessen</c>, wenn der Füllstand aus Buchungen kommt, sonst <c>geschaetzt</c> (aus früheren Laufzeiten).</summary>
    string FuellstandQuelle = "geschaetzt",
    /// <summary>
    /// Woher das Leer-Datum stammt: <c>gemessen</c> (Hochrechnung des gebuchten
    /// Verbrauchs), <c>geschaetzt</c> (frühere Laufzeiten) oder <c>null</c>
    /// (keine Prognose). Eigenes Feld, weil beide auseinanderfallen: ein
    /// gemessener Füllstand mit zu wenig Verbrauch für eine Hochrechnung bekommt
    /// sein Datum aus den früheren Laufzeiten.
    /// </summary>
    string? PrognoseQuelle = null);

public sealed record KostenArtikel(
    int Id,
    string Name,
    string? Hersteller,
    string? Produkt,
    double? PreisEur,
    string Einheit,
    double? Gebinde,
    int? TentId,
    string? Notiz,
    bool Aktiv,
    /// <summary>
    /// forkai.101: Muss mit heraus, sonst zeigt das Artikel-Formular immer
    /// „beim Kauf". Wer dann irgendetwas anderes am Artikel speichert, stellt
    /// das Buchungsziel unbemerkt zurueck — der Wert stand nie im Formular.
    /// </summary>
    bool AufGrowBuchen,
    KostenFuellungAktuell? Aktuell,
    int AnzahlFuellungen,
    double? MittlereLaufzeitTage,
    double SummeEurImGrow);

public sealed record KostenNachfuellung(
    int Id,
    int ArtikelId,
    string ArtikelName,
    string Einheit,
    DateTime ZeitpunktUtc,
    double Menge,
    double? KostenEur,
    DateTime? LeerAmUtc,
    double? LaufzeitTage,
    double? EurProTag,
    int? GrowId,
    string? GrowName,
    string? Notiz);

public sealed record KostenAnschaffung(
    int Id, string Name, string? Hersteller, string? Produkt, DateTime DatumUtc, int Stueck, double EinzelpreisEur, double GesamtEur,
    int? GrowId, string? GrowName, string? Notiz, int? HardwareItemId);

public sealed record KostenDurchgang(
    int GrowId,
    string Name,
    DateTime StartDate,
    DateTime? EndDate,
    bool Laeuft,
    double? StromEur,
    double ArtikelEur,
    double AnschaffungenEur,
    double? GesamtEur,
    /// <summary>Tage, an denen der Strom mit einem anderen Grow geteilt war.</summary>
    int StromGeteiltTage = 0);

public sealed record KostenSeite(
    KostenGrowInfo? Grow,
    KostenSumme Summe,
    KostenStrom Strom,
    IReadOnlyList<KostenArtikel> Artikel,
    IReadOnlyList<KostenNachfuellung> Nachfuellungen,
    IReadOnlyList<KostenAnschaffung> Anschaffungen,
    IReadOnlyList<KostenDurchgang> Durchgaenge,
    IReadOnlyList<string> Einheiten,
    IReadOnlyList<string> Hersteller,
    IReadOnlyList<KostenProdukt> Produkte,
    /// <summary>Die eingestellten Zähler — die Quelle fürs Formular, nicht der Zähler des gezeigten Grows.</summary>
    StromQuelle? Quelle = null,
    /// <summary>Die aktiven Zelte, falls ein Zelt einen eigenen Zähler bekommen soll.</summary>
    IReadOnlyList<KostenZelt>? Zelte = null);

/// <summary>Ein bekanntes Produkt mit seinem Hersteller — für den Vorschlag im Formular.</summary>
public sealed record KostenProdukt(string? Hersteller, string Produkt);

/// <summary>
/// Rechnet die Kosten-Seite zusammen. Die Rechnung selbst ist statisch und
/// ohne Datenbank, damit sie prüfbar ist (KostenSeiteServiceTests).
/// </summary>
/// <remarks>
/// <para><b>Strom kommt aus Zählerständen, nicht aus Watt × Stunden.</b> Das
/// Original schätzt (<see cref="GrowCostService"/>); hier steht die
/// DECT-Steckdose vor dem ganzen Zelt und zählt wirklich. Der Worker hält den
/// Stand täglich, bei Grow-Start und bei Phasenwechsel fest; die Seite rechnet
/// nur Differenzen. Ein Zählersprung nach unten (Reset) wird als Neustart bei
/// null gelesen.</para>
///
/// <para><b>Prognosen sind zeitbasiert.</b> Der CO₂-Sensor misst Konzentration,
/// nicht Verbrauch. Eine Flasche „ist zu x % voll", weil die letzten Füllungen
/// im Mittel n Tage hielten — nicht, weil jemand gewogen hätte. Genau das sagt
/// die Oberfläche auch.</para>
/// </remarks>
public sealed class KostenSeiteService
{
    public const string StromQuelleKey = "fork-kosten-strom-quelle";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly KostenRepository _kosten;
    private readonly GrowRepository _grows;
    private readonly AppSettingsRepository _settings;
    private readonly GrowCostService _original;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly HardwareRepository _hardware;

    public KostenSeiteService(
        KostenRepository kosten,
        GrowRepository grows,
        AppSettingsRepository settings,
        GrowCostService original,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        HardwareRepository hardware)
    {
        _hardware = hardware;
        _kosten = kosten;
        _grows = grows;
        _settings = settings;
        _original = original;
        _ha = ha;
        _haSettings = haSettings;
    }

    public StromQuelle StromQuelle
    {
        get => StromQuelleLesen(_settings);
        set => StromQuelleSchreiben(_settings, value);
    }

    /// <summary>Die Zähler-Einstellung lesen — auch für den Demobestand, darum statisch.</summary>
    public static StromQuelle StromQuelleLesen(AppSettingsRepository settings)
    {
        var raw = settings.GetValue(StromQuelleKey);
        if (string.IsNullOrWhiteSpace(raw)) return new StromQuelle();
        try { return JsonSerializer.Deserialize<StromQuelle>(raw, Json) ?? new StromQuelle(); }
        catch (JsonException) { return new StromQuelle(); }
    }

    /// <summary>
    /// Die Zähler-Einstellung schreiben. <see cref="StromQuelle.Zelte"/> = null
    /// lässt die Zelt-Zähler stehen, eine leere Liste entfernt sie.
    /// </summary>
    public static void StromQuelleSchreiben(AppSettingsRepository settings, StromQuelle value)
    {
        var zelte = value.Zelte is null
            ? StromQuelleLesen(settings).Zelte ?? []
            : value.Zelte
                .Where(z => z.TentId > 0 && !string.IsNullOrWhiteSpace(z.ZaehlerEntityId))
                .GroupBy(z => z.TentId)
                .Select(g => new ZeltZaehler { TentId = g.Key, ZaehlerEntityId = g.Last().ZaehlerEntityId!.Trim() })
                .OrderBy(z => z.TentId)
                .ToList();

        settings.SetValue(StromQuelleKey, JsonSerializer.Serialize(new StromQuelle
        {
            ZaehlerEntityId = Leer(value.ZaehlerEntityId),
            LeistungEntityId = Leer(value.LeistungEntityId),
            Zelte = zelte,
        }, Json));
    }

    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Jeden eingerichteten Zähler lesen und festhalten. Leer, wenn keine Quelle oder kein Wert.</summary>
    public async Task<IReadOnlyList<Zaehlerstand>> AlleZaehlerFesthaltenAsync(ZaehlerAnlass anlass, CancellationToken ct = default)
    {
        var gehalten = new List<Zaehlerstand>();
        foreach (var zaehler in StromQuelle.AlleZaehler())
        {
            if (await ZaehlerstandFesthaltenAsync(zaehler, anlass, ct) is { } stand) gehalten.Add(stand);
        }
        return gehalten;
    }

    /// <summary>Einen Zähler in HA lesen und festhalten. Null, wenn HA keinen Zahlenwert liefert.</summary>
    public async Task<Zaehlerstand?> ZaehlerstandFesthaltenAsync(string zaehler, ZaehlerAnlass anlass, CancellationToken ct = default)
    {
        var state = await _ha.GetEntityStateAsync(_haSettings.GetEffectiveHomeAssistantSettings(), zaehler, ct);
        if (state?.NumericValue is not { } kwh) return null;

        // GrowId und Phase sind eine Notiz für die Tabelle der Stände; gerechnet
        // wird über den Zähler (StromAufteilung).
        var (growId, phase) = LaufenderGrow(DateTime.Today, zaehler);
        var stand = new Zaehlerstand
        {
            ZeitpunktUtc = DateTime.UtcNow, Kwh = kwh, Anlass = anlass, GrowId = growId, Phase = phase, ZaehlerEntityId = zaehler,
        };
        stand.Id = _kosten.CreateZaehlerstand(stand);
        return stand;
    }

    /// <summary>
    /// Der älteste laufende Grow — mit <paramref name="zaehler"/> nur unter denen,
    /// die an diesem Zähler messen.
    /// </summary>
    /// <remarks>
    /// Ohne Zähler ist das die Voreinstellung für neue Nachfüllungen und
    /// Anschaffungen, wenn das Formular keinen Grow nennt.
    /// </remarks>
    public (int? GrowId, string? Phase) LaufenderGrow(DateTime heute, string? zaehler = null)
    {
        var quelle = zaehler is null ? null : StromQuelle;
        var grow = _grows.GetActiveGrows()
            .Where(g => g.Status == GrowStatus.Running)
            .Where(g => quelle is null || string.Equals(quelle.ZaehlerFuerZelt(g.TentId), zaehler, StringComparison.OrdinalIgnoreCase))
            .OrderBy(g => g.StartDate)
            .FirstOrDefault();
        return grow is null ? (null, null) : (grow.Id, GrowStageResolver.Resolve(grow, heute).ToString());
    }

    public async Task<KostenSeite> FuerGrowAsync(int? growId, CancellationToken ct = default)
    {
        var alle = _grows.GetAllGrows();
        var grow = growId is { } id ? alle.FirstOrDefault(g => g.Id == id) : null;
        grow ??= alle.Where(g => g.Status == GrowStatus.Running).OrderBy(g => g.StartDate).FirstOrDefault();

        var quelle = StromQuelle;
        var preis = _original.StrompreisCentProKwh;
        double? leistung = null;
        if (!string.IsNullOrWhiteSpace(quelle.LeistungEntityId))
        {
            var state = await _ha.GetEntityStateAsync(_haSettings.GetEffectiveHomeAssistantSettings(), quelle.LeistungEntityId, ct);
            leistung = state?.NumericValue;
        }

        var seite = Berechnen(
            grow, alle, quelle, preis, leistung,
            _kosten.GetZaehlerstaende(),
            _kosten.GetArtikel(),
            _kosten.GetNachfuellungen(),
            _kosten.GetAnschaffungen(),
            DateTime.UtcNow,
            _hardware.GetHardwareItems(),
            _kosten.GetVerbraeuche());

        var zelte = _grows.GetTents()
            .Where(t => t.Status == TentStatus.Active)
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Id)
            .Select(t => new KostenZelt(t.Id, t.Name))
            .ToList();
        return seite with { Quelle = quelle, Zelte = zelte };
    }

    /// <summary>
    /// Die Durchgänge mit ihren Kosten — dieselbe Rechnung wie die Tabelle auf
    /// der Kostenseite. Das Archiv übernimmt diese Zahl, statt eine zweite zu schätzen.
    /// </summary>
    public IReadOnlyList<KostenDurchgang> Durchgaenge()
        => DurchgaengeBerechnen(
            _grows.GetAllGrows(), StromQuelle, _original.StrompreisCentProKwh,
            _kosten.GetZaehlerstaende(), _kosten.GetArtikel(), _kosten.GetNachfuellungen(),
            _kosten.GetAnschaffungen(), _kosten.GetVerbraeuche(), DateTime.UtcNow);

    // ------------------------------------------------------------ Rechnung

    public static KostenSeite Berechnen(
        GrowRun? grow,
        IReadOnlyList<GrowRun> alleGrows,
        StromQuelle quelle,
        double? preisCent,
        double? leistungW,
        IReadOnlyList<Zaehlerstand> staende,
        IReadOnlyList<Verbrauchsartikel> artikel,
        IReadOnlyList<Nachfuellung> fuellungen,
        IReadOnlyList<Anschaffung> anschaffungen,
        DateTime jetztUtc,
        IReadOnlyList<HardwareItem>? hardware = null,
        IReadOnlyList<Verbrauch>? verbraeuche = null)
    {
        hardware ??= [];
        var hersteller = Stammdaten.Sortiert(
            artikel.Select(a => a.Hersteller)
            .Concat(anschaffungen.Select(a => a.Hersteller))
            .Concat(hardware.Select(h => h.Manufacturer)));
        var produkte = artikel.Select(a => (a.Hersteller, a.Produkt))
            .Concat(anschaffungen.Select(a => (a.Hersteller, a.Produkt)))
            .Concat(hardware.Select(h => (h.Manufacturer, h.Model)))
            .Where(t => !string.IsNullOrWhiteSpace(t.Item2))
            .Select(t => new KostenProdukt(t.Item1 is null ? null : Stammdaten.Normalisieren(t.Item1), Stammdaten.Normalisieren(t.Item2!)))
            .DistinctBy(p => (p.Hersteller?.ToLowerInvariant(), p.Produkt.ToLowerInvariant()))
            .OrderBy(p => p.Produkt, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var heute = jetztUtc.ToLocalTime().Date;
        var artikelNachId = artikel.ToDictionary(a => a.Id);
        var growNachId = alleGrows.ToDictionary(g => g.Id);

        // Der gezeigte Grow muss in der Aufteilung stehen, auch wenn ein Aufrufer
        // ihn nicht in der Liste aller Grows mitgibt.
        var growsMitGezeigtem = grow is null || alleGrows.Any(g => g.Id == grow.Id)
            ? alleGrows
            : alleGrows.Append(grow).ToList();
        var aufteilung = StromAufteilung.Aufteilen(growsMitGezeigtem, quelle, staende, jetztUtc);
        var strom = StromAusAnteil(grow, growsMitGezeigtem, quelle, preisCent, leistungW, aufteilung);
        var artikelListe = artikel.Select(a => ArtikelBerechnen(a, fuellungen, verbraeuche ?? Array.Empty<Verbrauch>(), grow?.Id, jetztUtc)).ToList();
        var fuellungenListe = fuellungen
            .OrderByDescending(f => f.ZeitpunktUtc)
            .Select(f =>
            {
                artikelNachId.TryGetValue(f.ArtikelId, out var a);
                var laufzeit = Laufzeit(f);
                return new KostenNachfuellung(
                    f.Id, f.ArtikelId, a?.Name ?? "?", a?.Einheit ?? string.Empty, f.ZeitpunktUtc, f.Menge, f.KostenEur,
                    f.LeerAmUtc, laufzeit, laufzeit is > 0 && f.KostenEur is { } k ? k / laufzeit.Value : null,
                    f.GrowId, f.GrowId is { } gid && growNachId.TryGetValue(gid, out var g) ? g.Name : null, f.Notiz);
            })
            .ToList();

        var artikelEur = grow is null ? 0 : ArtikelEurJeGrow(grow.Id, artikel, fuellungen, verbraeuche ?? []);
        var anschaffungenEur = grow is null ? 0 : anschaffungen.Where(a => a.GrowId == grow.Id).Sum(a => a.GesamtEur);
        var gesamt = (strom.EurSeitStart ?? 0) + artikelEur + anschaffungenEur;

        var anschaffungenListe = anschaffungen
            .OrderByDescending(a => a.DatumUtc).ThenByDescending(a => a.Id)
            .Select(a => new KostenAnschaffung(
                a.Id, a.Name, a.Hersteller, a.Produkt, a.DatumUtc, a.Stueck, a.EinzelpreisEur, a.GesamtEur,
                a.GrowId, a.GrowId is { } gid2 && growNachId.TryGetValue(gid2, out var g2) ? g2.Name : null, a.Notiz, a.HardwareItemId))
            .ToList();

        KostenGrowInfo? info = null;
        KostenSumme summe;
        if (grow is null)
        {
            summe = new KostenSumme(gesamt, strom.EurSeitStart, artikelEur, anschaffungenEur, null, null, null, null);
        }
        else
        {
            var tag = Math.Max(1, ((grow.EndDate ?? heute).Date - grow.StartDate.Date).Days + 1);
            var phase = GrowStageResolver.Resolve(grow, heute);
            info = new KostenGrowInfo(grow.Id, grow.Name, grow.StartDate, grow.EndDate, tag, PhaseLabel(phase.ToString()), grow.PlantCount);

            var proTag = gesamt / tag;
            // Ohne eine einzige Zahl gibt es auch keine Prognose — „≈ 0,00 €“ wäre eine Aussage, die niemand gemacht hat.
            var (prognose, hinweis) = gesamt > 0 ? Ernteprognose(grow, heute, gesamt, proTag) : (null, null);
            summe = new KostenSumme(
                gesamt, strom.EurSeitStart, artikelEur, anschaffungenEur,
                proTag,
                grow.PlantCount is > 0 ? gesamt / grow.PlantCount.Value : null,
                prognose, hinweis);
        }

        var durchgaenge = DurchgaengeAusAufteilung(alleGrows, quelle, preisCent, aufteilung, artikel, fuellungen, anschaffungen, verbraeuche ?? []);

        return new KostenSeite(info, summe, strom, artikelListe, fuellungenListe, anschaffungenListe, durchgaenge, VerbrauchsEinheiten.Alle, hersteller, produkte);
    }

    private static (double? Prognose, string? Hinweis) Ernteprognose(GrowRun grow, DateTime heute, double bisher, double proTag)
    {
        if (grow.EndDate is not null) return (null, null);
        int? wochen = (grow.BreederFlowerWeeksMin, grow.BreederFlowerWeeksMax) switch
        {
            ({ } min, { } max) => (min + max + 1) / 2,
            ({ } min, null) => min,
            (null, { } max) => max,
            _ => null,
        };
        // Blütebeginn aus dem Phasenanker, nicht aus dem Flipdatum: so zählen
        // auch die bestätigte Autoflower-Blüte und ein Einstieg „Blüte" ohne Flip.
        if (wochen is null || Phasenanker.Fuer(grow, heute).BlueteAb is not { } bluete) return (null, null);
        var ernte = bluete.Date.AddDays(wochen.Value * 7);
        var rest = (ernte - heute).Days;
        if (rest <= 0) return (bisher, $"Ernte laut Züchter-Angabe ({wochen} Wochen Blüte) erreicht");
        return (bisher + rest * proTag, $"bei {wochen} Wochen Blüte, Ernte ≈ {ernte:dd.MM.yyyy}");
    }

    /// <summary>
    /// Was ein Artikel einen Grow kostet — die EINE Regel für die Summe oben,
    /// die Zeile am Artikel und die Tabelle der Durchgänge.
    /// </summary>
    /// <remarks>
    /// <para>forkai.90: Zwei Buchungsziele, je Artikel gewählt.
    /// <c>AufGrowBuchen = false</c> (Voreinstellung): die Nachfüllung zählt voll
    /// in dem Durchgang, dem sie zugeordnet ist. <c>AufGrowBuchen = true</c>: die
    /// Nachfüllung ist lagerneutral, nur der gebuchte Verbrauch trifft den
    /// Durchgang — bewertet über <see cref="VerbrauchsansichtService.PreisJeEinheit"/>,
    /// also mit dem Preis der Füllung, aus der die Menge stammt. Ohne die
    /// Trennung zählte ein 10-L-Kanister voll auf den Lauf, in dem er gekauft
    /// wurde, obwohl er drei Läufe hält.</para>
    /// <para><b>Warum hier und nur hier (02.10.2026).</b> Die Durchgänge-Tabelle
    /// summierte jede Nachfüllung, die Summe oben rechnete nach dieser Regel. Ein
    /// 50-€-Kanister, von dem 10 € verbraucht waren, stand oben mit 10 € und
    /// in der Tabelle darunter mit 50 € — derselbe Grow, zwei Beträge.</para>
    /// </remarks>
    public static double ArtikelEurImGrow(
        Verbrauchsartikel artikel, int growId, IReadOnlyList<Nachfuellung> fuellungen, IReadOnlyList<Verbrauch> verbraeuche)
    {
        var eigene = fuellungen.Where(f => f.ArtikelId == artikel.Id).ToList();
        return artikel.AufGrowBuchen
            ? verbraeuche
                .Where(v => v.ArtikelId == artikel.Id && v.GrowId == growId)
                .Sum(v => VerbrauchsansichtService.PreisJeEinheit(eigene, v.ZeitpunktUtc, artikel) is { } preis ? v.Menge * preis : 0)
            : eigene.Where(f => f.GrowId == growId).Sum(f => f.KostenEur ?? 0);
    }

    /// <summary>Alle Artikel eines Grows — die Summe von <see cref="ArtikelEurImGrow"/>.</summary>
    public static double ArtikelEurJeGrow(
        int growId, IReadOnlyList<Verbrauchsartikel> artikel, IReadOnlyList<Nachfuellung> fuellungen, IReadOnlyList<Verbrauch> verbraeuche)
        => artikel.Sum(a => ArtikelEurImGrow(a, growId, fuellungen, verbraeuche));

    /// <summary>Die Durchgänge-Tabelle — für die Kostenseite und das Archiv.</summary>
    public static IReadOnlyList<KostenDurchgang> DurchgaengeBerechnen(
        IReadOnlyList<GrowRun> alleGrows, StromQuelle quelle, double? preisCent,
        IReadOnlyList<Zaehlerstand> staende, IReadOnlyList<Verbrauchsartikel> artikel,
        IReadOnlyList<Nachfuellung> fuellungen, IReadOnlyList<Anschaffung> anschaffungen,
        IReadOnlyList<Verbrauch> verbraeuche, DateTime jetztUtc)
        => DurchgaengeAusAufteilung(
            alleGrows, quelle, preisCent, StromAufteilung.Aufteilen(alleGrows, quelle, staende, jetztUtc),
            artikel, fuellungen, anschaffungen, verbraeuche);

    private static List<KostenDurchgang> DurchgaengeAusAufteilung(
        IReadOnlyList<GrowRun> alleGrows, StromQuelle quelle, double? preisCent,
        IReadOnlyDictionary<int, StromAnteil> aufteilung, IReadOnlyList<Verbrauchsartikel> artikel,
        IReadOnlyList<Nachfuellung> fuellungen, IReadOnlyList<Anschaffung> anschaffungen,
        IReadOnlyList<Verbrauch> verbraeuche)
        => alleGrows
            .OrderByDescending(g => g.StartDate)
            .Select(g =>
            {
                var s = StromAusAnteil(g, alleGrows, quelle, preisCent, null, aufteilung);
                var a = ArtikelEurJeGrow(g.Id, artikel, fuellungen, verbraeuche);
                var an = anschaffungen.Where(x => x.GrowId == g.Id).Sum(x => x.GesamtEur);
                var summeEur = s.EurSeitStart is { } se ? se + a + an : (a + an > 0 ? a + an : (double?)null);
                return new KostenDurchgang(g.Id, g.Name, g.StartDate, g.EndDate, g.Status == GrowStatus.Running,
                    s.EurSeitStart, a, an, summeEur, s.GeteiltTage);
            })
            .ToList();

    /// <summary>Strom eines einzelnen Grows — so, als liefe kein anderer.</summary>
    public static KostenStrom StromBerechnen(
        GrowRun? grow, StromQuelle quelle, double? preisCent, double? leistungW,
        IReadOnlyList<Zaehlerstand> staende, DateTime jetztUtc)
        => StromBerechnen(grow, grow is null ? [] : [grow], quelle, preisCent, leistungW, staende, jetztUtc);

    /// <summary>Strom eines Grows — sein Anteil am Verbrauch seines Zählers, neben allen anderen Grows.</summary>
    public static KostenStrom StromBerechnen(
        GrowRun? grow, IReadOnlyList<GrowRun> alleGrows, StromQuelle quelle, double? preisCent, double? leistungW,
        IReadOnlyList<Zaehlerstand> staende, DateTime jetztUtc)
    {
        var grows = grow is null || alleGrows.Any(g => g.Id == grow.Id) ? alleGrows : alleGrows.Append(grow).ToList();
        return StromAusAnteil(grow, grows, quelle, preisCent, leistungW, StromAufteilung.Aufteilen(grows, quelle, staende, jetztUtc));
    }

    private static KostenStrom StromAusAnteil(
        GrowRun? grow, IReadOnlyList<GrowRun> alleGrows, StromQuelle quelle, double? preisCent, double? leistungW,
        IReadOnlyDictionary<int, StromAnteil> aufteilung)
    {
        var zaehler = quelle.ZaehlerFuerZelt(grow?.TentId);
        var eingerichtet = zaehler is not null;
        var eigener = grow is not null && quelle.HatEigenenZaehler(grow.TentId);
        var leer = new KostenStrom(eingerichtet, zaehler, quelle.LeistungEntityId, preisCent, leistungW,
            null, null, null, null, null, null, null, null, string.Empty, [], eigener);

        if (!eingerichtet) return leer with { Hinweis = "Keine Strom-Quelle eingerichtet — kWh-Zähler in den Einstellungen unten wählen." };
        if (grow is null) return leer with { Hinweis = "Kein laufender Grow." };

        var anteil = aufteilung.GetValueOrDefault(grow.Id);
        var preisEur = preisCent is { } p ? p / 100.0 : (double?)null;

        if (anteil is null || anteil.Stuecke.Count == 0)
        {
            var imFenster = anteil?.StaendeImFenster ?? [];
            if (imFenster.Count == 0)
            {
                return leer with { Hinweis = "Noch kein Zählerstand für diesen Grow festgehalten — der Worker holt ihn innerhalb von 10 Minuten, oder unten „Zählerstand jetzt festhalten“." };
            }

            // Ein einziger Stand: noch kein Verbrauch, aber ein Anfang.
            return leer with
            {
                KwhSeitStart = 0,
                EurSeitStart = preisEur is null ? null : 0,
                ZaehlerStart = imFenster[0].Kwh,
                ZaehlerAktuell = imFenster[^1].Kwh,
                ErsterStandUtc = imFenster[0].ZeitpunktUtc,
                LetzterStandUtc = imFenster[^1].ZeitpunktUtc,
                Hinweis = "Erst ein Zählerstand — Verbrauch ergibt sich ab dem zweiten.",
            };
        }

        var erster = anteil.Erster!;
        var letzter = anteil.Letzter!;
        var kwh = anteil.Kwh;
        var tage = anteil.Stuecke.Sum(x => x.Tage);
        double? eur = preisEur is { } pe ? kwh * pe : null;
        var kwhProTag = tage >= 0.5 ? kwh / tage : (double?)null;

        var phasen = PhasenAusStuecken(grow, anteil.Stuecke, preisEur, grow.EndDate is null);

        var hinweis = erster.ZeitpunktUtc.ToLocalTime().Date > grow.StartDate.Date.AddDays(1)
            ? $"Zähler erst seit {erster.ZeitpunktUtc.ToLocalTime():dd.MM.yyyy} erfasst — davor fehlt der Strom dieses Grows."
            : preisEur is null
                ? "Kein Strompreis hinterlegt — nur kWh, keine Euro."
                : "Gemessen am kWh-Zähler, Preis aus den Kosten-Einstellungen.";

        var (geteiltTage, geteiltMit, teilungHinweis) = Teilung(anteil.Stuecke, alleGrows);

        return new KostenStrom(
            true, zaehler, quelle.LeistungEntityId, preisCent, leistungW,
            kwh, eur, kwhProTag, kwhProTag is { } k && preisEur is { } pr ? k * pr : null,
            erster.Kwh, letzter.Kwh, erster.ZeitpunktUtc, letzter.ZeitpunktUtc, hinweis, phasen,
            eigener, geteiltTage, geteiltMit, teilungHinweis);
    }

    /// <summary>An welchen Tagen und mit wem der Zähler geteilt war — samt dem Satz für die Seite.</summary>
    private static (int Tage, IReadOnlyList<KostenTeilung> Mit, string? Hinweis) Teilung(
        IReadOnlyList<StromStueck> stuecke, IReadOnlyList<GrowRun> alleGrows)
    {
        var tage = stuecke.Where(s => s.GeteiltDurch > 1).Select(s => s.Tag).Distinct().Count();
        if (tage == 0) return (0, [], null);

        var namen = alleGrows.ToDictionary(g => g.Id, g => g.Name);
        var mit = stuecke
            .SelectMany(s => s.MitGrows.Select(id => (Id: id, s.Tag)))
            .Distinct()
            .GroupBy(x => x.Id)
            .Select(g => new KostenTeilung(g.Key, namen.GetValueOrDefault(g.Key) ?? $"Grow {g.Key}", g.Count()))
            .OrderByDescending(t => t.Tage).ThenBy(t => t.Name, StringComparer.CurrentCulture)
            .ToList();

        static string TageText(int n) => n == 1 ? "1 Tag" : $"{n} Tagen";
        var wer = mit.Count == 1
            ? $"„{mit[0].Name}“"
            : string.Join(", ", mit.Take(mit.Count - 1).Select(t => $"„{t.Name}“ ({TageText(t.Tage)})"))
              + $" und „{mit[^1].Name}“ ({TageText(mit[^1].Tage)})";

        var hinweis = $"Zähler geteilt mit {wer} an {TageText(tage)} — an diesen Tagen ist sein Verbrauch "
            + "zu gleichen Teilen auf die Grows verteilt, die dort liefen.";
        return (tage, mit, hinweis);
    }

    /// <summary>kWh zwischen zwei Ständen — ein Sprung nach unten gilt als Zähler-Reset.</summary>
    public static double KwhZwischen(IReadOnlyList<Zaehlerstand> staende, int von, int bis)
    {
        double summe = 0;
        for (var i = von + 1; i <= bis; i++)
        {
            summe += StromAufteilung.Differenz(staende[i - 1], staende[i]);
        }
        return summe;
    }

    /// <summary>
    /// Die Phasen eines Grows aus seinen Verbrauchsstücken. Die Phase eines
    /// Tages sagt der Grow selbst (<see cref="GrowStageResolver"/>), nicht der
    /// Name am Zählerstand: läuft ein zweiter Grow am selben Zähler, gehört
    /// dieser Name nur einem von beiden.
    /// </summary>
    private static IReadOnlyList<KostenPhase> PhasenAusStuecken(
        GrowRun grow, IReadOnlyList<StromStueck> stuecke, double? preisEur, bool growLaeuft)
    {
        var phasen = new List<KostenPhase>();
        var i = 0;
        while (i < stuecke.Count)
        {
            var phase = GrowStageResolver.Resolve(grow, stuecke[i].Tag).ToString();
            var j = i;
            while (j + 1 < stuecke.Count && GrowStageResolver.Resolve(grow, stuecke[j + 1].Tag).ToString() == phase) j++;

            var gruppe = stuecke.Skip(i).Take(j - i + 1).ToList();
            var kwh = gruppe.Sum(x => x.Kwh);
            var letzte = j == stuecke.Count - 1;
            phasen.Add(new KostenPhase(
                phase, PhaseLabel(phase), gruppe[0].VonUtc, gruppe[^1].BisUtc, gruppe.Sum(x => x.Tage),
                kwh, preisEur is { } p ? kwh * p : null, letzte && growLaeuft));
            i = j + 1;
        }
        return phasen;
    }

    private static double? Laufzeit(Nachfuellung f)
        => f.LeerAmUtc is { } leer && leer > f.ZeitpunktUtc ? (leer - f.ZeitpunktUtc).TotalDays : null;

    private static KostenArtikel ArtikelBerechnen(Verbrauchsartikel a, IReadOnlyList<Nachfuellung> alle, IReadOnlyList<Verbrauch> verbraeuche, int? growId, DateTime jetztUtc)
    {
        var eigene = alle.Where(f => f.ArtikelId == a.Id).OrderByDescending(f => f.ZeitpunktUtc).ThenByDescending(f => f.Id).ToList();
        // Unter einem Tag war es kein Verbrauch, sondern ein Fehlgriff (Füllung
        // doppelt erfasst, gleich wieder „leer“ geklickt) — so etwas darf die
        // Prognose der nächsten Flasche nicht halbieren.
        var abgeschlossen = eigene.Where(f => Laufzeit(f) is >= 1).ToList();

        // Mittel der letzten drei Laufzeiten, je Mengeneinheit — eine halbe
        // Flasche hält halb so lang.
        double? tageJeEinheit = abgeschlossen.Count == 0
            ? null
            : abgeschlossen.Take(3).Average(f => Laufzeit(f)!.Value / Math.Max(f.Menge, 1e-9));
        double? mittlereLaufzeit = abgeschlossen.Count == 0 ? null : abgeschlossen.Take(3).Average(f => Laufzeit(f)!.Value);

        KostenFuellungAktuell? aktuell = null;
        var offen = eigene.FirstOrDefault(f => f.LeerAmUtc is null);
        if (offen is not null)
        {
            var tag = Math.Max(1, (int)Math.Floor((jetztUtc - offen.ZeitpunktUtc).TotalDays) + 1);
            double? prognoseTage = tageJeEinheit is { } t ? t * offen.Menge : null;
            var prognoseLeer = prognoseTage is { } pt ? offen.ZeitpunktUtc.AddDays(pt) : (DateTime?)null;
            double? fuellstand = prognoseTage is > 0 ? Math.Clamp(1 - (jetztUtc - offen.ZeitpunktUtc).TotalDays / prognoseTage.Value, 0, 1) * 100 : null;
            var quelle = "geschaetzt";
            string? prognoseQuelle = prognoseLeer is null ? null : "geschaetzt";

            // Fork AI (forkai.20): Gebuchter Verbrauch schlägt die Schätzung.
            // Die CO₂-Steuerung bucht jeden Abend, was durchs Ventil ging —
            // damit weiß die Flasche, wie voll sie ist, ohne dass jemand sie
            // wiegt oder vorher eine leer gemacht hat.
            var verbraucht = verbraeuche
                .Where(v => v.ArtikelId == a.Id && v.ZeitpunktUtc >= offen.ZeitpunktUtc)
                .Sum(v => v.Menge);
            if (verbraucht > 0 && offen.Menge > 0)
            {
                fuellstand = Math.Clamp(1 - verbraucht / offen.Menge, 0, 1) * 100;
                quelle = "gemessen";
                var tageSeitFuellung = Math.Max((jetztUtc - offen.ZeitpunktUtc).TotalDays, 0.5);
                var jeTag = verbraucht / tageSeitFuellung;

                // Fork AI (forkai.84): Aus wenig Verbrauch keine Laufzeit
                // hochrechnen.
                //
                // Die CO2-Flasche hatte nach sechs Tagen 77 g von 10 kg
                // gebucht - das sind 0,8 %. Daraus ergaben sich rechnerisch 794
                // Tage und "leer am 09.11.2028". Beides stimmt und beides ist
                // wertlos: eine Hochrechnung um Faktor 130 traegt nicht, schon
                // gar nicht wenn der gebuchte Verbrauch ein Netto-Wert ist, der
                // den echten untertreibt.
                //
                // Erst ab einem Zwanzigstel der Fuellung und zwei Wochen
                // Laufzeit ist die Grundlage breit genug. Vorher bleibt die
                // Prognose leer - die Seite sagt dann, woran es liegt, statt
                // eine Zahl zu zeigen, der niemand trauen sollte.
                var anteilVerbraucht = verbraucht / offen.Menge;
                var grundlageTraegt = anteilVerbraucht >= 0.05 && tageSeitFuellung >= 14;

                if (jeTag > 0 && grundlageTraegt)
                {
                    prognoseTage = offen.Menge / jeTag;
                    prognoseLeer = offen.ZeitpunktUtc.AddDays(prognoseTage.Value);
                    prognoseQuelle = "gemessen";
                }
                else if (jeTag > 0)
                {
                    // Auch die Schaetzung aus frueheren Laufzeiten faellt weg,
                    // wenn es keine gibt - sonst stuende hier die alte Zahl
                    // neben einem Fuellstand, der aus Messung stammt.
                    prognoseTage = tageJeEinheit is { } t2 ? t2 * offen.Menge : null;
                    prognoseLeer = prognoseTage is { } pt2 ? offen.ZeitpunktUtc.AddDays(pt2) : null;
                    // Der Füllstand ist gemessen, das Datum NICHT. Vorher trug
                    // beides „gemessen", und die Seite schrieb „aus dem
                    // gebuchten Verbrauch" unter ein geschätztes Datum.
                    prognoseQuelle = prognoseLeer is null ? null : "geschaetzt";
                }
            }
            double? eurProTag = offen.KostenEur is { } k && prognoseTage is > 0 ? k / prognoseTage.Value : null;
            aktuell = new KostenFuellungAktuell(offen.Id, offen.ZeitpunktUtc, offen.Menge, offen.KostenEur, tag, prognoseTage, prognoseLeer, fuellstand, eurProTag, verbraucht, quelle, prognoseQuelle);
        }

        // forkai.101: Die Zeile je Artikel muss dasselbe rechnen wie die Summe
        // darueber. Bei AufGrowBuchen zaehlen die Verbrauchsbuchungen, sonst die
        // Fuellungen. Vorher kannte diese Zeile nur Fuellungen — Purolyt stand
        // deshalb auf 0 EUR, obwohl 2,93 EUR gebucht waren und in der
        // Gesamtsumme auch auftauchten.
        var summeImGrow = growId is { } g ? ArtikelEurImGrow(a, g, eigene, verbraeuche) : 0;

        return new KostenArtikel(
            a.Id, a.Name, a.Hersteller, a.Produkt, a.PreisEur, a.Einheit, a.Gebinde, a.TentId, a.Notiz, a.Aktiv,
            a.AufGrowBuchen, aktuell,
            eigene.Count, mittlereLaufzeit, summeImGrow);
    }

    public static string PhaseLabel(string phase) => phase switch
    {
        nameof(GrowStage.Seedling) => "Sämling",
        nameof(GrowStage.Clone) => "Steckling",
        nameof(GrowStage.Veg) => "Wachstum", // wie deutsche-woerter.ts in der Oberfläche
        nameof(GrowStage.Transition) => "Übergang",
        nameof(GrowStage.Flower) => "Blüte",
        nameof(GrowStage.Finish) => "Finish",
        nameof(GrowStage.Dry) => "Trocknen",
        nameof(GrowStage.Cure) => "Aushärten",
        _ => phase,
    };
}
