using GrowDiary.Web.Infrastructure;

namespace GrowDiary.Web.Tests;

/// <summary>Die Aufbewahrungsregel für Sicherungen (Fehlerregister F-012).</summary>
public sealed class BackupAufbewahrungTests : IDisposable
{
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "BackupAufbewahrung_" + Guid.NewGuid().ToString("N"));

    public BackupAufbewahrungTests() => Directory.CreateDirectory(_ordner);

    public void Dispose()
    {
        try { Directory.Delete(_ordner, recursive: true); } catch { }
    }

    private void Datei(string name, int minutenAlt)
    {
        var pfad = Path.Combine(_ordner, name);
        File.WriteAllText(pfad, name);
        File.SetLastWriteTimeUtc(pfad, DateTime.UtcNow.AddMinutes(-minutenAlt));
    }

    [Fact]
    public void Aufraeumen_BehaeltJeArtFuenfUndSchontGeschuetzte()
    {
        for (var i = 0; i < 9; i++) Datei($"grow-os-backup-{i}.zip", minutenAlt: 100 - i);
        var aeltestes = "grow-os-backup-0.zip";
        Assert.Equal(9, Directory.GetFiles(_ordner).Length); // Mengenwächter

        var geloescht = BackupAufbewahrung.Aufraeumen(_ordner, new[] { aeltestes });

        var bleibt = Directory.GetFiles(_ordner).Select(Path.GetFileName).ToHashSet();
        Assert.Contains(aeltestes, bleibt);
        Assert.Equal(6, bleibt.Count);               // fünf neueste + das geschützte
        Assert.Equal(3, geloescht.Count);            // 1, 2, 3
        Assert.All(new[] { 4, 5, 6, 7, 8 }, i => Assert.Contains($"grow-os-backup-{i}.zip", bleibt));
    }

    [Fact]
    public void Aufraeumen_BeiFuenfOderWenigerPassiertNichts()
    {
        for (var i = 0; i < BackupAufbewahrung.JeArt; i++) Datei($"grow-os-backup-{i}.zip", minutenAlt: i);

        Assert.Empty(BackupAufbewahrung.Aufraeumen(_ordner, Array.Empty<string>()));
        Assert.Equal(BackupAufbewahrung.JeArt, Directory.GetFiles(_ordner).Length);
    }

    [Theory]
    [InlineData("grow-os-backup-20260101.zip", "sicherung")]
    [InlineData("grow-os-backup-import-safety-20260101.zip", "import")]
    [InlineData("GROW-OS-BACKUP-1.ZIP", "sicherung")]
    [InlineData("grow-os-backup-1.tar", null)]
    [InlineData("grow-diary.db", null)]
    [InlineData("meine-sicherung.zip", null)]
    public void ArtVon_ErkenntNurDasEigeneSchema(string datei, string? art)
        => Assert.Equal(art, BackupAufbewahrung.ArtVon(datei));

    [Fact]
    public void Auflisten_OhneOrdnerIstLeer()
        => Assert.Empty(BackupAufbewahrung.Auflisten(Path.Combine(_ordner, "gibt-es-nicht")));
}
