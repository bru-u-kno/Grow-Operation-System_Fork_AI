using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Eine Woche des Plans, so wie sie auf der Seite steht.</summary>
public sealed record WochenplanWocheDto(
    string Id,
    string Label,
    string Stage,
    int? Woche,
    bool IstJetzt,
    bool WirdGehalten,
    string? Ec,
    string? Ph,
    string? Wasser,
    string? Vpd,
    string? Rh,
    string? Luft,
    string? Co2,
    string? Ppfd,
    string? Dosierung);

/// <summary>Ein Wert, den der Sync betreut — für den Block „Übergabe an HA".</summary>
public sealed record WochenplanUebergabeDto(string Rolle, string Name, string EntityId, string Wert, string Zustand);

/// <summary>Der Plan eines laufenden Durchgangs.</summary>
public sealed record WochenplanDto(
    int GrowId,
    string GrowName,
    string? Sorte,
    string ProgrammName,
    bool WochenZieleAktiv,
    string? VegiStart,
    string? Flip,
    string? Erntefenster,
    string? JetztLabel,
    string? Haltehinweis,
    List<WochenplanWocheDto> Wochen,
    List<WochenplanUebergabeDto> Uebergabe,
    string? LetzteUebergabe);

/// <summary>Ein bearbeitbares Feld einer Woche (F-004).</summary>
public sealed record WochenwertFeldDto(
    string Feld,
    string Bezeichnung,
    string Einheit,
    double Min,
    double Max,
    double Schritt,
    double? Wert,
    double? Plan,
    bool Geaendert);

/// <summary>Eine Woche im Bearbeiten-Modus (F-004).</summary>
public sealed record WochenwertSpalteDto(
    string Id,
    string Label,
    string Stage,
    int? Woche,
    bool IstJetzt,
    List<WochenwertFeldDto> Felder);

/// <summary>Alle bearbeitbaren Wochen eines Grows (F-004).</summary>
public sealed record WochenwerteDto(
    int GrowId,
    string ProgrammId,
    string ProgrammName,
    int AndereGrows,
    List<WochenwertSpalteDto> Spalten);

/// <summary>Antwort auf das Speichern (F-004).</summary>
public sealed record WochenwerteGespeichertDto(WochenwerteDto Werte, int Uebergeben, string? Hinweis);

/// <summary>
/// Fork AI: die Seite „Wochenplan" — was der Plan für diesen Durchgang vorgibt.
/// </summary>
/// <remarks>
/// <para><b>Warum eigene Seite.</b> Die Wochenwerte wirken seit forkai.46
/// überall, waren aber nirgends am Stück zu sehen: EC und pH im Mischplan, das
/// Klima nur indirekt über die Kacheln. Wer wissen will, was nächste Woche
/// passiert, musste die Wissensdatenbank aufschlagen.</para>
///
/// <para><b>Nichts gerechnet.</b> Die Spalten kommen aus dem Düngeprogramm, die
/// laufende aus <see cref="MischplanService.ZielSpalteFuerGrow"/> — dieselbe
/// Auswahl, die auch Kacheln und Alarme benutzen. Die Anker (Vegi-Start, Flip)
/// stehen am Grow; das Erntefenster folgt den Blütewochen der Sorte.</para>
/// </remarks>
[ApiController]
[Route("api/wochenplan")]
[Produces("application/json")]
public sealed class WochenplanApiController : ApiControllerBase
{
    private static readonly Dictionary<string, string> Rollennamen = new()
    {
        [WochenplanSyncService.Rollen.WasserTag] = "Chiller Tag",
        [WochenplanSyncService.Rollen.WasserNacht] = "Chiller Nacht",
        [WochenplanSyncService.Rollen.RhObergrenze] = "RH-Obergrenze",
        [WochenplanSyncService.Rollen.Co2Ziel] = "CO₂-Ziel",
        [WochenplanSyncService.Rollen.LuftUnten] = "Alarmgrenze Luft unten",
        [WochenplanSyncService.Rollen.LuftNachtUnten] = "Alarmgrenze Luft unten (Nacht)",
        [WochenplanSyncService.Rollen.LuftNachtOben] = "Alarmgrenze Luft oben (Nacht)",
        [WochenplanSyncService.Rollen.LuftOben] = "Alarmgrenze Luft oben",
        [WochenplanSyncService.Rollen.FeuchteOben] = "Alarmgrenze Luftfeuchte",
    };

