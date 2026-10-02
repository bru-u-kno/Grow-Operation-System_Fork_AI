using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Infrastructure;

/// <summary>
/// Die Altlast-Zuordnung „Grow ohne Zelt → Hauptzelt" läuft einmal, nicht bei
/// jedem Start.
/// </summary>
/// <remarks>
/// <para><b>Der Befund (02.10.2026).</b> <c>AutoAssignExistingGrowsToTents</c>
/// lief bei JEDEM Start. Wer ein Zelt löscht, behält dessen archivierte Grows —
/// ohne Zelt (<c>DeleteTentWithCleanup</c>). Nach dem nächsten Neustart standen
/// sie im „Hauptzelt", zwischen dessen eigenen Läufen.</para>
/// </remarks>
public sealed class GrowZeltZuordnungTests : IDisposable
{
    private readonly string _temp;
    private readonly AppPaths _paths;

    public GrowZeltZuordnungTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "GrowZelt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _paths = new AppPaths(_temp);
        TestDatabase.Initialize(_paths);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    private void Neustart()
        => new DatabaseInitializer(_paths, NullLogger<DatabaseInitializer>.Instance).Initialize();

    private void Fuehre(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private int Grow(GrowRepository grows, int? zelt, string name, GrowStatus status) => grows.CreateGrow(new GrowRun
    {
        TentId = zelt,
        Name = name,
        StartDate = new DateTime(2026, 1, 1),
        Status = status,
    });

    [Fact]
    public void GrowAusGeloeschtemZeltBleibtNachDemNeustartOhneZelt()
    {
        var grows = new GrowRepository(_paths);
        var hauptzelt = grows.CreateTent(new Tent { Name = "Hauptzelt", TentType = TentType.Production });
        var klonbox = grows.CreateTent(new Tent { Name = "Klon-Box", TentType = TentType.MultiPurpose });
        var eigener = Grow(grows, hauptzelt.Id, "Lauf im Hauptzelt", GrowStatus.Completed);
        var fremder = Grow(grows, klonbox.Id, "Lauf der Klon-Box", GrowStatus.Completed);

        new TentRepository(_paths).DeleteTentWithCleanup(klonbox.Id);
        Assert.Null(grows.GetGrow(fremder)!.TentId); // Mengenwaechter: der Fall entsteht wirklich

        Neustart();
        Neustart(); // ein zweiter Start darf es auch nicht nachholen

        Assert.Null(grows.GetGrow(fremder)!.TentId);
        Assert.Equal(hauptzelt.Id, grows.GetGrow(eigener)!.TentId);
    }

    [Fact]
    public void EineDatenbankVorDenZeltenWirdEinmalZugeordnet()
    {
        // Die Altlast, fuer die die Zuordnung gebaut wurde: Grows aus einer
        // Fassung, in der es Grows.TentId noch nicht gab. Nachgebaut wie in
        // BestandsdatenbankStartetTests — aktuelle Datei, Neuerung heraus.
        var grows = new GrowRepository(_paths);
        var hauptzelt = grows.CreateTent(new Tent { Name = "Hauptzelt", TentType = TentType.Production });
        var alt = Grow(grows, null, "Lauf aus alter Zeit", GrowStatus.Completed);
        Fuehre("""
            DROP INDEX IF EXISTS IX_Grows_TentId_Status;
            ALTER TABLE Grows DROP COLUMN TentId;
            """);
        SqliteConnection.ClearAllPools();

        Neustart();

        Assert.Equal(hauptzelt.Id, grows.GetGrow(alt)!.TentId);
    }
}
