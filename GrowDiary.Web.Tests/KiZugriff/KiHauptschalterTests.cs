using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-011, 06.10.2026): Der globale Hauptschalter „KI-Funktionen".
/// </summary>
/// <remarks>
/// Voreinstellung: aus. Eine Installation, in der der Zugriff für KI-Assistenten
/// schon an ist (Brus), bleibt an, ohne dass etwas gespeichert werden muss —
/// und zwar solange, bis jemand den Schalter ausdrücklich setzt.
/// </remarks>
public sealed class KiHauptschalterTests : IDisposable
{
    private readonly string _ordner;
    private readonly AppSettingsRepository _einstellungen;
    private readonly KiHauptschalter _schalter;

    public KiHauptschalterTests()
    {
        _ordner = Path.Combine(Path.GetTempPath(), "KiHauptschalter_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_ordner);
        var pfade = new AppPaths(_ordner);
        TestDatabase.Initialize(pfade);
        _einstellungen = new AppSettingsRepository(pfade);
        _schalter = new KiHauptschalter(_einstellungen);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_ordner, recursive: true); } catch { }
    }

    [Fact]
    public void NeueInstallationHatKiAus()
        => Assert.False(_schalter.Aktiv);

    [Fact]
    public void BestehenderKiZugriffUebernimmtAnOhneZuSpeichern()
    {
        _einstellungen.SetValue(KiZugriffDienst.EinstellungAktiv, "true");

        Assert.True(_schalter.Aktiv);
        // Nichts gespeichert: wer den KI-Zugriff erst später einschaltet, bekommt keine eingefrorene Vorentscheidung.
        Assert.Null(_einstellungen.GetValue(KiHauptschalter.Einstellung));
    }

    [Fact]
    public void AusdruecklichesAusSchlaegtDenKiZugriff()
    {
        _einstellungen.SetValue(KiZugriffDienst.EinstellungAktiv, "true");

        _schalter.Setzen(false);

        Assert.False(_schalter.Aktiv);
    }

    [Fact]
    public void AusdruecklichesAnBleibtAn_AuchOhneKiZugriff()
    {
        _schalter.Setzen(true);

        Assert.True(_schalter.Aktiv);
        // „KI an" öffnet den Zugriff von außen NICHT von selbst.
        Assert.False(new KiZugriffDienst(new KiSchluesselRepository(new AppPaths(_ordner)), _einstellungen).Einstellungen().Aktiv);
    }

    [Fact]
    public void ZurueckschaltenStelltDenKiZugriffNichtUm()
    {
        _einstellungen.SetValue(KiZugriffDienst.EinstellungAktiv, "true");
        _schalter.Setzen(false);
        _schalter.Setzen(true);

        Assert.Equal("true", _einstellungen.GetValue(KiZugriffDienst.EinstellungAktiv));
    }
}
