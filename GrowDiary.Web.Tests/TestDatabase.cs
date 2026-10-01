using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests;

internal static class TestDatabase
{
    public static void Initialize(AppPaths paths)
    {
        new DatabaseInitializer(paths, NullLogger<DatabaseInitializer>.Instance).Initialize();
        KostenSchemaVergessen();
    }

    /// <summary>
    /// Eine neue Datenbank braucht auch die Kosten-Tabellen — der
    /// <see cref="KostenRepository"/> weiß das nicht.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Befund (01.10.2026).</b> <see cref="KostenRepository"/> legt
    /// seine Tabellen (<c>ForkVerbrauchsartikel</c> und vier weitere) selbst an
    /// und merkt sich das in einem <b>statischen</b> Feld — einmal je Prozess,
    /// nicht je Datenbank. Die Testmappe legt je Testfall eine neue Datenbank
    /// an; seit der Demobestand eine CO₂-Flasche sät, scheiterte jede zweite
    /// mit <c>no such table: ForkVerbrauchsartikel</c>.</para>
    ///
    /// <para><b>Im Betrieb ist das derselbe Fehler</b>, nur seltener: wer ein
    /// Backup zurückspielt, tauscht die Datenbankdatei im laufenden Prozess. Kam
    /// das Backup aus einer Fassung ohne Kosten-Tabellen, fehlen sie bis zum
    /// Neustart. Gemeldet, nicht hier repariert — die Reparatur gehört in den
    /// <see cref="KostenRepository"/>, nicht in die Testmappe.</para>
    ///
    /// <para><b>Hart statt still.</b> Fehlt das Feld (weil jemand den Fehler
    /// behoben hat), schlägt dieser Helfer laut fehl — dann gehört er
    /// entfernt, statt weiter ein Feld zu setzen, das es nicht mehr gibt.</para>
    /// </remarks>
    public static void KostenSchemaVergessen()
    {
        var feld = typeof(KostenRepository).GetField(
            "_schemaEnsured",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "KostenRepository._schemaEnsured gibt es nicht mehr. Ist der Fehler behoben "
                + "(Schema je Datenbank statt je Prozess)? Dann TestDatabase.KostenSchemaVergessen entfernen.");
        feld.SetValue(null, false);
    }

    public static Tent InitializeWithDefaultTent(AppPaths paths, string name = "Testzelt", TentType tentType = TentType.MultiPurpose)
    {
        Initialize(paths);
        return EnsureDefaultTent(paths, name, tentType);
    }

    public static Tent EnsureDefaultTent(AppPaths paths, string name = "Testzelt", TentType tentType = TentType.MultiPurpose)
    {
        var repository = new GrowRepository(paths);
        var existing = repository.GetTents().FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        return repository.CreateTent(new Tent
        {
            Name = name,
            Kind = "Grow Tent",
            TentType = tentType,
            AccentColor = "#69b578"
        });
    }
}
