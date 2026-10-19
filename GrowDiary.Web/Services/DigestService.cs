using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Builds and sends the once-a-day summary push: for each tent, its current live values
/// and whether anything needs attention. Two formats — a short "all OK / N issues" line,
/// or the full values per tent.
/// </summary>
public sealed class DigestService
{
    private readonly GrowRepository _repository;
    private readonly HomeAssistantService _homeAssistant;
    private readonly NotificationService _notifications;

    public DigestService(GrowRepository repository, HomeAssistantService homeAssistant, NotificationService notifications)
    {
        _repository = repository;
        _homeAssistant = homeAssistant;
        _notifications = notifications;
    }

    public readonly record struct DigestTent(string Name, int OpenRisks, IReadOnlyList<(string Label, string Value)> Metrics)
    {
        public bool Ok => OpenRisks == 0;
    }

    // Which live metrics go into the detailed digest, in order.
    private static readonly (string Key, string Label, string Unit)[] DigestMetrics =
    {
        ("reservoir-ph", "pH", ""),
        ("reservoir-ec", "EC", ""),
        ("reservoir-temp", "Wasser", "°C"),
        ("temperature", "Luft", "°C"),
        ("humidity", "RLF", "%"),
    };

    /// <summary>Pure formatter (testable): builds the whole push message from the per-tent digest.</summary>
    public static string BuildMessage(IReadOnlyList<DigestTent> tents, bool detailed)
    {
        if (tents.Count == 0)
        {
            return "Noch kein Zelt eingerichtet.";
        }

        var totalIssues = tents.Sum(tent => tent.OpenRisks);
        var lines = new List<string>
        {
            totalIssues == 0
                ? $"Alles im grünen Bereich — {tents.Count} Zelt(e) laufen."
                : $"{totalIssues} offene(r) Hinweis(e) über {tents.Count} Zelt(e)."
        };

        foreach (var tent in tents)
        {
            var head = tent.Ok ? $"✅ {tent.Name}" : $"⚠️ {tent.Name} — {tent.OpenRisks} Hinweis(e)";
            if (detailed && tent.Metrics.Count > 0)
            {
                var values = string.Join(" · ", tent.Metrics.Select(metric => $"{metric.Label} {metric.Value}"));
                lines.Add($"{head}\n{values}");
            }
            else
            {
                lines.Add(head);
            }
        }

        return string.Join('\n', lines);
    }

    public async Task<bool> BuildAndSendAsync(CancellationToken cancellationToken = default)
    {
        var settings = _notifications.GetSettings();
        if (!settings.IsConfigured || !settings.DailyDigest)
        {
            return false;
        }

        var haSettings = _repository.GetEffectiveHomeAssistantSettings();
        var openRisks = _repository.GetOpenRiskEvents();
        var tents = new List<DigestTent>();

        foreach (var tent in _repository.GetTents())
        {
            var states = await _homeAssistant.GetStatesAsync(haSettings, tent, cancellationToken);
            var risks = openRisks.Count(risk => risk.TentId == tent.Id);
            tents.Add(new DigestTent(tent.Name, risks, ExtractMetrics(states)));
        }

        var message = BuildMessage(tents, settings.DigestDetailed);
        return await _notifications.SendDigestAsync("🌱 Cultivate Control · Tagesüberblick", message, cancellationToken);
    }

    private static IReadOnlyList<(string, string)> ExtractMetrics(IReadOnlyDictionary<string, HomeAssistantState> states)
    {
        var metrics = new List<(string, string)>();
        foreach (var (key, label, unit) in DigestMetrics)
        {
            if (states.TryGetValue(key, out var state) && state.NumericValue is { } value)
            {
                metrics.Add((label, $"{value.ToString("0.#", CultureInfo.InvariantCulture)}{unit}"));
            }
        }

        return metrics;
    }
}
