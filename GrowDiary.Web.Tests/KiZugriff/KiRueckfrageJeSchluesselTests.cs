using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-005, 03.10.2026): Die Rückfrage steht je Schlüssel — die Übernahme
/// der alten globalen Regel und das Zurückspielen einer Sicherung.
/// </summary>
/// <remarks>
/// Gebaut wird die Tabelle so, wie forkai.163 sie angelegt hat (ohne
/// <c>Rueckfrage</c>), mit der alten Einstellung <c>ki-zugriff.rueckfrage-ab-stufe</c>.
/// </remarks>
public sealed class KiRueckfrageJeSchluesselTests : IDisposable
{
    private const string AlteEinstellung = "ki-zugriff.rueckfrage-ab-stufe";

    /// <summary>Wörtlich das Schema aus forkai.163 (KiSchluesselRepository.SchemaSql dieser Fassung).</summary>
    private const string SchemaForkai163 = """
        CREATE TABLE IF NOT EXISTS ForkKiSchluessel (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Praefix TEXT NOT NULL,
            Hash TEXT NOT NULL,
            Stufen INTEGER NOT NULL DEFAULT 0,
            ErstelltAmUtc TEXT NOT NULL,
            ZuletztGenutztAmUtc TEXT NULL,
            GesperrtAmUtc TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_ForkKiSchluessel_Praefix ON ForkKiSchluessel (Praefix);
        """;

