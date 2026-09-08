using System.Globalization;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Fork AI (forkai.6): Verbrauchsartikel, Nachfüllungen und Zählerstände.
/// </summary>
/// <remarks>
/// Die drei Tabellen legt das Repository selbst an (<c>CREATE TABLE IF NOT
/// EXISTS</c>), nicht das Kern-Schema: so bleibt der Abgleich mit dem Original
/// frei von Konflikten in <c>DatabaseInitializer.CoreSchemaSql</c>. Ein Export
/// des Originals kennt diese Tabellen nicht — sie hängen an der Datenbankdatei,
/// die das Add-on-Backup ohnehin mitnimmt.
/// </remarks>
public sealed class KostenRepository : RepositoryBase
{
    private static readonly object SchemaLock = new();
    private static bool _schemaEnsured;

    public KostenRepository(AppPaths paths) : base(paths)
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
                CREATE TABLE IF NOT EXISTS ForkVerbrauchsartikel (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Einheit TEXT NOT NULL DEFAULT 'kg',
                    Gebinde REAL NULL,
                    TentId INTEGER NULL,
                    Notiz TEXT NULL,
                    Aktiv INTEGER NOT NULL DEFAULT 1,
                    CreatedAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS ForkNachfuellungen (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ArtikelId INTEGER NOT NULL REFERENCES ForkVerbrauchsartikel(Id) ON DELETE CASCADE,
                    ZeitpunktUtc TEXT NOT NULL,
                    Menge REAL NOT NULL,
                    KostenEur REAL NULL,
                    GrowId INTEGER NULL,
                    Notiz TEXT NULL,
                    LeerAmUtc TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ForkNachfuellungen_Artikel ON ForkNachfuellungen(ArtikelId, ZeitpunktUtc);
                CREATE TABLE IF NOT EXISTS ForkZaehlerstaende (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ZeitpunktUtc TEXT NOT NULL,
                    Kwh REAL NOT NULL,
                    Anlass TEXT NOT NULL,
                    GrowId INTEGER NULL,
                    Phase TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ForkZaehlerstaende_Zeit ON ForkZaehlerstaende(ZeitpunktUtc);
                CREATE TABLE IF NOT EXISTS ForkAnschaffungen (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Hersteller TEXT NULL,
                    Produkt TEXT NULL,
                    DatumUtc TEXT NOT NULL,
                    Stueck INTEGER NOT NULL DEFAULT 1,
                    EinzelpreisEur REAL NOT NULL DEFAULT 0,
                    GrowId INTEGER NULL,
                    Notiz TEXT NULL,
                    HardwareItemId INTEGER NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ForkAnschaffungen_Datum ON ForkAnschaffungen(DatumUtc);
                """;
            command.ExecuteNonQuery();

            // forkai.8: drei Spalten nachgezogen. SQLite kennt kein
            // „ADD COLUMN IF NOT EXISTS", also erst nachsehen — das läuft auf
            // jeder Installation genau einmal.
            foreach (var (spalte, typ) in new[] { ("Hersteller", "TEXT NULL"), ("Produkt", "TEXT NULL"), ("PreisEur", "REAL NULL") })
            {
                if (!SpalteVorhanden(connection, "ForkVerbrauchsartikel", spalte))
                {
                    using var alter = connection.CreateCommand();
                    alter.CommandText = $"ALTER TABLE ForkVerbrauchsartikel ADD COLUMN {spalte} {typ};";
                    alter.ExecuteNonQuery();
                }
            }
            _schemaEnsured = true;
        }
    }

    private static bool SpalteVorhanden(SqliteConnection connection, string tabelle, string spalte)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tabelle});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["name"].ToString(), spalte, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- Artikel

    public List<Verbrauchsartikel> GetArtikel(bool nurAktive = false)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkVerbrauchsartikel"
            + (nurAktive ? " WHERE Aktiv = 1" : string.Empty)
            + " ORDER BY Aktiv DESC, Name COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        var list = new List<Verbrauchsartikel>();
        while (reader.Read()) list.Add(MapArtikel(reader));
        return list;
    }

    public Verbrauchsartikel? GetArtikel(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkVerbrauchsartikel WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapArtikel(reader) : null;
    }

    public int CreateArtikel(Verbrauchsartikel artikel)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ForkVerbrauchsartikel (Name, Hersteller, Produkt, PreisEur, Einheit, Gebinde, TentId, Notiz, Aktiv, CreatedAtUtc)
            VALUES ($name, $hersteller, $produkt, $preisEur, $einheit, $gebinde, $tentId, $notiz, $aktiv, $createdAtUtc);
            SELECT last_insert_rowid();
            """;
        BindArtikel(command, artikel);
        command.Parameters.AddWithValue("$createdAtUtc", ToStorageUtc(DateTime.UtcNow));
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    public void UpdateArtikel(Verbrauchsartikel artikel)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ForkVerbrauchsartikel
            SET Name = $name, Hersteller = $hersteller, Produkt = $produkt, PreisEur = $preisEur, Einheit = $einheit, Gebinde = $gebinde, TentId = $tentId, Notiz = $notiz, Aktiv = $aktiv
            WHERE Id = $id;
            """;
        BindArtikel(command, artikel);
        command.Parameters.AddWithValue("$id", artikel.Id);
        command.ExecuteNonQuery();
    }

    public void DeleteArtikel(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ForkVerbrauchsartikel WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void BindArtikel(SqliteCommand command, Verbrauchsartikel artikel)
    {
        command.Parameters.AddWithValue("$name", artikel.Name.Trim());
        command.Parameters.AddWithValue("$hersteller", (object?)NormalizeOptional(artikel.Hersteller) ?? DBNull.Value);
        command.Parameters.AddWithValue("$produkt", (object?)NormalizeOptional(artikel.Produkt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$preisEur", (object?)artikel.PreisEur ?? DBNull.Value);
        command.Parameters.AddWithValue("$einheit", string.IsNullOrWhiteSpace(artikel.Einheit) ? "kg" : artikel.Einheit.Trim());
        command.Parameters.AddWithValue("$gebinde", (object?)artikel.Gebinde ?? DBNull.Value);
        command.Parameters.AddWithValue("$tentId", (object?)artikel.TentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$notiz", (object?)NormalizeOptional(artikel.Notiz) ?? DBNull.Value);
        command.Parameters.AddWithValue("$aktiv", artikel.Aktiv ? 1 : 0);
    }

    private static Verbrauchsartikel MapArtikel(SqliteDataReader reader) => new()
    {
        Id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture),
        Name = reader["Name"].ToString() ?? string.Empty,
        Hersteller = NullString(reader["Hersteller"]),
        Produkt = NullString(reader["Produkt"]),
        PreisEur = NullableDouble(reader["PreisEur"]),
        Einheit = reader["Einheit"].ToString() ?? "kg",
        Gebinde = NullableDouble(reader["Gebinde"]),
        TentId = reader["TentId"] is DBNull ? null : Convert.ToInt32(reader["TentId"], CultureInfo.InvariantCulture),
        Notiz = NullString(reader["Notiz"]),
        Aktiv = Convert.ToInt32(reader["Aktiv"], CultureInfo.InvariantCulture) == 1,
        CreatedAtUtc = ParseStoredUtcDateTime(reader["CreatedAtUtc"].ToString()) ?? DateTime.UtcNow,
    };

    // ---------------------------------------------------------- Anschaffungen (forkai.9)

    public List<Anschaffung> GetAnschaffungen()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkAnschaffungen ORDER BY DatumUtc DESC, Id DESC;";
        using var reader = command.ExecuteReader();
        var list = new List<Anschaffung>();
        while (reader.Read()) list.Add(MapAnschaffung(reader));
        return list;
    }

    public Anschaffung? GetAnschaffung(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkAnschaffungen WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapAnschaffung(reader) : null;
    }

    public int CreateAnschaffung(Anschaffung a)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ForkAnschaffungen (Name, Hersteller, Produkt, DatumUtc, Stueck, EinzelpreisEur, GrowId, Notiz, HardwareItemId, CreatedAtUtc)
            VALUES ($name, $hersteller, $produkt, $datumUtc, $stueck, $einzelpreis, $growId, $notiz, $hardwareItemId, $createdAtUtc);
            SELECT last_insert_rowid();
            """;
        BindAnschaffung(command, a);
        command.Parameters.AddWithValue("$createdAtUtc", ToStorageUtc(DateTime.UtcNow));
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    public void UpdateAnschaffung(Anschaffung a)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ForkAnschaffungen
            SET Name = $name, Hersteller = $hersteller, Produkt = $produkt, DatumUtc = $datumUtc, Stueck = $stueck,
                EinzelpreisEur = $einzelpreis, GrowId = $growId, Notiz = $notiz, HardwareItemId = $hardwareItemId
            WHERE Id = $id;
            """;
        BindAnschaffung(command, a);
        command.Parameters.AddWithValue("$id", a.Id);
        command.ExecuteNonQuery();
    }

    public void DeleteAnschaffung(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ForkAnschaffungen WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void BindAnschaffung(SqliteCommand command, Anschaffung a)
    {
        command.Parameters.AddWithValue("$name", a.Name.Trim());
        command.Parameters.AddWithValue("$hersteller", (object?)NormalizeOptional(a.Hersteller) ?? DBNull.Value);
        command.Parameters.AddWithValue("$produkt", (object?)NormalizeOptional(a.Produkt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$datumUtc", ToStorageUtc(a.DatumUtc));
        command.Parameters.AddWithValue("$stueck", a.Stueck);
        command.Parameters.AddWithValue("$einzelpreis", a.EinzelpreisEur);
        command.Parameters.AddWithValue("$growId", (object?)a.GrowId ?? DBNull.Value);
        command.Parameters.AddWithValue("$notiz", (object?)NormalizeOptional(a.Notiz) ?? DBNull.Value);
        command.Parameters.AddWithValue("$hardwareItemId", (object?)a.HardwareItemId ?? DBNull.Value);
    }

    private static Anschaffung MapAnschaffung(SqliteDataReader reader) => new()
    {
        Id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture),
        Name = reader["Name"].ToString() ?? string.Empty,
        Hersteller = NullString(reader["Hersteller"]),
        Produkt = NullString(reader["Produkt"]),
        DatumUtc = ParseStoredUtcDateTime(reader["DatumUtc"].ToString()) ?? DateTime.UtcNow,
        Stueck = Convert.ToInt32(reader["Stueck"], CultureInfo.InvariantCulture),
        EinzelpreisEur = Convert.ToDouble(reader["EinzelpreisEur"], CultureInfo.InvariantCulture),
        GrowId = reader["GrowId"] is DBNull ? null : Convert.ToInt32(reader["GrowId"], CultureInfo.InvariantCulture),
        Notiz = NullString(reader["Notiz"]),
        HardwareItemId = reader["HardwareItemId"] is DBNull ? null : Convert.ToInt32(reader["HardwareItemId"], CultureInfo.InvariantCulture),
        CreatedAtUtc = ParseStoredUtcDateTime(reader["CreatedAtUtc"].ToString()) ?? DateTime.UtcNow,
    };

    // ---------------------------------------------------------- Nachfüllungen

    public List<Nachfuellung> GetNachfuellungen(int? artikelId = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkNachfuellungen"
            + (artikelId is null ? string.Empty : " WHERE ArtikelId = $artikelId")
            + " ORDER BY ZeitpunktUtc DESC, Id DESC;";
        if (artikelId is not null) command.Parameters.AddWithValue("$artikelId", artikelId.Value);
        using var reader = command.ExecuteReader();
        var list = new List<Nachfuellung>();
        while (reader.Read()) list.Add(MapNachfuellung(reader));
        return list;
    }

    public Nachfuellung? GetNachfuellung(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkNachfuellungen WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapNachfuellung(reader) : null;
    }

    public int CreateNachfuellung(Nachfuellung fuellung)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ForkNachfuellungen (ArtikelId, ZeitpunktUtc, Menge, KostenEur, GrowId, Notiz, LeerAmUtc, CreatedAtUtc)
            VALUES ($artikelId, $zeitpunktUtc, $menge, $kostenEur, $growId, $notiz, $leerAmUtc, $createdAtUtc);
            SELECT last_insert_rowid();
            """;
        BindNachfuellung(command, fuellung);
        command.Parameters.AddWithValue("$createdAtUtc", ToStorageUtc(DateTime.UtcNow));
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    public void UpdateNachfuellung(Nachfuellung fuellung)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ForkNachfuellungen
            SET ArtikelId = $artikelId, ZeitpunktUtc = $zeitpunktUtc, Menge = $menge, KostenEur = $kostenEur,
                GrowId = $growId, Notiz = $notiz, LeerAmUtc = $leerAmUtc
            WHERE Id = $id;
            """;
        BindNachfuellung(command, fuellung);
        command.Parameters.AddWithValue("$id", fuellung.Id);
        command.ExecuteNonQuery();
    }

    public void DeleteNachfuellung(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ForkNachfuellungen WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void BindNachfuellung(SqliteCommand command, Nachfuellung fuellung)
    {
        command.Parameters.AddWithValue("$artikelId", fuellung.ArtikelId);
        command.Parameters.AddWithValue("$zeitpunktUtc", ToStorageUtc(fuellung.ZeitpunktUtc));
        command.Parameters.AddWithValue("$menge", fuellung.Menge);
        command.Parameters.AddWithValue("$kostenEur", (object?)fuellung.KostenEur ?? DBNull.Value);
        command.Parameters.AddWithValue("$growId", (object?)fuellung.GrowId ?? DBNull.Value);
        command.Parameters.AddWithValue("$notiz", (object?)NormalizeOptional(fuellung.Notiz) ?? DBNull.Value);
        command.Parameters.AddWithValue("$leerAmUtc", fuellung.LeerAmUtc is { } leer ? ToStorageUtc(leer) : DBNull.Value);
    }

    private static Nachfuellung MapNachfuellung(SqliteDataReader reader) => new()
    {
        Id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture),
        ArtikelId = Convert.ToInt32(reader["ArtikelId"], CultureInfo.InvariantCulture),
        ZeitpunktUtc = ParseStoredUtcDateTime(reader["ZeitpunktUtc"].ToString()) ?? DateTime.UtcNow,
        Menge = Convert.ToDouble(reader["Menge"], CultureInfo.InvariantCulture),
        KostenEur = NullableDouble(reader["KostenEur"]),
        GrowId = reader["GrowId"] is DBNull ? null : Convert.ToInt32(reader["GrowId"], CultureInfo.InvariantCulture),
        Notiz = NullString(reader["Notiz"]),
        LeerAmUtc = ParseStoredUtcDateTime(NullString(reader["LeerAmUtc"])),
        CreatedAtUtc = ParseStoredUtcDateTime(reader["CreatedAtUtc"].ToString()) ?? DateTime.UtcNow,
    };

    // ----------------------------------------------------------- Zählerstände

    public List<Zaehlerstand> GetZaehlerstaende()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkZaehlerstaende ORDER BY ZeitpunktUtc ASC, Id ASC;";
        using var reader = command.ExecuteReader();
        var list = new List<Zaehlerstand>();
        while (reader.Read()) list.Add(MapZaehlerstand(reader));
        return list;
    }

    public Zaehlerstand? GetLetzterZaehlerstand()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkZaehlerstaende ORDER BY ZeitpunktUtc DESC, Id DESC LIMIT 1;";
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapZaehlerstand(reader) : null;
    }

    public int CreateZaehlerstand(Zaehlerstand stand)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ForkZaehlerstaende (ZeitpunktUtc, Kwh, Anlass, GrowId, Phase)
            VALUES ($zeitpunktUtc, $kwh, $anlass, $growId, $phase);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$zeitpunktUtc", ToStorageUtc(stand.ZeitpunktUtc));
        command.Parameters.AddWithValue("$kwh", stand.Kwh);
        command.Parameters.AddWithValue("$anlass", stand.Anlass.ToString());
        command.Parameters.AddWithValue("$growId", (object?)stand.GrowId ?? DBNull.Value);
        command.Parameters.AddWithValue("$phase", (object?)NormalizeOptional(stand.Phase) ?? DBNull.Value);
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    private static Zaehlerstand MapZaehlerstand(SqliteDataReader reader) => new()
    {
        Id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture),
        ZeitpunktUtc = ParseStoredUtcDateTime(reader["ZeitpunktUtc"].ToString()) ?? DateTime.UtcNow,
        Kwh = Convert.ToDouble(reader["Kwh"], CultureInfo.InvariantCulture),
        Anlass = ParseEnum(reader["Anlass"].ToString(), ZaehlerAnlass.Tag),
        GrowId = reader["GrowId"] is DBNull ? null : Convert.ToInt32(reader["GrowId"], CultureInfo.InvariantCulture),
        Phase = NullString(reader["Phase"]),
    };
}
