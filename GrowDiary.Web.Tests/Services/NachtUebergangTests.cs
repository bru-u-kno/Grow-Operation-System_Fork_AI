using System.Text.Json;
using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Xunit;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Cultivate Control (2.0.0-cc.255): Die Höchsttemperatur der Entfeuchter-Übertemperatur-Bremse gleitet nach Licht-aus
/// vom Tag- auf den Nachtwert, statt zu springen.
/// </summary>
/// <remarks>
/// <para>Der Anlass: Um Licht-aus sprang die Grenze von 24 auf 22 °C, das Zelt war noch bei 23 °C, und beide
/// Entfeuchter gingen sofort aus — an drei Abenden 88, 44 und 3 Minuten. Geprüft wird die Rechnung über die Zeit, die
/// Spanne über Mitternacht, das Verhalten mit Plan-Luft, der Katalog (Helfer, Zeitstempel, Automation), das Nachziehen
/// in einen vorhandenen Rechenwert und die Einstellung selbst.</para>
/// <para>Die Jinja-Fassung des Rechenwerts läuft nicht in diesen Tests, sondern in Home Assistant; ihr Verlauf ist am
/// laufenden Stand belegt (Bericht zum Release). Hier ist sie an die C#-Rechnung gebunden, soweit Text es erlaubt: gleiche
/// Helfer, gleiche Reihenfolge der Bedingungen.</para>
/// </remarks>
public sealed class NachtUebergangTests
{
    // ------------------------------------------------------------------ Rechnung über die Zeit

