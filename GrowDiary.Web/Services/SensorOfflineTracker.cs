namespace GrowDiary.Web.Services;

/// <summary>
/// Tracks, in memory, which mapped sensors have stopped reporting so Cultivate Control can push once
/// when a sensor goes offline and once when it recovers. A sensor must look offline for a
/// couple of consecutive polls before it counts, so a single transient Home Assistant hiccup
/// does not raise a false alarm. Owned by the snapshot worker (a single long-lived instance).
/// </summary>
public sealed class SensorOfflineTracker
{
    public enum Transition
    {
        None,
        WentOffline,
        CameOnline,
    }

    private const int OfflineThreshold = 2;

    private readonly Dictionary<string, int> _offlineStreak = new();
    private readonly HashSet<string> _notifiedOffline = new();

    /// <summary>
    /// Records one observation for a sensor. Returns <see cref="Transition.WentOffline"/> the
    /// first poll it has been offline long enough to alert, <see cref="Transition.CameOnline"/>
    /// the poll it recovers after having alerted, otherwise <see cref="Transition.None"/>.
    /// </summary>
    public Transition Observe(string key, bool offline)
    {
        if (offline)
        {
            var streak = (_offlineStreak.TryGetValue(key, out var current) ? current : 0) + 1;
            _offlineStreak[key] = streak;
            if (streak >= OfflineThreshold && _notifiedOffline.Add(key))
            {
                return Transition.WentOffline;
            }

            return Transition.None;
        }

        _offlineStreak[key] = 0;
        return _notifiedOffline.Remove(key) ? Transition.CameOnline : Transition.None;
    }
}
