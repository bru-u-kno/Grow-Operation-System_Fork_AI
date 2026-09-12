using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.58): Was der Anlege-Dienst an Home Assistant schickt.
/// </summary>
/// <remarks>
/// Die Befehle selbst lassen sich ohne laufendes Home Assistant nicht fahren.
/// Prüfbar ist aber das, was dabei am ehesten falsch wird: der richtige Befehl
/// zur Art, und Felder, aus denen genau die Entität fällt, die der Katalog
/// erwartet. Ein Tippfehler im Namen erzeugt sonst stumm einen Helfer, den
/// hinterher niemand findet.
/// </remarks>
public class SteuerungHelferServiceTests
{
    private static Bauteil Suche(string entityId)
        => SteuerungBauteile.Alle.Single(b => b.EntityId == entityId);

    [Fact]
    public void JedeArtBekommtIhrenEigenenBefehl()
    {
        Assert.Equal("input_number/create", SteuerungHelferService.Befehl(Suche("input_number.co2_hysterese")));
        Assert.Equal("input_boolean/create", SteuerungHelferService.Befehl(Suche("input_boolean.co2_autokalibrierung")));
        Assert.Equal("counter/create", SteuerungHelferService.Befehl(Suche("counter.co2_impulse_heute")));
        Assert.Equal("input_datetime/create", SteuerungHelferService.Befehl(Suche("input_datetime.co2_letzter_impuls")));
    }

    [Fact]
    public void RechenSensorenUndAutomationenLehntErAb()
    {
        // Sie brauchen andere Wege. Ein stiller Fehlschlag waere schlimmer als
        // ein lautes Nein - deshalb wirft der Dienst, statt etwas zu raten.
        Assert.False(SteuerungHelferService.Kann(BauteilArt.RechenSensor));
        Assert.False(SteuerungHelferService.Kann(BauteilArt.Automation));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SteuerungHelferService.Befehl(Suche("sensor.co2_impuls_bedarf")));
    }

    [Fact]
    public void AusDemNamenFaelltDieErwarteteEntityId()
    {
        // Home Assistant leitet die Objektkennung aus dem Namen ab: klein,
        // Sonderzeichen zu Unterstrichen. Passt das nicht, entsteht ein Helfer
        // unter einer Id, die der Katalog nie findet - und beim naechsten Lauf
        // wird er ein zweites Mal angelegt.
        foreach (var b in SteuerungBauteile.Alle.Where(x => SteuerungHelferService.Kann(x.Art)))
        {
            var felder = SteuerungHelferService.Felder(b);
            var name = Assert.IsType<string>(felder["name"]);
            var abgeleitet = new string(name.ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray());

            Assert.Equal(b.Objektkennung, abgeleitet);
        }
    }

    [Fact]
    public void EineZahlBringtGrenzenSchrittUndEinheitMit()
    {
        var felder = SteuerungHelferService.Felder(Suche("input_number.co2_hysterese"));

        Assert.Equal(20d, felder["min"]);
        Assert.Equal(300d, felder["max"]);
        Assert.Equal(10d, felder["step"]);
        Assert.Equal("ppm", felder["unit_of_measurement"]);
        Assert.Equal("box", felder["mode"]);
    }

    [Fact]
    public void EineZahlOhneEinheitBringtKeinLeeresFeldMit()
    {
        var felder = SteuerungHelferService.Felder(Suche("input_number.co2_max_impulse_je_zyklus"));
        Assert.False(felder.ContainsKey("unit_of_measurement"));
    }

    [Fact]
    public void EinZeitstempelTraegtDatumUndUhrzeit()
    {
        var felder = SteuerungHelferService.Felder(Suche("input_datetime.co2_letzter_impuls"));

        Assert.Equal(true, felder["has_date"]);
        Assert.Equal(true, felder["has_time"]);
    }

    [Fact]
    public void EinZaehlerFaengtBeiNullAn()
    {
        var felder = SteuerungHelferService.Felder(Suche("counter.co2_impulse_heute"));

        Assert.Equal(0, felder["initial"]);
        Assert.Equal(0, felder["minimum"]);
        Assert.Equal(1, felder["step"]);
    }

    [Fact]
    public void EinSchalterBringtNurSeinenNamenMit()
    {
        var felder = SteuerungHelferService.Felder(Suche("input_boolean.co2_autokalibrierung"));
        Assert.Single(felder);
        Assert.True(felder.ContainsKey("name"));
    }

    [Fact]
    public void ZahlenGehenMitPunktUeberDieLeitung()
    {
        // Auf einem deutschen System waere 0,001 ein Komma - Home Assistant
        // liest das als Fehler.
        Assert.Equal("0.001", SteuerungHelferService.Zahl(0.001));
        Assert.Equal("29.5", SteuerungHelferService.Zahl(29.5));
    }
}
