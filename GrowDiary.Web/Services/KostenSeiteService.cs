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
    IReadOnlyList<KostenPhase> Phasen);

public sealed record KostenFuellungAktuell(
    int Id,
    DateTime ZeitpunktUtc,
    double Menge,
    double? KostenEur,
    int Tag,
    double? PrognoseTage,
    DateTime? PrognoseLeerAmUtc,
    double? FuellstandProzent,
    double? EurProTag);

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

public sealed record KostenDurchgang(int GrowId, string Name, DateTime StartDate, DateTime? EndDate, bool Laeuft, double? StromEur, double ArtikelEur, double? GesamtEur);

public sealed record KostenSeite(
    KostenGrowInfo? Grow,
    KostenSumme Summe,
    KostenStrom Strom,
    IReadOnlyList<KostenArtikel> Artikel,
    IReadOnlyList<KostenNachfuellung> Nachfuellungen,
    IReadOnlyList<KostenDurchgang> Durchgaenge);

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

    public KostenSeiteService(
        KostenRepository kosten,
        GrowRepository grows,
        AppSettingsRepository settings,
        GrowCostService original,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings)
    {
        _kosten = kosten;
        _grows = grows;
        _settings = settings;
        _original = original;
        _ha = ha;
        _haSettings = haSettings;
    }

    public StromQuelle StromQuelle
    {
        get
        {
            var raw = _settings.GetValue(StromQuelleKey);
            if (string.IsNullOrWhiteSpace(raw)) return new StromQuelle();
            try { return JsonSerializer.Deserialize<StromQuelle>(raw, Json) ?? new StromQuelle(); }
            catch (JsonException) { return new StromQuelle(); }
        }
        set => _settings.SetValue(StromQuelleKey, JsonSerializer.Serialize(new StromQuelle
        {
            ZaehlerEntityId = Leer(value.ZaehlerEntityId),
            LeistungEntityId = Leer(value.LeistungEntityId),
        }, Json));
    }

    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Liest den Zähler in HA und hält ihn fest. Null, wenn keine Quelle oder kein Wert.</summary>
    public async Task<Zaehlerstand?> ZaehlerstandFesthaltenAsync(ZaehlerAnlass anlass, CancellationToken ct = default)
    {
        var quelle = StromQuelle;
        if (string.IsNullOrWhiteSpace(quelle.ZaehlerEntityId)) return null;
        var state = await _ha.GetEntityStateAsync(_haSettings.GetEffectiveHomeAssistantSettings(), quelle.ZaehlerEntityId, ct);
        if (state?.NumericValue is not { } kwh) return null;

        var (growId, phase) = LaufenderGrow(DateTime.Today);
        var stand = new Zaehlerstand { ZeitpunktUtc = DateTime.UtcNow, Kwh = kwh, Anlass = anlass, GrowId = growId, Phase = phase };
        stand.Id = _kosten.CreateZaehlerstand(stand);
        return stand;
    }

    /// <summary>Der Grow, dem ein Zählerstand zugeordnet wird: der älteste laufende.</summary>
    public (int? GrowId, string? Phase) LaufenderGrow(DateTime heute)
    {
        var grow = _grows.GetActiveGrows()
            .Where(g => g.Status == GrowStatus.Running)
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

        return Berechnen(
            grow, alle, quelle, preis, leistung,
            _kosten.GetZaehlerstaende(),
            _kosten.GetArtikel(),
            _kosten.GetNachfuellungen(),
            DateTime.UtcNow);
    }

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
        DateTime jetztUtc)
    {
        var heute = jetztUtc.ToLocalTime().Date;
        var artikelNachId = artikel.ToDictionary(a => a.Id);
        var growNachId = alleGrows.ToDictionary(g => g.Id);

        var strom = StromBerechnen(grow, quelle, preisCent, leistungW, staende, jetztUtc);
        var artikelListe = artikel.Select(a => ArtikelBerechnen(a, fuellungen, grow?.Id, jetztUtc)).ToList();
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

        var artikelEur = grow is null ? 0 : fuellungen.Where(f => f.GrowId == grow.Id).Sum(f => f.KostenEur ?? 0);
        var gesamt = (strom.EurSeitStart ?? 0) + artikelEur;

        KostenGrowInfo? info = null;
        KostenSumme summe;
        if (grow is null)
        {
            summe = new KostenSumme(gesamt, strom.EurSeitStart, artikelEur, null, null, null, null);
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
                gesamt, strom.EurSeitStart, artikelEur,
                proTag,
                grow.PlantCount is > 0 ? gesamt / grow.PlantCount.Value : null,
                prognose, hinweis);
        }

        var durchgaenge = alleGrows
            .OrderByDescending(g => g.StartDate)
            .Select(g =>
            {
                var s = StromBerechnen(g, quelle, preisCent, null, staende, jetztUtc);
                var a = fuellungen.Where(f => f.GrowId == g.Id).Sum(f => f.KostenEur ?? 0);
                var summeEur = s.EurSeitStart is { } se ? se + a : (a > 0 ? a : (double?)null);
                return new KostenDurchgang(g.Id, g.Name, g.StartDate, g.EndDate, g.Status == GrowStatus.Running, s.EurSeitStart, a, summeEur);
            })
            .ToList();

        return new KostenSeite(info, summe, strom, artikelListe, fuellungenListe, durchgaenge);
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
        if (wochen is null || grow.FlipDate is null) return (null, null);
        var ernte = grow.FlipDate.Value.Date.AddDays(wochen.Value * 7);
        var rest = (ernte - heute).Days;
        if (rest <= 0) return (bisher, $"Ernte laut Züchter-Angabe ({wochen} Wochen Blüte) erreicht");
        return (bisher + rest * proTag, $"bei {wochen} Wochen Blüte, Ernte ≈ {ernte:dd.MM.yyyy}");
    }

    /// <summary>Strom eines Grows aus den Zählerständen, die in seine Laufzeit fallen.</summary>
    public static KostenStrom StromBerechnen(
        GrowRun? grow, StromQuelle quelle, double? preisCent, double? leistungW,
        IReadOnlyList<Zaehlerstand> staende, DateTime jetztUtc)
    {
        var eingerichtet = !string.IsNullOrWhiteSpace(quelle.ZaehlerEntityId);
        var leer = new KostenStrom(eingerichtet, quelle.ZaehlerEntityId, quelle.LeistungEntityId, preisCent, leistungW,
            null, null, null, null, null, null, null, null, string.Empty, []);

        if (!eingerichtet) return leer with { Hinweis = "Keine Strom-Quelle eingerichtet — kWh-Zähler in den Einstellungen unten wählen." };
        if (grow is null) return leer with { Hinweis = "Kein laufender Grow." };

        // Nur Stände, die zu diesem Grow gehören ODER in seine Laufzeit fallen:
        // die GrowId sitzt am Stand, seit der Worker läuft; ältere Stände
        // (oder ein anderer laufender Grow im selben Zelt) fallen über die Zeit.
        var vonUtc = grow.StartDate.Date.ToUniversalTime();
        var bisUtc = grow.EndDate is { } ende ? ende.Date.AddDays(1).ToUniversalTime() : jetztUtc;
        var relevant = staende
            .Where(s => s.GrowId == grow.Id || (s.GrowId is null && s.ZeitpunktUtc >= vonUtc && s.ZeitpunktUtc <= bisUtc))
            .Where(s => s.ZeitpunktUtc <= bisUtc)
            .OrderBy(s => s.ZeitpunktUtc).ThenBy(s => s.Id)
            .ToList();

        if (relevant.Count == 0)
        {
            return leer with { Hinweis = "Noch kein Zählerstand für diesen Grow festgehalten — der Worker holt ihn innerhalb von 10 Minuten, oder unten „Zählerstand jetzt festhalten“." };
        }

        var erster = relevant[0];
        var letzter = relevant[^1];
        var kwh = KwhZwischen(relevant, 0, relevant.Count - 1);
        var tage = Math.Max((letzter.ZeitpunktUtc - erster.ZeitpunktUtc).TotalDays, 0);
        var preisEur = preisCent is { } p ? p / 100.0 : (double?)null;
        double? eur = preisEur is { } pe ? kwh * pe : null;
        var kwhProTag = tage >= 0.5 ? kwh / tage : (double?)null;

        var phasen = PhasenBerechnen(relevant, preisEur, grow.EndDate is null);

        var hinweis = relevant.Count == 1
            ? "Erst ein Zählerstand — Verbrauch ergibt sich ab dem zweiten."
            : erster.ZeitpunktUtc.Date > grow.StartDate.Date.AddDays(1)
                ? $"Zähler erst seit {erster.ZeitpunktUtc.ToLocalTime():dd.MM.yyyy} erfasst — davor fehlt der Strom dieses Grows."
                : preisEur is null
                    ? "Kein Strompreis hinterlegt — nur kWh, keine Euro."
                    : "Gemessen am kWh-Zähler, Preis aus den Kosten-Einstellungen.";

        return new KostenStrom(
            true, quelle.ZaehlerEntityId, quelle.LeistungEntityId, preisCent, leistungW,
            kwh, eur, kwhProTag, kwhProTag is { } k && preisEur is { } pr ? k * pr : null,
            erster.Kwh, letzter.Kwh, erster.ZeitpunktUtc, letzter.ZeitpunktUtc, hinweis, phasen);
    }

    /// <summary>kWh zwischen zwei Ständen — ein Sprung nach unten gilt als Zähler-Reset.</summary>
    public static double KwhZwischen(IReadOnlyList<Zaehlerstand> staende, int von, int bis)
    {
        double summe = 0;
        for (var i = von + 1; i <= bis; i++)
        {
            var delta = staende[i].Kwh - staende[i - 1].Kwh;
            summe += delta >= 0 ? delta : staende[i].Kwh;
        }
        return summe;
    }

    private static IReadOnlyList<KostenPhase> PhasenBerechnen(IReadOnlyList<Zaehlerstand> staende, double? preisEur, bool growLaeuft)
    {
        var phasen = new List<KostenPhase>();
        if (staende.Count < 2) return phasen;

        var start = 0;
        for (var i = 1; i <= staende.Count; i++)
        {
            var ende = i == staende.Count;
            var wechsel = !ende && !string.Equals(staende[i].Phase, staende[start].Phase, StringComparison.Ordinal);
            if (!ende && !wechsel) continue;

            var bisIndex = ende ? staende.Count - 1 : i;
            if (bisIndex > start)
            {
                var kwh = KwhZwischen(staende, start, bisIndex);
                var phase = staende[start].Phase ?? "?";
                phasen.Add(new KostenPhase(
                    phase, PhaseLabel(phase),
                    staende[start].ZeitpunktUtc, staende[bisIndex].ZeitpunktUtc,
                    (staende[bisIndex].ZeitpunktUtc - staende[start].ZeitpunktUtc).TotalDays,
                    kwh, preisEur is { } p ? kwh * p : null,
                    ende && growLaeuft));
            }
            start = i;
        }
        return phasen;
    }

    private static double? Laufzeit(Nachfuellung f)
        => f.LeerAmUtc is { } leer && leer > f.ZeitpunktUtc ? (leer - f.ZeitpunktUtc).TotalDays : null;

    private static KostenArtikel ArtikelBerechnen(Verbrauchsartikel a, IReadOnlyList<Nachfuellung> alle, int? growId, DateTime jetztUtc)
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
            double? eurProTag = offen.KostenEur is { } k && prognoseTage is > 0 ? k / prognoseTage.Value : null;
            aktuell = new KostenFuellungAktuell(offen.Id, offen.ZeitpunktUtc, offen.Menge, offen.KostenEur, tag, prognoseTage, prognoseLeer, fuellstand, eurProTag);
        }

        return new KostenArtikel(
            a.Id, a.Name, a.Hersteller, a.Produkt, a.PreisEur, a.Einheit, a.Gebinde, a.TentId, a.Notiz, a.Aktiv, aktuell,
            eigene.Count, mittlereLaufzeit,
            growId is { } g ? eigene.Where(f => f.GrowId == g).Sum(f => f.KostenEur ?? 0) : 0);
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