    private readonly GrowRepository _grows;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly WochenplanSyncService _sync;
    private readonly WochenwertRepository _wochenwerte;
    private readonly WochenwertUeberlagerung _ueberlagerung;
    private readonly GrowPlanService _plaene;

    public WochenplanApiController(
        GrowRepository grows,
        KnowledgeBaseLoader wissen,
        WochenplanSyncService sync,
        WochenwertRepository wochenwerte,
        WochenwertUeberlagerung ueberlagerung,
        GrowPlanService plaene)
    {
        _plaene = plaene;
        _grows = grows;
        _wissen = wissen;
        _sync = sync;
        _wochenwerte = wochenwerte;
        _ueberlagerung = ueberlagerung;
    }

    /// <summary>F-004: alle Wochen des Programms eines Grows, mit Planwert und eigenem Wert.</summary>
    [HttpGet("werte/{growId:int}")]
    [ProducesResponseType(typeof(WochenwerteDto), StatusCodes.Status200OK)]
    public ActionResult<WochenwerteDto> Werte(int growId)
    {
        if (Programm(growId) is not { } gefunden) return KeinProgramm();
        return Ok(WerteDto(gefunden.Grow, gefunden.Programm));
    }

    /// <summary>
    /// F-004: Änderungen aus dem Bearbeiten-Modus speichern und gleich an Home Assistant übergeben.
    /// </summary>
    /// <remarks>
    /// <para>Alle Änderungen werden zuerst gemeinsam geprüft; ist eine
    /// ungültig, wird keine gespeichert. Ein halb gespeicherter Plan wäre
    /// schlimmer als ein abgelehnter — der Speicherbalken sammelt ja gerade,
    /// damit die Werte zusammenpassen (etwa „VPD von" nicht über „VPD bis").</para>
    ///
    /// <para>Die Werte gelten für das <b>Programm</b>, also für jeden Grow, der
    /// es benutzt. Die Seite sagt das dazu (<c>AndereGrows</c>).</para>
    ///
    /// <para>Dass die Übergabe an HA fehlschlägt, macht das Speichern nicht
    /// ungültig: gespeichert ist gespeichert, der tägliche Lauf um 06:00 holt
    /// es nach. Die Antwort sagt dann, was los ist.</para>
    /// </remarks>
    [HttpPost("werte/{growId:int}")]
    [ProducesResponseType(typeof(WochenwerteGespeichertDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WochenwerteGespeichertDto>> WerteSpeichern(
        int growId, [FromBody] WochenwerteSpeichernRequest anfrage, CancellationToken ct)
    {
        if (Programm(growId) is not { } gefunden) return KeinProgramm();
        var (grow, programm) = gefunden;
        var spalten = programm.FeedChart!.Columns;

        if (anfrage.Aenderungen.Count == 0) return ValidationError("Es gibt nichts zu speichern.");

        var geprueft = new List<(string SpalteId, string Feld, double? Wert)>();
        // Was nach dem Speichern in jeder betroffenen Spalte stünde — für die Paar-Prüfung.
        var danach = new Dictionary<(string Spalte, string Feld), double?>();

        foreach (var aenderung in anfrage.Aenderungen)
        {
            var spalte = spalten.FirstOrDefault(s => string.Equals(s.Id, aenderung.SpalteId, StringComparison.OrdinalIgnoreCase));
            if (spalte is null)
                return ValidationError($"Die Woche „{aenderung.SpalteId}“ gibt es im Programm {programm.Name} nicht.");

            if (Wochenwertfelder.Finden(aenderung.Feld) is not { } feld)
                return ValidationError($"„{aenderung.Feld}“ lässt sich nicht bearbeiten.");

            if (aenderung.Wert is { } wert)
            {
                if (double.IsNaN(wert) || double.IsInfinity(wert) || wert < feld.Min || wert > feld.Max)
                    return ValidationError(
                        $"{spalte.Label}: {feld.Bezeichnung} muss zwischen {Zahl(feld.Min)} und {Zahl(feld.Max)} liegen.");
            }

            var neu = aenderung.Wert ?? Planwert(grow, programm, spalte, feld);
            danach[(spalte.Id, feld.Name)] = neu;
            geprueft.Add((spalte.Id, feld.Name, aenderung.Wert));
        }

        foreach (var spalte in spalten)
        {
            foreach (var feld in Wochenwertfelder.Alle.Where(f => f.Paar is not null))
            {
                var partner = Wochenwertfelder.Finden(feld.Paar!)!;
                var von = danach.TryGetValue((spalte.Id, feld.Name), out var a) ? a : feld.Lesen(spalte);
                var bis = danach.TryGetValue((spalte.Id, partner.Name), out var b) ? b : partner.Lesen(spalte);
                if (von is { } v && bis is { } z && v > z + 1e-9)
                    return ValidationError(
                        $"{spalte.Label}: {feld.Bezeichnung} ({Zahl(v)}) liegt über {partner.Bezeichnung} ({Zahl(z)}).");
            }
        }

        // Ein Wert, der dem Plan entspricht, ist keine Abweichung — dann wird
        // die Zeile gelöscht statt eine Kopie des Planwerts abzulegen.
        var zuSchreiben = geprueft
            .Select(g =>
            {
                var spalte = spalten.First(s => s.Id == g.SpalteId);
                var plan = Planwert(grow, programm, spalte, Wochenwertfelder.Finden(g.Feld)!);
                return g.Wert is { } w && plan is { } p && Math.Abs(w - p) < 1e-9 ? (g.SpalteId, g.Feld, (double?)null) : g;
            })
            .ToList();

        // Fork AI (Grow-Plan): hat der Grow einen Plan, gehört die Änderung in
        // den Plan — nicht ins Programm, das andere Grows mitbenutzen.
        if (GrowPlanService.HatPlan(grow.Id))
        {
            _plaene.WerteSetzen(grow.Id, zuSchreiben);
        }
        else
        {
            _wochenwerte.Speichern(programm.Id, zuSchreiben);
            _ueberlagerung.Auffrischen();
        }

        var uebergeben = 0;
        string? hinweis = null;
        try
        {
            uebergeben = await _sync.UebergebenAsync(ct);
        }
        catch (Exception)
        {
            hinweis = "Gespeichert. Home Assistant war gerade nicht erreichbar — die Übergabe holt der nächste Lauf nach.";
        }

        return Ok(new WochenwerteGespeichertDto(WerteDto(grow, programm), uebergeben, hinweis));
    }

    private (GrowRun Grow, NutrientProgramDefinition Programm)? Programm(int growId)
    {
        if (_grows.GetGrow(growId) is not { } grow) return null;
        var programm = MischplanService.ProgrammFuerGrow(grow, _wissen.NutrientPrograms);
        return programm?.FeedChart is { Columns.Count: > 0 } ? (grow, programm) : null;
    }

    /// <summary>Der Vergleichswert: Startstand des Grow-Plans, sonst der Dateiwert des Programms.</summary>
    private double? Planwert(GrowRun grow, NutrientProgramDefinition programm, FeedChartColumn spalte, Wochenwertfelder.Feld feld)
        => GrowPlanService.HatPlan(grow.Id)
            ? _plaene.Startwert(grow.Id, spalte.Id, feld)
            : _ueberlagerung.Planwert(spalte, feld);

    private bool IstGeaendert(GrowRun grow, NutrientProgramDefinition programm, FeedChartColumn spalte, Wochenwertfelder.Feld feld)
    {
        if (!GrowPlanService.HatPlan(grow.Id)) return _ueberlagerung.IstGeaendert(programm.Id, spalte.Id, feld.Name);
        var start = _plaene.Startwert(grow.Id, spalte.Id, feld);
        var jetzt = feld.Lesen(spalte);
        return start is null ? jetzt is not null : jetzt is null || Math.Abs(start.Value - jetzt.Value) > 1e-9;
    }

    private ActionResult KeinProgramm()
        => NotFoundError("wochenplan_ohne_programm", "Dieser Grow hat kein Düngeprogramm mit Wochenplan.");

    private WochenwerteDto WerteDto(GrowRun grow, NutrientProgramDefinition programm)
    {
        var aktiveId = MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms)?.Spalte.Id;
        // Mit eigenem Plan betrifft eine Änderung nur diesen Grow.
        var andere = GrowPlanService.HatPlan(grow.Id) ? 0 : _grows.GetActiveGrows().Count(g =>
            g.Id != grow.Id && !GrowPlanService.HatPlan(g.Id)
            && string.Equals(g.FeedProgramId, programm.Id, StringComparison.OrdinalIgnoreCase));

        return new WochenwerteDto(
            grow.Id,
            programm.Id,
            programm.Name,
            andere,
            programm.FeedChart!.Columns
                .Select(spalte => new WochenwertSpalteDto(
                    spalte.Id,
                    spalte.Label,
                    spalte.Stage,
                    spalte.Week,
                    spalte.Id == aktiveId,
                    Wochenwertfelder.Alle
                        .Select(feld => new WochenwertFeldDto(
                            feld.Name,
                            feld.Bezeichnung,
                            feld.Einheit,
                            feld.Min,
                            feld.Max,
                            feld.Schritt,
                            feld.Lesen(spalte),
                            Planwert(grow, programm, spalte, feld),
                            IstGeaendert(grow, programm, spalte, feld)))
                        .ToList()))
                .ToList());
    }

    /// <summary>Jetzt übergeben, ohne auf 06:00 oder den Wochenwechsel zu warten.</summary>
    [HttpPost("uebergeben")]
    public async Task<ActionResult<object>> Uebergeben(CancellationToken ct)
        => Ok(new { geschrieben = await _sync.UebergebenAsync(ct) });

    /// <summary>Einen von Hand verstellten Helfer wieder dem Plan überlassen.</summary>
    [HttpPost("freigeben/{rolle}")]
    public async Task<ActionResult> Freigeben(string rolle, CancellationToken ct)
    {
        // Fork AI (forkai.130): sofort übergeben, nicht erst beim Tageslauf um 06:00.
        await _sync.FreigebenUndUebergebenAsync(rolle, ct);
        return NoContent();
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WochenplanDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<WochenplanDto>> Get()
    {
        var liste = new List<WochenplanDto>();

        foreach (var grow in _grows.GetActiveGrows())
        {
            var programm = MischplanService.ProgrammFuerGrow(grow, _wissen.NutrientPrograms);

            if (programm?.FeedChart is not { } chart || chart.Columns.Count == 0) continue;

            var jetzt = MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms);
            var aktiveId = jetzt?.Spalte.Id;

            var wochen = chart.Columns
                .Select(spalte => Zeile(spalte, istJetzt: spalte.Id == aktiveId, grow))
                .ToList();

            liste.Add(new WochenplanDto(
                grow.Id,
                string.IsNullOrWhiteSpace(grow.Name) ? $"Grow {grow.Id}" : grow.Name,
                grow.Strain,
                programm.Name,
                // Ohne den Haken am Addback gilt der Plan nur fürs Anmischen,
                // nicht für Kacheln und Alarme. Das muss auf der Seite stehen,
                // sonst liest man Zahlen, die nirgends wirken.
                MischplanService.NutztWochenziele(grow),
                Datum(grow.VegStartedAt),
                Datum(grow.FlipDate),
                Erntefenster(grow),
                jetzt?.Spalte.Label,
                jetzt is { } j ? Haltehinweis(grow, j.Spalte) : null,
                wochen,
                _sync.Sollwerte()
                    .Select(u => new WochenplanUebergabeDto(
                        u.Rolle,
                        Rollennamen.GetValueOrDefault(u.Rolle, u.Rolle),
                        u.EntityId, // Helfer-Kennung oder zelt:{id}/{metrik}/{grenze}
                        Zahl(u.Wert),
                        u.Zustand))
                    .ToList(),
                _sync.Stand.LetzterLauf));
        }

        return Ok(liste);
    }

    private static WochenplanWocheDto Zeile(FeedChartColumn spalte, bool istJetzt, GrowRun grow)
        => new(
            spalte.Id,
            spalte.Label,
            spalte.Stage,
            spalte.Week,
            istJetzt,
            istJetzt && Haltehinweis(grow, spalte) is not null,
            spalte.EcTarget is { } ec ? Zahl(ec) : null,
            Spanne(spalte.PhMin, spalte.PhMax),
            Wasser(spalte),
            Spanne(spalte.VpdMin, spalte.VpdMax),
            spalte.RhMax is { } rh ? $"max {Zahl(rh)} %" : null,
            spalte.AirTempC is { } luft ? $"{Zahl(luft)} °C" : null,
            Spanne(spalte.Co2Min, spalte.Co2Max),
            Spanne(spalte.PpfdMin, spalte.PpfdMax),
            Dosierung(spalte));

    /// <summary>Die zwei, drei Komponenten, die beim Anmischen wirklich zählen.</summary>
    /// <remarks>
    /// Alle sechs würden die Zeile sprengen; die Vollständigkeit steht im
    /// Mischplan, der dafür gebaut ist.
    /// </remarks>
    private static string? Dosierung(FeedChartColumn spalte)
    {
        var teile = spalte.Items
            .Take(3)
            .Select(i => $"{i.Component} {Zahl(i.MinMlPerLiter)}")
            .ToList();

        return teile.Count == 0 ? null : string.Join(" · ", teile);
    }

    private static string? Wasser(FeedChartColumn spalte)
    {
        if (spalte.WaterTempDayC is not { } tag) return null;
        return spalte.WaterTempNightC is { } nacht && Math.Abs(tag - nacht) > 0.01
            ? $"{Zahl(tag)} / {Zahl(nacht)} °C"
            : $"{Zahl(tag)} °C";
    }

    /// <summary>
    /// Wann geerntet werden kann — Flip plus die Blütewochen der Sorte.
    /// </summary>
    /// <remarks>
    /// Ohne Flip gibt es kein Fenster: vor dem Umstellen ist die Blütedauer eine
    /// Eigenschaft der Sorte, kein Datum. Lieber nichts anzeigen als ein Datum,
    /// das sich beim Flip um Wochen verschiebt.
    /// </remarks>
    private static string? Erntefenster(GrowRun grow)
    {
        if (grow.FlipDate is not { } flip) return null;
        if (grow.BreederFlowerWeeksMin is not { } min && grow.BreederFlowerWeeksMax is not { } _) return null;

        var von = flip.AddDays(7 * (grow.BreederFlowerWeeksMin ?? grow.BreederFlowerWeeksMax!.Value));
        var bis = flip.AddDays(7 * (grow.BreederFlowerWeeksMax ?? grow.BreederFlowerWeeksMin!.Value));

        return von.Date == bis.Date
            ? von.ToString("dd.MM.", AppCulture.German)
            : $"{von:dd.MM.}–{bis:dd.MM.}";
    }

    private static string? Haltehinweis(GrowRun grow, FeedChartColumn spalte)
    {
        if (spalte.Week is not { } spaltenWoche) return null;

        var ist = MischplanService.WocheInPhase(grow, spalte.Stage);
        return ist > spaltenWoche
            ? $"gehalten seit Woche {spaltenWoche + 1} — du bist in Woche {ist} dieser Phase"
            : null;
    }

    private static string? Datum(DateTime? wert)
        => wert?.ToString("dd.MM.yyyy", AppCulture.German);

    private static string? Spanne(double? min, double? max)
    {
        if (min is null && max is null) return null;
        if (min is null) return $"bis {Zahl(max!.Value)}";
        if (max is null) return $"ab {Zahl(min.Value)}";
        return Math.Abs(min.Value - max.Value) < 0.001
            ? Zahl(min.Value)
            : $"{Zahl(min.Value)}–{Zahl(max.Value)}";
    }

    private static string Zahl(double wert) => wert.ToString("0.##", AppCulture.German);
}
