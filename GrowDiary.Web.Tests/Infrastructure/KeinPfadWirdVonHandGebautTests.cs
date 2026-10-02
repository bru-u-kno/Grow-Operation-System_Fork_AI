namespace GrowDiary.Web.Tests.Infrastructure;

/// <summary>
/// Niemand rechnet sich den Datenpfad selbst aus.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (01.09.2026).</b> Zehn Stellen bauten
/// <c>ContentRootPath + "App_Data" + …</c> von Hand zusammen. Auf einem
/// Entwicklungsrechner ist das derselbe Ordner wie <c>DataRootPath</c> — im
/// Add-on ist es das <b>nicht</b>: dort setzt das Dockerfile
/// <c>WORKDIR /app</c> und <c>GROWDIARY_DATA_PATH=/data</c>, und nur
/// <c>/data</c> ist als Volume deklariert.</para>
///
/// <para><b>Was das kostete</b> — drei belegte Fälle, alle nur im Add-on:</para>
/// <list type="bullet">
///   <item><b>Sicherungen</b> landeten unter <c>/app/App_Data/backups</c>, also
///   in der Schreibschicht des Containers. Beim nächsten Update war jede
///   Sicherung weg, ohne Meldung — auch die Sicherheitskopie vor einem
///   Import.</item>
///   <item><b>Kamerabilder</b> wurden nach <c>/data/snapshots</c> geschrieben
///   und aus <c>/app/App_Data/snapshots</c> gelesen: der Rückfall im
///   Zelt-Bildschirm gab immer 404.</item>
///   <item><b>Die Home-Assistant-Konfiguration</b> wurde an einer Stelle
///   gesucht, an der sie nicht liegt.</item>
/// </list>
///
/// <para>Dieselbe Klasse wie bei den Fotos, die deshalb nie gelöscht wurden
/// (<c>RepositoryBase.TryResolveUploadPath</c>). Ein Fehler, den kein Test
/// dieses Projekts fangen kann, solange er die Wege selbst vergleicht: jeder
/// Test baut <c>AppPaths</c> mit <b>einem</b> Wurzelverzeichnis, dort fallen
/// beide Wege zusammen. Deshalb prüft diese Zählung den <b>Quelltext</b>.</para>
/// </remarks>
public sealed class KeinPfadWirdVonHandGebautTests
{
    /// <summary>Stellen, die den Inhaltspfad wirklich meinen — mit Grund.</summary>
    private static readonly Dictionary<string, string> MitGrund = new(StringComparer.Ordinal)
    {
        ["AppPaths.cs"] =
            "Hier wohnt die Rechnung. DataRootPath faellt genau hier auf "
            + "ContentRoot/App_Data zurueck, wenn GROWDIARY_DATA_PATH fehlt.",
        ["SteuerungSicherungsOrdner.cs"] =
            "Kennt den alten Ort der Automations-Sicherungen (Programmordner), "
            + "um sie beim Start nach DataRootPath zu holen. Geschrieben wird dort nichts.",
    };

