using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Fork AI (F-004, forkai.112): Abweichungen vom Wochenplan eines Düngeprogramms.
/// </summary>
/// <remarks>
/// Eine Zeile je (Programm, Spalte, Feld). Eigene Tabelle statt eines
/// JSON-Dokuments in <c>ForkSteuerungEinstellungen</c>: gespeichert wird nur,
/// was abweicht, und jede Zeile trägt ihren eigenen Änderungszeitpunkt.
/// Das Schema legt das Repository selbst an — wie <see cref="SteuerungRepository"/>,
/// damit die Original-Wanderungen unberührt bleiben.
/// </remarks>
public sealed class WochenwertRepository : RepositoryBase
{
    private static readonly object SchemaLock = new();
    private static readonly HashSet<string> SchemaSteht = new(StringComparer.OrdinalIgnoreCase);

    public WochenwertRepository(AppPaths paths) : base(paths)
    {
    }

    private SqliteConnection Open()
    {
        var connection = OpenConnection();
        var datei = connection.DataSource ?? string.Empty;
        lock (SchemaLock)
        {
            if (!SchemaSteht.Contains(datei))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS ForkWochenwerte (
                        ProgrammId TEXT NOT NULL,
                        SpalteId TEXT NOT NULL,
                        Feld TEXT NOT NULL,
                        Wert REAL NOT NULL,
                        UpdatedAtUtc TEXT NOT NULL,
                        PRIMARY KEY (ProgrammId, SpalteId, Feld)
                    );
                    """;
                command.ExecuteNonQuery();
                SchemaSteht.Add(datei);
            }
        }
        return connection;
    }

    public IReadOnlyList<Wochenwert> Alle()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProgrammId, SpalteId, Feld, Wert FROM ForkWochenwerte ORDER BY ProgrammId, SpalteId, Feld;";
        using var reader = command.ExecuteReader();
        var liste = new List<Wochenwert>();
        while (reader.Read())
        {
            liste.Add(new Wochenwert(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3)));
        }
        return liste;
    }

    /// <summary>Schreibt alle Änderungen in einem Zug; <c>null</c> löscht die Abweichung.</summary>
    public void Speichern(string programmId, IEnumerable<(string SpalteId, string Feld, double? Wert)> aenderungen)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        var jetzt = ToStorageUtc(DateTime.UtcNow);

        foreach (var (spalteId, feld, wert) in aenderungen)
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            if (wert is { } zahl)
            {
                command.CommandText = """
                    INSERT INTO ForkWochenwerte (ProgrammId, SpalteId, Feld, Wert, UpdatedAtUtc)
                    VALUES ($p, $s, $f, $w, $u)
                    ON CONFLICT(ProgrammId, SpalteId, Feld) DO UPDATE SET Wert = excluded.Wert, UpdatedAtUtc = excluded.UpdatedAtUtc;
                    """;
                command.Parameters.AddWithValue("$w", zahl);
                command.Parameters.AddWithValue("$u", jetzt);
            }
            else
            {
                command.CommandText = "DELETE FROM ForkWochenwerte WHERE ProgrammId = $p AND SpalteId = $s AND Feld = $f;";
            }
            command.Parameters.AddWithValue("$p", programmId);
            command.Parameters.AddWithValue("$s", spalteId);
            command.Parameters.AddWithValue("$f", feld);
            command.ExecuteNonQuery();
        }

        tx.Commit();
    }
}
