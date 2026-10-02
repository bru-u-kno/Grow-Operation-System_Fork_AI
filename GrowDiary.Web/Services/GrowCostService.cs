using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Die Kostenaufstellung eines Grows — jede Zahl mit Herkunft.</summary>
/// <param name="StromEur">Stromkosten, berechnet; null ohne Watt oder Preis.</param>
/// <param name="StromHerkunft">Der Rechenweg in einem Satz.</param>
/// <param name="DuengerEur">Düngerkosten aus dem Dosier-Protokoll; null ohne Dosen.</param>
/// <param name="DuengerHerkunft">Woraus gerechnet wurde, samt Lücken.</param>
/// <param name="PumpenOhnePreis">Mittel, die dosiert wurden, aber keinen Preis tragen.</param>
/// <param name="SummeEur">Strom + Dünger, soweit vorhanden.</param>
/// <param name="EurProGramm">Summe je Gramm Trockenertrag; null ohne Ernte.</param>
public sealed record GrowKosten(
    double? StromEur,
    string? StromHerkunft,
    double? DuengerEur,
    string? DuengerHerkunft,
    IReadOnlyList<string> PumpenOhnePreis,
    double? SummeEur,
    double? EurProGramm);

/// <summary>
/// Rechnet zusammen, was ein Grow gekostet hat.
/// </summary>
/// <remarks>
/// <para>Alles hier ist BERECHNET und sagt das auch: der Strom kommt aus
/// Lampen-Watt × Lichtstunden × Tagen × Strompreis, nicht aus einem Zähler.
/// Nebenverbraucher (Pumpen, Lüfter, Chiller) fehlen bewusst — lieber eine
/// ehrliche Untergrenze als eine scheingenaue Gesamtzahl.</para>
///
/// <para>Der Dünger kommt aus dem Dosier-Protokoll: dort stehen die wirklich
/// gelaufenen Milliliter. Was von Hand in den Addback-Eimer kam, steht in
/// keinem Protokoll und fehlt hier — auch das sagt die Herkunftszeile.</para>
/// </remarks>
public sealed class GrowCostService
{
    private const string PreisKey = "cost-settings";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppSettingsRepository _settings;
    private readonly GrowRepository _grows;
    private readonly DosingRepository _dosing;
    private readonly LightRepository _light;
    private readonly HarvestRepository _harvest;

    public GrowCostService(
        AppSettingsRepository settings,
        GrowRepository grows,
        DosingRepository dosing,
        LightRepository light,
        HarvestRepository harvest)
    {
        _settings = settings;
        _grows = grows;
        _dosing = dosing;
        _light = light;
        _harvest = harvest;
    }

    /// <summary>Der Strompreis in Cent je kWh, wie in den Einstellungen hinterlegt.</summary>
    public double? StrompreisCentProKwh
    {
        get
        {
            var raw = _settings.GetValue(PreisKey);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                return JsonSerializer.Deserialize<KostenEinstellungen>(raw, Json)?.StrompreisCentProKwh;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        set => StrompreisSchreiben(_settings, value);
    }

    /// <summary>Den Strompreis schreiben — statisch, damit der Demobestand denselben Weg nimmt.</summary>
    public static void StrompreisSchreiben(AppSettingsRepository settings, double? centProKwh)
        => settings.SetValue(PreisKey, JsonSerializer.Serialize(new KostenEinstellungen { StrompreisCentProKwh = centProKwh }, Json));

    public GrowKosten? FuerGrow(int growId)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null) return null;

        var von = grow.StartDate;
        var bis = grow.EndDate ?? DateTime.Today;
        var tent = grow.TentId is { } tentId ? _grows.GetTent(tentId) : null;
        var lichtplan = grow.TentId is { } tid ? _light.GetActiveLightScheduleForTent(tid) : null;

        var dosen = grow.TentId is { } dtid
            ? _dosing.GetEvents(tentId: dtid, limit: 10_000)
                .Where(dose => dose.Outcome == DoseOutcome.Done && !dose.Simulated && dose.DosedMl > 0)
                .Where(dose => dose.OccurredAtUtc >= von.ToUniversalTime().AddDays(-1)
                            && dose.OccurredAtUtc <= bis.ToUniversalTime().AddDays(1))
                .ToList()
            : [];
        var pumpen = grow.TentId is { } ptid ? _dosing.GetPumps(ptid) : [];

        var ernte = _harvest.GetForGrow(growId);