    private readonly List<string> _ordner = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var ordner in _ordner)
        {
            try { Directory.Delete(ordner, recursive: true); } catch { }
        }
    }

    private AppPaths NeueDatenbank()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "KiRueckfrage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ordner);
        _ordner.Add(ordner);
        var pfade = new AppPaths(ordner);
        TestDatabase.Initialize(pfade);
        return pfade;
    }

    /// <summary>Eine Datenbank wie aus forkai.163: alte Tabelle, drei Schlüssel, die alte Einstellung (null = Eintrag fehlt).</summary>
    private AppPaths AlteDatenbank(string? alteRegel)
    {
        var pfade = NeueDatenbank();
        using var verbindung = Oeffnen(pfade);
        Ausfuehren(verbindung, SchemaForkai163);
        Ausfuehren(verbindung, """
            INSERT INTO ForkKiSchluessel (Name, Praefix, Hash, Stufen, ErstelltAmUtc) VALUES
              ('Alles ausser Verwaltung', 'aaaaaaaa', 'aa', 7, '2026-10-01T08:00:00Z'),
              ('Nur Doku', 'bbbbbbbb', 'bb', 1, '2026-10-01T08:00:00Z'),
              ('Nur Verwaltung', 'cccccccc', 'cc', 8, '2026-10-01T08:00:00Z');
            """);
        if (alteRegel is not null) new AppSettingsRepository(pfade).SetValue(AlteEinstellung, alteRegel);
        return pfade;
    }

    private static SqliteConnection Oeffnen(AppPaths pfade)
    {
        var verbindung = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = pfade.DatabasePath, Pooling = false }.ToString());
        verbindung.Open();
        return verbindung;
    }

    private static void Ausfuehren(SqliteConnection verbindung, string sql)
    {
        using var befehl = verbindung.CreateCommand();
        befehl.CommandText = sql;
        befehl.ExecuteNonQuery();
    }

    private static Dictionary<string, KiStufe> RueckfrageJeName(AppPaths pfade)
        => new KiSchluesselRepository(pfade).Alle().ToDictionary(s => s.Name, s => s.Rueckfrage);

    // ------------------------------------------------------------ Übernahme

    public static TheoryData<string?, KiStufe, KiStufe, KiStufe> AlteRegeln => new()
    {
        // alte Regel → Rückfrage von (7 = Doku|Planen|Schalten), (1 = Doku), (8 = Verwaltung)
        { "GeraeteSchalten", KiStufe.GeraeteSchalten, KiStufe.Keine, KiStufe.Verwaltung },
        { "Dokumentieren", KiStufe.Dokumentieren | KiStufe.GrowPlanen | KiStufe.GeraeteSchalten, KiStufe.Dokumentieren, KiStufe.Verwaltung },
        { "Verwaltung", KiStufe.Keine, KiStufe.Keine, KiStufe.Verwaltung },
        // „nie" stand als leerer Eintrag da.
        { "", KiStufe.Keine, KiStufe.Keine, KiStufe.Keine },
        // Eintrag fehlt: forkai.163 nahm die Vorbelegung „ab Grow planen" — und sagte das dem Assistenten.
        { null, KiStufe.GrowPlanen | KiStufe.GeraeteSchalten, KiStufe.Keine, KiStufe.Verwaltung },
        // Unbekannt: wie forkai.163 — die Vorbelegung.
        { "Quatsch", KiStufe.GrowPlanen | KiStufe.GeraeteSchalten, KiStufe.Keine, KiStufe.Verwaltung },
    };

    [Theory]
    [MemberData(nameof(AlteRegeln))]
    public void DieAlteRegelWirdJeSchluesselUebernommen(string? alteRegel, KiStufe alles, KiStufe doku, KiStufe verwaltung)
    {
        var pfade = AlteDatenbank(alteRegel);

        var rueckfrage = RueckfrageJeName(pfade);

        Assert.Equal(3, rueckfrage.Count);
        Assert.Equal(alles, rueckfrage["Alles ausser Verwaltung"]);
        Assert.Equal(doku, rueckfrage["Nur Doku"]);
        Assert.Equal(verwaltung, rueckfrage["Nur Verwaltung"]);
        // Die alte Einstellung ist danach weg.
        Assert.Null(new AppSettingsRepository(pfade).GetValue(AlteEinstellung));
    }

    [Fact]
    public void DieUebernahmeLaeuftNurEinmal()
    {
        var pfade = AlteDatenbank("GeraeteSchalten");
        var repository = new KiSchluesselRepository(pfade);
        var schluessel = repository.Alle().Single(s => s.Name == "Alles ausser Verwaltung");
        Assert.Equal(KiStufe.GeraeteSchalten, schluessel.Rueckfrage);

        // Der Betreiber stellt um; danach taucht die alte Einstellung wieder auf
        // (etwa durch ein Werkzeug von aussen) und der Merker wird vergessen wie
        // nach einem Zurückspielen.
        repository.Aendern(schluessel.Id, schluessel.Name, schluessel.Stufen, KiStufe.Keine);
        new AppSettingsRepository(pfade).SetValue(AlteEinstellung, "Dokumentieren");
        EigenesSchema.Vergessen(pfade.DatabasePath);

        var danach = RueckfrageJeName(pfade);
        Assert.Equal(KiStufe.Keine, danach["Alles ausser Verwaltung"]);
        Assert.Equal(KiStufe.Keine, danach["Nur Doku"]);
        // Die Spalte war schon da: die Einstellung wird nicht gelesen, nur aufgeräumt.
        Assert.Null(new AppSettingsRepository(pfade).GetValue(AlteEinstellung));
    }

    [Fact]
    public void EineNeueDatenbankBekommtDieSpalteGleich()
    {
        var pfade = NeueDatenbank();
        var repository = new KiSchluesselRepository(pfade);
        var id = repository.Anlegen("Neu", "dddddddd", "dd", KiStufe.Dokumentieren | KiStufe.GrowPlanen, KiStufe.GrowPlanen, DateTime.UtcNow);

        Assert.Equal(KiStufe.GrowPlanen, repository.Hole(id)!.Rueckfrage);
    }

    // ---------------------------------------------------------- Zurückspielen

    [Fact]
    public void ZurueckspielenKopiertJedeSpalte_AuchDieRueckfrage()
    {
        var jetzt = NeueDatenbank();
        var repository = new KiSchluesselRepository(jetzt);
        var id = repository.Anlegen("Claude", "eeeeeeee", "ee", KiStufe.Dokumentieren | KiStufe.GeraeteSchalten, KiStufe.GeraeteSchalten, DateTime.UtcNow);
        repository.ZuletztGenutzt(id, DateTime.UtcNow);

        var zustand = KiZustandBeimZurueckspielen.Lesen(jetzt.DatabasePath);

        // Zählprüfung: der Zustand trägt JEDE Spalte der Tabelle — kommt eine neue
        // dazu und jemand baut wieder eine feste Liste, wird das hier rot.
        var spalten = Spalten(jetzt);
        Assert.True(spalten.Count >= 9, $"Nur {spalten.Count} Spalten gesehen — prüft der Fall noch etwas?");
        Assert.Contains("Rueckfrage", spalten);
        Assert.Equal(spalten, zustand.Spalten);
        Assert.Single(zustand.Schluessel);

        // In eine Sicherung aus forkai.163 schreiben: die Spalte fehlt dort.
        var alt = AlteDatenbank("Dokumentieren");
        SqliteConnection.ClearAllPools();
        KiZustandBeimZurueckspielen.Schreiben(alt.DatabasePath, zustand);
        EigenesSchema.Vergessen(alt.DatabasePath);

        var zurueck = new KiSchluesselRepository(alt).Alle();
        var claude = Assert.Single(zurueck);
        Assert.Equal("Claude", claude.Name);
        Assert.Equal(KiStufe.Dokumentieren | KiStufe.GeraeteSchalten, claude.Stufen);
        Assert.Equal(KiStufe.GeraeteSchalten, claude.Rueckfrage);
        Assert.NotNull(claude.ZuletztGenutztAmUtc);
        // Die alte Regel aus der Sicherung lebt nicht wieder auf.
        Assert.Null(new AppSettingsRepository(alt).GetValue(AlteEinstellung));
    }

    private static List<string> Spalten(AppPaths pfade)
    {
        using var verbindung = Oeffnen(pfade);
        using var befehl = verbindung.CreateCommand();
        befehl.CommandText = "SELECT name FROM pragma_table_info('ForkKiSchluessel') ORDER BY cid;";
        using var leser = befehl.ExecuteReader();
        var namen = new List<string>();
        while (leser.Read()) namen.Add(leser.GetString(0));
        return namen;
    }
}
