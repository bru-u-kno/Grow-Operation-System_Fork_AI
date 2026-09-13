using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI: Die Kühler-Einstellungen — Rundweg, Grenzen, Übernahme und die
/// doppelt geschaltete Steckdose.
/// </summary>
/// <remarks>
/// <para><b>Warum ein eigener Test.</b> Der allgemeine
/// <c>RundwegVollstaendigTests</c> füllt jedes Feld mit derselben Probe (1). Bei
/// den Zieltemperaturen kollidiert das mit der Untergrenze von 4 °C — das PUT
/// lehnt mit 400 ab, und ein Rundweg, der nur Ablehnungen einsammelt, prüft
/// nichts.</para>
///
/// <para><b>Die Doppelsteuerung ist der teure Fall.</b> Schaltet die
/// Steckdosen-Funktion der Crop-Steering-Seite dieselbe Entität wie die
/// Regelung, greifen zwei Stellen nach demselben Kompressor. Sichtbar wird das
/// erst als Dose, die von selbst umspringt — deshalb muss die Erkennung geprüft
/// sein und nicht nur gemeint.</para>
/// </remarks>
public sealed class ChillerEinstellungenTests : IDisposable
{
    private readonly string _wurzel;
    private readonly SteuerungRepository _repo;

    public ChillerEinstellungenTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "ChillerRundweg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        var pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(pfade);
        _repo = new SteuerungRepository(pfade);
    }

    [Fact]
    public void JedesFeldUeberlebtDasSpeichern()
    {
        // Bewusst überall andere Werte als die Vorgaben: ein Feld, das
        // verlorengeht, fiele sonst auf den Standard zurück und der Vergleich
        // waere trotzdem gruen.
        var gesendet = new ChillerEinstellungen
        {
            ZielTagC = 21.5,
            ZielNachtC = 16.5,
            HystereseK = 0.4,
            MindestlaufzeitMin = 8,
            MindestpauseMin = 12,
            AutomatikAktiv = false,
        };

        _repo.SetEinstellungen(ChillerSteuerungService.Modul, gesendet);
        var gelesen = _repo.GetEinstellungen<ChillerEinstellungen>(ChillerSteuerungService.Modul);

        Assert.NotNull(gelesen);
        Assert.Equal(21.5, gelesen!.ZielTagC);
        Assert.Equal(16.5, gelesen.ZielNachtC);
        Assert.Equal(0.4, gelesen.HystereseK);
        Assert.Equal(8, gelesen.MindestlaufzeitMin);
        Assert.Equal(12, gelesen.MindestpauseMin);
        Assert.False(gelesen.AutomatikAktiv);
    }

    [Fact]
    public void EinZielAusserhalbDesBereichsWirdAbgelehnt()
    {
        var fehler = ChillerSteuerungService.Pruefen(new ChillerEinstellungen { ZielTagC = 2 });
        Assert.Contains(nameof(ChillerEinstellungen.ZielTagC), fehler.Keys);
    }

    [Fact]
    public void EinTotbandImMessrauschenWirdAbgelehnt()
    {
        // 0,05 K liegt unter der Auflösung der üblichen Wasserfühler: der
        // Kompressor takte dann im Rauschen.
        var fehler = ChillerSteuerungService.Pruefen(new ChillerEinstellungen { HystereseK = 0.05 });
        Assert.Contains(nameof(ChillerEinstellungen.HystereseK), fehler.Keys);
    }

    [Fact]
    public void NachtKaelterAlsTagIstErlaubtUndUmgekehrtAuch()
    {
        // Absichtlich keine Prüfung „Nacht muss kälter sein": in der späten
        // Blüte kann ein wärmeres Nachtziel gewollt sein, und eine Regel, die
        // das verbietet, stünde dem Nutzer im Weg.
        Assert.Empty(ChillerSteuerungService.Pruefen(new ChillerEinstellungen { ZielTagC = 18, ZielNachtC = 21 }));
    }

    [Fact]
    public void VorhandeneHelferWerdenUebernommen()
    {
        var zustaende = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ChillerSteuerungService.Entitaeten.ZielTag] = "19.5",
            [ChillerSteuerungService.Entitaeten.ZielNacht] = "17.0",
            [ChillerSteuerungService.Entitaeten.Mindestlaufzeit] = "7",
            [ChillerSteuerungService.Entitaeten.Mindestpause] = "9",
            [ChillerSteuerungService.Entitaeten.Automatik] = "off",
        };

        var e = ChillerSteuerungService.AusHomeAssistant(zustaende);

        Assert.Equal(19.5, e.ZielTagC);
        Assert.Equal(17.0, e.ZielNachtC);
        Assert.Equal(7, e.MindestlaufzeitMin);
        Assert.Equal(9, e.MindestpauseMin);
        Assert.False(e.AutomatikAktiv);
    }

    [Fact]
    public void EinFehlenderHelferLaesstDieVorgabeStehen()
    {
        var e = ChillerSteuerungService.AusHomeAssistant(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(new ChillerEinstellungen().ZielTagC, e.ZielTagC);
        Assert.True(e.AutomatikAktiv);
    }

    [Fact]
    public void DieselbeSteckdoseInBeidenWegenWirdGemeldet()
    {
        var zelte = new[]
        {
            new Tent { ChillerControlEnabled = true, ChillerSwitchEntityId = "switch.kuehler" },
        };

        Assert.Equal("switch.kuehler", ChillerSteuerungService.Doppelsteuerung(zelte, "switch.kuehler"));
        // Gross- und Kleinschreibung sind in Home Assistant dieselbe Entität.
        Assert.Equal("switch.kuehler", ChillerSteuerungService.Doppelsteuerung(zelte, "SWITCH.Kuehler"));
    }

    [Fact]
    public void EineAusgeschalteteKuehlerSteuerungIstKeineDoppelsteuerung()
    {
        // Der Eintrag darf stehenbleiben — geschaltet wird erst mit dem Schalter.
        var zelte = new[]
        {
            new Tent { ChillerControlEnabled = false, ChillerSwitchEntityId = "switch.kuehler" },
        };

        Assert.Null(ChillerSteuerungService.Doppelsteuerung(zelte, "switch.kuehler"));
    }

    [Fact]
    public void OhneZugeordneteSteckdoseGibtEsNichtsZuKollidieren()
    {
        var zelte = new[]
        {
            new Tent { ChillerControlEnabled = true, ChillerSwitchEntityId = "switch.kuehler" },
        };

        Assert.Null(ChillerSteuerungService.Doppelsteuerung(zelte, null));
        Assert.Null(ChillerSteuerungService.Doppelsteuerung(zelte, "   "));
    }

    [Fact]
    public void DieSperreRechnetGegenDenZeitstempelUndNichtGegenLastChanged()
    {
        var jetzt = new DateTime(2026, 9, 13, 17, 0, 0);
        var vorDreiMinuten = jetzt.AddMinutes(-3).ToString("yyyy-MM-dd HH:mm:ss");

        // Läuft der Kühler, zählt die Mindestlaufzeit: nach 3 von 5 Minuten
        // bleiben 2 übrig.
        var (restAn, letzter) = ChillerSteuerungService.Sperre(vorDreiMinuten, 5, 5, an: true, jetzt);
        Assert.Equal(2, restAn);
        Assert.NotNull(letzter);

        // Steht er, zählt die Pause — hier schon abgelaufen.
        var (restAus, _) = ChillerSteuerungService.Sperre(vorDreiMinuten, 5, 2, an: false, jetzt);
        Assert.Equal(0, restAus);
    }

    [Fact]
    public void OhneZeitstempelGibtEsKeineErfundeneSperre()
    {
        var (rest, letzter) = ChillerSteuerungService.Sperre("unknown", 5, 5, an: false, DateTime.Now);
        Assert.Null(rest);
        Assert.Null(letzter);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); }
        catch (IOException) { /* Aufräumen darf einen Test nicht rot färben. */ }
    }
}
