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
/// <param name="Rueckfrage">
/// Fork AI (A-005, 03.10.2026): Bei diesen Stufen soll der Assistent vorher
/// fragen. Immer eine Teilmenge von <paramref name="Stufen"/> — eine Stufe mit
/// Rückfrage ist freigegeben, der Assistent soll nur erst nachfragen.
/// </param>
public sealed record KiSchluessel(
    int Id,
    string Name,
    string Praefix,
    string Hash,
    KiStufe Stufen,
    KiStufe Rueckfrage,
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
/// Eigenes Schema je Datenbankdatei über <see cref="EigenesSchema"/> — dort
/// steht, warum ein Merker je Prozess nach einem Zurückspielen eine Tabelle
/// ohne neue Spalte hinterlässt. Gelöschte Schlüssel verschwinden ganz;
/// gesperrte bleiben sichtbar, damit der Betreiber sieht, was er abgeschaltet hat.
/// </remarks>
public sealed class KiSchluesselRepository : RepositoryBase
{
    /// <summary>Das Schema der Tabelle — so, wie eine neue Datei sie bekommt.</summary>
    private const string SchemaSql = """
                CREATE TABLE IF NOT EXISTS ForkKiSchluessel (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Praefix TEXT NOT NULL,
                    Hash TEXT NOT NULL,
                    Stufen INTEGER NOT NULL DEFAULT 0,
                    Rueckfrage INTEGER NOT NULL DEFAULT 0,
                    ErstelltAmUtc TEXT NOT NULL,
                    ZuletztGenutztAmUtc TEXT NULL,
                    GesperrtAmUtc TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ForkKiSchluessel_Praefix ON ForkKiSchluessel (Praefix);
                """;

    /// <summary>
    /// Fork AI (A-005, 03.10.2026): Die frühere globale Rückfrage-Regel
    /// („Vorher nachfragen ab …"). Wird genau einmal gelesen — bei der Übernahme
    /// in die Spalte <c>Rueckfrage</c> — und danach gelöscht.
    /// </summary>
    internal const string AlteEinstellungRueckfrage = "ki-zugriff.rueckfrage-ab-stufe";

    /// <summary>Was forkai.163 annahm, wenn die alte Einstellung fehlte — so stand es auch in <c>/api/ki-zugriff/ich</c>.</summary>
    internal const KiStufe AlteVorgabeRueckfrage = KiStufe.GrowPlanen;

    public KiSchluesselRepository(AppPaths paths) : base(paths)
    {
    }

    private SqliteConnection Open()
    {
        var connection = OpenConnection();
        EigenesSchema.Sicherstellen(nameof(KiSchluesselRepository), Paths.DatabasePath, () => SchemaSicherstellen(connection));
        return connection;
    }

    /// <summary>
    /// Tabelle anlegen, fehlende Spalten nachrüsten, die alte Einstellung übernehmen.
    /// Auch für <see cref="KiZustandBeimZurueckspielen"/> — eine zurückgespielte
    /// Sicherung aus forkai.163 hat die Tabelle ohne <c>Rueckfrage</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Die Übernahme (A-005).</b> Bis forkai.163 galt EINE Regel für alle
    /// Schlüssel: „Vorher nachfragen ab Stufe X" (alle Stufen mit einem Bit ≥ X).
    /// Jetzt steht die Rückfrage je Schlüssel. Fehlt die Spalte, bekommt jeder
    /// vorhandene Schlüssel einmalig <c>Rueckfrage = Stufen ∩ {Stufen ≥ X}</c>.
    /// War die Regel „nie" (leerer Eintrag), wird es 0. Fehlte der Eintrag ganz,
    /// galt in forkai.163 die Vorbelegung Grow planen — die hat der Assistent auch
    /// gesagt bekommen, also wird sie übernommen.</para>
    /// <para><b>Nur einmal.</b> Ausgelöst wird die Übernahme vom Fehlen der Spalte;
    /// ist sie einmal da, läuft nichts mehr. Die alte Einstellung wird danach
    /// gelöscht, damit niemand sie versehentlich wieder liest. Alles in einer
    /// Transaktion: bricht etwas ab, fehlt die Spalte weiter, und das nächste
    /// Öffnen versucht es noch einmal.</para>
    /// </remarks>
    internal static void SchemaSicherstellen(SqliteConnection verbindung, SqliteTransaction? aussen = null)
    {
        var transaktion = aussen ?? verbindung.BeginTransaction();
        try
        {
            Ausfuehren(verbindung, transaktion, SchemaSql);

            if (!SpalteDa(verbindung, transaktion, "Rueckfrage"))
            {
                Ausfuehren(verbindung, transaktion, "ALTER TABLE ForkKiSchluessel ADD COLUMN Rueckfrage INTEGER NOT NULL DEFAULT 0;");

                var maske = AlteRegelAlsMaske(AlteEinstellungLesen(verbindung, transaktion));
                using var uebernehmen = verbindung.CreateCommand();
                uebernehmen.Transaction = transaktion;
                uebernehmen.CommandText = "UPDATE ForkKiSchluessel SET Rueckfrage = Stufen & $maske;";
                uebernehmen.Parameters.AddWithValue("$maske", (int)maske);
                uebernehmen.ExecuteNonQuery();
            }

            if (TabelleDa(verbindung, transaktion, "AppSettings"))
            {
                using var loeschen = verbindung.CreateCommand();
                loeschen.Transaction = transaktion;
                loeschen.CommandText = "DELETE FROM AppSettings WHERE Key = $key;";
                loeschen.Parameters.AddWithValue("$key", AlteEinstellungRueckfrage);
                loeschen.ExecuteNonQuery();
            }

            if (aussen is null) transaktion.Commit();
        }
        finally
        {
            if (aussen is null) transaktion.Dispose();
        }
    }

    /// <summary>
    /// Die alte Regel als Bitmaske: alle Stufen ab der genannten. Die Bits sind
    /// aufsteigend nach Tragweite nummeriert, „ab X" heisst also „Bit ≥ X".
    /// </summary>
    /// <param name="ab">Die alte Stufe; null = nie.</param>
    internal static KiStufe AlteRegelAlsMaske(KiStufe? ab)
        => ab is not { } stufe
            ? KiStufe.Keine
            : KiZugriffDienst.AlleStufen.Where(s => s >= stufe).Aggregate(KiStufe.Keine, (summe, s) => summe | s);

    /// <summary>
    /// Die alte Einstellung so lesen, wie forkai.163 sie gelesen hat: fehlt sie,
    /// die Vorbelegung; leer heisst nie; ein unbekannter Name wie fehlend.
    /// </summary>
    private static KiStufe? AlteEinstellungLesen(SqliteConnection verbindung, SqliteTransaction transaktion)
    {
        if (!TabelleDa(verbindung, transaktion, "AppSettings")) return AlteVorgabeRueckfrage;
        using var befehl = verbindung.CreateCommand();
        befehl.Transaction = transaktion;
        befehl.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        befehl.Parameters.AddWithValue("$key", AlteEinstellungRueckfrage);
        var roh = befehl.ExecuteScalar();
        if (roh is null) return AlteVorgabeRueckfrage;
        var text = roh as string;
        if (string.IsNullOrWhiteSpace(text)) return null;
        return KiZugriffDienst.EinzelneStufe(text) ?? AlteVorgabeRueckfrage;
    }

    private static bool SpalteDa(SqliteConnection verbindung, SqliteTransaction transaktion, string spalte)
    {
        using var befehl = verbindung.CreateCommand();
        befehl.Transaction = transaktion;
        befehl.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ForkKiSchluessel') WHERE name = $spalte;";
        befehl.Parameters.AddWithValue("$spalte", spalte);
        return Convert.ToInt64(befehl.ExecuteScalar()) > 0;
    }

    private static bool TabelleDa(SqliteConnection verbindung, SqliteTransaction transaktion, string name)
    {
        using var befehl = verbindung.CreateCommand();
        befehl.Transaction = transaktion;
        befehl.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        befehl.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(befehl.ExecuteScalar()) > 0;
    }

    private static void Ausfuehren(SqliteConnection verbindung, SqliteTransaction transaktion, string sql)
    {
        using var befehl = verbindung.CreateCommand();
        befehl.Transaction = transaktion;
        befehl.CommandText = sql;
        befehl.ExecuteNonQuery();
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

    /// <remarks>Die Rückfrage wird auf die Stufen beschnitten — eine Rückfrage für eine gesperrte Stufe gibt es nicht.</remarks>
    public int Anlegen(string name, string praefix, string hash, KiStufe stufen, KiStufe rueckfrage, DateTime jetztUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ForkKiSchluessel (Name, Praefix, Hash, Stufen, Rueckfrage, ErstelltAmUtc)
            VALUES ($name, $praefix, $hash, $stufen, $rueckfrage, $erstellt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$praefix", praefix);
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$stufen", (int)stufen);
        command.Parameters.AddWithValue("$rueckfrage", (int)(rueckfrage & stufen));
        command.Parameters.AddWithValue("$erstellt", ToStorageUtc(jetztUtc));
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    /// <summary>Name, Stufen und Rückfrage ändern. False, wenn es den Schlüssel nicht gibt.</summary>
    public bool Aendern(int id, string name, KiStufe stufen, KiStufe rueckfrage)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ForkKiSchluessel SET Name = $name, Stufen = $stufen, Rueckfrage = $rueckfrage WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$stufen", (int)stufen);
        command.Parameters.AddWithValue("$rueckfrage", (int)(rueckfrage & stufen));
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
            var stufen = (KiStufe)Convert.ToInt32((long)leser["Stufen"]);
            liste.Add(new KiSchluessel(
                Id: Convert.ToInt32((long)leser["Id"]),
                Name: leser["Name"]?.ToString() ?? string.Empty,
                Praefix: leser["Praefix"]?.ToString() ?? string.Empty,
                Hash: leser["Hash"]?.ToString() ?? string.Empty,
                Stufen: stufen,
                // Rueckfrage ⊆ Stufen auch beim Lesen: sonst fragte der Assistent bei einer gesperrten Stufe nach.
                Rueckfrage: (KiStufe)Convert.ToInt32((long)leser["Rueckfrage"]) & stufen,
                ErstelltAmUtc: ParseStoredUtcDateTime(NullString(leser["ErstelltAmUtc"])) ?? DateTime.UnixEpoch,
                ZuletztGenutztAmUtc: ParseStoredUtcDateTime(NullString(leser["ZuletztGenutztAmUtc"])),
                GesperrtAmUtc: ParseStoredUtcDateTime(NullString(leser["GesperrtAmUtc"]))));
        }
        return liste;
    }
}
