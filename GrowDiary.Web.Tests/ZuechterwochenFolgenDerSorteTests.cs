using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Die Züchterwochen eines Grows folgen der Sorte, solange das Grow keinen
/// eigenen Wert hat.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (16.10.2026).</b> Mimosa EVO stand in der Sorte auf 9–10
/// Wochen, im Grow auf 8–9: das Grow hatte die Wochen beim Anlegen kopiert und
/// nie nachgezogen. Der Finish-Beginn (Flip + Maximum × 7 − 14 Tage) lag dadurch
/// eine Woche zu früh, und mit ihm Plan-Woche, Chiller- und Entfeuchter-Werte.</para>
///
/// <para><b>Die Regel.</b> Eigener Wert nur, wenn er bewusst gesetzt ist; sonst gilt
/// die Sorte, beim Lesen eingesetzt. Gespeichert wird nur der eigene Wert — sonst
/// kopierte jedes Speichern die Sorte wieder ins Grow.</para>
/// </remarks>
public sealed class ZuechterwochenFolgenDerSorteTests : IDisposable
{
    private readonly string _temp;
    private readonly AppPaths _paths;
    private readonly GrowRepository _grows;
    private readonly SetupRepository _setups;
    private readonly int _zeltId;

    public ZuechterwochenFolgenDerSorteTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "Zuechterwochen_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _paths = new AppPaths(_temp);
        _zeltId = TestDatabase.InitializeWithDefaultTent(_paths).Id;
        _grows = new GrowRepository(_paths);
        _setups = new SetupRepository(_paths);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_temp, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    [Fact]
    public void FolgendesGrow_ZiehtDieSorteNach()
    {
        var sorte = _setups.CreateStrain(new Strain { Name = "Mimosa EVO", FlowerWeeksMin = 9, FlowerWeeksMax = 10 });
        var id = _grows.CreateGrow(NeuerGrow(sorte.Id, eigen: false));

        var vorher = _grows.GetGrow(id)!;
        Assert.Equal((9, 10), (vorher.BreederFlowerWeeksMin, vorher.BreederFlowerWeeksMax));

        sorte.FlowerWeeksMin = 10;
        sorte.FlowerWeeksMax = 11;
        _setups.UpdateStrain(sorte);

        var einzeln = _grows.GetGrow(id)!;
        Assert.Equal((10, 11), (einzeln.BreederFlowerWeeksMin, einzeln.BreederFlowerWeeksMax));
        Assert.False(einzeln.BreederWochenEigen);

        // Liste und Einzelabruf muessen dasselbe sagen — die Liste fuehrt sonst
        // gern Felder nicht (Liste ≠ Einzelabruf, 13.10.2026).
        var inListe = _grows.GetActiveGrows().Single(g => g.Id == id);
        Assert.Equal((10, 11), (inListe.BreederFlowerWeeksMin, inListe.BreederFlowerWeeksMax));
    }

    [Fact]
    public void EigenerWert_BleibtBeiAenderungDerSorte_UndZeigtDieSorte()
    {
        var sorte = _setups.CreateStrain(new Strain { Name = "Mimosa EVO", FlowerWeeksMin = 9, FlowerWeeksMax = 10 });
        var id = _grows.CreateGrow(NeuerGrow(sorte.Id, eigen: true, min: 8, max: 9));

        sorte.FlowerWeeksMax = 11;
        _setups.UpdateStrain(sorte);

        var grow = _grows.GetGrow(id)!;
        Assert.Equal((8, 9), (grow.BreederFlowerWeeksMin, grow.BreederFlowerWeeksMax));
        Assert.True(grow.BreederWochenEigen);
        Assert.Equal((9, 11), (grow.SortenWochenMin, grow.SortenWochenMax));
    }

    [Fact]
    public void Speichern_KopiertDieWochenDerSorteNichtInsGrow()
    {
        var sorte = _setups.CreateStrain(new Strain { Name = "Mimosa EVO", FlowerWeeksMin = 9, FlowerWeeksMax = 10 });
        var id = _grows.CreateGrow(NeuerGrow(sorte.Id, eigen: false));

        // Ein anderes Feld speichern: das geladene Grow traegt die wirksamen
        // Wochen der Sorte, geschrieben werden darf davon nichts.
        var geladen = _grows.GetGrow(id)!;
        geladen.Notes = "nur eine Notiz";
        _grows.UpdateGrow(geladen);

        Assert.Equal((null, null, 0L), RohWerte(id));

        sorte.FlowerWeeksMax = 12;
        _setups.UpdateStrain(sorte);
        Assert.Equal(12, _grows.GetGrow(id)!.BreederFlowerWeeksMax);
    }

