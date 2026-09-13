using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.77): Wie der Verbrauch eines Artikels bepreist wird.
/// </summary>
/// <remarks>
/// Der heikle Teil ist der Preis. Eine Nachfüllung kann teurer gewesen sein als
/// die vorige; wer immer den Artikelpreis nimmt, rechnet alte Buchungen mit
/// neuen Preisen ab und umgekehrt.
/// </remarks>
public class VerbrauchsansichtServiceTests
{
    private static Nachfuellung Fuellung(string datum, double menge, double? kosten) => new()
    {
        ZeitpunktUtc = DateTime.Parse(datum, System.Globalization.CultureInfo.InvariantCulture),
        Menge = menge,
        KostenEur = kosten,
    };

    private static DateTime Am(string datum)
        => DateTime.Parse(datum, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void DiePassendeFuellungBezahltDenVerbrauch()
    {
        var fuellungen = new[]
        {
            Fuellung("2026-01-10", 10, 30),   // 3,00 je Einheit
            Fuellung("2026-06-01", 10, 40),   // 4,00 je Einheit
        };

        Assert.Equal(3.0, VerbrauchsansichtService.PreisJeEinheit(fuellungen, Am("2026-03-15"), null));
        Assert.Equal(4.0, VerbrauchsansichtService.PreisJeEinheit(fuellungen, Am("2026-07-15"), null));
    }

    [Fact]
    public void EineBuchungVorDerErstenFuellungNimmtDieErste()
    {
        // Altdaten koennen aelter sein als die erste erfasste Fuellung. Ohne
        // Rueckfall staende die Zeile ohne Betrag da, obwohl ein Preis bekannt
        // ist - der naechstliegende ist die bessere Auskunft als gar keine.
        var fuellungen = new[] { Fuellung("2026-06-01", 10, 40) };
        Assert.Equal(4.0, VerbrauchsansichtService.PreisJeEinheit(fuellungen, Am("2026-01-01"), null));
    }

    [Fact]
    public void OhneFuellungGreiftDerArtikelpreis()
    {
        var artikel = new Verbrauchsartikel { Name = "X", PreisEur = 65, Gebinde = 10000, Einheit = "ml" };
        Assert.Equal(0.0065, VerbrauchsansichtService.PreisJeEinheit(
            Array.Empty<Nachfuellung>(), Am("2026-06-01"), artikel));
    }

    [Fact]
    public void OhneJedenPreisBleibtDieZeileOhneBetrag()
    {
        // Null statt 0: Eine Null sieht aus wie 'hat nichts gekostet', und die
        // Summe waere vollstaendig und zu niedrig zugleich.
        Assert.Null(VerbrauchsansichtService.PreisJeEinheit(
            Array.Empty<Nachfuellung>(), Am("2026-06-01"), new Verbrauchsartikel { Name = "X" }));
    }

    [Fact]
    public void EineFuellungOhneKostenZaehltNichtAlsPreisquelle()
    {
        // Gratisproben werden mit 0 EUR erfasst - das ist ein Preis. Eine
        // Fuellung ohne Kostenangabe ist dagegen unbekannt, nicht kostenlos.
        var ohneKosten = new[] { Fuellung("2026-01-10", 10, null) };
        var artikel = new Verbrauchsartikel { Name = "X", PreisEur = 20, Gebinde = 10 };

        Assert.Equal(2.0, VerbrauchsansichtService.PreisJeEinheit(ohneKosten, Am("2026-03-01"), artikel));
    }

    [Fact]
    public void EineGratisprobeKostetNull()
    {
        var gratis = new[] { Fuellung("2026-01-10", 10, 0) };
        Assert.Equal(0.0, VerbrauchsansichtService.PreisJeEinheit(gratis, Am("2026-03-01"), null));
    }

    [Fact]
    public void SiebenTageBeginnenSechsTageVorHeute()
    {
        // Sieben Tage heisst heute plus sechs davor - nicht sieben davor.
        var (von, bis) = VerbrauchsansichtService.Grenzen(VerbrauchsansichtService.Spanne.SiebenTage, null, null);

        Assert.NotNull(von);
        Assert.Equal(DateTime.UtcNow.Date.AddDays(-6), von);
        Assert.Null(bis);
    }

    [Fact]
    public void AllesHatKeineGrenzen()
    {
        var (von, bis) = VerbrauchsansichtService.Grenzen(VerbrauchsansichtService.Spanne.Alles, null, null);
        Assert.Null(von);
        Assert.Null(bis);
    }

    [Fact]
    public void EinEigenerZeitraumWirdDurchgereicht()
    {
        var (von, bis) = VerbrauchsansichtService.Grenzen(
            VerbrauchsansichtService.Spanne.Eigen, Am("2026-05-01"), Am("2026-05-31"));

        Assert.Equal(Am("2026-05-01"), von);
        Assert.Equal(Am("2026-05-31"), bis);
    }
}
