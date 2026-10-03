using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>
/// Fork AI (A-003, Prüfer 03.10.2026): Die Schlüssel reisen nicht mit einer Sicherung zurück.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Eine Sicherung ist die ganze Datenbank, also auch
/// <c>ForkKiSchluessel</c> und die Einstellungen <c>ki-zugriff.*</c>. Wer eine
/// ältere Sicherung zurückspielte, holte damit jeden Schlüssel zurück, der
/// inzwischen gelöscht, gesperrt oder heruntergestuft war — auch einen, der
/// gerade deshalb gelöscht wurde, weil er in falsche Hände geraten war.</para>
///
/// <para><b>Die Regel.</b> Der Zugriff für KI-Assistenten ist Sicherheitszustand,
/// kein Datenbestand: nach dem Zurückspielen gilt, was <b>vorher</b> galt. Er wird
/// vor dem Austausch der Datei gelesen und danach in die zurückgespielte
/// Datenbank geschrieben, als Ganzes ersetzt.</para>
/// </remarks>
public static class KiZustandBeimZurueckspielen
{
    private const string EinstellungsMuster = "ki-zugriff.%";

    public sealed record Zustand(IReadOnlyList<object?[]> Schluessel, IReadOnlyList<(string Key, string? Value)> Einstellungen);

    /// <summary>Den Zugriffs-Zustand der Datenbank an diesem Pfad lesen; fehlt die Tabelle, ist er leer.</summary>
    public static Zustand Lesen(string datenbankPfad)
    {
        var schluessel = new List<object?[]>();
        var einstellungen = new List<(string, string?)>();
        if (!File.Exists(datenbankPfad)) return new Zustand(schluessel, einstellungen);

        using var verbindung = Oeffnen(datenbankPfad);
        if (TabelleDa(verbindung, "ForkKiSchluessel"))
        {
            using var befehl = verbindung.CreateCommand();
            befehl.CommandText = "SELECT Id, Name, Praefix, Hash, Stufen, ErstelltAmUtc, ZuletztGenutztAmUtc, GesperrtAmUtc FROM ForkKiSchluessel ORDER BY Id;";
            using var leser = befehl.ExecuteReader();
            while (leser.Read())
            {
                var zeile = new object?[leser.FieldCount];
                for (var i = 0; i < zeile.Length; i++) zeile[i] = leser.IsDBNull(i) ? null : leser.GetValue(i);
                schluessel.Add(zeile);
            }
        }

        if (TabelleDa(verbindung, "AppSettings"))
        {
            using var befehl = verbindung.CreateCommand();
            befehl.CommandText = "SELECT Key, Value FROM AppSettings WHERE Key LIKE $muster;";
            befehl.Parameters.AddWithValue("$muster", EinstellungsMuster);
            using var leser = befehl.ExecuteReader();
            while (leser.Read()) einstellungen.Add((leser.GetString(0), leser.IsDBNull(1) ? null : leser.GetString(1)));
        }

        return new Zustand(schluessel, einstellungen);
    }

    /// <summary>Den gelesenen Zustand in die Datenbank an diesem Pfad schreiben — ersetzt, was dort steht.</summary>
    public static void Schreiben(string datenbankPfad, Zustand zustand)
    {
        using var verbindung = Oeffnen(datenbankPfad);
        using var transaktion = verbindung.BeginTransaction();

        Ausfuehren(verbindung, transaktion, KiSchluesselRepository.SchemaSql);
        Ausfuehren(verbindung, transaktion, "DELETE FROM ForkKiSchluessel;");
        foreach (var zeile in zustand.Schluessel)
        {
            using var befehl = verbindung.CreateCommand();
            befehl.Transaction = transaktion;
            befehl.CommandText = """
                INSERT INTO ForkKiSchluessel (Id, Name, Praefix, Hash, Stufen, ErstelltAmUtc, ZuletztGenutztAmUtc, GesperrtAmUtc)
                VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6, $p7);
                """;
            for (var i = 0; i < 8; i++) befehl.Parameters.AddWithValue("$p" + i, zeile[i] ?? DBNull.Value);
            befehl.ExecuteNonQuery();
        }

        if (TabelleDa(verbindung, "AppSettings", transaktion))
        {
            using (var loeschen = verbindung.CreateCommand())
            {
                loeschen.Transaction = transaktion;
                loeschen.CommandText = "DELETE FROM AppSettings WHERE Key LIKE $muster;";
                loeschen.Parameters.AddWithValue("$muster", EinstellungsMuster);
                loeschen.ExecuteNonQuery();
            }

            foreach (var (key, value) in zustand.Einstellungen)
            {
                using var befehl = verbindung.CreateCommand();
                befehl.Transaction = transaktion;
                befehl.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES ($key, $value);";
                befehl.Parameters.AddWithValue("$key", key);
                befehl.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
                befehl.ExecuteNonQuery();
            }
        }

        transaktion.Commit();
    }

    private static SqliteConnection Oeffnen(string pfad)
    {
        // Ohne Pool: zwischen Lesen und Schreiben wird die Datei ausgetauscht.
        var verbindung = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = pfad, Pooling = false }.ToString());
        verbindung.Open();
        return verbindung;
    }

    private static bool TabelleDa(SqliteConnection verbindung, string name, SqliteTransaction? transaktion = null)
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
}
