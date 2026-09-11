using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Ein Zielwert, so wie er auf der Kachel steht.</summary>
/// <param name="VomWochenplan">
/// Wahr, wenn die Zahl aus dem Feed-Chart kommt und nicht aus dem Profil —
/// die Oberfläche hebt genau diese hervor.
/// </param>
public sealed record GeltendesZielDto(string Key, string Label, string Wert, bool VomWochenplan);

/// <summary>Was für einen laufenden Grow gerade als Ziel gilt — und woher es kommt.</summary>
public sealed record GeltendeZieleDto(
    int GrowId,
    string GrowName,
    string Phase,
    string ProfilName,
    string ProfilHerkunft,
    string? Wochenplan,
    string? Haltehinweis,
    string? Anmischen,
    List<GeltendesZielDto> Werte);

/// <summary>
/// Fork AI: die Auskunft „gilt gerade“ für die Seite Sollwert-Profile.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Die Seite zeigt Profile je Phase. Wer dort „Blüte:
/// EC 1,0–1,2“ liest, sein Messprotokoll aber gegen 1,1–1,3 bewertet sieht,
/// hat keinen Fehler gefunden, sondern das Feed-Chart: es überschreibt EC und
/// pH wochenweise. Ohne diese Auskunft steht die Seite da wie die einzige
/// Wahrheit, obwohl sie nur der erste von drei Schritten ist.</para>
///
/// <para><b>Nichts gerechnet, nur gezeigt.</b> Das Band kommt aus
/// <see cref="Zielband.FuerGrow"/> und die Lesart je Messgröße aus
/// <see cref="Zielband.FuerMetrik"/> — dieselben zwei Aufrufe, die auch die
/// Kacheln, das Messprotokoll und die Planziel-Alarme benutzen. Eine eigene
/// Rechnung hier wäre genau die zweite Auskunft, die Zielband abschaffen
/// sollte.</para>
///
/// <para><b>Alle laufenden Grows, kein Umschalter.</b> Die Seite gehört zu den
/// Einstellungen und hat keinen Grow-Bezug. Ein Auswahlfeld dort wäre Bedienung
/// für einen Fall, den es meistens nicht gibt; laufen zwei Grows, stehen eben
/// zwei Karten da.</para>
/// </remarks>
[ApiController]
[Route("api/setpoint-profiles/gilt-gerade")]
[Produces("application/json")]
public sealed class GeltendeZieleApiController : ApiControllerBase
{
    /// <summary>Die Chips in der Reihenfolge der Profiltabelle.</summary>
    private static readonly (string Key, string Label)[] Messgroessen =
    [
        ("reservoir-ph", "pH"),
        ("reservoir-ec", "EC"),
        ("orp", "ORP"),
        ("reservoir-temp", "H₂O °C"),
        ("vpd", "VPD"),
        ("ppfd", "PPFD"),
        ("co2", "CO₂"),
    ];

    private readonly GrowRepository _grows;
    private readonly HydroSetupRepository _setups;
    private readonly SetpointProfileRepository _profiles;
    private readonly TargetValueService _targets;
    private readonly KnowledgeBaseLoader _wissen;

