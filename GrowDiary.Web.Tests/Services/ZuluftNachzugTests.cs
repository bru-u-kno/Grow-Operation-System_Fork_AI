using System.Text.Json;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.225): Der Abschalt-Abstand der Zuluft und sein Nachziehen in vorhandene
/// Rechenwerte.
/// </summary>
/// <remarks>
/// <para>Die Formularprobe ist der echte Optionsdialog des Rechenwerts „Zuluft Bedarf" aus
/// einer Anlage, in der er von Hand gebaut und danach angepasst wurde (geglättete
/// Differenz). Gerade deshalb: das Nachziehen darf nur ein Textstück tauschen und
/// nichts von dem anfassen, was der Nutzer verändert hat.</para>
/// </remarks>
public sealed class ZuluftNachzugTests
{
    private const string BedarfAlt =
        "{% set d = states('sensor.zuluft_differenz_geglattet') | float %}{% set s = states('input_number.zuluft_mindest_differenz') | float %}"
        + "{% if d >= s %}on{% elif d < s - 0.5 or t < tm - 1 %}off{% else %}{{ 'on' if prev else 'off' }}{% endif %}";

    private static JsonElement Formular(string state)
    {
        object Feld(string name, object selector, object? wert = null) => wert is null
            ? new { selector, name, required = false }
            : new { selector, name, description = new { suggested_value = wert }, required = false };

        var form = new
        {
            type = "form",
            flow_id = "abc",
            step_id = "binary_sensor",
            data_schema = new object[]
            {
                Feld("state", new { template = new { } }, state),
                Feld("device_class", new { device_class = new { domain = "binary_sensor" } }, "running"),
                Feld("device_id", new { device = new { } }),
                new
                {
                    type = "expandable",
                    name = "additional_options",
                    expanded = false,
                    schema = new object[]
                    {
                        Feld("availability", new { template = new { } }, "{{ has_value('sensor.a') }}"),
                        Feld("delay_on", new { duration = new { } }),
                    },
                },
            },
        };
        return JsonSerializer.SerializeToElement(form);
    }

    private static IEnumerable<SteuerungRechenwertService.Nachzug> BedarfNachzuege
        => SteuerungRechenwertService.Nachzuege.Where(n => n.EntityId == "binary_sensor.zuluft_bedarf");

    /// <summary>Der Nachzug zum Abschalt-Abstand (forkai.225).</summary>
    private static SteuerungRechenwertService.Nachzug Bedarf => BedarfNachzuege.Single(n => n.Merker is null);

    /// <summary>Der Nachzug zum Merker (forkai.234, A-024).</summary>
    private static SteuerungRechenwertService.Nachzug Merker => BedarfNachzuege.Single(n => n.Merker is not null);

    // Das laufende Template einer von Hand angepassten Anlage (Entitäten neutral): das, was der Nachzug wirklich vorfindet.
    private const string BedarfLaufend =
        "{% set d = states('sensor.zuluft_differenz_geglattet') | float %}{% set s = states('input_number.zuluft_mindest_differenz') | float %}"
        + "{% set t = states('sensor.aussen_temp') | float %}{% set tm = states('input_number.zuluft_aussentemperatur_min') | float %}"
        + "{% set tz = states('sensor.zelt_temp') | float(99) %}{% set tzm = states('input_number.zuluft_zelttemperatur_min') | float(21) %}"
        + "{% set prev = (this.state == 'on') if this is defined else false %}"
        + "{% if d >= s and t >= tm and tz >= tzm + 1 %}on{% elif d < s - 0.5 or t < tm - 1 or tz < tzm %}off{% else %}{{ 'on' if prev else 'off' }}{% endif %}";

    [Fact]
    public void DasAlteTextstueckWirdGetauschtUndAllesAndereBleibt()
    {
        var felder = SteuerungRechenwertService.NachzugFelder(Formular(BedarfAlt), Bedarf, out var stand);

        Assert.Equal("angepasst", stand);
        Assert.NotNull(felder);
        var state = (string)felder!["state"]!;
        // Das Neue steht drin, das Alte nicht mehr ...
        Assert.Contains("input_number.zuluft_abschalt_abstand", state);
        Assert.DoesNotContain("s - 0.5", state);
        // ... und die Abweichung des Nutzers (geglättete Differenz) ist unangetastet.
        Assert.Contains("sensor.zuluft_differenz_geglattet", state);
        Assert.Contains("t < tm - 1", state);
        // Geräteklasse und Verfügbarkeit gehen mit zurück, sonst verlöre der Eintrag sie.
        Assert.Equal("running", ((JsonElement)felder["device_class"]!).GetString());
        var zusatz = (Dictionary<string, object?>)felder["additional_options"]!;
        Assert.Equal("{{ has_value('sensor.a') }}", ((JsonElement)zusatz["availability"]!).GetString());
        // Felder ohne eingestellten Wert werden nicht erfunden.
        Assert.False(felder.ContainsKey("device_id"));
        Assert.False(zusatz.ContainsKey("delay_on"));
    }