    [Fact]
    public void OhneSorte_GehoertDerWertDemGrow()
    {
        var id = _grows.CreateGrow(NeuerGrow(strainId: null, eigen: false, min: 8, max: 9));

        var grow = _grows.GetGrow(id)!;
        Assert.True(grow.BreederWochenEigen);
        Assert.Equal((8, 9), (grow.BreederFlowerWeeksMin, grow.BreederFlowerWeeksMax));
    }

    [Fact]
    public void Autoflower_FolgtNieDerSorte()
    {
        var sorte = _setups.CreateStrain(new Strain { Name = "Auto", FlowerWeeksMin = 9, FlowerWeeksMax = 10 });
        var grow = NeuerGrow(sorte.Id, eigen: false);
        grow.SeedType = SeedType.Autoflower;
        var id = _grows.CreateGrow(grow);

        var gelesen = _grows.GetGrow(id)!;
        Assert.True(gelesen.BreederWochenEigen);
        Assert.Null(gelesen.BreederFlowerWeeksMax);
    }

    [Fact]
    public void SorteOhneAngabe_FaelltAufDenEigenenWertZurueck()
    {
        var sorte = _setups.CreateStrain(new Strain { Name = "Ohne Angabe" });
        var id = _grows.CreateGrow(NeuerGrow(sorte.Id, eigen: false));

        // Nichts zu folgen: kein erfundener Wert, schon gar keine Null-Ueberschreibung.
        Assert.Null(_grows.GetGrow(id)!.BreederFlowerWeeksMax);
    }

    [Fact]
    public void Umstellung_OrdnetVorhandeneGrowsEin_UndNurEinmal()
    {
        var sorte = _setups.CreateStrain(new Strain { Name = "Mimosa EVO", FlowerWeeksMin = 9, FlowerWeeksMax = 10 });
        var gleich = _grows.CreateGrow(NeuerGrow(sorte.Id, eigen: true, min: 9, max: 10));
        var weichtAb = _grows.CreateGrow(NeuerGrow(sorte.Id, eigen: true, min: 8, max: 9));
        var ohneSorte = _grows.CreateGrow(NeuerGrow(null, eigen: true, min: 8, max: 9));
        var autoflower = NeuerGrow(sorte.Id, eigen: true);
        autoflower.SeedType = SeedType.Autoflower;
        var auto = _grows.CreateGrow(autoflower);

        // Stand vor dem Update: die Spalte gibt es noch nicht.
        using (var verbindung = Oeffnen())
        {
            using var loeschen = verbindung.CreateCommand();
            loeschen.CommandText = "ALTER TABLE Grows DROP COLUMN BreederWochenEigen;";
            loeschen.ExecuteNonQuery();
        }
        new DatabaseInitializer(_paths, NullLogger<DatabaseInitializer>.Instance).Initialize();

        Assert.Equal((null, null, 0L), RohWerte(gleich));     // folgt der Sorte, Kopie geleert
        Assert.Equal((8L, 9L, 1L), RohWerte(weichtAb));       // Abweichung bleibt, nichts wird still ueberschrieben
        Assert.Equal((8L, 9L, 1L), RohWerte(ohneSorte));
        Assert.Equal(1L, RohWerte(auto).Eigen);

        // Zweiter Start: nichts wird erneut eingeordnet.
        var sorteNeu = _setups.GetStrain(sorte.Id)!;
        sorteNeu.FlowerWeeksMin = 8;
        sorteNeu.FlowerWeeksMax = 9;
        _setups.UpdateStrain(sorteNeu);
        new DatabaseInitializer(_paths, NullLogger<DatabaseInitializer>.Instance).Initialize();
        Assert.Equal((8L, 9L, 1L), RohWerte(weichtAb));       // blieb eigen, obwohl er jetzt der Sorte gleicht
    }

    [Theory]
    [InlineData(null, 9, 10, 9, 10, false)]   // aelterer Client, gleich der Sorte -> folgt
    [InlineData(null, 8, 9, 9, 10, true)]     // aelterer Client, weicht ab -> bewusst
    [InlineData(null, null, null, 9, 10, false)] // aelterer Client, leer -> folgt
    [InlineData(true, 9, 10, 9, 10, true)]    // ausdruecklich eigen
    [InlineData(false, 9, 10, 9, 10, false)]  // ausdruecklich folgen, Zahlen gleich der Sorte
    [InlineData(false, 8, 9, 9, 10, false)]   // ausdruecklich folgen: die Zahlen daneben zaehlen nicht (veraltetes Formular)
    [InlineData(false, null, null, 9, 10, false)] // ausdruecklich folgen, keine Zahlen
    public void Regel_LeitetEigenAb(bool? angabe, int? min, int? max, int sorteMin, int sorteMax, bool erwartet)
    {
        var grow = new GrowRun { StrainId = 1, BreederFlowerWeeksMin = min, BreederFlowerWeeksMax = max };
        var sorte = new Strain { Id = 1, FlowerWeeksMin = sorteMin, FlowerWeeksMax = sorteMax };
        Assert.Equal(erwartet, ZuechterwochenRegel.Eigen(angabe, grow, sorte));
    }

