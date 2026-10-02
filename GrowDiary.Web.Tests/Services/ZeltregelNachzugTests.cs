using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (02.10.2026): der Wochenplan zieht die Zelt-Regeln nach — aber nie
/// zu einem vertauschten Paar.
/// </summary>
/// <remarks>
/// Der Fall aus der Durchsicht: das Maximum der Lufttemperatur steht von Hand
/// auf 24 °C, der Plan verlangt 28 °C ± 3. Je Feld nachgezogen entstand
/// Min 25 &gt; Max 24 — die Regel meldete dauerhaft „zu kalt".
/// </remarks>
public sealed class ZeltregelNachzugTests
{
    private const int Zelt = 7;

    private static TentAlertRule Luftregel() => new()
    {
        Id = 1,
        TentId = Zelt,
        MetricKey = "temperature",
        MinValue = 21,
        MaxValue = 24,
        NightMinValue = 17,
        NightMaxValue = 23,
        Quelle = Grenzwertquelle.Fest,
    };

    private static Dictionary<string, double> Plan(double tag, double nacht) => new(StringComparer.OrdinalIgnoreCase)
    {
        [WochenplanSyncService.Rollen.LuftUnten] = tag - 3,
        [WochenplanSyncService.Rollen.LuftOben] = tag + 3,
        [WochenplanSyncService.Rollen.LuftNachtUnten] = nacht - 3,
        [WochenplanSyncService.Rollen.LuftNachtOben] = nacht + 3,
    };

    private static string Schluessel(string feld) => $"zelt:{Zelt}/temperature/{feld}";

    /// <summary>Ein Stand, in dem der Dienst die Regel schon einmal geschrieben hat.</summary>
    private static WochenplanSyncStand Stand(TentAlertRule regel) => new()
    {
        LetzterLauf = "2026-10-01T06:00:00Z",
        Geschrieben = new(StringComparer.OrdinalIgnoreCase)
        {
            [Schluessel("min")] = regel.MinValue!.Value,
            [Schluessel("max")] = regel.MaxValue!.Value,
            [Schluessel("nacht-min")] = regel.NightMinValue!.Value,
            [Schluessel("nacht-max")] = regel.NightMaxValue!.Value,
        },
    };

    [Fact]
    public void EinVonHandGesetztesMaximumFuehrtNichtZuMinUeberMax()
    {
        var regel = Luftregel();
        var stand = Stand(regel);
        stand.VonDir.Add(Schluessel("max"));       // Max 24 von Hand

        var neu = WochenplanSyncService.ZeltregelNachziehen(regel, Zelt, Plan(28, 24), stand, ersterLauf: false, NullLogger.Instance);

        Assert.Null(neu);
        // Nichts als „geschrieben" gemerkt — sonst hielte der nächste Lauf die alte Grenze für eine Handänderung.
        Assert.Equal(21, stand.Geschrieben[Schluessel("min")]);
        Assert.DoesNotContain(Schluessel("min"), stand.VonDir);
    }

    [Fact]
    public void AuchDasNachtpaarSamtRueckfallWirdGeprueft()
    {
        // Nachts ohne eigene Obergrenze gilt das Tag-Maximum (24, von Hand).
        var regel = Luftregel();
        regel.NightMaxValue = null;
        var stand = new WochenplanSyncStand
        {
            LetzterLauf = "2026-10-01T06:00:00Z",
            Geschrieben = new(StringComparer.OrdinalIgnoreCase)
            {
                [Schluessel("min")] = 21,
                [Schluessel("nacht-min")] = 17,
            },
            VonDir = [Schluessel("max"), Schluessel("nacht-max")],
        };

        // Tag 22 ± 3 → Min 19 passt zu Max 24; Nacht 28 ± 3 → Nacht-Min 25 gegen Nacht-Max 24 (Rückfall).
        var neu = WochenplanSyncService.ZeltregelNachziehen(regel, Zelt, Plan(22, 28), stand, ersterLauf: false, NullLogger.Instance);

        Assert.Null(neu);
        Assert.Equal(17, stand.Geschrieben[Schluessel("nacht-min")]);
    }

    [Fact]
    public void EinStimmigerNachzugWirdGeschriebenUndGemerkt()
    {
        var regel = Luftregel();
        var stand = Stand(regel);

        var neu = WochenplanSyncService.ZeltregelNachziehen(regel, Zelt, Plan(25, 21), stand, ersterLauf: false, NullLogger.Instance);

        Assert.NotNull(neu);
        Assert.Equal((22.0, 28.0, 18.0, 24.0), (neu!.MinValue!.Value, neu.MaxValue!.Value, neu.NightMinValue!.Value, neu.NightMaxValue!.Value));
        Assert.Equal(22, stand.Geschrieben[Schluessel("min")]);
        Assert.Equal(24, stand.Geschrieben[Schluessel("nacht-max")]);

        // Zweiter Lauf mit derselben Woche: nichts mehr zu tun.
        var regelDanach = Luftregel();
        (regelDanach.MinValue, regelDanach.MaxValue, regelDanach.NightMinValue, regelDanach.NightMaxValue) = (22, 28, 18, 24);
        Assert.Null(WochenplanSyncService.ZeltregelNachziehen(regelDanach, Zelt, Plan(25, 21), stand, ersterLauf: false, NullLogger.Instance));
    }
}
