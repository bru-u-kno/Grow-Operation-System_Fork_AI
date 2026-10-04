using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Kein Abschnitt der KI-Mappe geht leer hinaus.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (02.09.2026).</b> <c>AgentPackageBuilder</c> stand auf
/// der Liste der Klassen, die kein Test je anfasst — <c>AgentPackageTests</c>
/// prüft den <i>Renderer</i>, nicht den Bauer.</para>
///
/// <para><b>Warum das zählt.</b> Diese Mappe ist der Weg, auf dem der Nutzer
/// seine Lage einer eigenen KI vorlegt (<c>/ki?tab=mappe</c>, alt <c>/berater</c>) — die App selbst hat
/// keine. Ein Abschnitt, der als Überschrift ohne Inhalt hinausgeht, fällt
/// niemandem auf: die Datei ist da, sie hat einen Namen, sie sieht vollständig
/// aus. Beraten wird danach ohne die Regeln, ohne die Sollwerte oder ohne die
/// Symptome — und die Antwort klingt trotzdem sicher.</para>
///
/// <para>Geprüft wird über die <b>Liste, die der Bauer selbst liefert</b>: ein
/// neuer Abschnitt wird damit automatisch mitgeprüft, statt hier nachgetragen
/// werden zu müssen.</para>
/// </remarks>
public sealed class DieMappeFuerDieKiIstNichtLeerTests : IDisposable
{
    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly GrowRepository _grows;
    private readonly int _growId;

    public DieMappeFuerDieKiIstNichtLeerTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "KiMappe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        KopiereWissen();
        _pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(_pfade);
        _grows = new GrowRepository(_pfade);

        var zelt = _grows.CreateTent(new Tent { Name = "Zelt", TentType = TentType.Production });
        _growId = _grows.CreateGrow(new GrowRun
        {
            Name = "White Widow",
            TentId = zelt.Id,
            HydroStyle = HydroStyle.RDWC,
            SeedType = SeedType.Feminized,
            Status = GrowStatus.Running,
            StartDate = DateTime.Today.AddDays(-60),
            FlipDate = DateTime.Today.AddDays(-30),
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    /// <summary>Jeder Abschnitt trägt Inhalt, nicht nur eine Überschrift.</summary>
    [Fact]
    public void KeinAbschnitt_GehtLeerHinaus()
    {
        var mappe = Mappe();
        Assert.True(mappe is not null, "Die Mappe kam gar nicht zustande.");

        // Mengenwaechter: eine leere Mappe bestuende jede Pruefung darunter.
        Assert.True(mappe!.Files.Count >= 8,
            $"Die Mappe hat nur {mappe.Files.Count} Abschnitte — dann prueft der Rest nichts.");

        var duenn = mappe.Files
            .Where(datei => Inhaltslaenge(datei.Markdown) < 200)
            .Select(datei => $"{datei.Name}: {Inhaltslaenge(datei.Markdown)} Zeichen Inhalt")
            .ToList();

        Assert.True(duenn.Count == 0,
            "Diese Abschnitte gehen (fast) leer an die KI des Nutzers:\n  "
            + string.Join("\n  ", duenn)
            + "\n\nDie Datei ist da, sie hat einen Namen, sie sieht vollstaendig aus — beraten "
            + "wird danach ohne diesen Teil, und die Antwort klingt trotzdem sicher.");
    }

    /// <summary>Der Lagebericht nennt den Grow beim Namen.</summary>
    /// <remarks>
    /// Die Gegenprobe zum Mengenwächter oben: 200 Zeichen Fliesstext bekommt
    /// auch eine Vorlage ohne Daten zusammen. Wenn der Name des Grows nicht
    /// drinsteht, ist es nicht <i>seine</i> Lage.
    /// </remarks>
    [Fact]
    public void DerLagebericht_NenntDenGrow()
    {
        var lage = Mappe()!.Files.Single(d => d.Name.Contains("lagebericht"));

        Assert.Contains("White Widow", lage.Markdown);
    }

    /// <summary>Ohne Grow gibt es keine Mappe — und keine leere.</summary>
    /// <remarks>
    /// Eine Mappe voller Wissen ohne Lagebericht wäre schlimmer als keine: sie
    /// sieht vollständig aus und beschreibt niemanden.
    /// </remarks>
    [Fact]
    public void OhneGrow_GibtEsKeineMappe()
    {
        Assert.True(Bauer().Build(999999, DateTime.UtcNow) is null,
            "Fuer einen Grow, den es nicht gibt, kam eine Mappe zurueck.");
    }

    // ------------------------------------------------------------------ Hilfe

    /// <summary>Zeichen ohne Überschriften und Leerraum — der echte Inhalt.</summary>
    private static int Inhaltslaenge(string markdown)
        => string.Join(string.Empty, markdown
            .Split('\n')
            .Where(zeile => !zeile.TrimStart().StartsWith('#'))
            .Select(zeile => zeile.Trim()))
            .Length;

    private AgentPackage? Mappe() => Bauer().Build(_growId, DateTime.UtcNow);

    private AgentPackageBuilder Bauer()
    {
        var wissen = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        wissen.Initialize();

        return new AgentPackageBuilder(
            new AgentContextBuilder(
                _grows,
                new SensorReadingRepository(_pfade),
                new AlertRuleRepository(_pfade),
                new TargetValueService(wissen),
                new HydroSetupRepository(_pfade, new TentRepository(_pfade)),
                new SetpointProfileRepository(_pfade),
                new DosingRepository(_pfade),
                new JournalRepository(_pfade),
                new WaterProfileStore(new AppSettingsRepository(_pfade)),
                wissen),
            wissen);
    }

    private void KopiereWissen()
    {
        var quelle = Path.Combine(ProjektWurzel(), "GrowDiary.Web", "wwwroot", "knowledge-defaults");
        var ziel = Path.Combine(_wurzel, "wwwroot", "knowledge-defaults");
        foreach (var datei in Directory.EnumerateFiles(quelle, "*.json", SearchOption.AllDirectories))
        {
            var pfad = Path.Combine(ziel, Path.GetRelativePath(quelle, datei));
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.Copy(datei, pfad);
        }
    }

    private static string ProjektWurzel()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
