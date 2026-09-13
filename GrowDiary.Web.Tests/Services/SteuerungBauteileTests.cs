using GrowDiary.Web.Models;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.45): Der Katalog beschreibt, was eine Steuerung in Home
/// Assistant braucht. Er speist Prüfen und später Anlegen aus derselben Quelle —
/// ein Fehler hier wird stumm zu einem Helfer, den niemand anlegt, oder zu einem
/// roten Punkt für etwas, das nie kommt.
/// </summary>
public class SteuerungBauteileTests
{
    private static readonly string[] KeineRolle = Array.Empty<string>();

    private static readonly string[] AlleRollen =
        SteuerungGeraeteRollen.FuerModul("co2").Select(r => r.Schluessel).ToArray();

    [Fact]
    public void JedesBauteilHatEineAufloesbareEntityId()
    {
        foreach (var b in SteuerungBauteile.Alle)
        {
            Assert.Contains('.', b.EntityId);
            Assert.False(string.IsNullOrWhiteSpace(b.Domaene), b.EntityId);
            Assert.False(string.IsNullOrWhiteSpace(b.Objektkennung), b.EntityId);
        }
    }

    [Fact]
    public void KeineEntityIdKommtZweimalVor()
    {
        var doppelt = SteuerungBauteile.Alle
            .GroupBy(b => b.EntityId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(doppelt);
    }

    [Fact]
    public void DieDomaenePasstZurArt()
    {
        var erwartet = new Dictionary<BauteilArt, string[]>
        {
            [BauteilArt.Zahl] = new[] { "input_number" },
            [BauteilArt.Schalter] = new[] { "input_boolean" },
            [BauteilArt.Zeitpunkt] = new[] { "input_datetime" },
            [BauteilArt.Zaehler] = new[] { "counter" },
            [BauteilArt.RechenSensor] = new[] { "sensor" },
            [BauteilArt.RechenSchalter] = new[] { "binary_sensor" },
            [BauteilArt.Automation] = new[] { "automation" },
        };

        foreach (var b in SteuerungBauteile.Alle)
        {
            Assert.Contains(b.Domaene, erwartet[b.Art]);
        }
    }

    [Fact]
    public void JedeZahlHatGrenzenUndSchritt()
    {
        foreach (var b in SteuerungBauteile.Alle.Where(x => x.Art == BauteilArt.Zahl))
        {
            Assert.True(b.Min.HasValue, b.EntityId);
            Assert.True(b.Max.HasValue, b.EntityId);
            Assert.True(b.Schritt is > 0, b.EntityId);
            Assert.True(b.Max > b.Min, b.EntityId);
        }
    }

    [Fact]
    public void OptionaleBauteileSagenWasAusfaellt()
    {
        // Ein roter Punkt ohne Erklaerung ist wertlos: wer keinen Luefter hat,
        // muss lesen koennen, was er dadurch verliert.
        foreach (var b in SteuerungBauteile.Alle.Where(x => !x.Pflicht))
        {
            Assert.False(string.IsNullOrWhiteSpace(b.OhneDas), b.EntityId);
        }
    }

    [Fact]
    public void PflichtBauteileHaengenAnKeinerOptionalenRolle()
    {
        // Sonst entsteht ein Bauteil, das als Pflicht gilt und trotzdem entfaellt
        // - die Seite meldete dann „eingerichtet" und „fehlt" zugleich.
        foreach (var b in SteuerungBauteile.Alle.Where(x => x.Pflicht))
        {
            Assert.Null(b.HaengtAn);
        }
    }

    [Fact]
    public void JedeGenannteRolleGibtEsAuch()
    {
        foreach (var b in SteuerungBauteile.Alle)
        {
            foreach (var rolle in b.HaengtAn ?? KeineRolle)
            {
                Assert.NotNull(SteuerungGeraeteRollen.Finden(b.Modul, rolle));
            }
        }
    }

    [Fact]
    public void OhneZugeordneteGeraeteBleibenNurDieUnabhaengigenBauteile()
    {
        var ohne = SteuerungBauteile.Anwendbar("co2", KeineRolle);
        var alle = SteuerungBauteile.FuerModul("co2");

        Assert.True(ohne.Count < alle.Count);
        Assert.All(ohne, b => Assert.Null(b.HaengtAn));

        // Der Waechter wird immer gebraucht - ohne ihn kann ein verschluckter
        // Abschaltbefehl das Ventil offen lassen.
        Assert.Contains(ohne, b => b.EntityId == "automation.co2_wachter_rdwc_port_5");
    }

    [Fact]
    public void MitAllenGeraetenIstNichtsAusgeschlossen()
    {
        Assert.Equal(
            SteuerungBauteile.FuerModul("co2").Count,
            SteuerungBauteile.Anwendbar("co2", AlleRollen).Count);
    }

    [Fact]
    public void DieAbluftHelferHaengenAmAbluftRegler()
    {
        var ohneAbluft = SteuerungBauteile.Anwendbar("co2", new[] { "co2_sensor", "licht", "port_zustand" })
            .Select(b => b.EntityId)
            .ToList();

        Assert.DoesNotContain("input_number.co2_t6_stufe_tief", ohneAbluft);
        Assert.DoesNotContain("automation.co2_abluft_drosselung_t6_rdwc_port_1", ohneAbluft);
        Assert.Contains("input_number.co2_hysterese", ohneAbluft);
    }
    // ------------------------------------------- Vorlagen der Rechenwerte

    [Fact]
    public void JederRechenwertBringtEineVorschriftMit()
    {
        // Ohne Vorschrift laesst sich ein Template-Helfer nicht anlegen - er
        // waere eine leere Huelle, die stumm nichts liefert.
        foreach (var b in SteuerungBauteile.Alle
                     .Where(x => x.Art is BauteilArt.RechenSensor or BauteilArt.RechenSchalter))
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Vorlage), b.EntityId);
        }
    }

    [Fact]
    public void JederPlatzhalterMeintEineEchteRolle()
    {
        foreach (var b in SteuerungBauteile.Alle.Where(x => x.Vorlage is not null))
        {
            foreach (var rolle in SteuerungBauteile.PlatzhalterIn(b.Vorlage!))
            {
                Assert.NotNull(SteuerungGeraeteRollen.Finden(b.Modul, rolle));
            }
        }
    }

    [Fact]
    public void EineVorlageHaengtAnDenRollenIhrerPlatzhalter()
    {
        // Sonst wird ein Rechenwert erwartet, dessen Geraet gar nicht da ist -
        // und beim Anlegen faellt er dann ueber einen leeren Platzhalter.
        foreach (var b in SteuerungBauteile.Alle.Where(x => x.Vorlage is not null))
        {
            var platzhalter = SteuerungBauteile.PlatzhalterIn(b.Vorlage!);
            if (platzhalter.Count == 0) continue;

            var haengtAn = b.HaengtAn ?? Array.Empty<string>();
            foreach (var rolle in platzhalter)
            {
                var rollePflicht = SteuerungGeraeteRollen.Finden(b.Modul, rolle)?.Pflicht ?? false;
                Assert.True(rollePflicht || haengtAn.Contains(rolle),
                    $"{b.EntityId} nutzt [[{rolle}]], haengt aber nicht daran.");
            }
        }
    }

    [Fact]
    public void GefuellteVorlageTraegtKeinenPlatzhalterMehr()
    {
        var zuordnung = SteuerungGeraeteRollen.FuerModul("co2")
            .ToDictionary(r => r.Schluessel, r => $"sensor.probe_{r.Schluessel}", StringComparer.Ordinal);

        foreach (var b in SteuerungBauteile.Alle.Where(x => x.Vorlage is not null))
        {
            var fertig = SteuerungBauteile.VorlageFuellen(b.Vorlage!, zuordnung);
            Assert.NotNull(fertig);
            Assert.DoesNotContain("[[", fertig);
        }
    }

    [Fact]
    public void OhneZuordnungBleibtDieVorlageUngefuellt()
    {
        // Null statt einer Vorschrift mit stehendem Platzhalter: die wuerde
        // nicht ungueltig, sondern stumm mit einem Ausweichwert weiterrechnen.
        var mitPlatzhalter = SteuerungBauteile.Alle
            .First(b => b.Vorlage is not null && SteuerungBauteile.PlatzhalterIn(b.Vorlage).Count > 0);

        Assert.Null(SteuerungBauteile.VorlageFuellen(
            mitPlatzhalter.Vorlage!, new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void DerPlatzhalterPasstAnBeidenStellen()
    {
        // states('[[x]]') liefert den Wert, states.[[x]] das Objekt mit
        // last_changed - beides faellt aus derselben Ersetzung.
        var zuordnung = new Dictionary<string, string>(StringComparer.Ordinal) { ["canopy"] = "sensor.blatt" };
        var fertig = SteuerungBauteile.VorlageFuellen(
            "{{ states('[[canopy]]') }} {{ states.[[canopy]].last_changed }}", zuordnung);

        Assert.Equal("{{ states('sensor.blatt') }} {{ states.sensor.blatt.last_changed }}", fertig);
    }

}
