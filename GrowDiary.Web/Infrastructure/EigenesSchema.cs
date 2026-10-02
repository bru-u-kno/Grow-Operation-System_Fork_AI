using System.Collections.Concurrent;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Merkt sich je <b>Datenbankdatei</b>, welches Repository seine eigenen
/// Tabellen schon angelegt hat.
/// </summary>
/// <remarks>
/// <para><b>Der Befund (02.10.2026).</b> <see cref="KostenRepository"/> und
/// <see cref="GeraeteRepository"/> legen ihre Tabellen selbst an (nicht das
/// Kern-Schema) und merkten sich das in einem statischen Feld — einmal je
/// <i>Prozess</i>. Zwei Wege führen damit zu „no such table":</para>
/// <list type="bullet">
/// <item><b>Eine zweite Datenbank im selben Prozess</b> — in der Testmappe je
/// Testfall, die dafür eine Umgehung per Reflexion brauchte. Dagegen hilft der
/// Pfad als Schlüssel.</item>
/// <item><b>Ein zurückgespieltes Backup.</b> Der Pfad bleibt derselbe, die
/// Datei dahinter ist eine andere — womöglich aus einer Fassung ohne diese
/// Tabellen. Dagegen hilft der Pfad nicht; deshalb ruft der Restore
/// (<c>SystemApiController.RestoreBackup</c>) <see cref="Vergessen"/>.</item>
/// </list>
/// <para>Warum nicht bei jedem Öffnen prüfen: das wären bei jeder Abfrage
/// ein Dutzend <c>CREATE … IF NOT EXISTS</c> und <c>PRAGMA table_info</c>.
/// Die Datei wechselt im laufenden Betrieb nur an genau einer Stelle — dort
/// wird vergessen.</para>
/// </remarks>
public static class EigenesSchema
{
    private static readonly ConcurrentDictionary<(string Bereich, string Datei), bool> Erledigt = new();
    private static readonly object Sperre = new();

    /// <summary>
    /// <paramref name="anlegen"/> genau einmal je Bereich und Datenbankdatei
    /// ausführen — und nach <see cref="Vergessen"/> wieder.
    /// </summary>
    public static void Sicherstellen(string bereich, string datenbankPfad, Action anlegen)
    {
        var schluessel = (bereich, Datei(datenbankPfad));
        if (Erledigt.ContainsKey(schluessel)) return;
        lock (Sperre)
        {
            if (Erledigt.ContainsKey(schluessel)) return;
            anlegen();
            Erledigt[schluessel] = true;
        }
    }

    /// <summary>
    /// Alle Merker für diese Datei verwerfen — nach dem Austausch der Datei
    /// (Restore). Das nächste Öffnen legt fehlende Tabellen wieder an.
    /// </summary>
    public static void Vergessen(string datenbankPfad)
    {
        var datei = Datei(datenbankPfad);
        lock (Sperre)
        {
            foreach (var schluessel in Erledigt.Keys)
            {
                if (string.Equals(schluessel.Datei, datei, StringComparison.Ordinal))
                {
                    Erledigt.TryRemove(schluessel, out _);
                }
            }
        }
    }

    private static string Datei(string pfad) => Path.GetFullPath(pfad);
}
