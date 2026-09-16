using System.Text.Json;
using System.Text.Json.Serialization;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Fork AI (Grow-Plan, 16.09.2026): Planstände und Änderungsbuch je Grow.
/// </summary>
/// <remarks>
/// Eigene Tabellen, Schema legt das Repository selbst an — wie
/// <see cref="WochenwertRepository"/>, damit die Original-Wanderungen
/// unberührt bleiben. Das Änderungsbuch kennt nur Einfügen: ein Eintrag, der
/// sich nachträglich ändern ließe, taugt nicht zur Auswertung.
/// </remarks>
public sealed class GrowPlanRepository : RepositoryBase
{
    private static readonly object SchemaLock = new();
    private static readonly HashSet<string> SchemaSteht = new(StringComparer.OrdinalIgnoreCase);

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GrowPlanRepository(AppPaths paths) : base(paths)
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
                    CREATE TABLE IF NOT EXISTS ForkGrowPlan (
                        GrowId INTEGER NOT NULL,
                        Stand TEXT NOT NULL,
                        InhaltJson TEXT NOT NULL,
                        Vermerk TEXT NULL,
                        AngelegtUtc TEXT NOT NULL,
                        GeaendertUtc TEXT NOT NULL,
                        PRIMARY KEY (GrowId, Stand)
                    );
                    CREATE TABLE IF NOT EXISTS ForkGrowPlanBuch (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        GrowId INTEGER NOT NULL,
                        ZeitUtc TEXT NOT NULL,
                        Art TEXT NOT NULL,
                        SpalteId TEXT NULL,
                        Feld TEXT NULL,
                        Alt TEXT NULL,
                        Neu TEXT NULL,
                        Ziel TEXT NULL,
                        Grund TEXT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_ForkGrowPlanBuch_Grow ON ForkGrowPlanBuch (GrowId, Id);
                    """;
                command.ExecuteNonQuery();
                SchemaSteht.Add(datei);
            }
        }
        return connection;
    }

    public GrowPlanStand? Laden(int growId, string stand)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT GrowId, Stand, InhaltJson, Vermerk, AngelegtUtc, GeaendertUtc FROM ForkGrowPlan WHERE GrowId = $g AND Stand = $s;";
        command.Parameters.AddWithValue("$g", growId);
        command.Parameters.AddWithValue("$s", stand);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Lesen(reader) : null;
    }

    public IReadOnlyList<GrowPlanStand> AlleStaende(string stand)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT GrowId, Stand, InhaltJson, Vermerk, AngelegtUtc, GeaendertUtc FROM ForkGrowPlan WHERE Stand = $s ORDER BY GrowId;";
        command.Parameters.AddWithValue("$s", stand);
        using var reader = command.ExecuteReader();
        var liste = new List<GrowPlanStand>();
        while (reader.Read()) liste.Add(Lesen(reader));
        return liste;
    }

    /// <summary>
    /// Schreibt Stände und Buch-Einträge in einem Zug — entweder alles oder nichts.
    /// </summary>
    /// <remarks>
    /// Der Startstand wird nie überschrieben: existiert er schon, bleibt er.
    /// </remarks>
    public void Speichern(IEnumerable<GrowPlanStand> staende, IEnumerable<GrowPlanEintrag> eintraege)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        foreach (var stand in staende)
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            var konflikt = stand.Stand == GrowPlanStaende.Start
                ? "DO NOTHING"
                : "DO UPDATE SET InhaltJson = excluded.InhaltJson, Vermerk = excluded.Vermerk, GeaendertUtc = excluded.GeaendertUtc";
            command.CommandText = $"""
                INSERT INTO ForkGrowPlan (GrowId, Stand, InhaltJson, Vermerk, AngelegtUtc, GeaendertUtc)
                VALUES ($g, $s, $i, $v, $a, $u)
                ON CONFLICT(GrowId, Stand) {konflikt};
                """;
            command.Parameters.AddWithValue("$g", stand.GrowId);
            command.Parameters.AddWithValue("$s", stand.Stand);
            command.Parameters.AddWithValue("$i", JsonSerializer.Serialize(stand.Inhalt, Json));
            command.Parameters.AddWithValue("$v", (object?)stand.Vermerk ?? DBNull.Value);
            command.Parameters.AddWithValue("$a", ToStorageUtc(stand.AngelegtUtc));
            command.Parameters.AddWithValue("$u", ToStorageUtc(stand.GeaendertUtc));
            command.ExecuteNonQuery();
        }

        foreach (var eintrag in eintraege)
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO ForkGrowPlanBuch (GrowId, ZeitUtc, Art, SpalteId, Feld, Alt, Neu, Ziel, Grund)
                VALUES ($g, $z, $art, $sp, $f, $alt, $neu, $ziel, $grund);
                """;
            command.Parameters.AddWithValue("$g", eintrag.GrowId);
            command.Parameters.AddWithValue("$z", ToStorageUtc(eintrag.ZeitUtc));
            command.Parameters.AddWithValue("$art", eintrag.Art);
            command.Parameters.AddWithValue("$sp", (object?)eintrag.SpalteId ?? DBNull.Value);
            command.Parameters.AddWithValue("$f", (object?)eintrag.Feld ?? DBNull.Value);
            command.Parameters.AddWithValue("$alt", (object?)eintrag.Alt ?? DBNull.Value);
            command.Parameters.AddWithValue("$neu", (object?)eintrag.Neu ?? DBNull.Value);
            command.Parameters.AddWithValue("$ziel", (object?)eintrag.Ziel ?? DBNull.Value);
            command.Parameters.AddWithValue("$grund", (object?)eintrag.Grund ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>
    /// Schreibt einen Stand ohne die Start-Sperre — nur für technische Nachträge
    /// (neue Felder, die frühere Versionen noch nicht führten).
    /// </summary>
    public void Nachtragen(GrowPlanStand stand)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ForkGrowPlan SET InhaltJson = $i WHERE GrowId = $g AND Stand = $s;";
        command.Parameters.AddWithValue("$i", JsonSerializer.Serialize(stand.Inhalt, Json));
        command.Parameters.AddWithValue("$g", stand.GrowId);
        command.Parameters.AddWithValue("$s", stand.Stand);
        command.ExecuteNonQuery();
    }

    /// <summary>Entfernt einen Stand — nur für den Endstand beim Wiederöffnen gedacht.</summary>
    public void StandEntfernen(int growId, string stand, GrowPlanEintrag eintrag)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = "DELETE FROM ForkGrowPlan WHERE GrowId = $g AND Stand = $s;";
            command.Parameters.AddWithValue("$g", growId);
            command.Parameters.AddWithValue("$s", stand);
            command.ExecuteNonQuery();
        }
        tx.Commit();
        Speichern([], [eintrag]);
    }

    public IReadOnlyList<GrowPlanEintrag> Buch(int growId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, GrowId, ZeitUtc, Art, SpalteId, Feld, Alt, Neu, Ziel, Grund FROM ForkGrowPlanBuch WHERE GrowId = $g ORDER BY Id DESC;";
        command.Parameters.AddWithValue("$g", growId);
        using var reader = command.ExecuteReader();
        var liste = new List<GrowPlanEintrag>();
        while (reader.Read())
        {
            liste.Add(new GrowPlanEintrag(
                reader.GetInt64(0),
                reader.GetInt32(1),
                ParseStoredUtcDateTime(reader.GetString(2)) ?? DateTime.UtcNow,
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }
        return liste;
    }

    private static GrowPlanStand Lesen(SqliteDataReader reader)
    {
        var inhalt = JsonSerializer.Deserialize<GrowPlanInhalt>(reader.GetString(2), Json) ?? new GrowPlanInhalt();
        return new GrowPlanStand(
            reader.GetInt32(0),
            reader.GetString(1),
            inhalt,
            reader.IsDBNull(3) ? null : reader.GetString(3),
            ParseStoredUtcDateTime(reader.GetString(4)) ?? DateTime.UtcNow,
            ParseStoredUtcDateTime(reader.GetString(5)) ?? DateTime.UtcNow);
    }
}
