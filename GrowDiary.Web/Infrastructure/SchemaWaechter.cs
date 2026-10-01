using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// forkai.157: Merkt sich, welche Fork-Tabellen in welcher Datenbankdatei
/// schon angelegt sind — und vergisst es, wenn die Datei ausgetauscht wird.
/// </summary>
/// <remarks>
/// <para><b>Wozu.</b> Fork AI legt seine Tabellen außerhalb des Kern-Schemas an
/// (<c>CREATE TABLE IF NOT EXISTS</c> beim ersten Zugriff), damit der Abgleich
/// mit dem Original konfliktfrei bleibt. Das soll nur einmal laufen, nicht bei
/// jeder Anfrage.</para>
///
/// <para><b>Was vorher schiefging.</b> <c>KostenRepository</c> und
/// <c>GeraeteRepository</c> hatten je einen eigenen statischen Merker „schon
/// angelegt" — für den ganzen Prozess. Zwei Fälle, beide nachgestellt:</para>
/// <list type="bullet">
/// <item>Eine zweite Datenbank im selben Prozess (die Tests legen viele an)
/// blieb ohne Tabellen: „no such table: ForkGeraete".</item>
/// <item>Die Wiederherstellung einer Sicherung tauscht die Datei im laufenden
/// Betrieb aus. Stammt die Sicherung aus einer Version vor den Tabellen oder
/// vor einer neuen Spalte, fehlten sie bis zum nächsten Neustart — Geräte- und
/// Kosten-Seite brachen ab. Die Versionsprüfung davor sieht nur das Kern-Schema.</item>
/// </list>
///
/// <para>Deshalb EIN Merker je Datei und Bereich, und <see cref="Vergessen"/>
/// nach jedem Dateitausch. Der nächste Zugriff legt dann an, was fehlt.</para>
/// </remarks>
public static class SchemaWaechter
{
    private static readonly object Sperre = new();
    private static readonly HashSet<(string Datei, string Bereich)> Angelegt = [];

    /// <summary>
    /// Führt <paramref name="anlegen"/> aus, wenn der <paramref name="bereich"/>
    /// in dieser Datenbankdatei noch nicht angelegt ist. Schlägt das Anlegen
    /// fehl, bleibt der Bereich offen und wird beim nächsten Zugriff wieder versucht.
    /// </summary>
    public static void Sichern(SqliteConnection verbindung, string bereich, Action<SqliteConnection> anlegen)
    {
        var schluessel = (verbindung.DataSource, bereich);
        lock (Sperre)
        {
            if (Angelegt.Contains(schluessel)) return;
            anlegen(verbindung);
            Angelegt.Add(schluessel);
        }
    }

    /// <summary>
    /// Alles vergessen — nach dem Austausch einer Datenbankdatei. Der nächste
    /// Zugriff jedes Bereichs prüft und ergänzt seine Tabellen und Spalten neu.
    /// </summary>
    public static void Vergessen()
    {
        lock (Sperre) Angelegt.Clear();
    }

    /// <summary>Ob in einer Tabelle eine Spalte fehlt — SQLite kennt kein „ADD COLUMN IF NOT EXISTS".</summary>
    public static bool SpalteVorhanden(SqliteConnection verbindung, string tabelle, string spalte)
    {
        using var befehl = verbindung.CreateCommand();
        befehl.CommandText = $"PRAGMA table_info({tabelle});";
        using var leser = befehl.ExecuteReader();
        while (leser.Read())
        {
            if (string.Equals(leser["name"].ToString(), spalte, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
