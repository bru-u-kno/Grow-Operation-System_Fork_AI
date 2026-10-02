using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Verschleiss, Prüfung, Sicherung — die Termine, die bisher nur herumlagen.
/// </summary>
/// <remarks>
/// Dieselbe Fehlerklasse wie beim Wasserwechsel vor beta.29: Zahlen am Datensatz,
/// die niemand liest. Diese Tests halten fest, dass sie jetzt gelesen werden —
/// und dass gerechnet wird mit dem, was der Betreiber selbst eingetragen hat.
/// </remarks>
public sealed class WartungDueTests
{
    private static readonly DateTime Jetzt = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);

    private static HardwareItem Geraet(
        int id = 1, string name = "Luftstein", int? lebensdauer = null,
        int? pruefintervall = null, int einbauVorTagen = 0)
        => new()
        {
            Id = id,
            Name = name,
            Status = HardwareItemStatus.Active,
            InstalledAtUtc = Jetzt.AddDays(-einbauVorTagen),
            ExpectedLifespanDays = lebensdauer,
            InspectionIntervalDays = pruefintervall,
        };

    /// <summary>Keine Fristen — für die Fälle, in denen es nur um Lebensdauer oder Sicherung geht.</summary>
    private static readonly IReadOnlyList<WartungsFrist> Keine = [];

    /// <summary>Eine frische Sicherung, damit sie den Tests nicht dazwischenfunkt.</summary>
    private static readonly DateTime FrischGesichert = Jetzt.AddDays(-1);

    [Fact]
    public void APartPastItsLifespanIsDueForReplacement()
    {
        var punkte = WartungDueService.Beurteilen(
            [Geraet(lebensdauer: 180, einbauVorTagen: 200)], Keine, FrischGesichert, Jetzt);

        var verschleiss = punkte.Single(p => p.Bereich == "Verschleiß");
        Assert.Equal("kritisch", verschleiss.Stufe);
        Assert.Contains("200 Tagen im Einsatz", verschleiss.Meldung);
        Assert.Contains("vorgesehen sind 180", verschleiss.Meldung);
        // Die Zahl ist SEINE — das muss dastehen, damit sie niemand fuer Wissen haelt.
        Assert.Contains("aus deinem Geräte-Eintrag", verschleiss.Herkunft);
    }

    [Fact]
    public void TheWarningComesEarlyEnoughToOrderAReplacement()
    {
        // 165 von 180 Tagen: 91 % — die Vorwarnung soll kommen, solange Zeit
        // zum Bestellen bleibt, nicht erst wenn das Teil schon durch ist.
        var punkte = WartungDueService.Beurteilen(
            [Geraet(lebensdauer: 180, einbauVorTagen: 165)], Keine, FrischGesichert, Jetzt);

        var verschleiss = punkte.Single(p => p.Bereich == "Verschleiß");
        Assert.Equal("warnung", verschleiss.Stufe);
        Assert.Contains("noch 15 von 180 Tagen", verschleiss.Meldung);
    }

    [Fact]
    public void AFreshPartSaysNothing()
    {
        var punkte = WartungDueService.Beurteilen(
            [Geraet(lebensdauer: 180, einbauVorTagen: 20)], Keine, FrischGesichert, Jetzt);

        Assert.Empty(punkte);
    }

    [Fact]
    public void WithoutNumbersOnTheItemNothingIsInvented()
    {
        // Kein Lebensdauer-, kein Pruefintervall-Eintrag: dann gibt es dazu auch
        // nichts zu sagen. Eine erfundene Standard-Lebensdauer waere geraten.
        var punkte = WartungDueService.Beurteilen(
            [Geraet(einbauVorTagen: 900)], Keine, FrischGesichert, Jetzt);

        Assert.Empty(punkte);
    }

    [Fact]
    public void TheInspectionClockStartsAtInstallAndResetsOnACompletedService()
    {
        var geraet = Geraet(id: 7, name: "pH-Sonde", pruefintervall: 30, einbauVorTagen: 100);

        // Nie geprueft: es zaehlt der Einbau, und der Text sagt das auch.
        var ohne = WartungDueService.Beurteilen(
            [geraet], WartungDueService.FristenRechnen([geraet], [], []), FrischGesichert, Jetzt);
        var punkt = ohne.Single(p => p.Bereich == "Prüfung");
        Assert.Contains("seit dem Einbau vor 100 Tagen", punkt.Meldung);
        Assert.Contains("ohne Prüfeintrag zählt das Einbaudatum", punkt.Herkunft);

        // Vor zehn Tagen gewartet: die Uhr beginnt neu, also nichts faellig.
        var gewartet = new MaintenanceEvent
        {
            Id = 1, HardwareItemId = 7, Status = MaintenanceEventStatus.Completed, PerformedAtUtc = Jetzt.AddDays(-10),
        };
        var mit = WartungDueService.Beurteilen(
            [geraet], WartungDueService.FristenRechnen([geraet], [gewartet], []), FrischGesichert, Jetzt);
        Assert.DoesNotContain(mit, p => p.Bereich == "Prüfung");
    }

    /// <summary>
    /// Fork AI (02.10.2026): Erinnerung und Wartungs-Reiter lesen dieselbe Frist.
    /// Vorher kannte die Erinnerung weder den Folgetermin einer Kalibrierung noch
    /// einen geplanten Termin.
    /// </summary>
    [Fact]
    public void DieErinnerungLiestDieselbeFristWieDerReiter()
    {
        var sonde = Geraet(id: 3, name: "pH-Sonde", einbauVorTagen: 120);
        sonde.CalibrationIntervalDays = 14;

        // Nie kalibriert: Einbau + 14 Tage liegt lange zurück — die Erinnerung kommt.
        var nie = WartungDueService.Beurteilen(
            [sonde], WartungDueService.FristenRechnen([sonde], [], []), FrischGesichert, Jetzt);
        var punkt = nie.Single(p => p.Bereich == "Kalibrierung");
        Assert.Equal("kritisch", punkt.Stufe);

        // Vor drei Tagen kalibriert, Folgetermin in elf Tagen: nichts fällig.
        var kalibriert = new CalibrationEvent
        {
            Id = 1, HardwareItemId = 3, Status = CalibrationEventStatus.Completed,
            PerformedAtUtc = Jetzt.AddDays(-3), NextDueAtUtc = Jetzt.AddDays(11),
        };
        var frisch = WartungDueService.Beurteilen(
            [sonde], WartungDueService.FristenRechnen([sonde], [], [kalibriert]), FrischGesichert, Jetzt);
        Assert.DoesNotContain(frisch, p => p.Bereich == "Kalibrierung");

        // Ein geplanter, überfälliger Termin steht auf der Aktionsseite schon als
        // Termin — die Erinnerung nennt ihn nicht ein zweites Mal.
        var geplant = new CalibrationEvent
        {
            Id = 2, HardwareItemId = 3, Status = CalibrationEventStatus.Planned, DueAtUtc = Jetzt.AddDays(-5),
        };
        var fristen = WartungDueService.FristenRechnen([sonde], [], [geplant]);
        Assert.Single(fristen);
        Assert.Equal(WartungsFristQuelle.Geplant, fristen[0].Quelle);
        Assert.DoesNotContain(
            WartungDueService.Beurteilen([sonde], fristen, FrischGesichert, Jetzt),
            p => p.Bereich == "Kalibrierung");
    }

    [Fact]
    public void NoBackupAtAllIsTheLoudestOfThemAll()
    {
        var punkte = WartungDueService.Beurteilen([], Keine, letzteSicherung: null, Jetzt);

        var sicherung = punkte.Single();
        Assert.Equal("kritisch", sicherung.Stufe);
        Assert.Contains("noch keine Sicherung", sicherung.Meldung);
    }

    [Fact]
    public void AnAgingBackupWarnsAndSaysWhatIsAtStake()
    {
        var punkte = WartungDueService.Beurteilen(
            [], Keine, Jetzt.AddDays(-40), Jetzt);

        var sicherung = punkte.Single();
        Assert.Equal("warnung", sicherung.Stufe);
        Assert.Contains("vor 40 Tagen", sicherung.Meldung);
        Assert.Contains("Faustregel", sicherung.Herkunft);
    }

    [Fact]
    public void RetiredGearIsNotNagged()
    {
        // Ausgemustertes Geraet: es liegt in der Schublade, nicht im Eimer.
        var alt = Geraet(lebensdauer: 30, einbauVorTagen: 900);
        alt.Status = HardwareItemStatus.Retired;

        var punkte = WartungDueService.Beurteilen(
            [alt], Keine, FrischGesichert, Jetzt);

        // Beurteilen filtert nicht selbst — das tut Offen(). Hier zaehlt nur,
        // dass die Rechnung stimmt; der Status-Filter hat seinen eigenen Weg.
        Assert.Single(punkte);
    }
}