    [Fact]
    public void EinSchonNeuerRechenwertWirdNichtNochEinmalGetauscht()
    {
        var neu = BedarfAlt.Replace(Bedarf.Alt, Bedarf.Neu);
        Assert.Null(SteuerungRechenwertService.NachzugFelder(Formular(neu), Bedarf, out var stand));
        Assert.Equal("aktuell", stand);
    }

    [Fact]
    public void BeideNachzuegeZusammenMachenAusDemLaufendenTemplateDasNeue()
    {
        Assert.Equal(2, BedarfNachzuege.Count());
        var state = BedarfLaufend;
        foreach (var n in BedarfNachzuege)
        {
            var felder = SteuerungRechenwertService.NachzugFelder(Formular(state), n, out var stand);
            Assert.Equal("angepasst", stand);
            state = (string)felder!["state"]!;
        }

        // Der Merker steht im Haltebereich vor this.state, und this.state bleibt als Rückfall.
        Assert.Contains("states('input_boolean.zuluft_bedarf_merker')", state);
        Assert.Contains("if m in ['on','off'] else ((this.state == 'on')", state);
        // Nutzer-Abweichungen bleiben (geglättete Differenz, eigene Fühler).
        Assert.Contains("sensor.zuluft_differenz_geglattet", state);
        Assert.Contains("sensor.zelt_temp", state);
        // Ein zweiter Durchlauf findet nichts mehr zu tun.
        foreach (var n in BedarfNachzuege)
        {
            Assert.Null(SteuerungRechenwertService.NachzugFelder(Formular(state), n, out var wieder));
            Assert.Equal("aktuell", wieder);
        }
    }

    [Fact]
    public void DerMerkerWirdVorDemAbschaltAbstandNachgezogen()
    {
        // Jeder Tausch lädt den Rechenwert neu. Käme der Abschalt-Abstand zuerst, rechnete der Rechenwert
        // danach ohne Merker mit this.state (nach dem Neuladen "unavailable") und fiele im Haltebereich auf "aus".
        var reihenfolge = BedarfNachzuege.ToList();
        Assert.Equal(2, reihenfolge.Count);
        Assert.NotNull(reihenfolge[0].Merker);
        Assert.Null(reihenfolge[1].Merker);
    }

    [Fact]
    public void DerMerkerNachzugWartetAufDieAutomationDieDenMerkerFuehrt()
    {
        // Ohne laufenden Abgleich friert der Merker ein und hielte den Bedarf im Haltebereich falsch.
        Assert.Equal(ZuluftSteuerungService.Entitaeten.MerkerAbgleich, Merker.Abgleich);
        var automation = SteuerungBauteile.FuerModul("zuluft").Single(b => b.EntityId == Merker.Abgleich);
        Assert.Equal(BauteilArt.Automation, automation.Art);
        // Der Name muss die Kennung ergeben, unter der Home Assistant die Automation anlegt.
        Assert.Equal("zuluft_merker_abgleich", automation.Name.ToLowerInvariant().Replace(' ', '_'));
    }

    [Fact]
    public void DerMerkerNachzugRechnetImHaltebereichMitDemMerkerUndNichtUmgekehrt()
    {
        // Texttreue Prüfung der Logik, weil .NET kein Jinja auswertet (am laufenden Home Assistant belegt, 17.10.2026):
        // Merker "on" -> vorher an; nur bei on/off zählt der Merker, sonst der alte Rückfall.
        Assert.Contains("{% set prev = (m == 'on') if m in ['on','off'] else ((this.state == 'on') if this is defined else false) %}", Merker.Neu);
        Assert.DoesNotContain("(m == 'off')", Merker.Neu);
    }

