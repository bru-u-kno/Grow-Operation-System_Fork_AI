using GrowDiary.Web.Infrastructure;

namespace GrowDiary.Web.Services;

/// <summary>
/// Wo die Sicherungen liegen, die vor dem Überschreiben einer Automation oder
/// eines Rechenwerts in Home Assistant angelegt werden.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (02.10.2026).</b> Beide Stellen schrieben nach
/// <c>AppContext.BaseDirectory/App_Data/automations-backup</c>. Im Add-on ist
/// das <c>/app</c> — die Schreibschicht des Containers, nicht das Volume
/// <c>/data</c>. Jede Sicherung war beim nächsten Update weg, und die Meldung
/// nach einem gescheiterten Zurückschreiben verwies trotzdem dorthin. Ein
/// Zurück, das ein Update nicht überlebt, ist keines.</para>
/// <para>Dieselbe Klasse wie bei den Sicherungen der Datenbank
/// (<see cref="AppPaths.BackupsPath"/>), gehalten von
/// <c>KeinPfadWirdVonHandGebautTests</c>. Diese Datei ist dort die einzige
/// Ausnahme — sie muss den alten Ort kennen, um ihn zu räumen.</para>
/// </remarks>
public static class SteuerungSicherungsOrdner
{
    private const string Name = "automations-backup";

    /// <summary>Der Ordner unter dem Datenpfad — im Add-on <c>/data/automations-backup</c>.</summary>
    public static string Fuer(AppPaths pfade) => Path.Combine(pfade.DataRootPath, Name);

    /// <summary>Wo bis 02.10.2026 gesichert wurde.</summary>
    public static string AlterOrt => Path.Combine(AppContext.BaseDirectory, "App_Data", Name);

    /// <summary>
    /// Sicherungen vom alten Ort an den neuen holen. Beim Start aufgerufen —
    /// was seit dem letzten Update am alten Ort entstand, ist sonst beim
    /// nächsten weg.
    /// </summary>
    /// <returns>Wie viele Dateien übernommen wurden.</returns>
    /// <remarks>
    /// Eine Datei, die es am neuen Ort schon gibt, wird nicht überschrieben.
    /// Die Quelle wird erst gelöscht, wenn die Kopie liegt.
    /// </remarks>
    public static int AlteUebernehmen(string alterOrdner, string neuerOrdner, ILogger log)
    {
        if (!Directory.Exists(alterOrdner)) return 0;
        if (string.Equals(Path.GetFullPath(alterOrdner).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(neuerOrdner).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            return 0;
        }

        var uebernommen = 0;
        foreach (var datei in Directory.EnumerateFiles(alterOrdner))
        {
            try
            {
                Directory.CreateDirectory(neuerOrdner);
                var ziel = Path.Combine(neuerOrdner, Path.GetFileName(datei));
                if (!File.Exists(ziel))
                {
                    File.Copy(datei, ziel);
                    uebernommen++;
                }
                File.Delete(datei);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.LogWarning(ex, "Sicherung {Datei} konnte nicht nach {Ziel} übernommen werden.", datei, neuerOrdner);
            }
        }

        if (uebernommen > 0)
        {
            log.LogInformation("{Anzahl} Sicherungen von Automationen nach {Ziel} übernommen.", uebernommen, neuerOrdner);
        }
        return uebernommen;
    }
}
