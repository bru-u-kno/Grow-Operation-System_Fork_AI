using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Wie frisch die Prüfung hinter einer Abweichungs-Karte ist (16.10.2026).
/// </summary>
public sealed class MessTaktTests
{
    private static readonly DateTime Jetzt = new(2026, 10, 16, 18, 0, 0, DateTimeKind.Utc);

    private static Measurement Messung(DateTime zeit, ValueOrigin quelle = ValueOrigin.HomeAssistant)
        => new() { TakenAt = zeit, Source = quelle, GrowId = 1 };

    /// <summary>Fünf automatische Messungen im 12-Stunden-Takt, die neueste um <paramref name="neuesteVorStunden"/> Stunden vor Jetzt.</summary>
    private static List<Measurement> ZweimalAmTag(double neuesteVorStunden, int anzahl = 5)
        => Enumerable.Range(0, anzahl)
            .Select(i => Messung(Jetzt.AddHours(-neuesteVorStunden - 12 * i)))
            .ToList();

    [Fact]
    public void OhneMessungenGibtEsKeinenStand()
        => Assert.Null(MessTakt.Bestimmen([], Jetzt));

    [Fact]
    public void DerTaktIstDerMedianDerAbstaendeUndDieNaechsteMessungFolgtDaraus()
    {
        var stand = MessTakt.Bestimmen(ZweimalAmTag(neuesteVorStunden: 2), Jetzt)!;

        Assert.Equal(TimeSpan.FromHours(12), stand.UeblicherAbstand);
        Assert.Equal(Jetzt.AddHours(-2), stand.NeuesteMessungUtc);
        Assert.Equal(Jetzt.AddHours(10), stand.NaechsteErwartetUtc);
        Assert.False(stand.Veraltet);
    }

    [Theory]
    [InlineData(17.5, false)]   // 12 × 1,5 = 18 Stunden: noch in der Toleranz
    [InlineData(18.5, true)]    // darüber: überfällig
    public void UeberfaelligIstDasEineinhalbfacheDesUeblichenAbstands(double neuesteVorStunden, bool erwartet)
    {
        var stand = MessTakt.Bestimmen(ZweimalAmTag(neuesteVorStunden), Jetzt)!;

        Assert.Equal(erwartet, stand.Veraltet);
    }

    [Fact]
    public void EineUeberfaelligePruefungNenntKeineNaechsteMessung()
    {
        var stand = MessTakt.Bestimmen(ZweimalAmTag(neuesteVorStunden: 30), Jetzt)!;

        Assert.True(stand.Veraltet);
        Assert.Null(stand.NaechsteErwartetUtc);
    }

    [Fact]
    public void WerEinmalAmTagMisstBekommtEineWeitereGrenze()
    {
        var taeglich = Enumerable.Range(0, 5).Select(i => Messung(Jetzt.AddHours(-30 - 24 * i))).ToList();

        var stand = MessTakt.Bestimmen(taeglich, Jetzt)!;

        Assert.Equal(TimeSpan.FromHours(24), stand.UeblicherAbstand);
        Assert.False(stand.Veraltet);   // 30 Stunden < 36
    }

    [Fact]
    public void ZuWenigeAutomatischeMessungenGebenKeinenTaktUndBehaupteNichtsUeberfaelliges()
    {
        var stand = MessTakt.Bestimmen(ZweimalAmTag(neuesteVorStunden: 200, anzahl: MessTakt.MindestMessungen - 1), Jetzt)!;

        Assert.Null(stand.UeblicherAbstand);
        Assert.Null(stand.NaechsteErwartetUtc);
        Assert.False(stand.Veraltet);
        Assert.Equal(Jetzt.AddHours(-200), stand.NeuesteMessungUtc);
    }

    [Fact]
    public void EinAusfallVerziehtDenTaktNicht()
    {
        // 12, 12, 12 und ein Ausfall von zwei Tagen: der Median bleibt 12 Stunden.
        var zeiten = new[] { 2.0, 14.0, 26.0, 38.0, 86.0 };
        var messungen = zeiten.Select(h => Messung(Jetzt.AddHours(-h))).ToList();

        Assert.Equal(TimeSpan.FromHours(12), MessTakt.Bestimmen(messungen, Jetzt)!.UeblicherAbstand);
    }

    [Fact]
    public void MessungenVonHandGebenNichtDenTaktZaehlenAberAlsNeuesteMessung()
    {
        var messungen = ZweimalAmTag(neuesteVorStunden: 20);
        messungen.Add(Messung(Jetzt.AddHours(-1), ValueOrigin.Manual));

        var stand = MessTakt.Bestimmen(messungen, Jetzt)!;

        Assert.Equal(TimeSpan.FromHours(12), stand.UeblicherAbstand);
        Assert.Equal(Jetzt.AddHours(-1), stand.NeuesteMessungUtc);
        Assert.False(stand.Veraltet);
    }

    [Fact]
    public void DieNaechsteAutomatischeMessungFolgtDemRhythmusDerAutomatischenNichtDerHandmessung()
    {
        // Automatisch zuletzt vor 5 Stunden (nächste also in 7), von Hand vor 1 Stunde.
        // Ab der Handmessung gerechnet käme „in 11 Stunden" heraus — das wäre falsch.
        var messungen = ZweimalAmTag(neuesteVorStunden: 5);
        messungen.Add(Messung(Jetzt.AddHours(-1), ValueOrigin.Manual));

        Assert.Equal(Jetzt.AddHours(7), MessTakt.Bestimmen(messungen, Jetzt)!.NaechsteErwartetUtc);
    }

    [Fact]
    public void EineMessungAusDerZukunftZaehltNicht()
    {
        var messungen = ZweimalAmTag(neuesteVorStunden: 2);
        messungen.Add(Messung(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(Jetzt.AddHours(-2), MessTakt.Bestimmen(messungen, Jetzt)!.NeuesteMessungUtc);
    }
}
