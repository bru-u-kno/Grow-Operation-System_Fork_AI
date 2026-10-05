using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Serves the sensor history Grow OS has been recording all along: raw readings (kept for
/// 7 days, one every 5 minutes) and the permanent daily statistics (min/median/max).
/// This is what the charts, the trend context and the watchdog read from.
/// </summary>
[ApiController]
[Route("api/tents")]
[Produces("application/json")]
public sealed class SensorHistoryApiController : ApiControllerBase
{
    public const string Daily = "daily";
    public const string Raw = "raw";

    private const int MaxDays = 365;
    /// <summary>Wie viele Messgrößen ein Abruf höchstens liefert — der Rest fällt still weg.</summary>
    /// <remarks>
    /// <para>16 statt 12 seit den Außenwerten (Raumluft am Controller): die
    /// Live-Seite holt alle Kurven des Verlaufsdiagramms in EINEM Abruf
    /// (<c>VERLAUFS_METRIKEN</c>, jetzt 14). Mit 12 wären die beiden letzten
    /// ohne Fehlermeldung abgeschnitten worden — ihre Wertekarten fehlten
    /// einfach. Die Grenze bleibt, weil jede Messgröße eine eigene Abfrage über
    /// bis zu sieben Tage Rohwerte ist; 16 lässt zwei Plätze Luft.</para>
    /// <para>Gehalten von <c>messgroessen-vollstaendig.test.ts</c>: die
    /// Liste der Oberfläche muss hier hineinpassen.</para>
    /// </remarks>
    public const int MaxMetrics = 16;

    private readonly GrowRepository _repository;
    private readonly SensorReadingRepository _readings;

    public SensorHistoryApiController(GrowRepository repository, SensorReadingRepository readings)
    {
        _repository = repository;
        _readings = readings;
    }

    /// <summary>
    /// Picks the resolution: raw detail only makes sense for a short window (and is only
    /// retained for 7 days), so anything longer falls back to the daily statistics.
    /// </summary>
    public static string ResolveResolution(string? requested, int days) => requested?.Trim().ToLowerInvariant() switch
    {
        Raw => Raw,
        Daily => Daily,
        _ => days <= 2 ? Raw : Daily,
    };

    /// <summary>Clamps the requested window to something the database can actually answer.</summary>
    public static int ClampDays(int days) => days < 1 ? 1 : days > MaxDays ? MaxDays : days;

    /// <summary>Splits the comma-separated metric list, trimming blanks and duplicates.</summary>
    public static IReadOnlyList<string> ParseMetrics(string? metrics) => (metrics ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxMetrics)
        .ToList();

    [HttpGet("{tentId:int}/history")]
    [ProducesResponseType(typeof(TentHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<TentHistoryDto> History(
        int tentId,
        [FromQuery] string? metrics,
        [FromQuery] int days = 14,
        [FromQuery] string? resolution = null)
    {
        if (_repository.GetTent(tentId) is null)
        {
            return NotFoundError("tent_not_found", $"Zelt mit Id {tentId} existiert nicht.");
        }

        var keys = ParseMetrics(metrics);
        if (keys.Count == 0)
        {
            return BadRequestError("metrics_required", "Bitte mindestens eine Messgroesse angeben (metrics=reservoir-ph,...).");
        }

        var window = ClampDays(days);
        var mode = ResolveResolution(resolution, window);
        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-window);

        var series = keys.Select(key => mode == Raw
                ? BuildRawSeries(tentId, key, fromUtc, toUtc)
                : BuildDailySeries(tentId, key, fromUtc, toUtc))
            .ToList();

        return Ok(new TentHistoryDto(tentId, mode, fromUtc, toUtc, series));
    }

    private HistorySeriesDto BuildRawSeries(int tentId, string metricKey, DateTime fromUtc, DateTime toUtc)
    {
        var readings = _readings.GetReadings(tentId, metricKey, fromUtc, toUtc);
        var points = readings
            .Select(reading => new HistoryPointDto(reading.CapturedAtUtc, reading.Value, null, null))
            .ToList();

        var (label, unit) = AlertEvaluationService.MetricDisplay(metricKey);
        return new HistorySeriesDto(metricKey, label, readings.FirstOrDefault()?.Unit ?? Normalize(unit), points);
    }

    private HistorySeriesDto BuildDailySeries(int tentId, string metricKey, DateTime fromUtc, DateTime toUtc)
    {
        var stats = _readings.GetDailyStats(
            tentId, metricKey, DateOnly.FromDateTime(fromUtc), DateOnly.FromDateTime(toUtc));

        // Median carries the day (robust against sensor spikes); min/max become the band.
        // Der Stichtag ist ein LOKALER Kalendertag — als Zeitpunkt gehoert ihm
        // die lokale Mitternacht, nicht die von UTC; sonst wandert der Punkt im
        // Chart um den Zeitzonen-Versatz neben seine Tagesbeschriftung.
        var points = stats
            .Select(stat => new HistoryPointDto(
                stat.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime(), stat.Median, stat.Min, stat.Max))
            .ToList();

        var (label, unit) = AlertEvaluationService.MetricDisplay(metricKey);
        return new HistorySeriesDto(metricKey, label, stats.FirstOrDefault()?.Unit ?? Normalize(unit), points);
    }

    // MetricDisplay returns units ready for message text (" °C"); the API wants them bare.
    private static string? Normalize(string unit)
    {
        var trimmed = unit.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
