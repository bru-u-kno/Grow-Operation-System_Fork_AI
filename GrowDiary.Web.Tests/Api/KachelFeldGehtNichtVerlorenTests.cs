using System.Reflection;
using GrowDiary.Web.Models;
using GrowDiary.Web.ViewModels.Live;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Was der Server für eine Kachel ausrechnet, kommt auch an ihr an.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (13.09.2026).</b> Das Nachtband war fertig — Regel,
/// Wochenplan, Alarm, Kachel — und auf dem Bildschirm änderte sich nichts.
/// Zwischen <see cref="MetricCard"/> und <see cref="MetricPayload"/> liegt eine
/// von Hand geschriebene Abbildung, und die kannte die fünf neuen Felder nicht.
/// Kein Fehler, kein Log, HTTP 200 — die Werte fielen still auf dem letzten
/// Meter heraus.</para>
///
/// <para><b>Warum als Zählung und nicht als fünf Zusicherungen.</b> Das nächste
/// Feld kommt bestimmt. Diese Prüfung fällt dann von selbst auf, ohne dass
/// jemand daran denken muss — dasselbe Mittel, das
/// <c>NachtbandRundwegTests</c> für den Grenzwert-Vertrag benutzt.</para>
/// </remarks>
public sealed class KachelFeldGehtNichtVerlorenTests
{
    [Fact]
    public void JedesFeldDerKachelStehtAuchImPayload()
    {
        var payloadFelder = typeof(MetricPayload)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // `Target` ist die alte Textform der Sollwert-Anzeige und geht bewusst
        // nicht mit — die Kachel setzt ihren Text selbst zusammen.
        var ausgenommen = new[] { "Target" };

        foreach (var feld in typeof(MetricCard)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Select(p => p.Name)
                     .Where(name => !ausgenommen.Contains(name)))
        {
            Assert.True(payloadFelder.Contains(feld),
                $"MetricPayload kennt {feld} nicht — der Wert erreicht die Kachel nie.");
        }
    }

    [Fact]
    public void DieAbbildungTraegtDasNachtbandMit()
    {
        var karte = new MetricCard
        {
            Key = "temperature",
            TargetMin = 18,
            TargetMax = 24,
            TargetDayMin = 22,
            TargetDayMax = 28,
            TargetNightMin = 18,
            TargetNightMax = 24,
            TargetPhase = "night",
        };

        var payload = karte.ToPayload();

        Assert.Equal(22, payload.TargetDayMin);
        Assert.Equal(28, payload.TargetDayMax);
        Assert.Equal(18, payload.TargetNightMin);
        Assert.Equal(24, payload.TargetNightMax);
        Assert.Equal("night", payload.TargetPhase);
    }
}
