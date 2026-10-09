using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (02.10.2026): Der Wochenplan schreibt nur, was der HA-Helfer annimmt —
/// und haelt einen begrenzten Wert danach nicht fuer eine Handaenderung.
/// </summary>
/// <remarks>
/// <para><b>Der Fund.</b> Planwerte gingen unbegrenzt an <c>input_number.set_value</c>.
/// Der Plan nennt etwa RH 85 %, der Helfer <c>co2_rh_obergrenze</c> reicht bis 80.
/// Home Assistant lehnte ab — bei jedem Lauf wieder, der Helfer blieb auf dem Wert
/// einer frueheren Woche.</para>
/// <para><b>Die Falle daneben.</b> Die Handerkennung vergleicht „zuletzt
/// geschrieben" mit dem gelesenen Wert. Wer begrenzt, aber den Planwert merkt,
/// erklaert jeden begrenzten Helfer beim naechsten Lauf zu „von dir gesetzt".</para>
/// </remarks>
public sealed class WochenplanHelferspanneTests
{
    private const string RhHelfer = "input_number.co2_rh_obergrenze";

    /// <summary>Ein Home Assistant, das wie das echte ausserhalb von min/max ablehnt.</summary>
    private sealed class StrengesHa
    {
        public Dictionary<string, double> Werte { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Entity, double Wert)> Schreibversuche { get; } = [];

        public Task<double?> Lesen(string entity)
            => Task.FromResult(Werte.TryGetValue(entity, out var w) ? w : (double?)null);

        public Task<bool> Schreiben(string entity, double wert)
        {
            Schreibversuche.Add((entity, wert));
            var (min, max) = SteuerungBauteile.Spanne(entity);
            if (wert < min || wert > max) return Task.FromResult(false);
            Werte[entity] = wert;
            return Task.FromResult(true);
        }
    }

    private static Task<int> Lauf(StrengesHa ha, WochenplanSyncStand stand, double planwert, bool ersterLauf = false)
        => WochenplanSyncService.HelferNachziehenAsync(
            [(RhHelfer, planwert)], ha.Lesen, ha.Schreiben, stand, ersterLauf,
            NullLogger.Instance);

    [Fact]
    public async Task PlanwertUeberDerSpanne_KommtBegrenztAn()
    {
        var (_, max) = SteuerungBauteile.Spanne(RhHelfer);
        var planwert = max + 5;
        var ha = new StrengesHa { Werte = { [RhHelfer] = 70 } };
        var stand = new WochenplanSyncStand();
        await Lauf(ha, stand, 70, ersterLauf: true);

        await Lauf(ha, stand, planwert);

        Assert.True(ha.Werte[RhHelfer] == max,
            $"Der Plan will {planwert} %, der Helfer reicht bis {max}. In HA steht weiter "
            + $"{ha.Werte[RhHelfer]} — set_value wurde abgelehnt (Versuche: "
            + string.Join(", ", ha.Schreibversuche.Select(v => v.Wert)) + ").");
    }

    /// <summary>
    /// Der zweite Lauf — dieselbe Woche, Seite nicht neu: kein „von dir gesetzt".
    /// </summary>
    [Fact]
    public async Task BegrenzterWert_GiltBeimNaechstenLaufNichtAlsHandaenderung()
    {
        var (_, max) = SteuerungBauteile.Spanne(RhHelfer);
        var ha = new StrengesHa { Werte = { [RhHelfer] = 70 } };
        var stand = new WochenplanSyncStand();
        await Lauf(ha, stand, 70, ersterLauf: true);

        await Lauf(ha, stand, max + 5);
        var zweiter = await Lauf(ha, stand, max + 5);

        Assert.True(stand.VonDir.Count == 0,
            "Nach dem Begrenzen gilt der Helfer als „von dir gesetzt\" — der Plan liesse ihn fuer "
            + "immer in Ruhe. Gemerkt werden muss der Wert, der wirklich in HA steht.");
        Assert.Equal(0, zweiter);

        // Und die Woche danach passt wieder in die Spanne: der Plan zieht nach.
        await Lauf(ha, stand, max - 10);
        Assert.Equal(max - 10, ha.Werte[RhHelfer]);
    }

    /// <summary>Die Handerkennung selbst bleibt scharf.</summary>
    [Fact]
    public async Task EchteHandaenderung_WirdWeiterErkannt()
    {
        var (_, max) = SteuerungBauteile.Spanne(RhHelfer);
        var ha = new StrengesHa { Werte = { [RhHelfer] = 70 } };
        var stand = new WochenplanSyncStand();
        await Lauf(ha, stand, 70, ersterLauf: true);
        await Lauf(ha, stand, max + 5);

        ha.Werte[RhHelfer] = 65; // ein Mensch dreht am Regler

        await Lauf(ha, stand, max + 5);

        Assert.Contains(RhHelfer, stand.VonDir);
        Assert.Equal(65, ha.Werte[RhHelfer]);
    }

    // ---- Helfer außerhalb des Katalogs (docs/pruefung-2026-10-01.md) ----

