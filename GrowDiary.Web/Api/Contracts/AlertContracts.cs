namespace GrowDiary.Web.Api.Contracts;

/// <param name="Quelle">
/// <c>Fest</c> (Vorgabe) oder <c>Plan</c>. Bei <c>Plan</c> leitet die Regel ihre
/// Grenzen aus dem Zielband der laufenden Woche ab, und MinValue/MaxValue
/// bleiben leer.
/// </param>
/// <param name="Toleranz">
/// Wie weit der Wert bei <c>Plan</c> ueber das Band hinausdarf. Leer = der
/// Standard der Messgroesse.
/// </param>
/// <remarks>
/// Quelle und Toleranz stehen am Ende und haben Vorgabewerte: aeltere Aufrufer
/// (und die Tests) senden sie nicht mit und bekommen weiter das alte Verhalten.
/// </remarks>
public sealed record AlertRuleDto(
    string MetricKey,
    double? MinValue,
    double? MaxValue,
    string NotifyService,
    bool Enabled,
    int CooldownMinutes,
    string Quelle = "Fest",
    double? Toleranz = null);

public sealed record TentAlertRulesDto(int TentId, IReadOnlyList<AlertRuleDto> Rules);

public sealed record SaveTentAlertRulesRequest(IReadOnlyList<AlertRuleDto> Rules);

public sealed record AlertTestRequest(string NotifyService);
