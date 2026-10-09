using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-016, Etappe 2): Welche Steuerungen der Nutzer hat — echte Datenbank, kein Mock.
/// </summary>
public sealed class SteuerungAuswahlTests : IDisposable
{
    private readonly string _temp;
    private readonly AppSettingsRepository _einstellungen;
    private readonly SteuerungRepository _steuerung;
    private readonly SteuerungAuswahlService _auswahl;

    public SteuerungAuswahlTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "Auswahl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        var pfade = new AppPaths(_temp);
        TestDatabase.Initialize(pfade);
        _einstellungen = new AppSettingsRepository(pfade);
        _steuerung = new SteuerungRepository(pfade);
        _auswahl = new SteuerungAuswahlService(_einstellungen, _steuerung);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { /* Aufräumen darf scheitern. */ }
    }

    [Fact]
    public void EineFrischeAnlageHatKeineSteuerungGewaehlt()
    {
        // Ohne Zuordnung und ohne Auswahl: nichts. Die Rückfall-Vorgaben der Rollen zählen nicht
        // (sonst wäre jede Steuerung auf einer neuen Anlage „gewählt").
        Assert.Empty(_auswahl.Gewaehlt());
        Assert.False(_auswahl.Gespeichert());
    }

    [Fact]
    public void EineBestandsanlageBehaeltIhreSteuerungenOhneEtwasZuSpeichern()
    {
        _steuerung.SetGeraet("co2", "co2_sensor", "sensor.irgendein_co2");
        _steuerung.SetGeraet(EntfeuchterZusatzSteuerungService.Modul, "zusatz_schalter", "switch.zweiter");
        _steuerung.SetGeraet("licht", "licht_modus", "select.lampe");

        // Der Zusatz-Entfeuchter gehört zur Auswahl „Entfeuchter".
        Assert.Equal(["co2", "entfeuchter", "licht"], _auswahl.Gewaehlt());
        Assert.False(_auswahl.Gespeichert());
    }

    [Fact]
    public void EineLeereZuordnungZaehltNicht()
    {
        _steuerung.SetGeraet("zuluft", "aussen_temp", "sensor.x");
        _steuerung.SetGeraet("zuluft", "aussen_temp", null); // wieder entfernt
        Assert.Empty(_auswahl.Gewaehlt());
    }

    [Fact]
    public void DieGespeicherteAuswahlGiltWoertlichAuchGegenZugeordneteGeraete()
    {
        _steuerung.SetGeraet("co2", "co2_sensor", "sensor.irgendein_co2");

        Assert.Empty(_auswahl.Speichern(["chiller"]));

        Assert.Equal(["chiller"], _auswahl.Gewaehlt());
        Assert.True(_auswahl.Gespeichert());
    }

    [Fact]
    public void EineLeereAuswahlIstEineAuswahl()
    {
        // „Ich habe nichts" ist eine Antwort — sie darf nicht als „nie gewählt" gelten und
        // auf die zugeordneten Geräte zurückfallen.
        _steuerung.SetGeraet("co2", "co2_sensor", "sensor.irgendein_co2");
        Assert.Empty(_auswahl.Speichern([]));

        Assert.Empty(_auswahl.Gewaehlt());
        Assert.True(_auswahl.Gespeichert());
    }

    [Fact]
    public void UnbekannteKennungenWerdenAbgelehntUndNichtGespeichert()
    {
        var unbekannt = _auswahl.Speichern(["co2", "kaffeemaschine"]);

        Assert.Equal(["kaffeemaschine"], unbekannt);
        Assert.False(_auswahl.Gespeichert());
    }

    [Fact]
    public void Doppeltes_Und_Leeres_WirdBereinigt()
    {
        Assert.Empty(_auswahl.Speichern(["licht", " licht ", "", "co2"]));
        Assert.Equal(["co2", "licht"], _auswahl.Gewaehlt()); // Reihenfolge der Übersicht, nicht der Eingabe
    }

    [Fact]
    public void EinKaputterEintragBlendetNichtAlleAus()
    {
        _einstellungen.SetValue(SteuerungAuswahlService.Schluessel, "{kein json");
        _steuerung.SetGeraet("zuluft", "aussen_temp", "sensor.x");

        Assert.False(_auswahl.Gespeichert());
        Assert.Equal(["zuluft"], _auswahl.Gewaehlt());
    }

    [Theory]
    [InlineData("co2", "co2", true)]
    [InlineData("abluft", "co2", true)]           // die Abluft-Zeile gehört zur CO₂-Begasung
    [InlineData("abluft", "zuluft", false)]
    [InlineData("entfeuchter-zusatz", "entfeuchter", true)]
    [InlineData("entfeuchter", "co2", false)]
    public void DieUebersichtsZeilenFolgenIhrerSteuerung(string zeile, string gewaehlt, bool sichtbar)
        => Assert.Equal(sichtbar, SteuerungAuswahlService.Sichtbar(zeile, [gewaehlt]));

    [Fact]
    public void JedeZeileDerUebersichtGehoertZuGenauEinerSteuerung()
    {
        // Sonst bliebe eine Zeile für immer unsichtbar oder erschiene doppelt.
        var kennungen = new[] { "co2", "abluft", "entfeuchter", "entfeuchter-zusatz", "zuluft", "chiller", "licht" };
        foreach (var k in kennungen)
        {
            Assert.Single(SteuerungAuswahlService.Alle, a => a.Uebersicht.Contains(k));
        }
    }

    [Fact]
    public void JedesModulDesKatalogsGehoertZuEinerSteuerung()
    {
        // Ein Modul mit Rollen, das in keiner Steuerung vorkommt, ließe sich nie auswählen.
        var vergeben = SteuerungAuswahlService.Alle.SelectMany(a => a.Module).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ohne = SteuerungGeraeteRollen.Module.Where(m => !vergeben.Contains(m)).ToList();
        // Bluelab ist ein Zusatz am Gerätealarm, keine Steuerung (eigene Seite).
        Assert.Equal(["bluelab"], ohne);
    }

    [Fact]
    public void DiePflichtrollenWerdenGezaehlt()
    {
        var art = SteuerungAuswahlService.Finden("zuluft")!;
        var (davor, gesamt) = _auswahl.Pflichtrollen(art);
        Assert.Equal(0, davor);
        Assert.True(gesamt >= 5);

        _steuerung.SetGeraet("zuluft", "aussen_temp", "sensor.draussen");
        Assert.Equal((1, gesamt), _auswahl.Pflichtrollen(art));
    }
}
