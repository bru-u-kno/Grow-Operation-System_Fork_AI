using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Steht der Füllstand still? Der Kern des Kalibrier-Assistenten.
/// </summary>
/// <remarks>
/// Der Nutzer soll füllen, nicht auf die Uhr schauen. Cultivate Control erkennt am
/// Stillstand des Werts, wann der Nullpunkt steht und wann „voll" erreicht sein
/// könnte — und fragt dann nach, weil eine Füllpause für den Sensor genauso
/// aussieht wie „fertig".
/// </remarks>
public sealed class LevelStabilityTests
{
    private static readonly DateTime Jetzt = new(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Eine Ablesung je Sekunde über die letzten <paramref name="sekunden"/>.</summary>
    private static List<LevelSample> Reihe(int sekunden, Func<int, double> wert)
        => Enumerable.Range(0, sekunden + 1)
            .Select(i => new LevelSample(Jetzt.AddSeconds(-sekunden + i), wert(i)))
            .ToList();

    [Fact]
    public void ASteadyValueIsRecognisedAfterTheWaitingTime()
    {
        var ruhig = Reihe(70, _ => 38.0);

        Assert.Equal(38.0, LevelStability.StableValue(ruhig, Jetzt, LevelStability.FullSeconds));
    }

    [Fact]
    public void RipplesFromTheCirculationPumpStillCountAsSteady()
    {
        // Die Umwaelzung kraeuselt die Oberflaeche: der eTape zittert um ein
        // paar Millimeter. „Exakt gleich" traete nie ein.
        var zitternd = Reihe(70, i => 38.0 + (i % 2 == 0 ? 0.1 : -0.1));

        Assert.NotNull(LevelStability.StableValue(zitternd, Jetzt, LevelStability.FullSeconds));
    }

    [Fact]
    public void WhileFillingItIsNotSteady()
    {
        var steigend = Reihe(70, i => 20.0 + i * 0.2);

        Assert.Null(LevelStability.StableValue(steigend, Jetzt, LevelStability.FullSeconds));
    }

    [Fact]
    public void AShortQuietSpellIsNotEnough()
    {
        // Nur 20 Sekunden beobachtet, gefragt sind 60 — auch wenn alles ruhig
        // aussieht. Sonst gaelte eine gerade begonnene Messung sofort als voll.
        var kurz = Reihe(20, _ => 38.0);

        Assert.Null(LevelStability.StableValue(kurz, Jetzt, LevelStability.FullSeconds));
        // Fuer den Nullpunkt reichen 15 Sekunden — da bewegt sich nichts mehr.
        Assert.NotNull(LevelStability.StableValue(kurz, Jetzt, LevelStability.EmptySeconds));
    }

    [Fact]
    public void ASingleOutlierDoesNotShiftTheResult()
    {
        // Eine Welle oder ein Funkaussetzer verschiebt den Median nicht — einen
        // Mittelwert schon.
        var mitAusreisser = Reihe(70, i => i == 5 ? 38.2 : 38.0);

        Assert.Equal(38.0, LevelStability.StableValue(mitAusreisser, Jetzt, LevelStability.FullSeconds));
    }

    [Fact]
    public void ABigJumpBreaksTheQuietSpell()
    {
        // Nachgefuellt: der Sprung liegt ausserhalb des Bandes.
        var sprung = Reihe(70, i => i < 65 ? 30.0 : 38.0);

        Assert.Null(LevelStability.StableValue(sprung, Jetzt, LevelStability.FullSeconds));
    }

    [Fact]
    public void TheSteadySecondsDriveTheProgressBar()
    {
        // Seit 30 Sekunden ruhig, davor gestiegen — die Anzeige soll 30 zeigen.
        var proben = Reihe(70, i => i < 40 ? 20.0 + i * 0.5 : 38.0);

        Assert.InRange(LevelStability.SecondsSteady(proben, Jetzt), 28, 32);
    }

    [Fact]
    public void WithoutSamplesNothingIsClaimed()
    {
        Assert.Null(LevelStability.StableValue([], Jetzt, LevelStability.FullSeconds));
        Assert.Equal(0, LevelStability.SecondsSteady([], Jetzt));
    }

    // ---------- Der ganze Ablauf ----------

    [Fact]
    public void TheAssistantWalksTheWholeSequence()
    {
        // 1. Leeres System, Wert ruhig bei 5 cm → Nullpunkt gefunden.
        var leer = Reihe(20, _ => 5.0);
        var (schritt1, null1) = LevelStability.NextStep(null, leer, Jetzt);
        Assert.Equal(1, schritt1);
        Assert.Equal(5.0, null1);

        // 2. Es wird gefuellt — der Wert steigt, also kein Vollstand.
        var steigend = Reihe(70, i => 5.0 + i * 0.5);
        Assert.Equal(1, LevelStability.NextStep(5.0, steigend, Jetzt).Step);

        // 3. Fuellstopp, eine Minute ruhig bei 38 cm → bestaetigen lassen.
        var voll = Reihe(70, _ => 38.0);
        var (schritt3, vollWert) = LevelStability.NextStep(5.0, voll, Jetzt);
        Assert.Equal(2, schritt3);
        Assert.Equal(38.0, vollWert);
    }

    [Fact]
    public void AFillingPauseAtTheZeroPointIsNotFull()
    {
        // Der Fall, den die Automatik allein nicht unterscheiden kann: ruhig,
        // aber es ist nichts drin. Deshalb bleibt es beim Fuellen.
        var ruhigAberLeer = Reihe(70, _ => 5.0);

        Assert.Equal(1, LevelStability.NextStep(5.0, ruhigAberLeer, Jetzt).Step);
    }

    [Fact]
    public void APauseWhileFillingLooksFullAndThatIsWhyWeAsk()
    {
        // Giesskanne wechseln: 60 s ruhig bei 20 cm. Fuer den Sensor ist das
        // „voll" — deshalb fragt der Assistent und behauptet es nicht.
        var pause = Reihe(70, _ => 20.0);

        Assert.Equal(2, LevelStability.NextStep(5.0, pause, Jetzt).Step);
    }
}