    [Fact]
    public void DerMerkerNachzugKennzeichnetSeinenHelferUndDerKatalogKenntIhn()
    {
        Assert.Equal(ZuluftSteuerungService.Entitaeten.BedarfMerker, Merker.Merker);
        var helfer = SteuerungBauteile.FuerModul("zuluft").Single(b => b.EntityId == Merker.Merker);
        Assert.Equal(BauteilArt.Schalter, helfer.Art);
        Assert.True(helfer.Pflicht);
        // Der Name muss die Kennung ergeben, sonst entsteht der Helfer bei jedem Lauf neu.
        Assert.Equal("zuluft_bedarf_merker", helfer.Name.ToLowerInvariant().Replace(' ', '_'));
        Assert.Contains(Merker.Neu, SteuerungBauteile.FuerModul("zuluft").Single(b => b.EntityId == Merker.EntityId).Vorlage);
        Assert.DoesNotContain(Merker.Alt, SteuerungBauteile.FuerModul("zuluft").Single(b => b.EntityId == Merker.EntityId).Vorlage);
    }

    [Theory]
    [InlineData("on", "off", SteuerungRechenwertService.MerkerSchritt.SetzeAn)]
    [InlineData("off", "on", SteuerungRechenwertService.MerkerSchritt.SetzeAus)]
    [InlineData("on", "on", SteuerungRechenwertService.MerkerSchritt.Passt)]
    [InlineData("off", "off", SteuerungRechenwertService.MerkerSchritt.Passt)]
    [InlineData("unavailable", "on", SteuerungRechenwertService.MerkerSchritt.ZustandUnklar)]
    [InlineData("unknown", "off", SteuerungRechenwertService.MerkerSchritt.ZustandUnklar)]
    [InlineData(null, "off", SteuerungRechenwertService.MerkerSchritt.ZustandUnklar)]
    [InlineData("on", null, SteuerungRechenwertService.MerkerSchritt.MerkerFehlt)]
    [InlineData("on", "unavailable", SteuerungRechenwertService.MerkerSchritt.MerkerFehlt)]
    [InlineData("on", "unknown", SteuerungRechenwertService.MerkerSchritt.MerkerFehlt)]
    public void DerMerkerWirdVorDemTauschAufDenJetzigenZustandGebracht(
        string? rechenwert, string? merker, SteuerungRechenwertService.MerkerSchritt erwartet)
        => Assert.Equal(erwartet, SteuerungRechenwertService.MerkerEntscheiden(rechenwert, merker));

