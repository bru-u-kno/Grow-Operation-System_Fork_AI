using GrowDiary.Web.Models;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (F-041): Ziel und Grenzen für die Live-Kachel — sauber getrennt.
/// </summary>
/// <remarks>
/// <para><b>Ziel</b> kommt aus dem Plan des Grows, genau so, wie er dort steht:
/// Luft als Einzelwert (min = max, keine erfundene Toleranz), Luftfeuchte als
/// „höchstens". Nachts gilt der Nachtwert des Plans (bzw. „nachts wie tags").</para>
///
/// <para><b>Grenzen</b> sind die der Alarmregel — feste Regeln direkt, Plan-Regeln
/// über <see cref="Planzielgrenzen.Wirksam"/>. Dieselben Zahlen, bei denen auch
/// die Handy-Meldung kommt.</para>
/// </remarks>
public static class KachelZiele
{
    /// <summary>Ziel eines Werts tags und nachts.</summary>
    public readonly record struct TagNacht(double? TagMin, double? TagMax, double? NachtMin, double? NachtMax);

    /// <summary>
    /// Luft- und Feuchteziel aus der Planwoche; null, wenn die Woche die Messgröße nicht nennt.
    /// </summary>
    public static TagNacht? AusPlan(string key, FeedChartColumn spalte, GrowPlanInhalt? inhalt)
    {
        var nacht = inhalt?.NachtWerte(spalte);
        switch (key)
        {
            case "temperature" when spalte.AirTempC is { } tag:
            {
                var n = nacht?.LuftC ?? spalte.AirTempNightC ?? tag - WochenplanSyncService.Nachtabsenkung;
                return new TagNacht(tag, tag, n, n);
            }
            case "humidity" when spalte.RhMax is { } rh:
            {
                var n = nacht?.RhMax ?? spalte.RhMaxNight ?? rh;
                return new TagNacht(null, rh, null, n);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Fork AI (F-043): Wasser-Ziel Tag/Nacht aus dem Plan (Einzelwerte, z. B. 20/18 °C).
    /// </summary>
    public static TagNacht? WasserAusPlan(FeedChartColumn spalte)
        => spalte.WaterTempDayC is { } tag
            ? new TagNacht(tag, tag, spalte.WaterTempNightC ?? tag, spalte.WaterTempNightC ?? tag)
            : null;

    /// <summary>
    /// Legt das Plan-Ziel für Luft und Feuchte auf die Kacheln. Läuft VOR den
    /// zurückgerechneten Klimabändern, die ein gesetztes Ziel nicht überschreiben.
    /// </summary>
    public static void ZieleSetzen(IEnumerable<MetricCard> cards, FeedChartColumn? spalte, GrowPlanInhalt? inhalt, LightsNow lichter)
    {
        if (spalte is null) return;
        foreach (var card in cards)
        {
            var ziel = card.Key == "reservoir-temp" ? WasserAusPlan(spalte) : AusPlan(card.Key, spalte, inhalt);
            if (ziel is not { } z) continue;
            var nachts = lichter == LightsNow.Off;
            card.TargetMin = nachts ? z.NachtMin : z.TagMin;
            card.TargetMax = nachts ? z.NachtMax : z.TagMax;
            card.TargetDayMin = z.TagMin;
            card.TargetDayMax = z.TagMax;
            card.TargetNightMin = z.NachtMin;
            card.TargetNightMax = z.NachtMax;
            card.TargetPhase = nachts ? "night" : "day";
            card.TargetNote = null;   // „ZIEL" steht auf der Kachel; die Woche nennt die Grenzwerte-Seite
            card.TargetDerived = false;
        }
    }

    /// <summary>
    /// Legt die Grenzwerte der Alarmregeln auf die Kacheln (nur Anzeige — gemeldet
    /// wird weiter von der Alarmauswertung).
    /// </summary>
    public static void GrenzenSetzen(
        IEnumerable<MetricCard> cards,
        IReadOnlyList<TentAlertRule>? regeln,
        HydroTargetValues? band,
        double? rampenBodenC,
        LightsNow lichter)
    {
        if (regeln is null || regeln.Count == 0) return;
        foreach (var card in cards)
        {
            var regel = regeln.FirstOrDefault(r =>
                r.Enabled && string.Equals(r.MetricKey, card.Key, StringComparison.OrdinalIgnoreCase));
            if (regel is null) continue;
            if (Planzielgrenzen.Wirksam(regel, band, rampenBodenC) is not { } wirksam) continue;

            // Nachts schweigen VPD/CO₂/PPFD — dann auch keine Grenze auf der Kachel.
            if (!(lichter == LightsNow.Off && LightClock.IsDaytimeOnly(card.Key)))
            {
                var (min, max) = wirksam.GrenzenFuer(lichter);
                card.AlarmMin = min;
                card.AlarmMax = max;
            }

            if (LightClock.HasNightBand(card.Key) || card.Key == "reservoir-temp")
            {
                var tag = wirksam.GrenzenFuer(LightsNow.On);
                var nacht = wirksam.GrenzenFuer(LightsNow.Off);
                card.AlarmDayMin = tag.Min;
                card.AlarmDayMax = tag.Max;
                card.AlarmNightMin = nacht.Min;
                card.AlarmNightMax = nacht.Max;
            }
        }
    }
}
