using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Neuling-Test 17.10.2026: Frisch angelegte Helfer bekommen die Werte des Fork
/// (<see cref="SteuerungStartwerteService"/>). Der Schreibweg selbst (Home Assistant) ist am laufenden Stand geprüft;
/// hier steht die Zählung gegen die Grundmenge — ein Tippfehler in der Liste ließe einen Helfer stumm auf seinem Minimum.
/// </summary>
public sealed class SteuerungStartwerteTests
{
    private static IReadOnlyList<Bauteil> EntfeuchterHelfer()
        => SteuerungBauteile.FuerModul(EntfeuchterSteuerungService.Modul)
            .Where(b => b.Art is BauteilArt.Zahl or BauteilArt.Schalter)
            .ToList();

    private static string Id(Bauteil b) => Kennungen.Id(b.EntityId);

    [Fact]
    public void DieStartwerteGehoerenAlleZuHelfernDesKatalogs()
    {
        var helfer = EntfeuchterHelfer().Select(Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var startwerte = EntfeuchterSteuerungService.Entitaeten.Startwerte;

        // Mengenwächter: ohne Grundmenge liefe die Prüfung null Mal und wäre grün.
        Assert.True(helfer.Count >= 12, $"Der Katalog kennt {helfer.Count} Zahl-/Schalter-Helfer des Entfeuchters.");
        Assert.True(startwerte.Count >= 12);
        Assert.Equal(startwerte.Count, startwerte.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(startwerte, id => Assert.Contains(id, helfer));
    }

    [Fact]
    public void JederHelferDesEntfeuchtersBekommtEinenStartwertOderHatEinenAnderenSchreiber()
    {
        // Was NICHT in der Startwerte-Liste steht, schreibt jemand anderes — sonst bliebe der Helfer auf dem Minimum von
        // Home Assistant. Neuer Helfer im Katalog → entweder in Startwerte aufnehmen oder hier mit Grund eintragen.
        var andererSchreiber = new[]
        {
            "input_number.vpd_ziel_unten",        // schreibt der Wochenplan (WochenplanSyncService)
            "input_number.vpd_ziel_abschaltung",  // schreibt der Wochenplan
            "input_number.vpd_blatt_offset",      // Blatt-Offset aus dem Zelt
        };

        var ohneStartwert = EntfeuchterHelfer()
            .Where(b => !EntfeuchterSteuerungService.Entitaeten.Startwerte.Contains(Id(b), StringComparer.OrdinalIgnoreCase))
            .Select(b => b.EntityId)
            .Order()
            .ToList();

        Assert.Equal(andererSchreiber.Order(), ohneStartwert);
    }

    [Theory]
    [InlineData("entfeuchter", true)]
    [InlineData("Entfeuchter", true)]
    [InlineData("co2", false)]
    [InlineData("zuluft", false)]
    [InlineData("chiller", false)]
    [InlineData("entfeuchter-zusatz", false)]
    public void NurDerEntfeuchterHatBisherEinenStartwertWeg(string modul, bool kann)
        => Assert.Equal(kann, SteuerungStartwerteService.Kann(modul));

    // ------------------------------------------------------------------ Ablauf (ohne Home Assistant)

    private static SteuerungHelferService.Ergebnis Neu(string entityId) => new(entityId, entityId, Angelegt: true, Fehler: null);
    private static SteuerungHelferService.Ergebnis Bestehend(string entityId) => new(entityId, entityId, Angelegt: false, Fehler: null);

    private static SteuerungHelferService.Bilanz Bilanz(bool erreichbar, params SteuerungHelferService.Ergebnis[] einzeln)
        => new(erreichbar, einzeln.Count(e => e.Angelegt), einzeln.Count(e => !e.Angelegt), 0, einzeln);

    private static string FeuchteEin => EntfeuchterSteuerungService.Entitaeten.FeuchteEinTag;
    private static string FeuchteAus => EntfeuchterSteuerungService.Entitaeten.FeuchteAusTag;
    private static string Hysterese => EntfeuchterSteuerungService.Entitaeten.Hysterese;

    [Fact]
    public void NurNeuAngelegteHelferBekommenEinenStartwert()
    {
        // Bestehende Helfer dürfen nie überschrieben werden — auch kein von Hand geänderter (Neuling-Test 17.10.2026).
        var ids = SteuerungStartwerteService.ZuSchreiben([Neu(FeuchteEin), Bestehend(FeuchteAus), Neu(Hysterese)]);

        Assert.Equal(new[] { FeuchteEin, Hysterese }.Order(StringComparer.OrdinalIgnoreCase), ids.Order(StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(FeuchteAus, ids, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EinNeuerHelferOhneSchreiberBekommtKeinenStartwert()
    {
        // vpd_ziel_unten schreibt der Wochenplan — der Fork darf ihn hier nicht mit etwas Eigenem belegen.
        var ids = SteuerungStartwerteService.ZuSchreiben([Neu("input_number.vpd_ziel_unten"), Neu(FeuchteEin)]);
        Assert.Equal([FeuchteEin], ids.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NichtsNeuAngelegtHeisstNichtsSchreiben()
    {
        Assert.Empty(SteuerungStartwerteService.ZuSchreiben([Bestehend(FeuchteEin), Bestehend(Hysterese)]));
        Assert.Empty(SteuerungStartwerteService.ZuSchreiben([]));
    }

    [Fact]
    public async Task DerStandWirdVorDemAnlegenFestgehaltenUndDanachNurDasNeueGeschrieben()
    {
        var reihenfolge = new List<string>();
        IReadOnlySet<string>? geschrieben = null;

        var bilanz = await SteuerungStartwerteService.AblaufAsync(
            festhalten: () => { reihenfolge.Add("festhalten"); return Task.FromResult("stand"); },
            anlegen: () => { reihenfolge.Add("anlegen"); return Task.FromResult(Bilanz(true, Neu(FeuchteEin), Bestehend(Hysterese))); },
            schreiben: (stand, ids) => { reihenfolge.Add("schreiben:" + stand); geschrieben = ids; return Task.FromResult(true); });

        // Festhalten muss VOR dem Anlegen liegen: danach stünden die Mindestwerte in Home Assistant, und der Fork
        // übernähme sie beim ersten Lesen als seine eigenen Einstellungen.
        Assert.Equal(["festhalten", "anlegen", "schreiben:stand"], reihenfolge);
        Assert.Equal([FeuchteEin], geschrieben!.ToList(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, bilanz.Startwerte);
        Assert.True(bilanz.StartwerteVollstaendig);
    }

    [Fact]
    public async Task OhneAntwortVonHomeAssistantWirdNichtGeschrieben()
    {
        var geschrieben = false;
        var bilanz = await SteuerungStartwerteService.AblaufAsync(
            festhalten: () => Task.FromResult(0),
            anlegen: () => Task.FromResult(Bilanz(false)),
            schreiben: (_, _) => { geschrieben = true; return Task.FromResult(true); });

        Assert.False(geschrieben);
        Assert.False(bilanz.Erreichbar);
        Assert.Equal(0, bilanz.Startwerte);
    }

    [Fact]
    public async Task WennAllesSchonDaWarWirdNichtGeschriebenUndNichtsAlsOffenGemeldet()
    {
        var geschrieben = false;
        var bilanz = await SteuerungStartwerteService.AblaufAsync(
            festhalten: () => Task.FromResult(0),
            anlegen: () => Task.FromResult(Bilanz(true, Bestehend(FeuchteEin), Bestehend(Hysterese))),
            schreiben: (_, _) => { geschrieben = true; return Task.FromResult(true); });

        Assert.False(geschrieben);
        Assert.Equal(0, bilanz.Startwerte);
        Assert.True(bilanz.StartwerteVollstaendig);
    }

    [Fact]
    public async Task NimmtHomeAssistantNichtAllesAnWirdDasGemeldet()
    {
        var bilanz = await SteuerungStartwerteService.AblaufAsync(
            festhalten: () => Task.FromResult(0),
            anlegen: () => Task.FromResult(Bilanz(true, Neu(FeuchteEin), Neu(Hysterese))),
            schreiben: (_, _) => Task.FromResult(false));

        Assert.Equal(0, bilanz.Startwerte);
        Assert.False(bilanz.StartwerteVollstaendig);
    }

    [Fact]
    public async Task WirftDasSchreibenBleibtDieBilanzDerAngelegtenHelferErhalten()
    {
        // Die Helfer sind angelegt. Ginge die Bilanz verloren, sähe die Oberfläche einen Fehler, ein zweiter Lauf legte
        // nichts mehr an, und die Startwerte würden nie nachgeholt.
        var bilanz = await SteuerungStartwerteService.AblaufAsync<int>(
            festhalten: () => Task.FromResult(0),
            anlegen: () => Task.FromResult(Bilanz(true, Neu(FeuchteEin), Neu(Hysterese))),
            schreiben: (_, _) => throw new HttpRequestException("Home Assistant antwortet nicht"));

        Assert.Equal(2, bilanz.Angelegt);
        Assert.Equal(0, bilanz.Startwerte);
        Assert.False(bilanz.StartwerteVollstaendig);
    }

    [Fact]
    public async Task EinAbbruchDurchDenNutzerWirdNichtVerschluckt()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => SteuerungStartwerteService.AblaufAsync<int>(
            festhalten: () => Task.FromResult(0),
            anlegen: () => Task.FromResult(Bilanz(true, Neu(FeuchteEin))),
            schreiben: (_, _) => throw new OperationCanceledException()));
    }

    // ------------------------------------------------------------------ Schreibumfang

    private static (string Entity, double Wert)[] Soll() =>
    [
        (FeuchteEin, 60), (FeuchteAus, 57), (Hysterese, 4),
    ];

    [Fact]
    public void OhneEinschraenkungWirdAllesGeschriebenUndDieRegelungGeschaltet()
    {
        var umfang = EntfeuchterSteuerungService.Schreibumfang(Soll(), nur: null);

        Assert.Equal(3, umfang.Zahlen.Count);
        Assert.True(umfang.VpdRegelung);
        Assert.True(umfang.Tagbetrieb);
        Assert.True(umfang.RegelungSchalten);
    }

    [Fact]
    public void MitEinschraenkungWirdNurDasGenannteGeschriebenUndDieRegelungBleibtUnberuehrt()
    {
        var umfang = EntfeuchterSteuerungService.Schreibumfang(Soll(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FeuchteEin });

        Assert.Equal([FeuchteEin], umfang.Zahlen.Select(z => z.Entity).ToList(), StringComparer.OrdinalIgnoreCase);
        Assert.False(umfang.VpdRegelung);
        Assert.False(umfang.Tagbetrieb);
        Assert.False(umfang.RegelungSchalten);
    }

    [Fact]
    public void EinSchalterWirdNurGeschriebenWennErGenanntIst()
    {
        var nurTagbetrieb = EntfeuchterSteuerungService.Schreibumfang(Soll(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { EntfeuchterSteuerungService.Entitaeten.Tagbetrieb });

        Assert.Empty(nurTagbetrieb.Zahlen);
        Assert.True(nurTagbetrieb.Tagbetrieb);
        Assert.False(nurTagbetrieb.VpdRegelung);
        Assert.False(nurTagbetrieb.RegelungSchalten);
    }

    [Fact]
    public void EinLeererAuftragSchreibtNichts()
    {
        var umfang = EntfeuchterSteuerungService.Schreibumfang(Soll(), new HashSet<string>());

        Assert.Empty(umfang.Zahlen);
        Assert.False(umfang.VpdRegelung || umfang.Tagbetrieb || umfang.RegelungSchalten);
    }

    // ------------------------------------------------------------------ Beschriftung

    [Fact]
    public void JederStartwertHatEinenAnzeigenamenMitEinheit()
    {
        // Die Abweichungs-Anzeige nennt den Helfer beim Namen; ohne Eintrag stünde dort die rohe Entity-ID.
        foreach (var id in EntfeuchterSteuerungService.Entitaeten.Startwerte.Where(i => i.StartsWith("input_number.", StringComparison.OrdinalIgnoreCase)))
        {
            var (name, einheit) = EntfeuchterSteuerungService.BeschriftungFuer(id);
            Assert.NotEqual(id, name);
            Assert.False(string.IsNullOrWhiteSpace(einheit), $"{id} hat keine Einheit.");
        }

        Assert.Equal(("unbekannt", string.Empty), EntfeuchterSteuerungService.BeschriftungFuer("unbekannt"));
    }
}