        return Berechnen(
            von, bis, Phasenanker.Fuer(grow, bis).BlueteAb,
            tent?.LightWatt,
            lichtplan is null ? null : Lichtstunden(lichtplan),
            StrompreisCentProKwh,
            dosen, pumpen,
            ernte?.DryWeightG);
    }

    /// <summary>Stunden Licht je Tag laut Plan — oder null, wenn die Zeiten nicht lesbar sind.</summary>
    private static double? Lichtstunden(LightSchedule plan)
    {
        if (!TimeSpan.TryParse(plan.LightsOnTime, out var an)) return null;
        if (!TimeSpan.TryParse(plan.LightsOffTime, out var aus)) return null;

        // Gleiche An- und Aus-Zeit ist kein Dauerlicht, sondern ein Tippfehler —
        // dieselbe Lesart wie in der LightClock. Null heisst: die 18/12-Konvention
        // uebernimmt, statt die „ehrliche Untergrenze" mit 24 h aufzublasen.
        if (an == aus) return null;

        var stunden = (aus - an).TotalHours;
        return stunden > 0 ? stunden : stunden + 24;
    }

    /// <summary>
    /// Die reine Rechnung — statisch und ohne Datenbank, damit sie prüfbar ist.
    /// </summary>
    public static GrowKosten Berechnen(
        DateTime von, DateTime bis, DateTime? flip,
        int? lampenWatt, double? planStundenProTag, double? strompreisCent,
        IReadOnlyList<DoseEvent> dosen, IReadOnlyList<DosingPump> pumpen,
        double? trockenGramm)
    {
        double? stromEur = null;
        string? stromHerkunft = null;

        var tageGesamt = Math.Max(1, (bis.Date - von.Date).Days + 1);

        if (lampenWatt is { } watt && watt > 0 && strompreisCent is { } cent && cent > 0)
        {
            double kwh;
            string stundenText;
            if (planStundenProTag is { } stunden)
            {
                kwh = watt / 1000.0 * stunden * tageGesamt;
                stundenText = $"{stunden.ToString("0.#", AppCulture.German)} h/Tag laut Lichtplan";
            }
            else
            {
                // Ohne Plan die Konvention: 18 Stunden bis zum Flip, danach 12.
                var tageVor = flip is { } f ? Math.Clamp((f.Date - von.Date).Days, 0, tageGesamt) : tageGesamt;
                var tageNach = tageGesamt - tageVor;
                kwh = watt / 1000.0 * (tageVor * 18 + tageNach * 12);
                stundenText = flip is null ? "18 h/Tag angenommen" : "18/12 h um den Flip angenommen";
            }

            stromEur = Math.Round(kwh * cent / 100.0, 2);
            stromHerkunft = $"berechnet: {watt} W Licht × {stundenText} × {tageGesamt} Tage × "
                + $"{strompreisCent?.ToString("0.#", AppCulture.German)} ct/kWh — Nebenverbraucher nicht enthalten";
        }

        double? duengerEur = null;
        string? duengerHerkunft = null;
        var ohnePreis = new List<string>();

        if (dosen.Count > 0)
        {
            var preise = pumpen.ToDictionary(p => p.Id, p => p.CostPerLiterEur);
            double summe = 0;
            var bepreist = 0;

            foreach (var gruppe in dosen.GroupBy(d => d.PumpId))
            {
                var ml = gruppe.Sum(d => d.DosedMl);
                if (preise.TryGetValue(gruppe.Key, out var preis) && preis is { } proLiter)
                {
                    summe += ml / 1000.0 * proLiter;
                    bepreist++;
                }
                else
                {
                    var name = pumpen.FirstOrDefault(p => p.Id == gruppe.Key)?.Name ?? $"Pumpe {gruppe.Key}";
                    ohnePreis.Add(name);
                }
            }

            if (bepreist > 0)
            {
                duengerEur = Math.Round(summe, 2);
                duengerHerkunft = $"aus dem Dosier-Protokoll ({dosen.Count} Dosen)"
                    + (ohnePreis.Count > 0 ? " — Mittel ohne Preis fehlen" : "")
                    + "; Handzugaben stehen in keinem Protokoll";
            }
            else
            {
                duengerHerkunft = "Dosen protokolliert, aber kein Mittel hat einen Preis — auf der Dosierungs-Seite je Pumpe eintragen";
            }
        }

        double? summeEur = stromEur is null && duengerEur is null
            ? null
            : Math.Round((stromEur ?? 0) + (duengerEur ?? 0), 2);

        double? proGramm = summeEur is { } s && trockenGramm is { } g && g > 0
            ? Math.Round(s / g, 2)
            : null;

        return new GrowKosten(stromEur, stromHerkunft, duengerEur, duengerHerkunft, ohnePreis, summeEur, proGramm);
    }

    private sealed class KostenEinstellungen
    {
        public double? StrompreisCentProKwh { get; set; }
    }
}
