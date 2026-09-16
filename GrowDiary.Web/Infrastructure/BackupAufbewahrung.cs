namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Wie viele Sicherungen im Backup-Ordner liegen bleiben.
/// </summary>
/// <remarks>
/// <para><b>Warum es das gibt.</b> Bis 16.09.2026 legte jede Sicherung eine neue
/// ZIP an (bei Bru ≈6 MB) und nichts räumte je auf. Der Ordner liegt unter
/// <c>/data</c> und wandert damit in jede Home-Assistant-Sicherung mit — er
/// wuchs unbegrenzt, und über die Oberfläche ließ sich keine Datei entfernen
/// (Fehlerregister F-012).</para>
///
/// <para><b>Zwei Arten, getrennt gezählt.</b> Normale Sicherungen (von Hand,
/// vor einer Wiederherstellung) und Import-Sicherungen (vor einem Grow-Import)
/// behalten je <see cref="JeArt"/> Stück. Getrennt, weil sonst eine Reihe von
/// Importen die letzte von Hand angelegte Sicherung verdrängen würde.</para>
///
/// <para><b>Was nie gelöscht wird:</b> die gerade angelegte Datei und alles,
/// was der Aufrufer ausdrücklich schützt — etwa das Backup, das gerade
/// wiederhergestellt wird. Dateien, die nicht dem Namensschema folgen, fasst
/// die Regel nicht an.</para>
/// </remarks>
public static class BackupAufbewahrung
{
    /// <summary>So viele Sicherungen je Art bleiben liegen.</summary>
    public const int JeArt = 5;

    public const string Praefix = "grow-os-backup-";
    public const string ImportPraefix = "grow-os-backup-import-safety-";

    /// <summary>Eine Sicherung im Ordner.</summary>
    public sealed record Eintrag(string Datei, string Art, long Bytes, DateTime GeaendertUtc);

    /// <summary>Welcher Art die Datei ist — oder <c>null</c>, wenn sie nicht zum Schema gehört.</summary>
    public static string? ArtVon(string datei)
    {
        if (!datei.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        if (datei.StartsWith(ImportPraefix, StringComparison.OrdinalIgnoreCase)) return "import";
        if (datei.StartsWith(Praefix, StringComparison.OrdinalIgnoreCase)) return "sicherung";
        return null;
    }

    /// <summary>Alle Sicherungen, neueste zuerst.</summary>
    public static IReadOnlyList<Eintrag> Auflisten(string ordner)
    {
        if (!Directory.Exists(ordner)) return [];

        return Directory.EnumerateFiles(ordner, "*.zip")
            .Select(pfad => new FileInfo(pfad))
            .Select(info => (info, art: ArtVon(info.Name)))
            .Where(x => x.art is not null)
            .Select(x => new Eintrag(x.info.Name, x.art!, x.info.Length, x.info.LastWriteTimeUtc))
            .OrderByDescending(e => e.GeaendertUtc)
            .ThenByDescending(e => e.Datei, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Löscht je Art alles über <see cref="JeArt"/> hinaus, die ältesten zuerst.
    /// </summary>
    /// <returns>Die gelöschten Dateinamen.</returns>
    public static IReadOnlyList<string> Aufraeumen(string ordner, IEnumerable<string> geschuetzt)
    {
        var schutz = new HashSet<string>(geschuetzt.Where(d => !string.IsNullOrWhiteSpace(d)), StringComparer.OrdinalIgnoreCase);
        var geloescht = new List<string>();

        foreach (var gruppe in Auflisten(ordner).GroupBy(e => e.Art))
        {
            // Geschützte Dateien zählen mit: sie belegen einen der Plätze.
            var ueberzaehlig = gruppe.Skip(JeArt).Where(e => !schutz.Contains(e.Datei));
            foreach (var eintrag in ueberzaehlig)
            {
                try
                {
                    File.Delete(Path.Combine(ordner, eintrag.Datei));
                    geloescht.Add(eintrag.Datei);
                }
                catch (IOException)
                {
                    // Eine gerade gelesene Datei bleibt liegen — beim nächsten Mal.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return geloescht;
    }
}
