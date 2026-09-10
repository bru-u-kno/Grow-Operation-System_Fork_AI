using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Evaluates a tent's alert rules against its current live sensor values and pushes a
/// Home Assistant notification when a threshold is crossed. Level-triggered: while a value
/// stays out of range it re-notifies every CooldownMinutes (the repeat interval), so a
/// persistent breach keeps reminding instead of firing once and going silent.
/// </summary>
public sealed class AlertEvaluationService
{
    private readonly AlertRuleRepository _rules;
    private readonly NotificationService _notifications;
    private readonly LightRepository? _lights;
    private readonly GrowRepository? _grows;
    private readonly HarvestRepository? _harvests;
    private readonly TargetValueService? _targetValues;
    private readonly Services.Knowledge.KnowledgeBaseLoader? _knowledge;
    private readonly HydroSetupRepository? _hydroSetups;
    private readonly ILogger<AlertEvaluationService> _logger;

    public AlertEvaluationService(
        AlertRuleRepository rules,
        NotificationService notifications,
        ILogger<AlertEvaluationService> logger,
        LightRepository? lights = null,
        GrowRepository? grows = null,
        HarvestRepository? harvests = null,
        TargetValueService? targetValues = null,
        Services.Knowledge.KnowledgeBaseLoader? knowledge = null,
        HydroSetupRepository? hydroSetups = null)
    {
        _rules = rules;
        _notifications = notifications;
        _lights = lights;
        _grows = grows;
        _harvests = harvests;
        _targetValues = targetValues;
        _knowledge = knowledge;
        _hydroSetups = hydroSetups;
        _logger = logger;
    }

    public const string InRange = "InRange";
    public const string Below = "Below";
    public const string Above = "Above";

    public readonly record struct AlertDecision(string NewState, bool SendBreach, bool SendRecovery);

    /// <summary>
    /// Pure decision logic (no side effects) so it can be unit-tested exhaustively.
    /// Given a rule, the current value and the time, it returns the new persisted state and
    /// whether a breach or recovery notification should be sent.
    /// </summary>
    public static AlertDecision Decide(TentAlertRule rule, double value, DateTime nowUtc)
    {
        var breach =
            rule.MinValue is { } min && value < min ? Below :
            rule.MaxValue is { } max && value > max ? Above :
            InRange;

        if (breach == InRange)
        {
            var recovered = rule.LastState is Below or Above;
            return new AlertDecision(InRange, SendBreach: false, SendRecovery: recovered);
        }

        // Level-triggered: send on the first breach and then again every CooldownMinutes
        // while it stays out of range. CooldownMinutes is the repeat interval — a fresh
        // rule (LastNotifiedUtc == null) fires immediately, e.g. right after saving.
        var cooledDown = rule.LastNotifiedUtc is null
            || (nowUtc - rule.LastNotifiedUtc.Value) >= TimeSpan.FromMinutes(Math.Max(1, rule.CooldownMinutes));

        return new AlertDecision(breach, SendBreach: cooledDown, SendRecovery: false);
    }