    public GeltendeZieleApiController(
        GrowRepository grows,
        HydroSetupRepository setups,
        SetpointProfileRepository profiles,
        TargetValueService targets,
        KnowledgeBaseLoader wissen)
    {
        _grows = grows;
        _setups = setups;
        _profiles = profiles;
        _targets = targets;
        _wissen = wissen;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GeltendeZieleDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<GeltendeZieleDto>> Get()
    {
        var heute = DateTime.Today;
        var liste = new List<GeltendeZieleDto>();

        foreach (var grow in _grows.GetActiveGrows())
        {
            var stage = GrowStageResolver.Resolve(grow, heute);
            var systemProfil = grow.SystemId is { } systemId
                ? _setups.GetSystem(systemId)?.SetpointProfileId
                : null;

            var profil = SetpointProfileResolver.Resolve(grow.SetpointProfileId, systemProfil, grow.HydroStyle);

            // Ohne Band gibt es nichts zu zeigen: das Profil kennt die Phase
            // nicht (z. B. Steckling in einem Profil ohne Stecklingswerte).
            var band = Zielband.FuerGrow(_targets, _wissen, grow, stage, systemProfil, eigeneGrenzen: null);
            if (band is null) continue;

            var chart = MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms);

            // Wie auf der Kachel: die Nachtabsenkung zieht die Untergrenze der
            // Wassertemperatur mit nach unten.
            var rampenBoden = Wasserband.RampenBodenC(
                grow,
                _targets.GetTargets(profil.ProfileId, GrowStage.Flower),
                _targets.GetTargets(profil.ProfileId, GrowStage.Finish));

            var werte = new List<GeltendesZielDto>();
            foreach (var (key, label) in Messgroessen)
            {
                var (min, max) = Zielband.FuerMetrik(key, band, key == "reservoir-temp" ? rampenBoden : null);
                if (Spanne(min, max) is not { } text) continue;

                var vomChart = chart is { } c && (
                    (key == "reservoir-ec" && c.Spalte.EcTarget is not null)
                    || (key == "reservoir-ph" && c.Spalte.PhMin is not null && c.Spalte.PhMax is not null));

                werte.Add(new GeltendesZielDto(key, label, text, vomChart));
            }

            liste.Add(new GeltendeZieleDto(
                grow.Id,
                string.IsNullOrWhiteSpace(grow.Name) ? $"Grow {grow.Id}" : grow.Name,
                StageLabel(stage),
                ProfilName(profil.ProfileId),
                HerkunftText(profil.Origin),
                chart?.Herkunft,
                chart is { } gehalten ? Haltehinweis(grow, gehalten.Spalte) : null,
                chart is { } spalte ? Anmischen(spalte.Spalte) : null,
                werte));
        }

        return Ok(liste);
    }

    /// <summary>
    /// Steht der Plan auf seiner letzten Spalte, während die Phase weiterläuft?
    /// </summary>
    /// <remarks>
    /// Der Fall tritt bei jeder gestreckten Vegi ein: das Chart endet bei Vega
    /// W4, der Grow ist in Woche 8. <c>SpalteFuer</c> hält dann die letzte
    /// Spalte — sinnvoll, aber ohne diesen Satz sieht es aus, als sei der Plan
    /// stehengeblieben oder falsch.
    /// </remarks>
    private static string? Haltehinweis(GrowRun grow, FeedChartColumn spalte)
    {
        if (spalte.Week is not { } spaltenWoche) return null;

        var ist = MischplanService.WocheInPhase(grow, spalte.Stage);
        return ist > spaltenWoche
            ? $"gehalten seit Woche {spaltenWoche + 1} — du bist in Woche {ist} dieser Phase"
            : null;
    }

    /// <summary>
    /// Was das Chart zum Anmischen sagt — der Punktwert, nicht das Band.
    /// </summary>
    /// <remarks>
    /// Beim pH nennt das Chart eine Zahl, gemessen wird aber gegen den
    /// Handlungsbereich (5,8–6,2). Beides steht da: oben, wogegen bewertet
    /// wird, darunter, worauf man anmischt. Ohne die zweite Zeile sähe es aus,
    /// als hätte das Chart 5,8–6,2 gesagt.
    /// </remarks>
    private static string? Anmischen(FeedChartColumn spalte)
    {
        var teile = new List<string>();
        if (spalte.EcTarget is { } ec) teile.Add($"EC {Zahl(ec)}");
        if (spalte.PhMin is { } min && spalte.PhMax is { } max)
        {
            teile.Add($"pH {(Math.Abs(min - max) < 0.001 ? Zahl(min) : $"{Zahl(min)}–{Zahl(max)}")}");
        }

        return teile.Count == 0 ? null : string.Join(" · ", teile);
    }

    private string ProfilName(string profileId)
    {
        if (SetpointProfile.IdFromReference(profileId) is { } eigenes)
        {
            return _profiles.Get(eigenes)?.Name ?? profileId;
        }

        return profileId switch
        {
            "rdwc-default" => "RDWC Standard",
            "dwc-default" => "DWC Standard",
            _ => profileId,
        };
    }

    private static string HerkunftText(ProfileOrigin origin) => origin switch
    {
        ProfileOrigin.Grow => "am Grow gewählt",
        ProfileOrigin.System => "vom System geerbt",
        _ => "aus dem Anbaustil",
    };

    private static string StageLabel(GrowStage stage) => stage switch
    {
        GrowStage.Seedling => "Sämling",
        GrowStage.Clone => "Steckling",
        GrowStage.Veg => "Vegetativ",
        GrowStage.Transition => "Transition",
        GrowStage.Flower => "Blüte",
        GrowStage.Finish => "Finish",
        _ => stage.ToString(),
    };

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
