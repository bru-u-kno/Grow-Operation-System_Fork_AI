using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (02.10.2026): Der Wochenplan schreibt nur, was der HA-Helfer annimmt —
/// und haelt einen begrenzten Wert danach nicht fuer eine Handaenderung.
/// </summary>
/// <remarks>
/// <para><b>Der Fund.</b> Planwerte gingen unbegrenzt an <c>input_number.set_value</c>.
/// Der Plan nennt etwa RH 85 %, der Helfer <c>co2_rh_obergrenze</c> reicht bis 80.
/// Home Assistant lehnte ab — bei jedem Lauf wieder, der Helfer blieb auf dem Wert
/// einer frueheren Woche.</para>
/// <para><b>Die Falle daneben.</b> Die Handerkennung vergleicht „zuletzt
/// geschrieben" mit dem gelesenen Wert. Wer begrenzt, aber den Planwert merkt,
/// erklaert jeden begrenzten Helfer beim naechsten Lauf zu „von dir gesetzt".</para>
/// </remarks>
public sealed class WochenplanHelferspanneTests
{
    private const string RhHelfer = "input_number.co2_rh_obergrenze";

    /// <summary>Ein Home Assistant, das wie das echte ausserhalb von min/max ablehnt.</summary>
    private sealed class StrengesHa
    {
        public Dictionary<string, double> Werte { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Entity, double Wert)> Schreibversuche { get; } = [];

        public Task<double?> Lesen(string entity)
            => Task.FromResult(Werte.TryGetValue(entity, out var w) ? w : (double?)null);

        public Task<bool> Schreiben(string entity, double wert)
        {
            Schreibversuche.Add((entity, wert));
            var (min, max) = SteuerungBauteile.Spanne(entity);
            if (wert < min || wert > max) return Task.FromResult(false);
            Werte[entity] = wert;
            return Task.FromResult(true);
        }
    }

    private static Task<int> Lauf(StrengesHa ha, WochenplanSyncStand stand, double planwert, bool ersterLauf = false)
        => WochenplanSyncService.HelferNachziehenAsync(
            [(RhHelfer, planwert)], ha.Lesen, ha.Schreiben, stand, ersterLauf,
            NullLogger.Instance);

    [Fact]
    public async Task PlanwertUeberDerSpanne_KommtBegrenztAn()
    {
        var (_, max) = SteuerungBauteile.Spanne(RhHelfer);
        var planwert = max + 5;
        var ha = new StrengesHa { Werte = { [RhHelfer] = 70 } };
        var stand = new WochenplanSyncStand();
        await Lauf(ha, stand, 70, ersterLauf: true);

        await Lauf(ha, stand, planwert);

        Assert.True(ha.Werte[RhHelfer] == max,
            $"Der Plan will {planwert} %, der Helfer reicht bis {max}. In HA steht weiter "
            + $"{ha.Werte[RhHelfer]} — set_value wurde abgelehnt (Versuche: "
            + string.Join(", ", ha.Schreibversuche.Select(v => v.Wert)) + ").");
    }

    /// <summary>
    /// Der zweite Lauf — dieselbe Woche, Seite nicht neu: kein „von dir gesetzt".
    /// </summary>
    [Fact]
    public async Task BegrenzterWert_GiltBeimNaechstenLaufNichtAlsHandaenderung()
    {
        var (_, max) = SteuerungBauteile.Spanne(RhHelfer);
        var ha = new StrengesHa { Werte = { [RhHelfer] = 70 } };
        var stand = new WochenplanSyncStand();
        await Lauf(ha, stand, 70, ersterLauf: true);

        await Lauf(ha, stand, max + 5);
        var zweiter = await Lauf(ha, stand, max + 5);

        Assert.True(stand.VonDir.Count == 0,
            "Nach dem Begrenzen gilt der Helfer als „von dir gesetzt\" — der Plan liesse ihn fuer "
            + "immer in Ruhe. Gemerkt werden muss der Wert, der wirklich in HA steht.");
        Assert.Equal(0, zweiter);

        // Und die Woche danach passt wieder in die Spanne: der Plan zieht nach.
        await Lauf(ha, stand, max - 10);
        Assert.Equal(max - 10, ha.Werte[RhHelfer]);
    }

    /// <summary>Die Handerkennung selbst bleibt scharf.</summary>
    [Fact]
    public async Task EchteHandaenderung_WirdWeiterErkannt()
    {
        var (_, max) = SteuerungBauteile.Spanne(RhHelfer);
        var ha = new StrengesHa { Werte = { [RhHelfer] = 70 } };
        var stand = new WochenplanSyncStand();
        await Lauf(ha, stand, 70, ersterLauf: true);
        await Lauf(ha, stand, max + 5);

        ha.Werte[RhHelfer] = 65; // ein Mensch dreht am Regler

        await Lauf(ha, stand, max + 5);

        Assert.Contains(RhHelfer, stand.VonDir);
        Assert.Equal(65, ha.Werte[RhHelfer]);
    }
}
