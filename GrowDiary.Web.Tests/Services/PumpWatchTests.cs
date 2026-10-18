using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Der Pumpen-Wächter — die Warnung, die einen ganzen Lauf rettet.
/// </summary>
/// <remarks>
/// <para>An diesen Entscheidungen hängt eine Push-Nachricht mitten in der
/// Nacht. Sie muss kommen, wenn die Pumpe steht, und sie darf nicht kommen,
/// wenn alles läuft — eine Warnung, die zweimal grundlos weckt, schaltet der
/// Betreiber ab, und dann nützt der beste Wächter nichts.</para>
/// </remarks>
public sealed class PumpWatchTests
{
    private static readonly DateTime Jetzt = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);

    private static Dictionary<string, HomeAssistantState> Zustand(params (string Key, string State, double? Watt, int? SeitMinuten)[] eintraege)
        => eintraege.ToDictionary(
            e => e.Key,
            e => new HomeAssistantState
            {
                EntityId = "switch." + e.Key,
                State = e.State,
                NumericValue = e.Watt,
                LastChanged = e.SeitMinuten is { } m ? Jetzt.AddMinutes(-m) : null,
            });

    [Fact]
    public void ARunningAirPumpSaysNothingAlarming()
    {
        var befunde = PumpWatchService.Beurteilen(
            Zustand(("pump-air", "on", null, 4000), ("pump-air-power", "18.0", 18.0, 4000)), Jetzt);

        var luft = befunde.Single(b => b.Schluessel == "pump-air");
        Assert.Equal("ok", luft.Stufe);
        Assert.Contains("stimmen überein", luft.Herkunft);
    }

    [Fact]
    public void AnAirPumpThatIsOffIsCriticalAndSaysWhatToDoNow()
    {
        var befunde = PumpWatchService.Beurteilen(
            Zustand(("pump-air", "off", null, 45)), Jetzt);

        var luft = befunde.Single();
        Assert.Equal("kritisch", luft.Stufe);
        Assert.Contains("45 Minuten", luft.Meldung);
        // Der Satz muss sagen, was es kostet UND was man sofort tun kann.
        Assert.Contains("sauerstoffarm", luft.Meldung);
        Assert.Contains("von Hand umwälzen", luft.Meldung);
    }

    [Fact]
    public void TheGracePeriodKeepsShortSwitchingQuiet()
    {
        // Eine Pumpe, die vor drei Minuten ausging, ist keine Meldung wert —
        // sonst weckt jede Dosier-Pause und jeder Schaltvorgang den Betreiber.
        var befunde = PumpWatchService.Beurteilen(
            Zustand(("pump-air", "off", null, 3)), Jetzt);

        Assert.Empty(befunde);
    }

    [Fact]
    public void ThePumpThatClaimsToRunButDrawsNothingIsTheExpensiveCase()
    {
        // Gerissene Membran, blockiertes Laufrad: der Schalter sagt „an", die
        // Steckdose sagt 0,3 W. Nur das zweite Signal deckt das auf.
        var befunde = PumpWatchService.Beurteilen(
            Zustand(("pump-air", "on", null, 4000), ("pump-air-power", "0.3", 0.3, 20)), Jetzt);

        var luft = befunde.Single();
        Assert.Equal("kritisch", luft.Stufe);
        Assert.Contains("zieht aber nur 0,3\u00a0W", luft.Meldung);
        Assert.Contains("Leerlauf", luft.Herkunft);
    }

    [Fact]
    public void TheCirculationPumpIsAWarningNotACatastropheAndNamesTheIntervalCase()
    {
        // Ohne Umwaelzung stirbt nichts binnen Stunden — und mancher faehrt sie
        // absichtlich im Intervall. Deshalb Warnung statt kritisch, mit dem
        // Hinweis auf die Schonfrist.
        var befunde = PumpWatchService.Beurteilen(
            Zustand(("pump-circulation", "off", null, 30)), Jetzt);

        var umwaelzung = befunde.Single();
        Assert.Equal("warnung", umwaelzung.Stufe);
        Assert.Contains("Intervall-Betrieb", umwaelzung.Meldung);
    }

    [Fact]
    public void WithoutAnySensorTheWatchStaysSilent()
    {
        // Die meisten haben fuer ihre Pumpen gar keinen Sensor. „Unbekannt"
        // als Gefahr zu lesen waere das Gegenteil von hilfreich.
        Assert.Empty(PumpWatchService.Beurteilen(
            Zustand(("temperature", "23.5", 23.5, 10)), Jetzt));
    }

    [Fact]
    public void PowerAloneIsEnoughToRaiseTheAlarm()
    {
        // Wer nur eine Messsteckdose hat und keinen Zustand, bekommt trotzdem
        // die Warnung — beide Signale zaehlen einzeln.
        var befunde = PumpWatchService.Beurteilen(
            Zustand(("pump-air-power", "0.0", 0.0, 60)), Jetzt);

        Assert.Equal("kritisch", befunde.Single().Stufe);
        Assert.Contains("Leistungsaufnahme", befunde.Single().Herkunft);
    }

    [Fact]
    public void AnUnavailableEntityCountsAsOff()
    {
        // „unavailable" heisst: der Schalter antwortet nicht mehr. Das als „laeuft
        // schon" zu lesen waere genau der Fehler, den dieser Waechter verhindern soll.
        var befunde = PumpWatchService.Beurteilen(
            Zustand(("pump-air", "unavailable", null, 60)), Jetzt);

        Assert.Equal("kritisch", befunde.Single().Stufe);
    }

    [Fact]
    public void ALongerGraceSilencesTheIntervalOperator()
    {
        var zustand = Zustand(("pump-circulation", "off", null, 20));

        Assert.Single(PumpWatchService.Beurteilen(zustand, Jetzt, schonfristMinuten: 15));
        Assert.Empty(PumpWatchService.Beurteilen(zustand, Jetzt, schonfristMinuten: 45));
    }
}
