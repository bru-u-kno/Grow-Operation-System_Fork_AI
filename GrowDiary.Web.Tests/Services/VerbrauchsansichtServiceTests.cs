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

    /// <summary>
    /// Die Kostenseite reicht die Füllungen so herein, wie das Repository sie
    /// liefert: NEUESTE ZUERST. Bis zum 01.10.2026 galt dann der Preis der
    /// ältesten Füllung — 1 kg im August kostete 3 statt 4 €.
    /// </summary>
    [Fact]
    public void DieReihenfolgeDerFuellungenSpieltKeineRolle()
    {
        var neuesteZuerst = new[]
        {
            Fuellung("2026-06-01", 10, 40),   // 4,00 je Einheit
            Fuellung("2026-01-10", 10, 30),   // 3,00 je Einheit
        };

        Assert.Equal(4.0, VerbrauchsansichtService.PreisJeEinheit(neuesteZuerst, Am("2026-08-15"), null));
        Assert.Equal(3.0, VerbrauchsansichtService.PreisJeEinheit(neuesteZuerst, Am("2026-03-15"), null));
        Assert.Equal(3.0, VerbrauchsansichtService.PreisJeEinheit(neuesteZuerst, Am("2025-12-01"), null));
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

        // Ortstage: Mitternacht des Add-ons, nicht Mitternacht UTC.
        Assert.NotNull(von);
        Assert.Equal(DateTime.SpecifyKind(DateTime.Today.AddDays(-6), DateTimeKind.Local).ToUniversalTime(), von);
        Assert.Null(bis);
    }

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static Verbrauch Buchung(int id, DateTime utc, double menge, int? growId) => new()
    {
        Id = id, ArtikelId = 1, ZeitpunktUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc), Menge = menge, GrowId = growId,
    };

    /// <summary>
    /// Eine Buchung um 00:30 Ortszeit gehört auf den Ortstag, nicht auf den
    /// UTC-Vortag — und „7 Tage" beginnt um Mitternacht Ortszeit.
    /// </summary>
    /// <remarks>
    /// Die Zone steht ausdrücklich im Aufruf: ein Rechner in UTC (das Tor) sähe
    /// den Fehler sonst nie, weil dort Orts- und UTC-Tag zusammenfallen.
    /// </remarks>
    [Fact]
    public void TageSindOrtstage()
    {
        // 09.09.2026 00:30 in Berlin (Sommerzeit) = 08.09.2026 22:30 UTC.
        var nachMitternacht = Buchung(1, new DateTime(2026, 9, 8, 22, 30, 0), 1.0, 7);
        // 02.09.2026 23:30 in Berlin = 02.09.2026 21:30 UTC — vor dem 7-Tage-Fenster.
        var zuFrueh = Buchung(2, new DateTime(2026, 9, 2, 21, 30, 0), 1.0, 7);
        // 03.09.2026 00:30 in Berlin = 02.09.2026 22:30 UTC — erster Tag des Fensters.
        var ersterTag = Buchung(3, new DateTime(2026, 9, 2, 22, 30, 0), 1.0, 7);
        var jetzt = new DateTime(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc);
        var artikel = new Verbrauchsartikel { Id = 1, Name = "CO₂", Einheit = "kg", PreisEur = 40, Gebinde = 10 };

        var ansicht = VerbrauchsansichtService.Zusammenstellen(
            1, artikel, [], [zuFrueh, ersterTag, nachMitternacht],
            VerbrauchsansichtService.Spanne.SiebenTage, null, null, null, jetzt, Berlin);

        Assert.Equal(new DateTime(2026, 9, 2, 22, 0, 0, DateTimeKind.Utc).ToString("o"), ansicht.VonIso);
        Assert.Equal([3, 1], ansicht.Zeilen.Select(z => z.Id));
        Assert.Equal("2026-09-03", ansicht.Zeilen[0].Datum);
        Assert.Equal("2026-09-09", ansicht.Zeilen[1].Datum);
    }

    /// <summary>„Dieser Grow" zeigt nur die Buchungen des gezeigten Grows — und ohne Grow keine.</summary>
    [Fact]
    public void DieserGrowFiltertAufDenGezeigtenGrow()
    {
        var jetzt = new DateTime(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc);
        var buchungen = new[]
        {
            Buchung(1, jetzt.AddDays(-3), 1.0, 7),
            Buchung(2, jetzt.AddDays(-2), 2.0, 8),
            Buchung(3, jetzt.AddDays(-1), 4.0, null),
        };

        var fuer8 = VerbrauchsansichtService.Zusammenstellen(
            1, null, [], buchungen, VerbrauchsansichtService.Spanne.DieserGrow, null, null, 8, jetzt, Berlin);
        Assert.Equal([2], fuer8.Zeilen.Select(z => z.Id));

        var ohneGrow = VerbrauchsansichtService.Zusammenstellen(
            1, null, [], buchungen, VerbrauchsansichtService.Spanne.DieserGrow, null, null, null, jetzt, Berlin);
        Assert.Empty(ohneGrow.Zeilen);

        // Gegenprobe: „Alles" sieht alle drei — der Filter oben ist kein leerer Bestand.
        var alles = VerbrauchsansichtService.Zusammenstellen(
            1, null, [], buchungen, VerbrauchsansichtService.Spanne.Alles, null, null, null, jetzt, Berlin);
        Assert.Equal(3, alles.Zeilen.Count);
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
