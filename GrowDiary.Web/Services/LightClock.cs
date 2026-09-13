using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Ist im Zelt gerade Licht an, aus — oder wissen wir es nicht?</summary>
public enum LightsNow
{
    On,
    Off,
    Unknown,
}

/// <summary>
/// Entscheidet, ob im Zelt gerade Tag ist.
/// </summary>
/// <remarks>
/// <para>Warum das eine eigene Frage ist: nachts ist PPFD 0 richtig, CO₂ bei
/// Umgebungsluft richtig (die Pflanze verbraucht ohne Licht keins), die
/// Temperatur darf 4–6 °C fallen, und VPD-Ziele gelten für die Lichtphase.
/// Wer das nicht weiss, malt jede Nacht rote Kacheln und schickt Alarme —
/// und wer jede Nacht falschen Alarm bekommt, glaubt auch dem echten nicht
/// mehr.</para>
///
/// <para>Zwei Quellen, in dieser Reihenfolge: der gemappte Licht-Sensor (der
/// sieht auch einen Ausfall), sonst der Lichtplan des Zelts (der kennt auch
/// Nächte, in denen der Sensor nichts meldet). Gibt es beides nicht, bleibt es
/// bei Unbekannt — und Unbekannt heisst: alles verhält sich wie bisher. Lieber
/// ein unnötiges Nacht-Urteil als ein unterdrücktes Tag-Urteil.</para>
/// </remarks>
public static class LightClock
{
    /// <summary>Messgrößen, deren Ziel nur bei Licht an gilt.</summary>
    /// <remarks>
    /// PPFD: ohne Licht ist 0 der Sollzustand. CO₂: ohne Photosynthese kein
    /// Verbrauch, Anreicherung ist nachts aus. VPD: die Pflanze verdunstet
    /// nachts kaum; die Zielbänder aller Quellen beziehen sich auf den Tag.
    /// </remarks>
    public static readonly string[] DaytimeOnlyKeys = ["ppfd", "co2", "vpd"];

    public static bool IsDaytimeOnly(string metricKey)
        => DaytimeOnlyKeys.Contains(metricKey, StringComparer.OrdinalIgnoreCase);

    /// <summary>Messgrößen, für die sich ein eigenes Nachtband lohnt.</summary>
    /// <remarks>
    /// Nur die beiden Klima-Kacheln. Temperatur darf nachts fallen, und die
    /// Luftfeuchte steigt dabei von selbst — beides Fragen der Lichtphase.
    /// pH und EC kennen keinen Unterschied zwischen Tag und Nacht; ein zweites
    /// Band waere dort eine Spalte ohne Aussage. Die Wassertemperatur hat ihr
    /// Tag/Nacht-Paar schon im Wissen (siehe Wasserband) und bleibt deshalb
    /// hier aussen vor.
    /// </remarks>
    public static readonly string[] NightBandKeys = ["temperature", "humidity"];

    public static bool HasNightBand(string metricKey)
        => NightBandKeys.Contains(metricKey, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sensor zuerst, sonst Plan, sonst Unbekannt.</summary>
    public static LightsNow Resolve(
        HomeAssistantState? lightState,
        LightSchedule? schedule,
        DateTime utcNow)
    {
        if (lightState is not null)
        {
            var normalized = LightStateNormalizer.Normalize(lightState.State);
            if (normalized == LightState.On) return LightsNow.On;
            if (normalized == LightState.Off) return LightsNow.Off;
        }

        if (schedule is not null)
        {
            return FromSchedule(schedule, LocalTime(schedule, utcNow));
        }

        return LightsNow.Unknown;
    }

    /// <summary>
    /// Der Plan allein: liegt <paramref name="now"/> in der Licht-an-Spanne?
    /// </summary>
    /// <remarks>
    /// Auch über Mitternacht: 20:00–08:00 ist ein echter Plan — wer die
    /// Lichtwärme in die kalte Nacht legt, fährt genau so. Gleiche An- und
    /// Aus-Zeit ist kein Plan, sondern ein Tippfehler: dann Unbekannt.
    /// </remarks>
    public static LightsNow FromSchedule(LightSchedule schedule, TimeOnly now)
    {
        if (!TimeOnly.TryParse(schedule.LightsOnTime, out var an)
            || !TimeOnly.TryParse(schedule.LightsOffTime, out var aus)
            || an == aus)
        {
            return LightsNow.Unknown;
        }

        var tag = an < aus
            ? now >= an && now < aus
            : now >= an || now < aus;

        return tag ? LightsNow.On : LightsNow.Off;
    }

    /// <summary>
    /// Die Uhrzeit im Zelt — nicht die des Servers.
    /// </summary>
    /// <remarks>
    /// Der Add-on-Container läuft gern auf UTC. Ein Plan „08:00–20:00" meint
    /// aber die Wanduhr im Growraum. Der Plan darf eine Zeitzone tragen; ohne
    /// sie bleibt die lokale Zeit des Servers — im Add-on ist das die von Home
    /// Assistant gesetzte.
    /// </remarks>
    private static TimeOnly LocalTime(LightSchedule schedule, DateTime utcNow)
    {
        if (!string.IsNullOrWhiteSpace(schedule.TimeZoneId))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
                return TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone));
            }
            catch (TimeZoneNotFoundException)
            {
                // Eine falsch geschriebene Zone darf die Lichtfrage nicht crashen.
            }
        }

        return TimeOnly.FromDateTime(utcNow.ToLocalTime());
    }
}
