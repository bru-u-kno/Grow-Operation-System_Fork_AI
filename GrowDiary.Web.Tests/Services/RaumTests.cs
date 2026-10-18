using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.241): Der Raum ist eine eigene Einheit. Die Kellerfühler der Zuluft gehören jetzt dem Raum — die
/// Zuordnung eines Bestands darf dabei weder verloren gehen noch doppelt auftauchen, und die Zuluft muss dieselben
/// Fühler weiter sehen.
/// </summary>
public class RaumTests
{
    private static SqliteConnection Tabelle()
    {
        var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE ForkSteuerungGeraete (Modul TEXT NOT NULL, Rolle TEXT NOT NULL, EntityId TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL, PRIMARY KEY (Modul, Rolle));
            INSERT INTO ForkSteuerungGeraete VALUES
                ('zuluft','keller_temp','sensor.a_temp','t1'), ('zuluft','keller_rh','sensor.a_rh','t1'),
                ('zuluft','aussen_temp','sensor.aussen','t1');
            """;
        cmd.ExecuteNonQuery();
        return c;
    }

    private static List<string> Zeilen(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Modul || '/' || Rolle || '=' || EntityId FROM ForkSteuerungGeraete ORDER BY 1";
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    [Fact]
    public void Kellerfuehler_wandern_in_den_Raum_und_ein_zweiter_Lauf_aendert_nichts()
    {
        using var c = Tabelle();
        SteuerungRepository.KellerfuehlerInRaumUebernehmen(c);
        var nach1 = Zeilen(c);
        Assert.Equal(["raum/raum_rh=sensor.a_rh", "raum/raum_temp=sensor.a_temp", "zuluft/aussen_temp=sensor.aussen"], nach1);

        SteuerungRepository.KellerfuehlerInRaumUebernehmen(c);
        Assert.Equal(nach1, Zeilen(c));
    }

    [Fact]
    public void Eine_vorhandene_Raum_Zeile_gewinnt_und_ein_geleertes_Feld_kommt_nicht_zurueck()
    {
        using var c = Tabelle();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO ForkSteuerungGeraete VALUES ('raum','raum_temp','sensor.neu','t2')";
            cmd.ExecuteNonQuery();
        }
        SteuerungRepository.KellerfuehlerInRaumUebernehmen(c);
        Assert.Contains("raum/raum_temp=sensor.neu", Zeilen(c));

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM ForkSteuerungGeraete WHERE Modul='raum' AND Rolle='raum_rh'";
            cmd.ExecuteNonQuery();
        }
        SteuerungRepository.KellerfuehlerInRaumUebernehmen(c);
        Assert.DoesNotContain(Zeilen(c), z => z.StartsWith("raum/raum_rh"));
    }

    [Fact]
    public void Die_Zuluft_benutzt_die_Raum_Fuehler_mit_und_hat_keine_eigenen_mehr()
    {
        Assert.Equal([(SteuerungGeraeteRollen.RaumModul, "raum_temp"), (SteuerungGeraeteRollen.RaumModul, "raum_rh")],
            SteuerungGeraeteRollen.MitbenutztVon("zuluft"));
        Assert.DoesNotContain(SteuerungGeraeteRollen.FuerModul("zuluft"), r => r.Schluessel.StartsWith("keller_"));
        Assert.Contains(SteuerungGeraeteRollen.FuerModulMitMitbenutzten("zuluft"), r => r.Schluessel == "raum_temp");
        Assert.Equal(2, SteuerungGeraeteRollen.FuerModul("raum").Count);
    }

    [Theory]
    [InlineData("haupt", "haupt")]
    [InlineData(" HAUPT ", "haupt")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("irgendwas", null)]
    public void Ein_Zelt_zeigt_nur_auf_den_bekannten_Raum_oder_auf_keinen(string? roh, string? erwartet)
        => Assert.Equal(erwartet, RaumService.RaumIdNormal(roh));
}
