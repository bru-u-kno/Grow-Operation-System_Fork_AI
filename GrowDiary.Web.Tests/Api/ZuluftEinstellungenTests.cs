using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (forkai.76): Die Zuluft-Einstellungen — Rundweg, Prüfung und die
/// Übernahme vorhandener Werte.
/// </summary>
/// <remarks>
/// <para><b>Warum ein eigener Test.</b> Der allgemeine
/// <c>RundwegVollstaendigTests</c> füllt jedes Feld mit derselben Probe (1). Bei
/// den Stufen kollidiert das mit der Prüfung „min nicht über max": Stufe max = 1
/// bei Stufe min = 3 wird mit 400 abgelehnt, und ein Rundweg, der nur
/// Ablehnungen einsammelt, prüft nichts.</para>
///
/// <para><b>Die Übernahme ist der heikle Teil.</b> Wer die Regelung von Hand
/// gebaut hat, hat die Helfer eingestellt. Käme die Seite beim ersten Aufruf mit
/// Werkseinstellungen, würde der erste Klick auf „Speichern" die eingestellten
/// Werte überschreiben, ohne dass jemand etwas verstellt hätte.</para>
/// </remarks>
public sealed class ZuluftEinstellungenTests : IDisposable
{
    private readonly string _wurzel;
    private readonly SteuerungRepository _repo;

