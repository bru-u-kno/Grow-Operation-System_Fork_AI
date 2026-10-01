using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Tests.Infrastructure;

/// <summary>
/// forkai.157: Die Fork-Tabellen von Geräten und Kosten entstehen in JEDER
/// Datenbankdatei, und ältere Tabellen bekommen fehlende Spalten nachgerüstet.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Beide Repositories merkten sich „Tabellen angelegt"
/// für den ganzen Prozess. Die zweite Datenbank blieb ohne Tabellen — nachgestellt
/// mit „no such table: ForkGeraete". Jetzt führt <see cref="SchemaWaechter"/>
/// den Merker je Datei. Die Wiederherstellung einer Sicherung fährt
/// <c>SystemApiControllerTests.RestoreBackup_ForkTabellenEntstehenNachDemTauschNeu</c>.</para>
///
/// <para><b>Dazu der Rundweg der Geräte-Korrekturen.</b> Speichern, Verwerfen und
/// Zuordnen hatten keinen einzigen Test; nur ein Ausnahme-Eintrag im Rundweg-Zähler
/// erwähnte sie.</para>
/// </remarks>
public sealed class ForkTabellenTests : IDisposable
{
    private readonly List<string> _ordner = [];

    private AppPaths NeueDatenbank()
    {
        var wurzel = Path.Combine(Path.GetTempPath(), "ForkTabellen_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(wurzel);
        _ordner.Add(wurzel);
        var pfade = new AppPaths(wurzel);
        TestDatabase.Initialize(pfade);
        return pfade;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var wurzel in _ordner)
        {
            try { Directory.Delete(wurzel, recursive: true); } catch { /* Aufräumen nach bestem Bemühen */ }
        }
    }

    [Fact]
    public void ZweiDatenbankenImSelbenProzessHabenBeideIhreTabellen()
    {
        var erste = NeueDatenbank();
        var zweite = NeueDatenbank();

        Assert.Empty(new GeraeteRepository(erste).Geraete());
        Assert.Empty(new KostenRepository(erste).GetAnschaffungen());

        // Vorher: „no such table: ForkGeraete" — der Merker galt schon als gesetzt.
        Assert.Empty(new GeraeteRepository(zweite).Geraete());
        Assert.Empty(new KostenRepository(zweite).GetAnschaffungen());
    }

    [Fact]
    public void GeraetSpeichernNochmalSpeichernUndVerwerfen()
    {
        var repo = new GeraeteRepository(NeueDatenbank());
        var geraet = new GespeichertesGeraet
        {
            Schluessel = "ha:pumpe1",
            Name = "Umwälzpumpe",
            TentId = 3,
            HardwareItemId = 7,
            ElternSchluessel = "ha:steckdose",
            Anschluss = "Port 2",
            IstRubrik = true,
        };

        repo.GeraetSpeichern(geraet);
        repo.EntitaetZuordnen("switch.pumpe1", "ha:pumpe1");
        var gelesen = repo.Geraete()["ha:pumpe1"];
        Assert.Equal("Umwälzpumpe", gelesen.Name);
        Assert.Equal(3, gelesen.TentId);
        Assert.Equal(7, gelesen.HardwareItemId);
        Assert.Equal("ha:steckdose", gelesen.ElternSchluessel);
        Assert.Equal("Port 2", gelesen.Anschluss);
        Assert.True(gelesen.IstRubrik);

        // Nochmal speichern — der Fall „schon vorhanden" (ON CONFLICT), mit
        // geleerten Feldern: sie müssen leer zurückkommen, nicht die alten.
        repo.GeraetSpeichern(new GespeichertesGeraet { Schluessel = "ha:pumpe1", Name = "Pumpe neu" });
        var zweimal = repo.Geraete()["ha:pumpe1"];
        Assert.Equal("Pumpe neu", zweimal.Name);
        Assert.Null(zweimal.TentId);
        Assert.Null(zweimal.HardwareItemId);
        Assert.Null(zweimal.ElternSchluessel);
        Assert.False(zweimal.IstRubrik);
        Assert.Single(repo.Geraete());

        // Verwerfen nimmt die zugeschlagene Entität mit.
        repo.GeraetVerwerfen("ha:pumpe1");
        Assert.Empty(repo.Geraete());
        Assert.Empty(repo.Zuordnungen());
    }

    [Fact]
    public void EntitaetUmhaengenUndLoesen()
    {
        var repo = new GeraeteRepository(NeueDatenbank());

        repo.EntitaetZuordnen("sensor.ph", "ha:a");
        repo.EntitaetZuordnen("sensor.ph", "ha:b"); // umhängen, keine zweite Zeile
        Assert.Equal("ha:b", Assert.Single(repo.Zuordnungen()).Value);

        repo.EntitaetZuordnen("sensor.ph", "  "); // leer löst
        Assert.Empty(repo.Zuordnungen());
    }

