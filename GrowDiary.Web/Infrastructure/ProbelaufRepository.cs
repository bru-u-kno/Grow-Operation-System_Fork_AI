using System.Globalization;
using System.Text.Json;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Fork AI (A-010, 07.10.2026): Die Probeläufe — ein Eintrag je Lauf, mit allem, was zum Zurückstellen
/// und zur Auswertung gebraucht wird.
/// </summary>
/// <remarks>
/// <para><b>Der Wecker liegt hier.</b> Geplantes und hartes Ende stehen in der Datenbank, nicht im
/// Speicher: nach einem Neustart des Add-ons findet der Fork den offenen Lauf und stellt zurück.</para>
/// <para>Grenzen, Messreihe und Auswertung sind JSON-Spalten — sie werden nie abgefragt, nur als Ganzes
/// gelesen und geschrieben.</para>
/// </remarks>
public sealed class ProbelaufRepository : RepositoryBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ProbelaufRepository(AppPaths paths) : base(paths)
    {
    }

    private SqliteConnection Open()
    {
        var connection = OpenConnection();
        // Je Datenbankdatei, nicht je Prozess — siehe EigenesSchema.
        EigenesSchema.Sicherstellen(nameof(ProbelaufRepository), Paths.DatabasePath, () => SchemaAnlegen(connection));
        return connection;
    }

    private static void SchemaAnlegen(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ForkProbelauf (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Modul TEXT NOT NULL,
                Status INTEGER NOT NULL,
                StartUtc TEXT NOT NULL,
                GeplantesEndeUtc TEXT NOT NULL,
                HartesEndeUtc TEXT NOT NULL,
                EingriffEndeUtc TEXT NULL,
                EndeUtc TEXT NULL,
                AbbruchGrund TEXT NULL,
                Grenzen TEXT NOT NULL,
                Ausgangszustand TEXT NOT NULL,
                Messreihe TEXT NOT NULL,
                Auswertung TEXT NULL,
                Empfehlung TEXT NULL,
                TagPhaseBeiStart INTEGER NULL,
                RueckstellVersuche INTEGER NOT NULL DEFAULT 0,
                FuehlerLosSeitUtc TEXT NULL,
                LetzteMessungUtc TEXT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private const string Spalten =
        "Id, Modul, Status, StartUtc, GeplantesEndeUtc, HartesEndeUtc, EingriffEndeUtc, EndeUtc, AbbruchGrund, "
        + "Grenzen, Ausgangszustand, Messreihe, Auswertung, Empfehlung, TagPhaseBeiStart, RueckstellVersuche, "
        + "FuehlerLosSeitUtc, LetzteMessungUtc";

    private static string Zeit(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static object ZeitOderNull(DateTime? utc) => utc is null ? DBNull.Value : Zeit(utc.Value);

    private static void Parameter(SqliteCommand c, ProbelaufLauf l)
    {
        c.Parameters.AddWithValue("@modul", l.Modul);
        c.Parameters.AddWithValue("@status", (int)l.Status);
        c.Parameters.AddWithValue("@start", Zeit(l.StartUtc));
        c.Parameters.AddWithValue("@geplant", Zeit(l.GeplantesEndeUtc));
        c.Parameters.AddWithValue("@hart", Zeit(l.HartesEndeUtc));
        c.Parameters.AddWithValue("@eingriffEnde", ZeitOderNull(l.EingriffEndeUtc));
        c.Parameters.AddWithValue("@ende", ZeitOderNull(l.EndeUtc));
        c.Parameters.AddWithValue("@grund", (object?)l.AbbruchGrund ?? DBNull.Value);
        c.Parameters.AddWithValue("@grenzen", JsonSerializer.Serialize(l.Grenzen, Json));
        c.Parameters.AddWithValue("@ausgang", l.Ausgangszustand);
        c.Parameters.AddWithValue("@reihe", JsonSerializer.Serialize(l.Messreihe, Json));
        c.Parameters.AddWithValue("@auswertung", l.Auswertung is null ? DBNull.Value : JsonSerializer.Serialize(l.Auswertung, Json));
        c.Parameters.AddWithValue("@empfehlung", (object?)l.Empfehlung ?? DBNull.Value);
        c.Parameters.AddWithValue("@tag", l.TagPhaseBeiStart is null ? DBNull.Value : l.TagPhaseBeiStart.Value ? 1 : 0);
        c.Parameters.AddWithValue("@versuche", l.RueckstellVersuche);
        c.Parameters.AddWithValue("@fuehlerlos", ZeitOderNull(l.FuehlerLosSeitUtc));
        c.Parameters.AddWithValue("@letzte", ZeitOderNull(l.LetzteMessungUtc));
    }

    /// <summary>Legt einen Lauf an und liefert seine Id.</summary>
    public long Anlegen(ProbelaufLauf lauf)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ForkProbelauf (Modul, Status, StartUtc, GeplantesEndeUtc, HartesEndeUtc, EingriffEndeUtc, EndeUtc, AbbruchGrund,
                Grenzen, Ausgangszustand, Messreihe, Auswertung, Empfehlung, TagPhaseBeiStart, RueckstellVersuche, FuehlerLosSeitUtc, LetzteMessungUtc)
            VALUES (@modul, @status, @start, @geplant, @hart, @eingriffEnde, @ende, @grund,
                @grenzen, @ausgang, @reihe, @auswertung, @empfehlung, @tag, @versuche, @fuehlerlos, @letzte);
            SELECT last_insert_rowid();
            """;
        Parameter(command, lauf);
        lauf.Id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        return lauf.Id;
    }

    /// <summary>Schreibt den ganzen Lauf zurück.</summary>
    public void Speichern(ProbelaufLauf lauf)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ForkProbelauf SET Modul=@modul, Status=@status, StartUtc=@start, GeplantesEndeUtc=@geplant, HartesEndeUtc=@hart,
                EingriffEndeUtc=@eingriffEnde, EndeUtc=@ende, AbbruchGrund=@grund, Grenzen=@grenzen, Ausgangszustand=@ausgang,
                Messreihe=@reihe, Auswertung=@auswertung, Empfehlung=@empfehlung, TagPhaseBeiStart=@tag,
                RueckstellVersuche=@versuche, FuehlerLosSeitUtc=@fuehlerlos, LetzteMessungUtc=@letzte
            WHERE Id=@id;
            """;
        Parameter(command, lauf);
        command.Parameters.AddWithValue("@id", lauf.Id);
        command.ExecuteNonQuery();
    }

    public ProbelaufLauf? Holen(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Spalten} FROM ForkProbelauf WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var leser = command.ExecuteReader();
        return leser.Read() ? Lesen(leser) : null;
    }

    /// <summary>Die letzten Läufe, neueste zuerst.</summary>
    public IReadOnlyList<ProbelaufLauf> Liste(int hoechstens = 50)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Spalten} FROM ForkProbelauf ORDER BY Id DESC LIMIT @max;";
        command.Parameters.AddWithValue("@max", hoechstens);
        var liste = new List<ProbelaufLauf>();
        using var leser = command.ExecuteReader();
        while (leser.Read()) liste.Add(Lesen(leser));
        return liste;
    }

    /// <summary>Läufe, die noch nicht abgeschlossen sind: Eingriff aktiv, Nachlauf oder Zurückstellen offen.</summary>
    public IReadOnlyList<ProbelaufLauf> Offene()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Spalten} FROM ForkProbelauf WHERE Status IN (@laeuft, @nachlauf, @offen) ORDER BY Id;";
        command.Parameters.AddWithValue("@laeuft", (int)ProbelaufStatus.Laeuft);
        command.Parameters.AddWithValue("@nachlauf", (int)ProbelaufStatus.Nachlauf);
        command.Parameters.AddWithValue("@offen", (int)ProbelaufStatus.RueckstellungOffen);
        var liste = new List<ProbelaufLauf>();
        using var leser = command.ExecuteReader();
        while (leser.Read()) liste.Add(Lesen(leser));
        return liste;
    }

    private ProbelaufLauf Lesen(SqliteDataReader r)
    {
        DateTime? Optional(int i) => r.IsDBNull(i) ? null : ParseStoredUtcDateTime(r.GetString(i));

        return new ProbelaufLauf
        {
            Id = r.GetInt64(0),
            Modul = r.GetString(1),
            Status = (ProbelaufStatus)r.GetInt32(2),
            StartUtc = ParseStoredUtcDateTime(r.GetString(3)) ?? DateTime.UtcNow,
            GeplantesEndeUtc = ParseStoredUtcDateTime(r.GetString(4)) ?? DateTime.UtcNow,
            HartesEndeUtc = ParseStoredUtcDateTime(r.GetString(5)) ?? DateTime.UtcNow,
            EingriffEndeUtc = Optional(6),
            EndeUtc = Optional(7),
            AbbruchGrund = r.IsDBNull(8) ? null : r.GetString(8),
            Grenzen = JsonSerializer.Deserialize<ProbelaufGrenzen>(r.GetString(9), Json) ?? new(null, null, null, null),
            Ausgangszustand = r.GetString(10),
            Messreihe = JsonSerializer.Deserialize<ProbelaufMessreihe>(r.GetString(11), Json) ?? ProbelaufMessreihe.Leer(),
            Auswertung = r.IsDBNull(12) ? null : JsonSerializer.Deserialize<ProbelaufAuswertung>(r.GetString(12), Json),
            Empfehlung = r.IsDBNull(13) ? null : r.GetString(13),
            TagPhaseBeiStart = r.IsDBNull(14) ? null : r.GetInt64(14) != 0,
            RueckstellVersuche = r.GetInt32(15),
            FuehlerLosSeitUtc = Optional(16),
            LetzteMessungUtc = Optional(17),
        };
    }
}