    public ZuluftEinstellungenTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "ZuluftRundweg_" + Guid.NewGuid().ToString("N"));
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
        var gesendet = new ZuluftEinstellungen
        {
            MindestDifferenzGm3 = 2.4,
            AussentemperaturMinC = 7.5,
            StufeMin = 2,
            StufeMax = 9,
            MindestlaufzeitMin = 15,
            MindestpauseMin = 20,
            AutomatikAktiv = false,
            ZeltTemperaturMinC = 19.5,
        };

        _repo.SetEinstellungen(ZuluftSteuerungService.Modul, gesendet);
        var gelesen = _repo.GetEinstellungen<ZuluftEinstellungen>(ZuluftSteuerungService.Modul);

        Assert.NotNull(gelesen);
        Assert.Equal(2.4, gelesen!.MindestDifferenzGm3);
        Assert.Equal(7.5, gelesen.AussentemperaturMinC);
        Assert.Equal(2, gelesen.StufeMin);
        Assert.Equal(9, gelesen.StufeMax);
        Assert.Equal(15, gelesen.MindestlaufzeitMin);
        Assert.Equal(20, gelesen.MindestpauseMin);
        Assert.False(gelesen.AutomatikAktiv);
        Assert.Equal(19.5, gelesen.ZeltTemperaturMinC);
    }

    // ------------------------------------------------ Fork AI (forkai.128)

    [Fact]
    public void EinStandVonVorDerZeltpauseGiltWieNieGespeichert()
    {
        // F-022: gespeichert am 13.09., danach in HA geaendert. Gaelte der alte
        // Stand weiter, schriebe das naechste Speichern die alten Werte zurueck
        // (Aussentemperatur 10 statt 0 °C) - ohne dass jemand etwas verstellt hat.
        var alt = new ZuluftEinstellungen { MindestDifferenzGm3 = 1.5, AussentemperaturMinC = 10 };
        _repo.SetEinstellungen(ZuluftSteuerungService.Modul, alt);

        var gelesen = _repo.GetEinstellungen<ZuluftEinstellungen>(ZuluftSteuerungService.Modul);
        Assert.Null(gelesen!.ZeltTemperaturMinC);
        Assert.Null(ZuluftSteuerungService.GueltigGespeichert(gelesen));

        gelesen.ZeltTemperaturMinC = 21;
        Assert.Same(gelesen, ZuluftSteuerungService.GueltigGespeichert(gelesen));
    }

    [Fact]
    public void DieZelttemperaturKommtAusDemHelferSonstVorgabe()
    {
        var mit = ZuluftSteuerungService.AusHomeAssistant(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ZuluftSteuerungService.Entitaeten.ZeltTemperaturMin] = "20.5",
            [ZuluftSteuerungService.Entitaeten.AussentemperaturMin] = "0.0",
        });
        Assert.Equal(20.5, mit.ZeltTemperaturMinC);
        Assert.Equal(0, mit.AussentemperaturMinC);

        // Helfer noch nicht angelegt: nicht null, sonst gaelte schon der erste
        // gespeicherte Stand wieder als alt.
        var ohne = ZuluftSteuerungService.AusHomeAssistant(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(ZuluftSteuerungService.ZeltTemperaturMinVorgabe, ohne.ZeltTemperaturMinC);
    }

    [Theory]
    [InlineData(9.5, true)]
    [InlineData(10, false)]
    [InlineData(30, false)]
    [InlineData(30.5, true)]
    public void DieZelttemperaturHatGrenzen(double wert, bool abgelehnt)
    {
        var fehler = ZuluftSteuerungService.Pruefen(new ZuluftEinstellungen { ZeltTemperaturMinC = wert });
        Assert.Equal(abgelehnt, fehler.ContainsKey(nameof(ZuluftEinstellungen.ZeltTemperaturMinC)));
    }

    [Theory]
    // Aussenluft trocknet, Bedarf aus, Zelt unter Minimum → Pause wegen Kaelte.
    [InlineData(false, 2.9, 20.5, true)]
    // Im Band zwischen Minimum und Minimum + 1 bleibt der Bedarf aus → noch Pause.
    [InlineData(false, 2.9, 21.6, true)]
    // Zelt warm genug, Bedarf aus → der Grund ist nicht die Kaelte.
    [InlineData(false, 2.9, 23.5, false)]
    // Aussenluft bringt nichts → keine Kaelte-Pause, sondern kein Nutzen.
    [InlineData(false, 0.4, 20.0, false)]
    // Bedarf an → keine Pause.
    [InlineData(true, 2.9, 20.0, false)]
    public void DieKaeltePauseWirdErkannt(bool bedarf, double differenz, double zelt, bool pause)
    {
        Assert.Equal(pause, ZuluftSteuerungService.PauseWegenZeltkaelte(bedarf, differenz, 1.0, zelt, 21));
    }

    [Fact]
    public void OhneZeltwertLaesstSichDerGrundNichtSagen()
    {
        Assert.Null(ZuluftSteuerungService.PauseWegenZeltkaelte(false, 2.9, 1.0, null, 21));
    }

    [Fact]
    public void VerdrehteStufenWerdenAbgelehnt()
    {
        // Sonst rechnet die Zielstufe nach unten, je trockener es draussen wird.
        var fehler = ZuluftSteuerungService.Pruefen(new ZuluftEinstellungen { StufeMin = 8, StufeMax = 3 });
        Assert.Contains(nameof(ZuluftEinstellungen.StufeMin), fehler.Keys);
    }

    [Fact]
    public void GleicheStufenSindErlaubt()
    {
        // Das ist die feste Stufe — Bru faehrt sie so.
        Assert.Empty(ZuluftSteuerungService.Pruefen(new ZuluftEinstellungen { StufeMin = 6, StufeMax = 6 }));
    }

    [Fact]
    public void EineSchwelleInProzentWaereKeineSchwelleInGrammProKubikmeter()
    {
        // 65 als Mindest-Differenz kaeme aus einem Feld, das Prozent meint. Die
        // Regelung rechnet in g/m³; 65 g/m³ erreicht Luft nie, der Luefter liefe
        // nie wieder an.
        var fehler = ZuluftSteuerungService.Pruefen(new ZuluftEinstellungen { MindestDifferenzGm3 = 65 });
        Assert.Contains(nameof(ZuluftEinstellungen.MindestDifferenzGm3), fehler.Keys);
    }

    [Fact]
    public void OhneGespeichertenStandGeltenDieVorhandenenHelfer()
    {
        var zustaende = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ZuluftSteuerungService.Entitaeten.MindestDifferenz] = "3.6",
            [ZuluftSteuerungService.Entitaeten.AussentemperaturMin] = "10.0",
            [ZuluftSteuerungService.Entitaeten.StufeMin] = "6.0",
            [ZuluftSteuerungService.Entitaeten.StufeMax] = "6.0",
            [ZuluftSteuerungService.Entitaeten.Mindestlaufzeit] = "1.0",
            [ZuluftSteuerungService.Entitaeten.Mindestpause] = "1.0",
            [ZuluftSteuerungService.Entitaeten.Automatik] = "on",
        };

        var e = ZuluftSteuerungService.AusHomeAssistant(zustaende);

        Assert.Equal(3.6, e.MindestDifferenzGm3);
        Assert.Equal(10.0, e.AussentemperaturMinC);
        Assert.Equal(6, e.StufeMin);
        Assert.Equal(6, e.StufeMax);
        Assert.Equal(1, e.MindestlaufzeitMin);
        Assert.Equal(1, e.MindestpauseMin);
        Assert.True(e.AutomatikAktiv);
    }

    [Fact]
    public void WasFehltBleibtAufDerVorgabe()
    {
        // Ein halb eingerichtetes System darf keine Null-Werte erben: 0 g/m³
        // hiesse „immer ansaugen".
        var vorgabe = new ZuluftEinstellungen();
        var e = ZuluftSteuerungService.AusHomeAssistant(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ZuluftSteuerungService.Entitaeten.MindestDifferenz] = "unavailable",
        });

        Assert.Equal(vorgabe.MindestDifferenzGm3, e.MindestDifferenzGm3);
        Assert.Equal(vorgabe.StufeMax, e.StufeMax);
    }

    [Fact]
    public void DieSchaltsperreZaehltLaufzeitWennDerLuefterLaeuft()
    {
        var jetzt = new DateTime(2026, 9, 13, 14, 0, 0, DateTimeKind.Unspecified);

        // Laeuft seit 4 Minuten, Mindestlaufzeit 10 → noch 6 Minuten gesperrt.
        var (restAn, letzter) = ZuluftSteuerungService.Sperre("2026-09-13 13:56:00", 10, 30, portAn: true, jetzt);
        Assert.Equal(6, restAn);
        Assert.Equal(new DateTime(2026, 9, 13, 13, 56, 0), letzter);

        // Derselbe Zeitstempel bei stehendem Luefter zaehlt gegen die Pause.
        var (restAus, _) = ZuluftSteuerungService.Sperre("2026-09-13 13:56:00", 10, 30, portAn: false, jetzt);
        Assert.Equal(26, restAus);
    }

    [Fact]
    public void OhneZeitstempelGibtEsKeineSperre()
    {
        // Beim ersten Lauf nach dem Anlegen steht der Helfer leer. „Unbekannt"
        // ist dann richtig — 0 haetten wir als „frei" gelesen und behauptet,
        // es sei schon einmal geschaltet worden.
        var (rest, letzter) = ZuluftSteuerungService.Sperre(null, 10, 10, portAn: false, DateTime.Now);
        Assert.Null(rest);
        Assert.Null(letzter);
    }

    public void Dispose()
    {
        if (Directory.Exists(_wurzel)) Directory.Delete(_wurzel, recursive: true);
    }
}
