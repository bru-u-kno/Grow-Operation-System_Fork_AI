using System.Collections.Concurrent;
using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI: Die Live-Kachel „Strom" — die Leistung der Steckdose vor dem Zelt, darunter
/// der Verbrauch seit Mitternacht und was er kostet.
/// </summary>
/// <remarks>
/// <para><b>Keine neue Zuordnung.</b> Die Leistungs-Entität steht schon auf der
/// Kostenseite (<see cref="StromQuelle.LeistungEntityId"/>); die Kachel nimmt sie
/// von dort. Wer dort nichts eingetragen hat, sieht keine Kachel — kein leerer Platzhalter.</para>
///
/// <para><b>Wie jede andere Messgröße.</b> Der Wert kommt als Zustand <c>power</c> in dasselbe
/// Wörterbuch wie pH und EC. Dadurch schreibt der Erfassungstakt ihn mit in die Rohwerte,
/// und der Verlauf hat eine Kurve, ohne dass dafür etwas Eigenes gebaut wird.</para>
///
/// <para><b>Verbrauch seit Mitternacht</b> aus dem Verlauf des kWh-Zählers in Home Assistant
/// (<see cref="EntfeuchterZusatzSteuerungService.EnergieHeute"/> — dieselbe Rechnung wie bei
/// den Entfeuchtern), nicht aus den Zählerständen der Kostenseite: die sind nur einmal am Tag
/// festgehalten. Zwei Minuten zwischengespeichert, weil die Live-Seite alle 30 Sekunden fragt.</para>
/// </remarks>
public sealed class StromKachel
{
    /// <summary>Die Kennung der Messgröße — in Kachel, Rohwerten und Verlauf dieselbe.</summary>
    public const string Key = "power";

    private static readonly TimeSpan Haltbar = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<string, (DateTime Stand, double? Kwh)> HeuteKwh = new(StringComparer.OrdinalIgnoreCase);

    private readonly AppSettingsRepository _einstellungen;
    private readonly HomeAssistantService _ha;

    public StromKachel(AppSettingsRepository einstellungen, HomeAssistantService ha)
    {
        _einstellungen = einstellungen;
        _ha = ha;
    }

    /// <summary>
    /// Legt die Leistung als Zustand <c>power</c> zu den Zuständen eines Zelts.
    /// Nichts, wenn keine Leistungs-Entität eingetragen ist, Home Assistant keine Zahl liefert,
    /// der Zustand schon da ist — oder das Zelt einen eigenen Zähler hat.
    /// </summary>
    /// <remarks>
    /// Die Leistung der Kostenseite ist die der gemeinsamen Steckdose. Ein Zelt mit eigenem
    /// kWh-Zähler misst woanders: seine Kachel würde die Watt der einen und die kWh der anderen
    /// Steckdose zeigen, und sein Verlauf behauptete eine Messung, die es nicht gibt.
    /// </remarks>
    public async Task ErgaenzenAsync(
        int tentId, Dictionary<string, HomeAssistantState> zustaende, HomeAssistantSettings settings, CancellationToken ct)
    {
        if (zustaende.ContainsKey(Key)) return;
        var quelle = KostenSeiteService.StromQuelleLesen(_einstellungen);
        if (quelle.HatEigenenZaehler(tentId)) return;
        var entitaet = quelle.LeistungEntityId;
        if (string.IsNullOrWhiteSpace(entitaet)) return;

        var zustand = InWatt(await _ha.GetEntityStateAsync(settings, entitaet.Trim(), ct));
        if (zustand?.NumericValue is not null) zustaende[Key] = zustand;
    }

    /// <summary>Schreibt unter die Kachel „Strom": heute verbraucht und was das kostet.</summary>
    public async Task KartenErgaenzenAsync(
        IEnumerable<MetricCard> karten, Tent tent, HomeAssistantSettings settings, CancellationToken ct)
    {
        var karte = karten.FirstOrDefault(k => k.Key == Key);
        if (karte is null) return;

        var zaehler = KostenSeiteService.StromQuelleLesen(_einstellungen).ZaehlerFuerZelt(tent.Id);
        if (zaehler is null) return;

        var kwh = await HeuteAsync(settings, zaehler, DateTime.UtcNow, ct);
        if (Fusszeile(kwh, GrowCostService.StrompreisLesen(_einstellungen)) is { } zeile) karte.Hint = zeile;
    }

    private async Task<double?> HeuteAsync(HomeAssistantSettings settings, string zaehler, DateTime jetztUtc, CancellationToken ct)
    {
        if (HeuteKwh.TryGetValue(zaehler, out var gemerkt)
            && jetztUtc - gemerkt.Stand < Haltbar
            && gemerkt.Stand.ToLocalTime().Date == jetztUtc.ToLocalTime().Date)
        {
            return gemerkt.Kwh;
        }

        var mitternacht = jetztUtc.ToLocalTime().Date.ToUniversalTime();
        // Testbetrieb: Home Assistant gibt es nicht, der Zähler des Testbestands ist aber eine reine
        // Funktion der Zeit — so steht auch dort eine Zahl unter der Kachel.
        var kwh = DemoData.IsEnabled
            ? Math.Round(DemoData.StromZaehlerKwh(jetztUtc) - DemoData.StromZaehlerKwh(mitternacht), 2)
            : EntfeuchterZusatzSteuerungService.EnergieHeute(
            await _ha.GetVerlaufAsync(settings, zaehler, mitternacht, jetztUtc, ct));
        HeuteKwh[zaehler] = (jetztUtc, kwh);
        return kwh;
    }