    public async Task EvaluateAsync(
        Tent tent,
        IReadOnlyDictionary<string, HomeAssistantState> states,
        CancellationToken cancellationToken = default)
    {
        var rules = _rules.GetEnabledForTent(tent.Id);
        if (rules.Count == 0)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;

        // PPFD, CO₂ und VPD haben nachts kein Ziel: 0 µmol ist bei Licht aus der
        // Sollzustand, CO₂ faellt ohne Verbrauch auf Umgebungsluft, und die
        // VPD-Baender aller Quellen meinen den Tag. Vorher klingelte das Handy
        // jede Nacht — und wer jede Nacht falschen Alarm bekommt, glaubt auch
        // dem echten nicht mehr. Ohne Licht-Sensor und ohne Lichtplan bleibt es
        // beim alten Verhalten.
        states.TryGetValue("light-status", out var lightNow);
        var lights = LightClock.Resolve(lightNow, _lights?.GetActiveLightScheduleForTent(tent.Id), nowUtc);

        // Waehrend der Trocknung ist das Reservoir abgelassen: die Sonden
        // liegen trocken und melden Unsinn — eine pH-Sonde an der Luft zeigt
        // irgendwas um 7 und haette genau in den kritischen Trocknungstagen
        // Alarm um Alarm geschickt. Bewusst NUR im Trocknungsfenster, nicht
        // pauschal „ohne Grow": wer ein Reservoir ohne Grow-Eintrag faehrt und
        // sich Regeln setzt, meint sie ernst.
        var trocknung = DryingWindow.DayFor(_grows, _harvests, tent.Id, DateTime.Today) is not null;

        // Das Zielband der laufenden Phase/Woche — einmal je Durchlauf, nicht je
        // Regel. Nur Regeln mit Quelle „Plan" brauchen es; laeuft kein Grow im
        // Zelt, bleibt es null und genau diese Regeln schweigen (siehe unten).
        var band = PlanbandFuer(tent);

        foreach (var regel in rules)
        {
            // Die Regel, gegen die wirklich gemessen wird: bei „Fest" die
            // eingetragene, bei „Plan" eine Kopie mit den Grenzen aus dem Band.
            var rule = Planzielgrenzen.Wirksam(regel, band.Ziele, band.RampenBodenC);
            if (rule is null)
            {
                // Plan-Regel ohne Band: kein aktiver Grow, keine Phase oder eine
                // Messgroesse, fuer die der Plan nichts hergibt. Schweigen ist
                // hier richtig — eine Grenze, die niemand kennt, darf nicht
                // melden, und ein leeres Zelt hat kein Ziel.
                continue;
            }

            if (lights == LightsNow.Off && LightClock.IsDaytimeOnly(rule.MetricKey))
            {
                continue;
            }

            if (trocknung && DryingWindow.IsReservoirKey(rule.MetricKey))
            {
                continue;
            }

            if (!states.TryGetValue(rule.MetricKey, out var state) || state.NumericValue is not { } value)
            {
                continue;
            }

            var decision = Decide(rule, value, nowUtc);

            try
            {
                if (decision.SendBreach)
                {
                    var sent = await _notifications.SendAsync(
                        NotificationCategory.Threshold, BuildTitle(tent),
                        BuildBreachMessage(rule, value, decision.NewState, band.Herkunft), cancellationToken);
                    if (sent)
                    {
                        _rules.UpdateState(rule.Id, decision.NewState, nowUtc);
                        _logger.LogInformation("Alarm gesendet: Zelt {TentId}, {MetricKey} = {Value}.", tent.Id, rule.MetricKey, value);
                    }
                    // Not sent (quiet hours, notifications unconfigured, or HA unreachable):
                    // keep the previous state so the breach is retried on the next poll and
                    // fires once sending becomes possible, instead of being swallowed silently.
                }
                else if (decision.SendRecovery)
                {
                    // Dieselbe Regel wie beim Alarm eine Etage hoeher: kam die
                    // Nachricht nicht raus (Ruhezeit, HA weg), bleibt der alte
                    // Zustand stehen und der naechste Takt versucht es wieder.
                    // Sonst saehe der Nutzer den Alarm — aber nie die Entwarnung.
                    var sent = await _notifications.SendAsync(
                        NotificationCategory.Threshold, BuildTitle(tent), BuildRecoveryMessage(rule, value), cancellationToken);
                    if (sent)
                    {
                        _rules.UpdateState(rule.Id, decision.NewState, rule.LastNotifiedUtc);
                    }
                }
                else if (!string.Equals(rule.LastState, decision.NewState, StringComparison.Ordinal))
                {
                    _rules.UpdateState(rule.Id, decision.NewState, rule.LastNotifiedUtc);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Alarm-Auswertung fehlgeschlagen: Zelt {TentId}, {MetricKey}.", tent.Id, rule.MetricKey);
            }
        }
    }

    /// <summary>Das Zielband des Zelts, sein Rampenboden und die Herkunft.</summary>
    private readonly record struct Planband(
        HydroTargetValues? Ziele, double? RampenBodenC, string? Herkunft);

    /// <summary>
    /// Die Sollwerte, an denen sich Plan-Regeln orientieren.
    /// </summary>
    /// <remarks>
    /// <para>Dieselbe Kette wie die Live-Kacheln: Profil (Grow → Anlage →
    /// Anbaustil), die Werte der Phase, die Wochenspalte des Feedcharts, wenn
    /// der Grow sie will. Die eigenen Grenzen des Nutzers kommen bewusst NICHT
    /// mit — sonst legte sich eine Alarmregel ueber das Band, aus dem sie
    /// selbst entsteht.</para>
    ///
    /// <para>Der Rampenboden muss mit, weil die Nachtabsenkung die
    /// Wassertemperatur planmaessig unter den Nachtsollwert faehrt. Ohne ihn
    /// meldete der Alarm jede Nacht die eigene Regelung der App.</para>
    /// </remarks>
    private Planband PlanbandFuer(Tent tent)
    {
        if (_targetValues is null || _grows is null)
        {
            return default;
        }

        var grow = _grows.GetActiveGrowsForTent(tent.Id).FirstOrDefault();
        if (grow is null)
        {
            return default;
        }

        var stage = GrowStageResolver.Resolve(grow, DateTime.Today);
        var systemProfil = grow.SystemId is { } systemId
            ? _hydroSetups?.GetSystem(systemId)?.SetpointProfileId
            : null;

        var ziele = Zielband.FuerGrow(_targetValues, _knowledge, grow, stage, systemProfil, null);
        if (ziele is null)
        {
            return default;
        }

        var profil = SetpointProfileResolver.Resolve(grow.SetpointProfileId, systemProfil, grow.HydroStyle);
        var rampenBoden = Wasserband.RampenBodenC(
            grow,
            _targetValues.GetTargets(profil.ProfileId, GrowStage.Flower),
            _targetValues.GetTargets(profil.ProfileId, GrowStage.Finish));

        var herkunft = _knowledge is not null
            && MischplanService.ZielSpalteFuerGrow(grow, _knowledge.NutrientPrograms) is { } chart
                ? chart.Herkunft
                : stage.ToString();

        return new Planband(ziele, rampenBoden, herkunft);
    }

    private static string BuildTitle(Tent tent) => $"🌱 Grow OS · {tent.Name}";

    /// <param name="herkunft">
    /// Woher die Grenze stammt, wenn die Regel dem Plan folgt — z. B.
    /// „SKX Canna Aqua · Flores · Woche 3“.
    /// </param>
    /// <remarks>
    /// Bei einer Plan-Regel steht die Herkunft mit in der Nachricht. Wer nachts
    /// eine Zahl aufs Handy bekommt, die er nirgends eingetragen hat, muss
    /// erkennen koennen, woher sie kommt — sonst sucht er sie in den
    /// Grenzwerten und findet dort ein leeres Feld.
    /// </remarks>
    private static string BuildBreachMessage(
        TentAlertRule rule, double value, string breach, string? herkunft = null)
    {
        var (label, unit) = MetricDisplay(rule.MetricKey);
        var direction = breach == Below ? "unter" : "über";
        var limit = breach == Below ? rule.MinValue : rule.MaxValue;
        var limitText = limit is { } l ? $" (Grenze {Format(l)}{unit})" : string.Empty;
        var planText = rule.Quelle == Grenzwertquelle.Plan && !string.IsNullOrWhiteSpace(herkunft)
            ? $" — {herkunft}"
            : string.Empty;
        return $"{label} {direction} Zielbereich: {Format(value)}{unit}{limitText}{planText}.";
    }

    private static string BuildRecoveryMessage(TentAlertRule rule, double value)
    {
        var (label, unit) = MetricDisplay(rule.MetricKey);
        return $"{label} wieder im Zielbereich: {Format(value)}{unit}.";
    }

    private static string Format(double value)
    {
        var rounded = Math.Round(value, 2);
        return rounded.ToString(rounded == Math.Truncate(rounded) ? "0.##" : "0.##", CultureInfo.InvariantCulture);
    }

    public static (string Label, string Unit) MetricDisplay(string metricKey) => metricKey switch
    {
        "reservoir-ph" => ("pH", ""),
        "reservoir-ec" => ("EC", " mS/cm"),
        "reservoir-temp" => ("Wassertemp.", " °C"),
        "reservoir-level" => ("Wasserstand", " L"),
        "reservoir-level-cm" => ("Wasserstand", " cm"),
        "orp" => ("ORP", " mV"),
        "dissolved-oxygen" => ("DO", " mg/L"),
        "temperature" => ("Lufttemp.", " °C"),
        "humidity" => ("Luftfeuchte", " %"),
        "vpd" => ("VPD", " kPa"),
        "co2" => ("CO₂", " ppm"),
        "ppfd" => ("PPFD", " µmol/m²/s"),
        _ => (metricKey, ""),
    };
}