    [Fact]
    public void NiemandRechnetSichDenDatenpfadSelbstAus()
    {
        var wurzel = QuellVerzeichnis();
        var dateien = Directory.EnumerateFiles(wurzel, "*.cs", SearchOption.AllDirectories)
            .Where(d => !d.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(d => !d.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        // Mengenwaechter: ohne Grundmenge laeuft die Schleife null Mal durch.
        Assert.True(dateien.Count >= 100,
            $"Nur {dateien.Count} Quelldateien gefunden — die Grundmenge stimmt nicht, "
            + "und diese Zaehlung prueft dann nichts.");

        var treffer = new List<string>();
        foreach (var datei in dateien)
        {
            var name = Path.GetFileName(datei);
            if (MitGrund.ContainsKey(name)) continue;

            var zeilen = File.ReadAllLines(datei);
            for (var i = 0; i < zeilen.Length; i += 1)
            {
                var zeile = zeilen[i].Trim();

                // Eine Erwaehnung ist keine Verwendung: Kommentare und XML-Doku
                // nennen den Namen, ohne den Pfad zu bauen.
                if (zeile.StartsWith("//", StringComparison.Ordinal)
                    || zeile.StartsWith("///", StringComparison.Ordinal)
                    || zeile.StartsWith("*", StringComparison.Ordinal)
                    || zeile.StartsWith("/*", StringComparison.Ordinal))
                {
                    continue;
                }

                // Fork AI (02.10.2026): Der Programmordner ist im Add-on derselbe
                // Inhaltspfad (/app) — die Sicherungen der Automationen landeten
                // ueber AppContext.BaseDirectory genau dort.
                if ((zeile.Contains("ContentRootPath", StringComparison.Ordinal)
                        || zeile.Contains("AppContext.BaseDirectory", StringComparison.Ordinal))
                    && zeile.Contains("App_Data", StringComparison.Ordinal))
                {
                    treffer.Add($"{name}:{i + 1}  {zeile}");
                }
            }
        }

        Assert.True(treffer.Count == 0,
            "Diese Stellen rechnen den Datenpfad aus dem INHALTSpfad aus:\n  "
            + string.Join("\n  ", treffer)
            + "\n\nIm Add-on ist ContentRoot /app und DataRoot /data — nur /data ueberlebt "
            + "ein Update und liegt in den Sicherungen von Home Assistant. Richtig sind die "
            + "Eigenschaften von AppPaths (DataRootPath, BackupsPath, SnapshotsPath, "
            + "UploadRootPath, KnowledgeDataPath, HaConfigPath).");
    }

    /// <summary>
    /// Und die beiden Wurzeln sind wirklich verschieden, wenn der Datenpfad gesetzt ist.
    /// </summary>
    /// <remarks>
    /// Ohne diesen Fall wäre oben nicht belegt, dass es überhaupt einen
    /// Unterschied gibt — auf diesem Rechner fallen beide Wege zusammen, und
    /// genau deshalb ist der Fehler so lange unbemerkt geblieben.
    /// </remarks>
    [Fact]
    public void MitGesetztemDatenpfad_LiegenDieWegeAuseinander()
    {
        var vorher = Environment.GetEnvironmentVariable("GROWDIARY_DATA_PATH");
        try
        {
            Environment.SetEnvironmentVariable("GROWDIARY_DATA_PATH", Path.Combine(Path.GetTempPath(), "daten-woanders"));
            var pfade = new GrowDiary.Web.Infrastructure.AppPaths(Path.Combine(Path.GetTempPath(), "app"));

            Assert.False(pfade.BackupsPath.StartsWith(pfade.ContentRootPath, StringComparison.OrdinalIgnoreCase),
                $"Die Sicherungen liegen unter {pfade.BackupsPath} und damit im Inhaltspfad "
                + $"{pfade.ContentRootPath} — im Add-on waeren sie beim naechsten Update weg.");
            Assert.False(pfade.SnapshotsPath.StartsWith(pfade.ContentRootPath, StringComparison.OrdinalIgnoreCase),
                $"Die Kamerabilder liegen unter {pfade.SnapshotsPath} und damit im Inhaltspfad.");
            Assert.False(pfade.HaConfigPath.StartsWith(pfade.ContentRootPath, StringComparison.OrdinalIgnoreCase),
                $"Die Home-Assistant-Konfiguration liegt unter {pfade.HaConfigPath} und damit im Inhaltspfad.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GROWDIARY_DATA_PATH", vorher);
        }
    }

    private static string QuellVerzeichnis()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var kandidat = Path.Combine(dir, "GrowDiary.Web");
            if (Directory.Exists(Path.Combine(kandidat, "Services"))) return kandidat;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Quellverzeichnis nicht gefunden.");
    }
}