    [Theory]
    [InlineData(0, 24.0)]      // Licht-aus: der Tagwert gilt weiter
    [InlineData(15, 23.67)]
    [InlineData(30, 23.33)]    // Beispiel: um 17:30 bei Licht-aus um 17:00 ≈ 23,3 °C
    [InlineData(45, 23.0)]     // Mitte
    [InlineData(60, 22.67)]
    [InlineData(89, 22.02)]
    [InlineData(90, 22.0)]     // Ende: der Nachtwert
    [InlineData(91, 22.0)]
    [InlineData(600, 22.0)]    // tief in der Nacht
    public void DieGrenzeSinktGleichmaessigVonTagAufNacht(double seitMin, double erwartet)
        => Assert.Equal(erwartet, NachtUebergang.Grenze(24, 22, 90, seitMin), 2);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(45)]
    [InlineData(300)]
    public void MitDauerNullSpringtDieGrenzeSofortAufDenNachtwert(double seitMin)
        => Assert.Equal(22, NachtUebergang.Grenze(24, 22, 0, seitMin));

    [Fact]
    public void OhneBekanntenZeitpunktGiltSofortDerNachtwert()
        => Assert.Equal(22, NachtUebergang.Grenze(24, 22, 90, null));

    [Theory]
    [InlineData(22, 22)]   // Tag = Nacht: nichts zu gleiten
    [InlineData(21, 22)]   // Nacht höher als Tag: kein Absenken, sofort der Nachtwert
    public void LiegtDerNachtwertNichtUnterDemTagwertGiltErSofort(double tag, double nacht)
        => Assert.Equal(nacht, NachtUebergang.Grenze(tag, nacht, 90, 10));

    [Fact]
    public void DieGrenzeLiegtNieUeberDemTagwertUndNieUnterDemNachtwert()
    {
        var gesehen = 0;
        for (var dauer = 0; dauer <= 240; dauer += 15)
        {
            for (var seit = -30; seit <= 400; seit += 7)
            {
                var g = NachtUebergang.Grenze(26.5, 21.5, dauer, seit);
                Assert.InRange(g, 21.5, 26.5);
                gesehen++;
            }
        }

        Assert.True(gesehen > 300, "Die Prüfung sieht ihre Grundmenge nicht.");
    }

    [Fact]
    public void DieGrenzeSinktNurundSteigtNieInnerhalbDerNacht()
    {
        var vorher = double.MaxValue;
        for (var seit = 0; seit <= 200; seit++)
        {
            var g = NachtUebergang.Grenze(24, 22, 90, seit);
            Assert.True(g <= vorher, $"Bei {seit} Minuten steigt die Grenze von {vorher} auf {g}.");
            vorher = g;
        }
    }

    // ------------------------------------------------------------------ Über Mitternacht und mit Plan-Luft

    [Fact]
    public void DerUebergangLaeuftUeberMitternachtWeil_NichtUhrzeitenSondernVergangeneMinutenZaehlen()
    {
        // Licht aus um 23:30 (Lichtplan 17/7 oder 23/1: egal), Mitternacht liegt mitten im Übergang.
        var lichtAus = new DateTime(2026, 10, 19, 23, 30, 0);
        var um0015 = new DateTime(2026, 10, 20, 0, 15, 0);
        var um0100 = new DateTime(2026, 10, 20, 1, 0, 0);

        Assert.Equal(45, NachtUebergang.MinutenSeit(lichtAus, um0015));
        Assert.Equal(23.0, NachtUebergang.Grenze(24, 22, 90, NachtUebergang.MinutenSeit(lichtAus, um0015)), 2);
        Assert.Equal(22.0, NachtUebergang.Grenze(24, 22, 90, NachtUebergang.MinutenSeit(lichtAus, um0100)), 2);
    }

    [Fact]
    public void EinZeitstempelAusDerZukunftOderOhneWertGibtKeineMinuten()
    {
        var jetzt = new DateTime(2026, 10, 19, 12, 0, 0);
        Assert.Null(NachtUebergang.MinutenSeit(null, jetzt));
        Assert.Null(NachtUebergang.MinutenSeit(jetzt.AddMinutes(5), jetzt));
    }

    [Fact]
    public void DieNachtgrenzeAusDemPlanGleitetGenauSo()
    {
        // Tag: Plan-Luft 19 + 5 K = 24 °C; Nacht: Plan-Luft 17 + 5 K = 22 °C — dieselben Helfer-Spannen wie beim Schreiben.
        var tag = EntfeuchterSteuerungService.TempMaxFuer(EntfeuchterSteuerungService.Entitaeten.TempMaxTag, TempMaxModus.Plan, 5, 29, 19);
        var nacht = EntfeuchterSteuerungService.TempMaxFuer(EntfeuchterSteuerungService.Entitaeten.TempMaxNacht, TempMaxModus.Plan, 5, 25, 17);
        Assert.Equal(24, tag);
        Assert.Equal(22, nacht);

        Assert.Equal(23.0, NachtUebergang.Grenze(tag, nacht, 90, 45), 2);

        // Die nächste Plan-Woche macht die Nacht kühler (16 + 5 = 21): der Übergang nimmt den neuen Nachtwert, ohne Sprung beim Start.
        var kuehler = EntfeuchterSteuerungService.TempMaxFuer(EntfeuchterSteuerungService.Entitaeten.TempMaxNacht, TempMaxModus.Plan, 5, 25, 16);
        Assert.Equal(24, NachtUebergang.Grenze(tag, kuehler, 90, 0));
        Assert.Equal(22.5, NachtUebergang.Grenze(tag, kuehler, 90, 45), 2);
        Assert.Equal(21, NachtUebergang.Grenze(tag, kuehler, 90, 90));
    }

    // ------------------------------------------------------------------ Einstellung

    [Fact]
    public void DieVoreinstellungSind90MinutenUndGiltFuerBestehendeStaende()
    {
        Assert.Equal(90, new EntfeuchterEinstellungen().NachtUebergangMin);

        // Ein vor dieser Version gespeicherter Stand kennt das Feld nicht: er bekommt die Voreinstellung.
        var alt = JsonSerializer.Deserialize<EntfeuchterEinstellungen>("{\"hystereseProzent\":4}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(90, alt.NachtUebergangMin);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(90, true)]
    [InlineData(240, true)]
    [InlineData(-1, false)]
    [InlineData(241, false)]
    public void DieDauerWirdAufNullBisVierStundenGeprueft(int minuten, bool gueltig)
    {
        var f = EntfeuchterSteuerungService.Pruefen(new EntfeuchterEinstellungen { NachtUebergangMin = minuten });
        Assert.Equal(!gueltig, f.ContainsKey(nameof(EntfeuchterEinstellungen.NachtUebergangMin)));
    }

    [Fact]
    public void DerWertKommtAusDemHelferInHomeAssistant()
    {
        var zustaende = new Dictionary<string, string?> { [EntfeuchterSteuerungService.Entitaeten.NachtUebergang] = "45.0" };
        Assert.Equal(45, EntfeuchterSteuerungService.AusHomeAssistant(zustaende).NachtUebergangMin);
        // Ohne Helfer bleibt die Voreinstellung.
        Assert.Equal(90, EntfeuchterSteuerungService.AusHomeAssistant(new Dictionary<string, string?>()).NachtUebergangMin);
    }

    [Fact]
    public void DerHelferBekommtBeimAnlegenSeinenStartwertUndEinBestehenderBleibtUnberuehrt()
    {
        Assert.Contains(EntfeuchterSteuerungService.Entitaeten.NachtUebergang, EntfeuchterSteuerungService.Entitaeten.Startwerte);

        var soll = new (string Entity, double Wert)[]
        {
            (EntfeuchterSteuerungService.Entitaeten.NachtUebergang, 90),
            (EntfeuchterSteuerungService.Entitaeten.Hysterese, 4),
        };
        var nurNeu = EntfeuchterSteuerungService.Schreibumfang(soll, new HashSet<string> { EntfeuchterSteuerungService.Entitaeten.NachtUebergang });
        Assert.Equal([(EntfeuchterSteuerungService.Entitaeten.NachtUebergang, 90d)], nurNeu.Zahlen.ToArray());
        Assert.False(nurNeu.RegelungSchalten);
    }

    [Fact]
    public void DieSpanneDesHelfersStehtImKatalogUndKlemmtWerteDarauf()
    {
        Assert.Equal((0d, 240d), SteuerungBauteile.Spanne(NachtUebergang.DauerId));
        Assert.Equal(240, SteuerungBauteile.AufSpanne(NachtUebergang.DauerId, 999));
        Assert.Equal(0, SteuerungBauteile.AufSpanne(NachtUebergang.DauerId, -5));
        Assert.Equal(("Nacht-Übergang", "min"), EntfeuchterSteuerungService.BeschriftungFuer(NachtUebergang.DauerId));
    }

    // ------------------------------------------------------------------ Katalog

    [Fact]
    public void DerKatalogFuehrtDauerZeitstempelUndMerkerAutomation()
    {
        var teile = SteuerungBauteile.FuerRaum(KennungsRaum.Neu).Where(b => b.Modul == "entfeuchter").ToList();

        var dauer = teile.Single(b => b.EntityId == NachtUebergang.DauerId);
        Assert.Equal(BauteilArt.Zahl, dauer.Art);
        Assert.False(dauer.Pflicht);
        Assert.NotNull(dauer.OhneDas);

        var stempel = teile.Single(b => b.EntityId == NachtUebergang.LichtAusSeitId);
        Assert.Equal(BauteilArt.Zeitpunkt, stempel.Art);
        Assert.False(stempel.Pflicht);

        var merker = teile.Single(b => b.VorlagenDatei == "licht_aus_merker");
        Assert.Equal(BauteilArt.Automation, merker.Art);
        Assert.Equal("automation.entfeuchter_licht_aus_merker", merker.EntityId);
        Assert.Equal(ProbelaufRolle.Laufenlassen, merker.Probelauf);
    }

    [Fact]
    public void DieMerkerAutomationReagiertNurAufDenEchtenWechselVonAnAufAusUndSchreibtDenZeitstempel()
    {
        var pfad = Path.Combine(SteuerungAutomationService.VorlagenWurzel, "entfeuchter", "licht_aus_merker.json");
        var vorlage = (JsonObject)JsonNode.Parse(File.ReadAllText(pfad))!;

        var ausloeser = vorlage["triggers"]!.AsArray().Single()!.AsObject();
        Assert.Equal("state", (string?)ausloeser["trigger"]);
        Assert.Equal("[[licht_zustand]]", (string?)ausloeser["entity_id"]);
        // Aussetzer des Sensors (nicht verfügbar → aus) zählen nicht: sonst begänne der Übergang nachts von vorn.
        Assert.Equal("on", (string?)ausloeser["from"]);
        Assert.Equal("off", (string?)ausloeser["to"]);

        var aktion = vorlage["actions"]!.AsArray().Single()!.AsObject();
        Assert.Equal("input_datetime.set_datetime", (string?)aktion["action"]);
        Assert.Equal(NachtUebergang.LichtAusSeitId, (string?)aktion["target"]!["entity_id"]);
        Assert.Contains("now()", (string?)aktion["data"]!["datetime"]);
        Assert.Contains(NachtUebergang.DauerId, (string?)vorlage["description"] ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void DerRechenwertLiestDieDreiHelferUndHatNochNurDenLichtPlatzhalter()
    {
        var rechenwert = SteuerungBauteile.FuerRaum(KennungsRaum.Neu).Single(b => b.EntityId == "sensor.fork_entfeuchter_temp_max_aktiv");
        var vorlage = rechenwert.Vorlage!;

        Assert.Contains(NachtUebergang.Neu, vorlage, StringComparison.Ordinal);
        Assert.Contains(NachtUebergang.DauerId, vorlage, StringComparison.Ordinal);
        Assert.Contains(NachtUebergang.LichtAusSeitId, vorlage, StringComparison.Ordinal);
        Assert.Equal(["licht_zustand"], SteuerungBauteile.PlatzhalterIn(vorlage));

        // Der Rückfall: Nachtwert, wenn Dauer 0, Zeitstempel fehlt oder die Dauer um ist.
        Assert.Contains("dauer > 0", vorlage, StringComparison.Ordinal);
        Assert.Contains("0 <= vergangen < dauer", vorlage, StringComparison.Ordinal);
        Assert.Contains("{% else %}{{ nacht }}{% endif %}", vorlage, StringComparison.Ordinal);
        // Der Zweig für „Licht unbekannt" bleibt: dann hält der Rechenwert seinen letzten Wert.
        Assert.Contains("this.state not in ['unknown','unavailable']", vorlage, StringComparison.Ordinal);
    }

    [Fact]
    public void DieVorlageEnthaeltKeineFesteEntitaetDesEntwicklers()
    {
        var alles = NachtUebergang.Alt + NachtUebergang.Neu
            + File.ReadAllText(Path.Combine(SteuerungAutomationService.VorlagenWurzel, "entfeuchter", "licht_aus_merker.json"));
        Assert.DoesNotContain("binary_sensor.", NachtUebergang.Neu, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ Nachzug in einen vorhandenen Rechenwert

    // Der Rechenwert, wie er vor dieser Version angelegt wurde (neuer Namensraum; die Licht-Entität steht fest drin).
    private const string RechenwertVorher =
        "{% set led = states('binary_sensor.zelt_licht') %}{% if led == 'on' %}{{ states('input_number.fork_entfeuchter_temp_max_tag') | float(28.5) }}"
        + "{% elif led == 'off' %}{{ states('input_number.fork_entfeuchter_temp_max_nacht') | float(30) }}"
        + "{% else %}{{ this.state if this is defined and this.state not in ['unknown','unavailable'] else states('input_number.fork_entfeuchter_temp_max_nacht') | float(30) }}{% endif %}";

    private static JsonElement Formular(string state)
    {
        var form = new
        {
            type = "form",
            flow_id = "abc",
            step_id = "sensor",
            data_schema = new object[]
            {
                new { selector = new { template = new { } }, name = "state", description = new { suggested_value = state }, required = false },
                new { selector = new { text = new { } }, name = "unit_of_measurement", description = new { suggested_value = "°C" }, required = false },
                new { selector = new { select = new { } }, name = "state_class", description = new { suggested_value = "measurement" }, required = false },
            },
        };
        return JsonSerializer.SerializeToElement(form);
    }

    private static SteuerungRechenwertService.Nachzug Nachzug
        => SteuerungRechenwertService.Nachzuege.Single(n => n.EntityId == "sensor.fork_entfeuchter_temp_max_aktiv");

    [Fact]
    public void DerNachzugTauschtNurDenNachtZweigUndLaesstAllesAndereStehen()
    {
        var felder = SteuerungRechenwertService.NachzugFelder(Formular(RechenwertVorher), Nachzug, out var stand);

        Assert.Equal("angepasst", stand);
        var state = (string)felder!["state"]!;
        Assert.Contains(NachtUebergang.Neu, state, StringComparison.Ordinal);
        Assert.DoesNotContain(NachtUebergang.Alt, state, StringComparison.Ordinal);
        // Was der Nutzer an der Vorlage gebaut hat, bleibt: seine Licht-Entität und der Haltezweig.
        Assert.Contains("states('binary_sensor.zelt_licht')", state, StringComparison.Ordinal);
        Assert.Contains("this.state not in ['unknown','unavailable']", state, StringComparison.Ordinal);
        Assert.Equal("°C", felder["unit_of_measurement"]!.ToString());
        Assert.Equal("measurement", felder["state_class"]!.ToString());
    }

    [Fact]
    public void DerNachzugIstIdempotent()
    {
        var erst = (string)SteuerungRechenwertService.NachzugFelder(Formular(RechenwertVorher), Nachzug, out _)!["state"]!;

        var zweiter = SteuerungRechenwertService.NachzugFelder(Formular(erst), Nachzug, out var stand);

        Assert.Null(zweiter);
        Assert.Equal("aktuell", stand);
    }

    [Fact]
    public void EineVomNutzerUmgebauteVorlageBleibtUnberuehrt()
    {
        var umgebaut = RechenwertVorher.Replace("float(30)", "float(26)", StringComparison.Ordinal);

        var felder = SteuerungRechenwertService.NachzugFelder(Formular(umgebaut), Nachzug, out var stand);

        Assert.Null(felder);
        Assert.Equal("abweichend", stand);
    }

    [Fact]
    public void DerNachzugIstInDerAktuellenVorlageDesKatalogsAlsNeuesStueckEnthalten()
    {
        var neu = SteuerungBauteile.FuerRaum(KennungsRaum.Neu).Single(b => b.EntityId == Nachzug.EntityId).Vorlage!;
        // Frisch angelegte Anlagen und nachgezogene Anlagen tragen denselben Zweig.
        Assert.Contains(Nachzug.Neu, neu, StringComparison.Ordinal);
        // Und die alte Fassung des Katalogs ist genau die, die der Nachzug ersetzt.
        var vorher = neu.Replace(Nachzug.Neu, Nachzug.Alt, StringComparison.Ordinal)
            .Replace("[[licht_zustand]]", "binary_sensor.zelt_licht", StringComparison.Ordinal);
        Assert.Equal(RechenwertVorher, vorher);
    }
}

/// <summary>Dasselbe für eine Anlage mit den alten Kennungen (<see cref="KennungsRaum.Bestand"/>).</summary>
[Collection(BestandRaumSammlung.Name)]
public sealed class NachtUebergangBestandTests : IDisposable
{
    public NachtUebergangBestandTests() => Kennungen.Festlegen(KennungsRaum.Bestand);

    public void Dispose() => Kennungen.Festlegen(KennungsRaum.Neu);

    // Der laufende Rechenwert in einer Anlage mit alten Kennungen (Licht-Entität neutral benannt).
    private const string RechenwertBestand =
        "{% set led = states('binary_sensor.zelt_licht') %}{% if led == 'on' %}{{ states('input_number.trotec_temp_max_tag') | float(28.5) }}"
        + "{% elif led == 'off' %}{{ states('input_number.trotec_temp_max') | float(30) }}"
        + "{% else %}{{ this.state if this is defined and this.state not in ['unknown','unavailable'] else states('input_number.trotec_temp_max') | float(30) }}{% endif %}";

    private static JsonElement Formular(string state)
        => JsonSerializer.SerializeToElement(new
        {
            type = "form",
            flow_id = "abc",
            step_id = "sensor",
            data_schema = new object[]
            {
                new { selector = new { template = new { } }, name = "state", description = new { suggested_value = state }, required = false },
            },
        });

    private static SteuerungRechenwertService.Nachzug Nachzug
        => SteuerungRechenwertService.Nachzuege.Single(n => n.EntityId == "sensor.fork_entfeuchter_temp_max_aktiv");

    [Fact]
    public void ImBestandHeisstDerRechenwertUndSeinTitelWieBeiDerAnlage()
    {
        Assert.Equal("sensor.trotec_temp_max_aktiv", Nachzug.Kennung);
        Assert.Equal("Trotec Temp Max aktiv", Kennungen.Name(Nachzug.EntityId, Nachzug.Name, Kennungen.Raum));
    }

    [Fact]
    public void DerNachzugFindetImBestandDasAlteStueckMitDenAltenNamenUndSetztDasNeueMitDenAltenNamen()
    {
        var felder = SteuerungRechenwertService.NachzugFelder(Formular(RechenwertBestand), Nachzug, out var stand);

        Assert.Equal("angepasst", stand);
        var state = (string)felder!["state"]!;
        // Die Grenzen heißen dort wie bisher; die neuen Helfer haben keine alte Kennung und heißen überall gleich.
        Assert.Contains("states('input_number.trotec_temp_max')", state, StringComparison.Ordinal);
        Assert.Contains("states('input_number.trotec_temp_max_tag')", state, StringComparison.Ordinal);
        Assert.Contains($"states('{NachtUebergang.DauerId}')", state, StringComparison.Ordinal);
        Assert.Contains($"states('{NachtUebergang.LichtAusSeitId}')", state, StringComparison.Ordinal);
        Assert.Equal(NachtUebergang.DauerId, EntfeuchterSteuerungService.Entitaeten.NachtUebergang);
        Assert.DoesNotContain("fork_entfeuchter_temp_max", state, StringComparison.Ordinal);
        Assert.Contains(NachtUebergang.DauerId, Kennungen.NurNeu);
        Assert.Contains("states('binary_sensor.zelt_licht')", state, StringComparison.Ordinal);
    }

    [Fact]
    public void ImBestandIstDerNachzugEbensoIdempotent()
    {
        var erst = (string)SteuerungRechenwertService.NachzugFelder(Formular(RechenwertBestand), Nachzug, out _)!["state"]!;

        Assert.Null(SteuerungRechenwertService.NachzugFelder(Formular(erst), Nachzug, out var stand));
        Assert.Equal("aktuell", stand);
    }

    [Fact]
    public void DerFrischAngelegteRechenwertImBestandStimmtMitDemNachgezogenenUeberein()
    {
        var angelegt = SteuerungBauteile.Alle.Single(b => b.EntityId == "sensor.trotec_temp_max_aktiv").Vorlage!
            .Replace("[[licht_zustand]]", "binary_sensor.zelt_licht", StringComparison.Ordinal);
        var nachgezogen = (string)SteuerungRechenwertService.NachzugFelder(Formular(RechenwertBestand), Nachzug, out _)!["state"]!;

        Assert.Equal(angelegt, nachgezogen);
    }
}