    [Fact]
    public void Regel_VeraltetesFormular_SchreibtKeineAlteKopieAlsEigenFest()
    {
        // Geladen, als die Sorte noch 8-9 hiess; inzwischen steht sie auf 9-10.
        var bisher = new GrowRun { StrainId = 1, BreederWochenEigen = false, BreederFlowerWeeksMin = 8, BreederFlowerWeeksMax = 9 };
        var geschickt = new GrowRun { StrainId = 1, BreederFlowerWeeksMin = 8, BreederFlowerWeeksMax = 9 };
        var sorte = new Strain { Id = 1, FlowerWeeksMin = 9, FlowerWeeksMax = 10 };

        Assert.False(ZuechterwochenRegel.Eigen(false, geschickt, sorte, bisher));
        Assert.False(ZuechterwochenRegel.Eigen(null, geschickt, sorte, bisher));

        // Gegenprobe: ausdruecklich eigen bleibt eigen, und ohne Vorgaenger
        // (Anlegen) sind abweichende Zahlen ohne Angabe bewusst gesetzt.
        Assert.True(ZuechterwochenRegel.Eigen(true, geschickt, sorte, bisher));
        Assert.True(ZuechterwochenRegel.Eigen(null, geschickt, sorte));
    }

    [Fact]
    public void Regel_FormularVorDerSortenAenderungGeladen_SchreibtKeineKopieFest()
    {
        // Der Fall, den die unabhaengige Pruefung gefunden hat: Formular offen mit
        // „folgt, 8-9"; in einem anderen Tab wird die Sorte auf 9-10 geaendert.
        // Beim Speichern liest der Server das Grow NEU — „bisher" traegt schon 9-10,
        // die geschickten 8-9 gleichen weder der Sorte noch „bisher".
        var bisher = new GrowRun { StrainId = 1, BreederWochenEigen = false, BreederFlowerWeeksMin = 9, BreederFlowerWeeksMax = 10 };
        var geschickt = new GrowRun { StrainId = 1, BreederFlowerWeeksMin = 8, BreederFlowerWeeksMax = 9 };
        var sorte = new Strain { Id = 1, FlowerWeeksMin = 9, FlowerWeeksMax = 10 };

        Assert.False(ZuechterwochenRegel.Eigen(false, geschickt, sorte, bisher));
    }

    [Fact]
    public void Regel_OhneSorteOderBeiAutoflower_IstImmerEigen()
    {
        var sorte = new Strain { Id = 1, FlowerWeeksMin = 9, FlowerWeeksMax = 10 };
        Assert.True(ZuechterwochenRegel.Eigen(false, new GrowRun { StrainId = null }, null));
        Assert.True(ZuechterwochenRegel.Eigen(false, new GrowRun { StrainId = 1 }, null)); // Sorte nicht auffindbar
        Assert.True(ZuechterwochenRegel.Eigen(false, new GrowRun { StrainId = 1, SeedType = SeedType.Autoflower }, sorte));
    }

    private GrowRun NeuerGrow(int? strainId, bool eigen, int? min = null, int? max = null) => new()
    {
        Name = "Testlauf",
        TentId = _zeltId,
        HydroStyle = HydroStyle.RDWC,
        StrainId = strainId,
        BreederFlowerWeeksMin = min,
        BreederFlowerWeeksMax = max,
        BreederWochenEigen = eigen,
        StartDate = DateTime.Today,
        Status = GrowStatus.Planning,
    };

    private SqliteConnection Oeffnen()
    {
        var verbindung = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath }.ToString());
        verbindung.Open();
        return verbindung;
    }

    /// <summary>Was wirklich in der Tabelle steht — nicht, was der Server daraus macht.</summary>
    private (long? Min, long? Max, long Eigen) RohWerte(int growId)
    {
        using var verbindung = Oeffnen();
        using var befehl = verbindung.CreateCommand();
        befehl.CommandText = "SELECT BreederFlowerWeeksMin, BreederFlowerWeeksMax, BreederWochenEigen FROM Grows WHERE Id = $id;";
        befehl.Parameters.AddWithValue("$id", growId);
        using var leser = befehl.ExecuteReader();
        Assert.True(leser.Read());
        return (leser.IsDBNull(0) ? null : leser.GetInt64(0), leser.IsDBNull(1) ? null : leser.GetInt64(1), leser.GetInt64(2));
    }
}
