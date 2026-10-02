using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GrowDiary.Web.Infrastructure;

public sealed partial class DatabaseInitializer
{
    /// <summary>
    /// Stammt die Datenbank aus der Zeit vor <c>Grows.TentId</c>?
    /// </summary>
    /// <remarks>
    /// <para><b>Das Merkmal für die Altlast-Zuordnung.</b> Sie lief bis zum
    /// 02.10.2026 bei JEDEM Start und schob jeden Grow ohne Zelt ins
    /// „Hauptzelt". Ohne Zelt ist ein Grow aber nicht nur aus alter Zeit,
    /// sondern auch, wenn sein Zelt gelöscht wurde
    /// (<c>TentRepository.DeleteTentWithCleanup</c> setzt
    /// <c>Grows.TentId = NULL</c>) — die archivierten Läufe eines gelöschten
    /// Zelts erschienen nach dem nächsten Neustart im Hauptzelt.</para>
    ///
    /// <para><b>Warum die fehlende Spalte und kein Eintrag in den
    /// AppSettings.</b> So erkennt auch <c>DropLegacyTentSchemaIfNeeded</c>
    /// seinen Einmal-Schritt: am Schema selbst. Ein Merker in den AppSettings
    /// würde beim ersten Start nach dem Update noch einmal zuordnen — und dabei
    /// genau die Grows aus gelöschten Zelten erwischen, um die es geht. Die
    /// fehlende Spalte dagegen gibt es nur in einer Datenbank, in der noch
    /// kein Grow je ein Zelt hatte; nach diesem Start nie wieder.</para>
    ///
    /// <para><b>Bestehende Installationen verlieren nichts:</b> ihre alten
    /// Grows hat die frühere Fassung bei jedem Start längst zugeordnet, und
    /// diese Zuordnung bleibt stehen. Neu ist nur, dass ein Grow ohne Zelt
    /// ohne Zelt bleibt.</para>
    /// </remarks>
    private bool GrowsHabenNochKeineZeltSpalte()
    {
        using var connection = OpenConnection();
        if (!TableExists(connection, "Grows"))
        {
            return false; // frische Datenbank: es gibt nichts zuzuordnen
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Grows') WHERE name = 'TentId';";
        return Convert.ToInt32(command.ExecuteScalar()) == 0;
    }

    /// <summary>
    /// Grows aus der Zeit vor den Zelten dem „Hauptzelt" zuordnen — nur
    /// einmal, siehe <see cref="GrowsHabenNochKeineZeltSpalte"/>.
    /// </summary>
    private void AutoAssignExistingGrowsToTents()
    {
        using var connection = OpenConnection();
        var mainTentId = GetTentId(connection, "Hauptzelt");
        if (mainTentId == 0) return;

        using var select = connection.CreateCommand();
        select.CommandText = "SELECT Id FROM Grows WHERE TentId IS NULL;";
        using var reader = select.ExecuteReader();
        var ids = new List<int>();
        while (reader.Read())
            ids.Add(Convert.ToInt32((long)reader["Id"]));
        reader.Close();

        foreach (var id in ids)
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE Grows SET TentId = $tentId WHERE Id = $id;";
            update.Parameters.AddWithValue("$tentId", mainTentId);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }
    }


    private static int GetTentId(SqliteConnection connection, string tentName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Tents WHERE Name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tentName);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

}
