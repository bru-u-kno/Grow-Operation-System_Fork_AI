using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Frist je Gerät — die eine Rechnung, aus der Wartungs-Reiter und Erinnerung lesen.
/// </summary>
/// <remarks>
/// Fork AI (02.10.2026): Die Fälle stammen unverändert aus
/// <c>GrowDiary.React/src/features/geraete/wartung-zeilen.test.ts</c>. Dort rechnete
/// der Reiter die Fristen bisher selbst; mit dem Umzug ins Backend darf sich an
/// keinem Ergebnis etwas ändern. Der Befund dahinter (Durchsicht 02.10.2026):
/// gerechnet wurde nur Einbaudatum + Intervall — erledigte Wartungen und
/// Kalibrierungen, abgesagte Termine und ausgemusterte Geräte spielten keine Rolle.
/// </remarks>
public sealed class WartungsFristenTests
{
    private static readonly DateTime Juni = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Tag(string iso) => DateTime.SpecifyKind(DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);

    private static HardwareItem Teil(int id, int? pruefen = null, int? kalibrieren = null, bool mitEinbau = true,
        HardwareItemStatus status = HardwareItemStatus.Active)
        => new()
        {
            Id = id, Name = $"Gerät {id}", Category = "Sensor", Status = status,
            InstalledAtUtc = mitEinbau ? Juni : null,
            InspectionIntervalDays = pruefen, CalibrationIntervalDays = kalibrieren,
        };

    private static MaintenanceEvent Wartung(int id, int geraet, MaintenanceEventStatus status = MaintenanceEventStatus.Planned,
        string? faellig = null, string? erledigt = null)
        => new()
        {
            Id = id, HardwareItemId = geraet, Status = status, Title = "Prüfung",
            DueAtUtc = faellig is null ? null : Tag(faellig),
            PerformedAtUtc = erledigt is null ? null : Tag(erledigt),
        };

    private static CalibrationEvent Kalibrierung(int id, int geraet, CalibrationEventStatus status = CalibrationEventStatus.Completed,
        string? faellig = null, string? erledigt = null, string? naechster = null)
        => new()
        {
            Id = id, HardwareItemId = geraet, Status = status, Title = "pH kalibrieren",
            DueAtUtc = faellig is null ? null : Tag(faellig),
            PerformedAtUtc = erledigt is null ? null : Tag(erledigt),
            NextDueAtUtc = naechster is null ? null : Tag(naechster),
        };

    private static string? Iso(DateTime? d) => d?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void NachEinerKalibrierungZaehltIhrFolgetermin_NichtDasEinbaudatum()
    {
        var fristen = WartungDueService.FristenRechnen(
            [Teil(1, kalibrieren: 14)], [],
            [Kalibrierung(1, 1, erledigt: "2026-09-29", naechster: "2026-10-13")]);

        Assert.Single(fristen);
        Assert.Equal("2026-10-13", Iso(fristen[0].FaelligUtc));
    }

    [Fact]
    public void OhneEinbaudatumAberMitKalibrierung_StehtTrotzdemEineFristDa()
    {
        var fristen = WartungDueService.FristenRechnen(
            [Teil(1, kalibrieren: 14, mitEinbau: false)], [],
            [Kalibrierung(1, 1, erledigt: "2026-09-29")]);

        Assert.False(fristen[0].OhneFrist);
        Assert.Equal("2026-10-13", Iso(fristen[0].FaelligUtc));
    }

    [Fact]
    public void EinGeplanterTerminTraegtSeineEigeneFrist_UndVerdraengtDieGerechnete()
    {
        var fristen = WartungDueService.FristenRechnen(
            [Teil(1, kalibrieren: 14)], [],
            [
                Kalibrierung(1, 1, erledigt: "2026-09-13", naechster: "2026-09-27"),
                Kalibrierung(2, 1, status: CalibrationEventStatus.Planned, faellig: "2026-09-27"),
            ]);

        Assert.Single(fristen);
        Assert.Equal("kalibrierung-2", fristen[0].Schluessel);
    }

    [Fact]
    public void NachEinerErledigtenPruefungZaehltSie_NichtDerEinbau()
    {
        var fristen = WartungDueService.FristenRechnen(
            [Teil(1, pruefen: 30)],
            [Wartung(1, 1, MaintenanceEventStatus.Completed, erledigt: "2026-09-20")], []);

        Assert.Equal("2026-10-20", Iso(fristen[0].FaelligUtc));
    }

    [Fact]
    public void EinAbgesagterTerminIstNichtOffen()
    {
        var fristen = WartungDueService.FristenRechnen(
            [Teil(1)], [Wartung(1, 1, MaintenanceEventStatus.Cancelled, faellig: "2026-08-01")], []);

        Assert.Empty(fristen);
    }

    [Fact]
    public void AusgemusterteGeraeteZaehlenNichtMit_WederIntervallNochTermin()
    {
        var fristen = WartungDueService.FristenRechnen(
            [Teil(1, pruefen: 30, status: HardwareItemStatus.Retired)],
            [Wartung(1, 1, faellig: "2026-08-01")], []);

        Assert.Empty(fristen);
    }

    [Fact]
    public void KalibrierenUndPruefenAmSelbenGeraet_SindZweiZeilen()
    {
        var fristen = WartungDueService.FristenRechnen([Teil(1, pruefen: 30, kalibrieren: 14)], [], []);

        Assert.Equal(["Kalibrieren", "Prüfen"], fristen.Select(f => f.Titel).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OhneEinbauUndOhneAbschluss_OhneFrist()
    {
        var fristen = WartungDueService.FristenRechnen([Teil(1, kalibrieren: 14, mitEinbau: false)], [], []);

        Assert.True(fristen[0].OhneFrist);
    }

    // ---------- über die Vitest-Fälle hinaus ----------

    [Fact]
    public void SortiertNachFrist_OhneFristAmEnde()
    {
        var fristen = WartungDueService.FristenRechnen(
            [Teil(1, pruefen: 30), Teil(2, pruefen: 10), Teil(3, kalibrieren: 14, mitEinbau: false)], [], []);

        Assert.Equal(["wartung-t-2", "wartung-t-1", "kalibrierung-t-3"], fristen.Select(f => f.Schluessel));
    }

    [Fact]
    public void EineGescheiterteKalibrierungHatTrotzdemStattgefunden()
    {
        var fristen = WartungDueService.FristenRechnen(
            [Teil(1, kalibrieren: 14)], [],
            [Kalibrierung(1, 1, status: CalibrationEventStatus.Failed, erledigt: "2026-09-29")]);

        Assert.Equal(WartungsFristQuelle.LetzterAbschluss, fristen[0].Quelle);
        Assert.Equal("2026-10-13", Iso(fristen[0].FaelligUtc));
    }
}