    [Fact]
    public void DieRegelungSchaltetBeiEinemAussetzerDesBedarfsNichtAus()
    {
        // Gefunden im Docker-Test (17.10.2026): Der Merker hielt den Bedarf bei "an", aber die Regelung schaltete
        // schon in den Sekunden "nicht verfügbar" aus ("nicht an" galt als aus) — und wegen der Mindestpause
        // blieb der Lüfter danach noch zehn Minuten aus. Ausschalten darf nur bei "aus" oder nach langem Ausfall.
        var datei = Path.Combine(SteuerungAutomationService.VorlagenWurzel, "zuluft", "regelung.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(datei))!.AsObject();
        var optionen = json["actions"]![0]!["choose"]!.AsArray();
        Assert.True(optionen.Count >= 2, "Die Regelung hat weniger Zweige als erwartet — die Prüfung sähe nichts.");
        var ausschalten = optionen.Single(o => (string?)o!["alias"] == "Ausschalten")!.AsObject();
        var bedingungen = ausschalten["conditions"]!.ToJsonString();

        // Kein "nicht an" mehr auf den Bedarf: das schließt "nicht verfügbar" ein.
        Assert.DoesNotContain("\"condition\":\"not\"", bedingungen.Replace(" ", ""));
        // Stattdessen: ausdrücklich "off" ...
        Assert.Contains("\"state\":\"off\"", bedingungen.Replace(" ", ""));
        // ... oder ein langer Ausfall (15 Minuten), damit ein toter Fühler den Lüfter nicht ewig laufen lässt.
        var vorlagen = string.Join(" ", ausschalten["conditions"]!.AsArray()
            .SelectMany(c => c!["conditions"]?.AsArray().Select(k => (string?)k!["value_template"]) ?? [])
            .Where(t => t is not null));
        Assert.Contains("b.state != 'on' and", vorlagen);
        Assert.Contains("> 900", vorlagen);
        Assert.Contains("last_changed", vorlagen);
    }

    [Fact]
    public void DieMerkerAutomationReagiertNieAufNichtVerfuegbarUndStehtImKatalog()
    {
        var bauteil = SteuerungBauteile.FuerModul("zuluft").Single(b => b.EntityId == ZuluftSteuerungService.Entitaeten.MerkerAbgleich);
        Assert.Equal(BauteilArt.Automation, bauteil.Art);
        Assert.Equal("merker", bauteil.VorlagenDatei);
        var datei = Path.Combine(SteuerungAutomationService.VorlagenWurzel, "zuluft", "merker.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(datei))!.AsObject();

        // Die Auslöser nennen nur on/off als Ziel (eine Auslöser-Zählung mit Mengenwächter) ...
        var ausloeser = json["triggers"]!.AsArray().Select(t => t!.AsObject()).Where(t => (string?)t["trigger"] == "state").ToList();
        Assert.True(ausloeser.Count >= 1);
        foreach (var t in ausloeser)
        {
            var ziele = t["to"]!.AsArray().Select(z => (string?)z).ToList();
            Assert.Equal(new[] { "off", "on" }, ziele.OrderBy(z => z).ToArray());
        }

        // ... und die Bedingung verlangt on/off bei Bedarf UND Merker, sonst liefe auch der Minutentakt in "unavailable" hinein.
        var bedingung = (string)json["conditions"]![0]!["value_template"]!;
        Assert.Contains("states('binary_sensor.zuluft_bedarf') in ['on', 'off']", bedingung);
        Assert.Contains("states('input_boolean.zuluft_bedarf_merker') in ['on', 'off']", bedingung);

        // Jeder Zweig schaltet den Merker passend zum Bedarf: "on" -> turn_on, "off" -> turn_off (nie vertauscht).
        var zweige = json["actions"]![0]!["choose"]!.AsArray();
        Assert.Equal(2, zweige.Count);
        foreach (var zweig in zweige)
        {
            var bedarf = (string)zweig!["conditions"]![0]!["state"]!;
            var dienst = (string)zweig["sequence"]![0]!["action"]!;
            Assert.Equal($"input_boolean.turn_{bedarf}", dienst);
        }

        // Nur schreiben, wenn Bedarf und Merker verschieden sind (sonst schriebe der Minutentakt jede Minute).
        Assert.Contains("states('binary_sensor.zuluft_bedarf') != states('input_boolean.zuluft_bedarf_merker')", bedingung);

        // Die Aktionen betreffen nur den Merker und nichts am Lüfter.
        var wirksam = json["actions"]!.ToJsonString();
        Assert.Contains("input_boolean.zuluft_bedarf_merker", wirksam);
        Assert.DoesNotContain("select.", wirksam);
        Assert.DoesNotContain("number.", wirksam.Replace("input_number", ""));
    }

    [Fact]
    public void EineVorlageOhneDasAlteTextstueckBleibtUnberuehrt()
    {
        // Der Nutzer hat die Hysterese selbst anders geschrieben (0.8 statt 0.5).
        var eigen = BedarfAlt.Replace("s - 0.5", "s - 0.8");
        Assert.Null(SteuerungRechenwertService.NachzugFelder(Formular(eigen), Bedarf, out var stand));
        Assert.Equal("abweichend", stand);
    }

    [Fact]
    public void EinFormularOhneErwartetenAufbauWirdNichtGeraten()
    {
        var leer = JsonDocument.Parse("{\"type\":\"abort\"}").RootElement;
        Assert.Null(SteuerungRechenwertService.NachzugFelder(leer, Bedarf, out var stand));
        Assert.Equal("abweichend", stand);
    }

    [Fact]
    public void DerKatalogUndDasNachziehenSagenDasselbe()
    {
        // Läuft beides auseinander, bekäme eine neue Anlage eine andere Regel als eine
        // nachgezogene. Das Neue steht im Katalog, das Alte nicht mehr.
        var bauteil = SteuerungBauteile.FuerModul("zuluft").Single(b => b.EntityId == Bedarf.EntityId);
        var nachzuege = BedarfNachzuege.ToList();
        Assert.True(nachzuege.Count >= 2);
        foreach (var n in nachzuege)
        {
            Assert.Contains(n.Neu, bauteil.Vorlage);
            Assert.DoesNotContain(n.Alt, bauteil.Vorlage);
        }
    }

    [Fact]
    public void DerHelferStehtImKatalogMitDenGrenzenDesPruefers()
    {
        var helfer = SteuerungBauteile.FuerModul("zuluft")
            .Single(b => b.EntityId == ZuluftSteuerungService.Entitaeten.AbschaltAbstand);
        Assert.Equal(BauteilArt.Zahl, helfer.Art);
        Assert.Equal(0, helfer.Min);
        Assert.Equal(ZuluftSteuerungService.AbschaltAbstandMax, helfer.Max);
        // Der Name muss die Kennung ergeben, sonst entsteht der Helfer bei jedem Lauf neu.
        Assert.Equal("zuluft_abschalt_abstand", helfer.Name.ToLowerInvariant().Replace(' ', '_'));
    }
}