    [Fact]
    public void AlteGeraeteTabelleBekommtIstRubrikNachgeruestet()
    {
        var pfade = NeueDatenbank();
        // Die Fassung vor forkai.27 — ohne IstRubrik.
        Ausfuehren(pfade, """
            CREATE TABLE ForkGeraete (
                Schluessel TEXT PRIMARY KEY, Name TEXT NOT NULL, TentId INTEGER NULL,
                HardwareItemId INTEGER NULL, ElternSchluessel TEXT NULL, Anschluss TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL);
            INSERT INTO ForkGeraete (Schluessel, Name, UpdatedAtUtc) VALUES ('ha:alt', 'Altes Gerät', '2026-09-01T00:00:00Z');
            """);

        var gelesen = new GeraeteRepository(pfade).Geraete()["ha:alt"];

        Assert.Equal("Altes Gerät", gelesen.Name);
        Assert.False(gelesen.IstRubrik);
    }

    [Fact]
    public void AlteAnschaffungstabelleBekommtDieSpaltenAusForkai157()
    {
        var pfade = NeueDatenbank();
        // Die Fassung bis forkai.156 — ohne Nutzungsdauer, Zelt, Ausmusterung.
        Ausfuehren(pfade, """
            CREATE TABLE ForkAnschaffungen (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Hersteller TEXT NULL,
                Produkt TEXT NULL, DatumUtc TEXT NOT NULL, Stueck INTEGER NOT NULL DEFAULT 1,
                EinzelpreisEur REAL NOT NULL DEFAULT 0, GrowId INTEGER NULL, Notiz TEXT NULL,
                HardwareItemId INTEGER NULL, CreatedAtUtc TEXT NOT NULL);
            INSERT INTO ForkAnschaffungen (Name, DatumUtc, EinzelpreisEur, GrowId, CreatedAtUtc)
                VALUES ('Schere', '2026-09-01T10:00:00Z', 4.9, 1, '2026-09-01T10:00:00Z');
            """);

        var alt = Assert.Single(new KostenRepository(pfade).GetAnschaffungen());

        Assert.Equal("Schere", alt.Name);
        Assert.Null(alt.NutzungsdauerMonate); // bleibt einmalig, wie vorher
        Assert.Null(alt.TentId);
        Assert.Null(alt.AusgemustertAmUtc);
        Assert.Equal(1, alt.GrowId);
    }

    /// <summary>
    /// ZÄHLUNG: Jede Datei, die eine Fork-Tabelle anlegt, tut das über den
    /// <see cref="SchemaWaechter"/> — sonst überlebt ihr eigener Merker die
    /// Wiederherstellung einer Sicherung, und ihre Tabellen fehlen bis zum Neustart.
    /// </summary>
    /// <remarks>
    /// So entstand der Fehler: fünf Repositories, fünf eigene Merker, zwei davon
    /// je Prozess statt je Datei, keiner kannte die Wiederherstellung. Gezählt wird
    /// über die Grundmenge (jede .cs-Datei unter GrowDiary.Web), ohne Kommentare —
    /// ein Name in der Doku ist kein Aufruf.
    /// </remarks>
    [Fact]
    public void JedeForkTabelleEntstehtUeberDenSchemaWaechter()
    {
        var wurzel = Path.Combine(Projektwurzel(), "GrowDiary.Web");
        var anlegende = new List<string>();
        var ohneWaechter = new List<string>();

        foreach (var datei in Directory.EnumerateFiles(wurzel, "*.cs", SearchOption.AllDirectories))
        {
            if (datei.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || datei.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var code = OhneKommentare(File.ReadAllText(datei));
            if (!code.Contains("CREATE TABLE IF NOT EXISTS Fork", StringComparison.Ordinal)) continue;

            var name = Path.GetRelativePath(wurzel, datei);
            anlegende.Add(name);
            if (!code.Contains("SchemaWaechter.Sichern(", StringComparison.Ordinal)) ohneWaechter.Add(name);
        }

        // Mengenwächter: Kosten, Geräte, Steuerung, Grow-Plan, Wochenwerte.
        Assert.True(anlegende.Count >= 5, $"Nur {anlegende.Count} Dateien legen Fork-Tabellen an — die Zählung sieht ihre Grundmenge nicht.");
        Assert.True(ohneWaechter.Count == 0,
            "Diese Dateien legen Fork-Tabellen ohne SchemaWaechter an — nach einer Wiederherstellung fehlen ihre Tabellen bis zum Neustart: "
            + string.Join(", ", ohneWaechter));
    }

    private static string OhneKommentare(string code)
    {
        code = System.Text.RegularExpressions.Regex.Replace(code, @"/\*[\s\S]*?\*/", string.Empty);
        return System.Text.RegularExpressions.Regex.Replace(code, @"//.*$", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
    }

    private static string Projektwurzel()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }

    private static void Ausfuehren(AppPaths pfade, string sql)
    {
        using var verbindung = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = pfade.DatabasePath }.ToString());
        verbindung.Open();
        using var befehl = verbindung.CreateCommand();
        befehl.CommandText = sql;
        befehl.ExecuteNonQuery();
    }
}
