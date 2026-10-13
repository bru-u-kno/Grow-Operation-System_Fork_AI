using System.Text.RegularExpressions;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.217): Die Objekte des Forks heißen in einer frischen Installation neutral
/// (<c>fork_entfeuchter_*</c>); eine Installation, die schon mit den alten Kennungen lief, behält sie — Zeichen für
/// Zeichen. Beide Hälften müssen stimmen: Wer neu beginnt, bekommt nichts mit fremder Marke im Namen, und wer schon
/// eine laufende Regelung hat, verliert keinen Helfer, bekommt keinen zweiten und sieht nichts überschrieben.
/// </summary>
public class KennungsRaumTests
{
    /// <summary>
    /// Der eingefrorene Stand VOR der Umstellung (forkai.216): genau diese Kennungen müssen im Bestand herauskommen.
    /// Bewusst ausgeschrieben und nicht aus der Tabelle abgeleitet — sonst prüfte die Tabelle sich selbst.
    /// </summary>
    private static readonly string[] AlterStand =
    {
        "input_boolean.trotec_vpd_regelung",
        "input_number.trotec_hysterese",
        "input_number.trotec_mindestlaufzeit",
        "input_number.trotec_einschaltverzoegerung",
        "input_number.trotec_wartezeit_aussenluft",
        "input_boolean.trotec_tagbetrieb_erlauben",
        "input_number.trotec_temp_max_tag",
        "input_number.trotec_temp_max",
        "input_number.trotec_feuchte_ein_tag",
        "input_number.trotec_feuchte_aus_tag",
        "input_number.trotec_feuchte_ein",
        "input_number.trotec_feuchte_aus",
        "sensor.trotec_feuchte_ein_aktiv",
        "sensor.trotec_feuchte_aus_aktiv",
        "sensor.trotec_temp_max_aktiv",
        "binary_sensor.trotec_feuchte_uber_ein",
        "automation.rdwc_trotec_nachtregelung_port_7_dehumi",
        "input_number.trotec_zelt_vpd_hysterese",
        "input_number.trotec_zelt_mindestlaufzeit",
        "input_number.trotec_zelt_mindestpause",
        "input_number.trotec_zelt_folge_abstand",
        "input_number.trotec_zelt_wieder_ein_abstand",
        "input_number.trotec_zelt_zuschalt_verzogerung",
        "input_boolean.trotec_zelt_tagbetrieb_erlauben",
        "input_boolean.trotec_zelt_nacht_durchlaufen",
        "input_boolean.trotec_zelt_melden",
        "input_number.trotec_zelt_melde_grenze_w",
        "input_number.trotec_zelt_melde_dauer_min",
        "input_number.trotec_zelt_melde_wiederholung_h",
        "sensor.trotec_zelt_vpd_ein_schwelle",
        "sensor.trotec_zelt_vpd_aus_schwelle",
        "sensor.trotec_zelt_folge_aus_temperatur",
        "sensor.trotec_zelt_wieder_ein_temperatur",
        "automation.rdwc_trotec_zelt_shelly_plan_regelung",
    };

    private static readonly string[] Entfeuchtermodule = { "entfeuchter", SteuerungGeraeteRollen.ZusatzModul };

    [Fact]
    public void ImBestandHeissenDieBauteileSoWieVorDerUmstellung()
    {
        var bestand = SteuerungBauteile.FuerRaum(KennungsRaum.Bestand).Select(b => b.EntityId).ToHashSet();

        Assert.Equal(34, AlterStand.Length);
        Assert.Empty(AlterStand.Where(id => !bestand.Contains(id)));
    }

    [Fact]
    public void ImBestandFehltKeineKennungUndKeineNeueBleibtStehen()
    {
        var neu = SteuerungBauteile.FuerRaum(KennungsRaum.Neu);
        var bestand = SteuerungBauteile.FuerRaum(KennungsRaum.Bestand);

        Assert.Equal(neu.Count, bestand.Count);
        Assert.Empty(bestand.Where(b => b.EntityId.Contains("fork_entfeuchter_", StringComparison.Ordinal)));
        Assert.Equal(bestand.Count, bestand.Select(b => b.EntityId).Distinct().Count());
        Assert.Equal(neu.Count, neu.Select(b => b.EntityId).Distinct().Count());
    }