    /// <summary>
    /// Ein vom Nutzer angelegter Helfer: der Katalog kennt ihn nicht, Home
    /// Assistant meldet seine Spanne als Attribute und lehnt außerhalb ab.
    /// </summary>
    private sealed class FremderHelfer(string entity, double min, double max)
    {
        public double Wert { get; set; } = (min + max) / 2;
        public List<double> Schreibversuche { get; } = [];

        public Task<HelferLesung> Lesen(string e)
            => Task.FromResult(new HelferLesung(Wert, min, max));

        public Task<bool> Schreiben(string e, double wert)
        {
            Assert.Equal(entity, e);
            Schreibversuche.Add(wert);
            if (wert < min || wert > max) return Task.FromResult(false);
            Wert = wert;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Ein Helfer, den der Katalog NICHT kennt — der Nutzer hat ihn selbst angelegt. Bis A-016 war
    /// das <c>vpd_ziel_unten</c>; der steht jetzt im Katalog (der Fork legt ihn an), die Regel
    /// „Spanne aus Home Assistant" gilt aber weiter für jeden Helfer, den der Plan beschreibt und der
    /// nicht im Katalog steht. Deshalb ein erfundener Name.
    /// </summary>
    private const string VpdHelfer = "input_number.eigenes_vpd_ziel";

    [Fact]
    public void DerFremdeHelferStehtNichtImKatalog()
    {
        // Selbsttest: stünde er im Katalog, prüften die Fälle unten nur den alten Weg.
        Assert.Throws<InvalidOperationException>(() => SteuerungBauteile.Spanne(VpdHelfer));
        // ... und der echte VPD-Helfer des Plans steht seit A-016 drin, mit der Spanne der Anlage.
        Assert.Equal((0.4, 2.0), SteuerungBauteile.Spanne(EntfeuchterSteuerungService.Entitaeten.VpdUnten));
    }

    [Theory]
    [InlineData(1.9, 1.6)]   // über der Spanne
    [InlineData(0.2, 0.4)]   // unter der Spanne
    public async Task EinHelferAusserhalbDesKatalogs_WirdAufDieSpanneAusHaBegrenzt(double planwert, double erwartet)
    {
        var ha = new FremderHelfer(VpdHelfer, min: 0.4, max: 1.6);
        var stand = new WochenplanSyncStand();
        await WochenplanSyncService.HelferNachziehenAsync(
            [(VpdHelfer, 1.0)], ha.Lesen, ha.Schreiben, stand, ersterLauf: true, NullLogger.Instance);

        await WochenplanSyncService.HelferNachziehenAsync(
            [(VpdHelfer, planwert)], ha.Lesen, ha.Schreiben, stand, ersterLauf: false, NullLogger.Instance);

        Assert.True(ha.Wert == erwartet,
            $"Der Plan will VPD {planwert}, der Helfer nimmt 0,4 bis 1,6. In HA steht {ha.Wert} — "
            + "set_value wurde abgelehnt (Versuche: " + string.Join(", ", ha.Schreibversuche) + ").");

        // Die Reparatur einmal wiederholen: zweiter Lauf, gleiche Woche — kein „von dir gesetzt".
        var zweiter = await WochenplanSyncService.HelferNachziehenAsync(
            [(VpdHelfer, planwert)], ha.Lesen, ha.Schreiben, stand, ersterLauf: false, NullLogger.Instance);
        Assert.Empty(stand.VonDir);
        Assert.Equal(0, zweiter);
    }

    /// <summary>Der Katalog geht vor — er ist die Vorlage, aus der der Fork seine Helfer anlegt.</summary>
    [Fact]
    public void DerKatalogGehtVorDerSpanneAusHa()
    {
        var (_, max) = SteuerungBauteile.Spanne(RhHelfer);
        Assert.Equal(max, SteuerungBauteile.AufSpanne(RhHelfer, max + 5, haMin: 0, haMax: max + 100));
    }

    /// <summary>
    /// Die Schicht, in der die Spanne entsteht: Home Assistants Antwort auf
    /// <c>GET /api/states/…</c>. Ohne die gelesenen Attribute käme beim Begrenzen nichts an.
    /// </summary>
    [Fact]
    public async Task DieSpanneKommtAusDenAttributenDesHelfers()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json(
            $$$"""{"entity_id":"{{{VpdHelfer}}}","state":"1.2","attributes":{"min":0.4,"max":1.6,"step":0.05,"unit_of_measurement":"kPa"}}"""));
        var ha = new HomeAssistantService(new StubHttpClientFactory(handler), NullLogger<HomeAssistantService>.Instance);

        var lesung = HelferLesung.Aus(await ha.GetEntityStateAsync(
            new HomeAssistantSettings { BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true }, VpdHelfer));

        Assert.Equal(new HelferLesung(1.2, 0.4, 1.6), lesung);
        Assert.Equal(1.6, SteuerungBauteile.AufSpanne(VpdHelfer, 1.9, lesung.Min, lesung.Max));
    }
}
