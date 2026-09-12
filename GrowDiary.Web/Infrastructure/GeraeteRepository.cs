using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Fork AI (forkai.22): Die Korrekturen des Nutzers am geratenen Gerätebild.
/// </summary>
/// <remarks>
/// <para><b>Zwei Tabellen, ein Zweck.</b> <c>ForkGeraete</c> hält die Gerätezeile
/// selbst (Name, Zelt, Verbindung ins Inventar), <c>ForkGeraetEntitaeten</c> sagt,
/// welche Entität zu welchem Gerät gehört. Ohne Eintrag greift die Vermutung aus
/// <see cref="GeraeteSchluessel"/> — die Tabellen sind also leer, solange der
/// Nutzer nichts korrigiert hat, und genau das ist der Normalfall nach dem Update.</para>
///
/// <para><b>Warum die Entität der Schlüssel ist.</b> Eine Entität gehört zu genau
/// einem Gerät. Andersherum wäre eine Liste je Gerät nötig, und jede Umhängung
/// müsste zwei Zeilen anfassen — dabei entstehen verwaiste Doppelzuordnungen.</para>
/// </remarks>
public sealed class GeraeteRepository : RepositoryBase
{
    private static readonly object SchemaLock = new();
    private static bool _schemaEnsured;

    public GeraeteRepository(AppPaths paths) : base(paths)
    {
    }

    private SqliteConnection Open()
    {
        var connection = OpenConnection();
        EnsureSchema(connection);
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        if (_schemaEnsured) return;
        lock (SchemaLock)
        {
            if (_schemaEnsured) return;
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ForkGeraete (
                    Schluessel TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    TentId INTEGER NULL,
                    HardwareItemId INTEGER NULL,
                    ElternSchluessel TEXT NULL,
                    Anschluss TEXT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS ForkGeraetEntitaeten (
                    EntityId TEXT PRIMARY KEY,
                    Schluessel TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ForkGeraetEntitaeten_Schluessel
                    ON ForkGeraetEntitaeten(Schluessel);
                """;
            command.ExecuteNonQuery();
            _schemaEnsured = true;
        }
    }

    /// <summary>Alle vom Nutzer gesetzten Gerätezeilen, nach Schlüssel.</summary>
    public IReadOnlyDictionary<string, GespeichertesGeraet> Geraete()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Schluessel, Name, TentId, HardwareItemId, ElternSchluessel, Anschluss, UpdatedAtUtc FROM ForkGeraete;";

        var liste = new Dictionary<string, GespeichertesGeraet>(StringComparer.OrdinalIgnoreCase);
        using var leser = command.ExecuteReader();
        while (leser.Read())
        {
            var eintrag = new GespeichertesGeraet
            {
                Schluessel = leser.GetString(0),
                Name = leser.GetString(1),
                TentId = leser.IsDBNull(2) ? null : leser.GetInt32(2),
                HardwareItemId = leser.IsDBNull(3) ? null : leser.GetInt32(3),
                ElternSchluessel = leser.IsDBNull(4) ? null : leser.GetString(4),
                Anschluss = leser.IsDBNull(5) ? null : leser.GetString(5),
                UpdatedAtUtc = ParseStoredUtcDateTime(leser.GetString(6)) ?? DateTime.UtcNow,
            };
            liste[eintrag.Schluessel] = eintrag;
        }

        return liste;
    }

    /// <summary>Welche Entität zu welchem Gerät gehört — Entity-ID auf Schlüssel.</summary>
    public IReadOnlyDictionary<string, string> Zuordnungen()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EntityId, Schluessel FROM ForkGeraetEntitaeten;";

        var liste = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var leser = command.ExecuteReader();
        while (leser.Read()) liste[leser.GetString(0)] = leser.GetString(1);
        return liste;
    }

    public void GeraetSpeichern(GespeichertesGeraet geraet)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ForkGeraete (Schluessel, Name, TentId, HardwareItemId, ElternSchluessel, Anschluss, UpdatedAtUtc)
            VALUES ($schluessel, $name, $tent, $hardware, $eltern, $anschluss, $updated)
            ON CONFLICT(Schluessel) DO UPDATE SET
                Name = excluded.Name, TentId = excluded.TentId,
                HardwareItemId = excluded.HardwareItemId,
                ElternSchluessel = excluded.ElternSchluessel, Anschluss = excluded.Anschluss,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$schluessel", geraet.Schluessel);
        command.Parameters.AddWithValue("$name", geraet.Name);
        command.Parameters.AddWithValue("$tent", (object?)geraet.TentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$hardware", (object?)geraet.HardwareItemId ?? DBNull.Value);
        command.Parameters.AddWithValue("$eltern", (object?)geraet.ElternSchluessel ?? DBNull.Value);
        command.Parameters.AddWithValue("$anschluss", (object?)geraet.Anschluss ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", ToStorageUtc(DateTime.UtcNow));
        command.ExecuteNonQuery();
    }

    /// <summary>Eine Entität einem Gerät zuschlagen; leerer Schlüssel löst die Zuordnung.</summary>
    public void EntitaetZuordnen(string entityId, string? schluessel)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(schluessel))
        {
            command.CommandText = "DELETE FROM ForkGeraetEntitaeten WHERE EntityId = $entity;";
            command.Parameters.AddWithValue("$entity", entityId);
            command.ExecuteNonQuery();
            return;
        }

        command.CommandText = """
            INSERT INTO ForkGeraetEntitaeten (EntityId, Schluessel, UpdatedAtUtc)
            VALUES ($entity, $schluessel, $updated)
            ON CONFLICT(EntityId) DO UPDATE SET
                Schluessel = excluded.Schluessel, UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$entity", entityId);
        command.Parameters.AddWithValue("$schluessel", schluessel);
        command.Parameters.AddWithValue("$updated", ToStorageUtc(DateTime.UtcNow));
        command.ExecuteNonQuery();
    }
}