    /// <summary>
    /// „heute 12,4 kWh · ca. 3,60 €". Ohne Preis nur der Verbrauch, ohne Verbrauch nichts —
    /// dann bleibt die Beschriftung des Sensors stehen, statt etwas zu behaupten.
    /// </summary>
    public static string? Fusszeile(double? kwh, double? centProKwh)
    {
        if (kwh is not { } k) return null;
        var de = CultureInfo.GetCultureInfo(Deutsch.Kennung);
        var text = $"heute {k.ToString("0.0", de)} kWh";
        return centProKwh is { } preis ? $"{text} · ca. {(k * preis / 100).ToString("0.00", de)} €" : text;
    }

    /// <summary>
    /// Bietet einem eigenen Layout die Strom-Kachel einmal an: sie kommt in den Klima-Bereich, hinter
    /// die letzte Messwert-Kachel. Wahr, wenn das Layout dabei geändert wurde und gespeichert werden muss.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum.</b> Ein selbst angeordnetes Layout enthält nur, was dort steht — neue Messgrößen
    /// erscheinen darin nie von selbst. Wer die Leistung auf der Kostenseite einträgt, würde die Kachel
    /// also nirgends finden. Die Installation richtet sie deshalb einmal ein, wie bei einem frischen Layout.</para>
    /// <para><b>Einmal heißt einmal.</b> Der Merker je Zelt bleibt stehen, auch wenn jemand die Kachel
    /// später entfernt — sie kommt nicht zurück. Ohne eingetragene Leistung passiert nichts und nichts
    /// wird gemerkt, damit sie nachkommt, sobald die Quelle da ist.</para>
    /// </remarks>
    public static bool Anbieten(DashboardLayout layout, AppSettingsRepository einstellungen)
    {
        var merker = $"dashboard:strom-angeboten:{layout.TentId}";
        if (einstellungen.GetValue(merker) == "1") return false;
        var quelle = KostenSeiteService.StromQuelleLesen(einstellungen);
        if (string.IsNullOrWhiteSpace(quelle.LeistungEntityId)) return false;
        if (quelle.HatEigenenZaehler(layout.TentId)) return false;   // dort gibt es keine Strom-Kachel (siehe ErgaenzenAsync)

        einstellungen.SetValue(merker, "1");

        static bool Messwert(DashboardTile t) => t.Kind == DashboardTileKind.Metric && !string.IsNullOrWhiteSpace(t.MetricKey);
        if (layout.Sections.Any(s => s.Tiles.Any(t => Messwert(t) && t.MetricKey == Key))) return false;

        var klima = new[] { "temperature", "humidity", "vpd" };
        var bereich = layout.Sections.FirstOrDefault(s => s.Tiles.Any(t => Messwert(t) && klima.Contains(t.MetricKey)))
            ?? layout.Sections.FirstOrDefault(s => s.Tiles.Any(Messwert));
        if (bereich is null) return false;

        var nach = bereich.Tiles.FindLastIndex(Messwert);
        bereich.Tiles.Insert(nach + 1, new DashboardTile { Kind = DashboardTileKind.Metric, MetricKey = Key, Span = 1 });
        return true;
    }

    /// <summary>
    /// Bietet jedem Zelt mit eigenem gespeicherten Layout die Strom-Kachel an (<see cref="Anbieten"/>).
    /// Läuft im Erfassungstakt, nicht beim Lesen des Layouts: ein Lesezugriff darf nichts speichern —
    /// er gälte sonst für jeden Schlüssel, auch für einen, der nur dokumentieren darf.
    /// </summary>
    /// <returns>Wie viele Layouts geändert wurden.</returns>
    public static int AnbietenFuerAlleZelte(GrowRepository zelte, DashboardLayoutRepository layouts, AppSettingsRepository einstellungen)
    {
        var geaendert = 0;
        foreach (var zelt in zelte.GetTents())
        {
            if (layouts.GetSaved(zelt.Id) is not { } gespeichert) continue;   // das Standard-Layout zeigt sie ohnehin
            if (!Anbieten(gespeichert, einstellungen)) continue;
            layouts.Save(gespeichert);
            geaendert++;
        }
        return geaendert;
    }

    /// <summary>
    /// Eine Leistung in kW wird zu W: die Kachel, die Kurve und die Rohwerte rechnen alle in Watt,
    /// und eine Kurve, die zwischen 1,1 und 1100 springt, weil der Sensor die Einheit wechselt, ist keine.
    /// </summary>
    public static HomeAssistantState? InWatt(HomeAssistantState? zustand)
    {
        if (zustand?.NumericValue is not { } wert) return zustand;
        if (!string.Equals(zustand.UnitOfMeasurement?.Trim(), "kW", StringComparison.OrdinalIgnoreCase)) return zustand;

        return new HomeAssistantState
        {
            EntityId = zustand.EntityId,
            State = (wert * 1000).ToString("0.###", CultureInfo.InvariantCulture),
            FriendlyName = zustand.FriendlyName,
            UnitOfMeasurement = "W",
            LastChanged = zustand.LastChanged,
            LastUpdated = zustand.LastUpdated,
            NumericValue = wert * 1000,
        };
    }
}