    [Fact]
    public void ImNeuenRaumStehtNirgendsMehrEineMarkeOderEinPortNameDesEntwicklers()
    {
        // Katalog, Rechenvorschriften und alle Vorlagen: nichts darf auf die Anlage des Entwicklers deuten.
        var texte = SteuerungBauteile.FuerRaum(KennungsRaum.Neu)
            .Where(b => Entfeuchtermodule.Contains(b.Modul))
            .SelectMany(b => new[] { b.EntityId, b.Name, b.Vorlage ?? "", b.Verfuegbarkeit ?? "" })
            .Concat(Directory.EnumerateFiles(SteuerungAutomationService.VorlagenWurzel, "*.json", SearchOption.AllDirectories)
                .Where(d => Entfeuchtermodule.Contains(Path.GetFileName(Path.GetDirectoryName(d)!)))
                .Select(File.ReadAllText))
            .ToList();

        Assert.True(texte.Count > 50, "Die Suche greift nicht — zu wenige Texte gelesen.");
        Assert.Empty(texte.Where(t => Regex.IsMatch(t, "trotec|rdwc|port_[0-9]|shelly", RegexOptions.IgnoreCase)));
    }

    [Theory]
    [InlineData(KennungsRaum.Neu)]
    [InlineData(KennungsRaum.Bestand)]
    public void DerNameLeitetDieKennungAb_InJedemRaum(KennungsRaum raum)
    {
        // Home Assistant bildet die Objektkennung aus dem Namen. Weichen beide ab, entsteht ein Helfer unter einer
        // Kennung, die der Katalog nie findet — und beim nächsten Lauf ein zweiter.
        var neu = SteuerungBauteile.FuerRaum(KennungsRaum.Neu);
        var imRaum = SteuerungBauteile.FuerRaum(raum);
        var geprueft = 0;
        for (var i = 0; i < neu.Count; i++)
        {
            if (neu[i].Art == BauteilArt.Automation || Kennungen.Tabelle.All(t => t.NeueId != neu[i].EntityId)) continue;

            var erwartet = Regex.Replace(imRaum[i].Name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            Assert.Equal(erwartet, imRaum[i].Objektkennung);
            geprueft++;
        }

        Assert.Equal(Kennungen.Tabelle.Count(t => t.Domaene != "automation"), geprueft);
    }

    [Fact]
    public void DieVorlagenVerweisenImBestandNurAufAlteKennungen()
    {
        foreach (var datei in Directory.EnumerateFiles(Path.Combine(SteuerungAutomationService.VorlagenWurzel, "entfeuchter"), "*.json")
                     .Concat(Directory.EnumerateFiles(Path.Combine(SteuerungAutomationService.VorlagenWurzel, SteuerungGeraeteRollen.ZusatzModul), "*.json")))
        {
            var neu = File.ReadAllText(datei);
            var bestand = Kennungen.Uebersetzen(neu, KennungsRaum.Bestand);

            Assert.Equal(neu, Kennungen.Uebersetzen(neu, KennungsRaum.Neu));
            Assert.DoesNotContain("fork_entfeuchter_", bestand, StringComparison.Ordinal);
            foreach (Match m in Regex.Matches(bestand, @"(?:input_number|input_boolean|sensor|binary_sensor)\.(?:trotec_[a-z0-9_]+)"))
            {
                Assert.Contains(m.Value, AlterStand);
            }
        }
    }

    [Fact]
    public void LaengereKennungenWerdenNichtAnEinerKuerzerenAbgeschnitten()
    {
        // „…_feuchte_ein_nacht“ steckt als Vorspann in „…_feuchte_ein_nacht_x“ nur dann, wenn die Grenze fehlt.
        var text = "a input_number.fork_entfeuchter_feuchte_ein_nacht b sensor.fork_entfeuchter_feuchte_ein_aktiv c input_number.fork_entfeuchter_feuchte_ein_tag";
        var alt = Kennungen.Uebersetzen(text, KennungsRaum.Bestand);

        Assert.Equal("a input_number.trotec_feuchte_ein b sensor.trotec_feuchte_ein_aktiv c input_number.trotec_feuchte_ein_tag", alt);
    }

    [Fact]
    public void ImBestandGibtEsZweiWegeZurRegelungUndImNeuenRaumEinen()
    {
        var handgebaut = "automation.rdwc_trotec_nachtregelung_port_7_dehumi";
        var angelegt = "automation.entfeuchter_regelung_tag_nacht";
        var vorhanden = new[]
        {
            new HomeAssistantEntity { EntityId = handgebaut },
            new HomeAssistantEntity { EntityId = "automation.irgendwie_anders", KonfigKennung = "fork_ai_entfeuchter_regelung" },
        };

        var bestand = SteuerungBauteile.AutomationFinden(
            SteuerungBauteile.FuerRaum(KennungsRaum.Bestand).Single(b => b.Modul == "entfeuchter" && b.Art == BauteilArt.Automation), vorhanden);
        var neu = SteuerungBauteile.AutomationFinden(
            SteuerungBauteile.FuerRaum(KennungsRaum.Neu).Single(b => b.Modul == "entfeuchter" && b.Art == BauteilArt.Automation),
            new[] { new HomeAssistantEntity { EntityId = angelegt, KonfigKennung = "fork_ai_entfeuchter_regelung" } });

        Assert.Equal([handgebaut, "automation.irgendwie_anders"], bestand);
        Assert.Equal([angelegt], neu);
    }

    [Fact]
    public void EineFremdeAutomationUnterDerNeuenKennungWirdNichtUeberschrieben()
    {
        var fremd = new HomeAssistantEntity { EntityId = "automation.entfeuchter_regelung_tag_nacht" };

        Assert.Equal("automation.entfeuchter_regelung_tag_nacht", SteuerungAutomationService.HandgebauteDaneben("entfeuchter", "regelung", new[] { fremd }));
    }

    [Theory]
    [InlineData(null, false, false, KennungsRaum.Neu)]
    [InlineData(null, true, false, KennungsRaum.Bestand)]
    [InlineData(null, false, true, KennungsRaum.Bestand)]
    [InlineData(null, true, true, KennungsRaum.Bestand)]
    public void DieWahlFolgtDemWasDerForkSchonGespeichertHat(string? gespeichert, bool einstellungen, bool geraete, KennungsRaum erwartet)
    {
        var stand = gespeichert is null ? null : new KennungsRaumStand { Raum = gespeichert };

        Assert.Equal(erwartet, KennungsRaumWahl.Bestimmen(stand, einstellungen, geraete).Raum);
    }

    [Theory]
    [InlineData("neu", true, true, KennungsRaum.Neu)]
    [InlineData("bestand", false, false, KennungsRaum.Bestand)]
    public void EinGespeicherterEntscheidGiltImmer(string gespeichert, bool einstellungen, bool geraete, KennungsRaum erwartet)
    {
        // Wer neu beginnt und später Einstellungen speichert, rutscht beim nächsten Start nicht in den Bestand.
        Assert.Equal(erwartet, KennungsRaumWahl.Bestimmen(new KennungsRaumStand { Raum = gespeichert }, einstellungen, geraete).Raum);
    }

    private static SteuerungRepository FrischeDatenbank(out string wurzel)
    {
        wurzel = Path.Combine(Path.GetTempPath(), "KennungsRaum_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(wurzel);
        var pfade = new AppPaths(wurzel);
        TestDatabase.InitializeWithDefaultTent(pfade);
        return new SteuerungRepository(pfade);
    }

    [Fact]
    public void EineFrischeInstallationBekommtDenNeuenRaumUndBehaeltIhnAuchNachDemSpeichern()
    {
        var repo = FrischeDatenbank(out var wurzel);
        try
        {
            Assert.Equal(KennungsRaum.Neu, KennungsRaumWahl.Entscheiden(repo, NullLogger.Instance));

            // Der Nutzer richtet die Steuerung ein und speichert Einstellungen — beim nächsten Start bleibt es beim neuen Raum.
            repo.SetEinstellungen("entfeuchter", new EntfeuchterEinstellungen());
            repo.SetGeraet("entfeuchter", "zelt_rh", "sensor.x");
            Assert.Equal(KennungsRaum.Neu, KennungsRaumWahl.Entscheiden(repo, NullLogger.Instance));
        }
        finally
        {
            Directory.Delete(wurzel, true);
        }
    }

    [Fact]
    public void EineInstallationMitGespeichertenEinstellungenBleibtImBestand()
    {
        var repo = FrischeDatenbank(out var wurzel);
        try
        {
            repo.SetEinstellungen("entfeuchter", new EntfeuchterEinstellungen());

            Assert.Equal(KennungsRaum.Bestand, KennungsRaumWahl.Entscheiden(repo, NullLogger.Instance));
            Assert.Equal("bestand", repo.GetEinstellungen<KennungsRaumStand>(KennungsRaumWahl.Modul)!.Raum);
        }
        finally
        {
            Directory.Delete(wurzel, true);
        }
    }

    [Fact]
    public void EineInstallationNurMitZugeordnetenGeraetenBleibtImBestand()
    {
        var repo = FrischeDatenbank(out var wurzel);
        try
        {
            repo.SetGeraet(SteuerungGeraeteRollen.ZusatzModul, "zusatz_schalter", "switch.x");

            Assert.Equal(KennungsRaum.Bestand, KennungsRaumWahl.Entscheiden(repo, NullLogger.Instance));
        }
        finally
        {
            Directory.Delete(wurzel, true);
        }
    }
}
