using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Tests.Infrastructure;

/// <summary>
/// Kosten- und Geräte-Tabellen müssen in JEDER Datenbankdatei da sein, die der
/// Prozess anfasst — nicht nur in der ersten.
/// </summary>
/// <remarks>
/// <para><b>Der Befund (02.10.2026).</b> <see cref="KostenRepository"/> und
/// <see cref="GeraeteRepository"/> merkten sich „Schema angelegt" in einem
/// statischen Feld. Nach einem Restore aus einer Fassung ohne diese Tabellen
/// fehlten sie bis zum Neustart; die Testmappe brauchte eine Umgehung per
/// Reflexion (<c>TestDatabase.KostenSchemaVergessen</c>, jetzt entfernt).</para>
///
/// <para>Beide Fälle hier laufen OHNE diese Umgehung — genau das, was sie
/// verdeckt hat.</para>
/// </remarks>
public sealed class EigenesSchemaTests : IDisposable
{
    private readonly string _temp;

    public EigenesSchemaTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "EigenesSchema_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    private AppPaths NeueDatenbank(string name)
    {
        var paths = new AppPaths(Path.Combine(_temp, name));
        TestDatabase.Initialize(paths);
        return paths;
    }

    private static bool TabelleDa(AppPaths paths, string tabelle)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tabelle);
        return (long)command.ExecuteScalar()! > 0;
    }

    [Fact]
    public void ZweiteDatenbankImSelbenProzessBekommtIhreTabellen()
    {
        var erste = NeueDatenbank("a");
        var zweite = NeueDatenbank("b");
        Assert.NotEqual(erste.DatabasePath, zweite.DatabasePath);

        // Erste Datei: legt an und merkt es sich.
        new KostenRepository(erste).GetArtikel();
        new GeraeteRepository(erste).Geraete();
        Assert.True(TabelleDa(erste, "ForkVerbrauchsartikel"));
        Assert.True(TabelleDa(erste, "ForkGeraete"));

        // Zweite Datei: vorher „no such table", weil der Merker schon stand.
        Assert.False(TabelleDa(zweite, "ForkVerbrauchsartikel"), "Die zweite Datei darf die Tabelle vorher nicht haben, sonst prueft der Fall nichts.");
        var id = new KostenRepository(zweite).CreateArtikel(new Verbrauchsartikel { Name = "CO₂-Flasche", Einheit = "kg" });
        Assert.Contains(new KostenRepository(zweite).GetArtikel(), a => a.Id == id);
        Assert.Empty(new GeraeteRepository(zweite).Geraete());
        Assert.True(TabelleDa(zweite, "ForkGeraete"));
    }

    [Fact]
    public void NachDemRestoreSindDieTabellenWiederDa()
    {
        var paths = NeueDatenbank("restore");
        var system = new SystemApiController(paths, new GrowRepository(paths), new SystemAuditRepository(paths));

        // Das Backup entsteht, BEVOR jemand Kosten oder Geräte angefasst hat —
        // so wie eines aus einer Fassung, die diese Tabellen noch nicht kannte.
        var backup = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(system.CreateBackup().Result).Value);

        // Im laufenden Betrieb: Tabellen angelegt, Merker gesetzt.
        new KostenRepository(paths).CreateArtikel(new Verbrauchsartikel { Name = "vor dem Restore", Einheit = "kg" });
        new GeraeteRepository(paths).Geraete();

        Assert.IsType<OkObjectResult>(system.RestoreBackup(backup.FileName).Result);
        Assert.False(TabelleDa(paths, "ForkVerbrauchsartikel"), "Die zurueckgespielte Datei muss ohne Kosten-Tabellen sein, sonst prueft der Fall nichts.");

        // Vorher: „no such table: ForkVerbrauchsartikel" bis zum Neustart.
        Assert.Empty(new KostenRepository(paths).GetArtikel());
        Assert.Empty(new GeraeteRepository(paths).Geraete());
        Assert.True(TabelleDa(paths, "ForkVerbrauchsartikel"));
        Assert.True(TabelleDa(paths, "ForkGeraete"));
    }

    private static bool SpalteDa(AppPaths paths, string tabelle, string spalte)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tabelle}') WHERE name = $spalte;";
        command.Parameters.AddWithValue("$spalte", spalte);
        return (long)command.ExecuteScalar()! > 0;
    }

    private static void Ausfuehren(AppPaths paths, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Die Schema-Ergänzungen des <see cref="DatabaseInitializer"/> laufen nach dem
    /// Restore sofort — nicht erst beim Neustart (docs/pruefung-2026-10-01.md,
    /// „Noch offen, klein").
    /// </summary>
    /// <remarks>
    /// Die „alte Fassung" entsteht, indem eine Spalte entfernt wird, die nur
    /// <c>EnsureColumn</c> nachrüstet (nicht das CREATE TABLE einer Altdatenbank):
    /// <c>CalibrationEvents.PointsJson</c>, seit 01.09.2026. Gesichert wird DIESE
    /// Datei; danach läuft der Prozess weiter mit der vollständigen.
    /// </remarks>
    [Fact]
    public void NachDemRestoreLaufenDieSchemaErgaenzungenSofort()
    {
        var paths = NeueDatenbank("restore-schema");
        var system = new SystemApiController(paths, new GrowRepository(paths), new SystemAuditRepository(paths));

        SqliteConnection.ClearAllPools();
        Ausfuehren(paths, "ALTER TABLE CalibrationEvents DROP COLUMN PointsJson;");
        Assert.False(SpalteDa(paths, "CalibrationEvents", "PointsJson"), "Die alte Fassung hat die Spalte noch — dann prüft der Fall nichts.");
        var backup = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(system.CreateBackup().Result).Value);

        // Der laufende Prozess hat die Spalte (Start).
        TestDatabase.Initialize(paths);
        Assert.True(SpalteDa(paths, "CalibrationEvents", "PointsJson"));

        var ergebnis = system.RestoreBackup(backup.FileName).Result;
        Assert.True(ergebnis is OkObjectResult, $"Restore schlug fehl: {(ergebnis as ObjectResult)?.Value}");

        // Vorher: die Spalte fehlte bis zum Neustart.
        Assert.True(SpalteDa(paths, "CalibrationEvents", "PointsJson"),
            "Nach dem Restore fehlt CalibrationEvents.PointsJson — die Schema-Ergänzungen laufen erst beim Neustart.");

        // Die Reparatur einmal wiederholen: ein zweiter Restore derselben alten Datei.
        ergebnis = system.RestoreBackup(backup.FileName).Result;
        Assert.True(ergebnis is OkObjectResult, $"Zweiter Restore schlug fehl: {(ergebnis as ObjectResult)?.Value}");
        Assert.True(SpalteDa(paths, "CalibrationEvents", "PointsJson"), "Beim zweiten Restore fehlt die Spalte.");
    }
}
