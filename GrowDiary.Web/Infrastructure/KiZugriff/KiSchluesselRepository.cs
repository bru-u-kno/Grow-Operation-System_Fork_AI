using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Ein gespeicherter Schlüssel — ohne Klartext.
/// </summary>
/// <remarks>
/// Der Klartext existiert genau einmal: in der Antwort auf das Anlegen. Hier
/// steht nur sein SHA-256 (<see cref="Hash"/>) und zum Wiedererkennen die
/// ersten acht Zeichen nach <c>gok_</c> (<see cref="Praefix"/>).
/// </remarks>
public sealed record KiSchluessel(
    int Id,
    string Name,
    string Praefix,
    string Hash,
    KiStufe Stufen,
    DateTime ErstelltAmUtc,
    DateTime? ZuletztGenutztAmUtc,
    DateTime? GesperrtAmUtc)
{
    public bool Gesperrt => GesperrtAmUtc is not null;
}

/// <summary>
/// Fork AI (A-003, 03.10.2026): Die Tabelle <c>ForkKiSchluessel</c>.
/// </summary>
/// <remarks>
/// Eigenes Schema je Datenbankdatei wie <see cref="SteuerungRepository"/> —
/// dort steht, warum ein einzelnes <c>bool</c> als Merker in den Tests eine
/// leere Datei hinterlässt. Gelöschte Schlüssel verschwinden ganz; gesperrte
/// bleiben sichtbar, damit der Betreiber sieht, was er abgeschaltet hat.
/// </remarks>
public sealed class KiSchluesselRepository : RepositoryBase
{
    private static readonly object SchemaLock = new();
    private static readonly HashSet<string> SchemaSteht = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Das Schema der Tabelle — auch für <see cref="KiZustandBeimZurueckspielen"/>, damit es nur einmal dasteht.</summary>
    internal const string SchemaSql = """
                CREATE TABLE IF NOT EXISTS ForkKiSchluessel (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Praefix TEXT NOT NULL,
                    Hash TEXT NOT NULL,
                    Stufen INTEGER NOT NULL DEFAULT 0,
                    ErstelltAmUtc TEXT NOT NULL,
                    ZuletztGenutztAmUtc TEXT NULL,
                    GesperrtAmUtc TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ForkKiSchluessel_Praefix ON ForkKiSchluessel (Praefix);
                """;

    public KiSchluesselRepository(AppPaths paths) : base(paths)
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
        var datei = connection.DataSource ?? string.Empty;
        lock (SchemaLock)
        {
            if (SchemaSteht.Contains(datei)) return;
            using var command = connection.CreateCommand();
            command.CommandText = SchemaSql;
            command.ExecuteNonQuery();
            SchemaSteht.Add(datei);
        }
    }

    public IReadOnlyList<KiSchluessel> Alle()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkKiSchluessel ORDER BY Id;";
        return Lesen(command);
    }

    public KiSchluessel? Hole(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkKiSchluessel WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return Lesen(command).FirstOrDefault();
    }

    /// <summary>Alle Schlüssel mit diesem Präfix — der Hash entscheidet danach (zeitkonstant, im Dienst).</summary>
    public IReadOnlyList<KiSchluessel> MitPraefix(string praefix)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkKiSchluessel WHERE Praefix = $praefix;";
        command.Parameters.AddWithValue("$praefix", praefix);
        return Lesen(command);
    }

    public int Anlegen(string name, string praefix, string hash, KiStufe stufen, DateTime jetztUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ForkKiSchluessel (Name, Praefix, Hash, Stufen, ErstelltAmUtc)
            VALUES ($name, $praefix, $hash, $stufen, $erstellt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$praefix", praefix);
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$stufen", (int)stufen);
        command.Parameters.AddWithValue("$erstellt", ToStorageUtc(jetztUtc));
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    /// <summary>Name und Stufen ändern. False, wenn es den Schlüssel nicht gibt.</summary>
    public bool Aendern(int id, string name, KiStufe stufen)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ForkKiSchluessel SET Name = $name, Stufen = $stufen WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$stufen", (int)stufen);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>Sperren. Ein schon gesperrter behält seinen ersten Zeitpunkt.</summary>
    public bool Sperren(int id, DateTime jetztUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ForkKiSchluessel SET GesperrtAmUtc = COALESCE(GesperrtAmUtc, $jetzt) WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$jetzt", ToStorageUtc(jetztUtc));
        return command.ExecuteNonQuery() > 0;
    }

    public bool Loeschen(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ForkKiSchluessel WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public void ZuletztGenutzt(int id, DateTime jetztUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ForkKiSchluessel SET ZuletztGenutztAmUtc = $jetzt WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$jetzt", ToStorageUtc(jetztUtc));
        command.ExecuteNonQuery();
    }

    private static List<KiSchluessel> Lesen(SqliteCommand command)
    {
        var liste = new List<KiSchluessel>();
        using var leser = command.ExecuteReader();
        while (leser.Read())
        {
            liste.Add(new KiSchluessel(
                Id: Convert.ToInt32((long)leser["Id"]),
                Name: leser["Name"]?.ToString() ?? string.Empty,
                Praefix: leser["Praefix"]?.ToString() ?? string.Empty,
                Hash: leser["Hash"]?.ToString() ?? string.Empty,
                Stufen: (KiStufe)Convert.ToInt32((long)leser["Stufen"]),
                ErstelltAmUtc: ParseStoredUtcDateTime(NullString(leser["ErstelltAmUtc"])) ?? DateTime.UnixEpoch,
                ZuletztGenutztAmUtc: ParseStoredUtcDateTime(NullString(leser["ZuletztGenutztAmUtc"])),
                GesperrtAmUtc: ParseStoredUtcDateTime(NullString(leser["GesperrtAmUtc"]))));
        }
        return liste;
    }
}
