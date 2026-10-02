using System.Globalization;
using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.90): Zaehlerstaende nachtraeglich aus der Langzeitstatistik
/// von Home Assistant holen.
/// </summary>
/// <remarks>
/// <para>Der Worker haelt ab seiner Einrichtung jeden Tag einen Stand fest. Fuer
/// alles davor gibt es nichts — ein Grow, der vor der Einrichtung begann, zeigt
/// deshalb nur die Tage seit der Einrichtung als Strom. Die Zahl ist nicht
/// falsch, aber sie beantwortet eine andere Frage als die, die daneben steht.</para>
/// <para>Home Assistant behaelt die Statistik dauerhaft, mit Tagesaufloesung.
/// Dieser Dienst liest sie ueber <c>recorder/statistics_during_period</c> und
/// legt je Tag einen <see cref="Zaehlerstand"/> mit
/// <see cref="ZaehlerAnlass.Tag"/> an — fuer den Zaehler, an dem der Grow
/// misst (eigener Zaehler seines Zelts oder der gemeinsame). Die Staende gehoeren
/// dem Zaehler; <see cref="StromAufteilung"/> verteilt sie auf alle Grows, die
/// an ihm laufen.</para>
/// <para><b>Bestehende Staende werden nie ueberschrieben.</b> Ein Tag, fuer den
/// schon ein Stand existiert, wird uebersprungen. Der Import ist damit
/// wiederholbar, ohne Dubletten zu erzeugen.</para>
/// </remarks>
public sealed class ZaehlerstandImportService
{
    private readonly KostenRepository _kosten;
    private readonly KostenSeiteService _seite;
    private readonly GrowRepository _grows;
    private readonly HomeAssistantSettingsRepository _haSettings;

    public ZaehlerstandImportService(
        KostenRepository kosten,
        KostenSeiteService seite,
        GrowRepository grows,
        HomeAssistantSettingsRepository haSettings)
    {
        _kosten = kosten;
        _seite = seite;
        _grows = grows;
        _haSettings = haSettings;
    }

    /// <param name="Angelegt">Wie viele Staende neu geschrieben wurden.</param>
    /// <param name="Uebersprungen">Tage, fuer die schon ein Stand vorlag.</param>
    /// <param name="VonUtc">Erster importierter Tag, null wenn nichts kam.</param>
    /// <param name="BisUtc">Letzter importierter Tag, null wenn nichts kam.</param>
    /// <param name="Hinweis">Was passiert ist, in einem Satz — auch im Erfolgsfall.</param>
    /// <param name="Unstimmig">Tageswerte, die zwischen vorhandenen Staenden rueckwaerts gesprungen waeren.</param>
    public sealed record Ergebnis(
        bool Erfolg,
        int Angelegt,
        int Uebersprungen,
        int Geloescht,
        DateTime? VonUtc,
        DateTime? BisUtc,
        string Hinweis,
        int Unstimmig = 0);

    /// <summary>Ein Tageswert aus der Statistik: der Zaehlerstand am ENDE des Tages.</summary>
    public sealed record Tageswert(DateTime EndeUtc, double Kwh);

    /// <summary>Was ein Import tun wuerde — rein, ohne Datenbank und ohne Home Assistant.</summary>
    public sealed record Plan(
        IReadOnlyList<Zaehlerstand> Neu, IReadOnlyList<int> Loeschen, int Uebersprungen, int Unstimmig);

    /// <summary>Die Staende eines Grows aus der HA-Statistik nachziehen.</summary>
    /// <param name="neuAufbauen">
    /// Die Reihe des Zaehlers im Zeitraum dieses Grows verwerfen und komplett aus
    /// der Statistik neu aufbauen. Gebraucht, wenn sich Staende aus verschiedenen
    /// Quellen gemischt haben — so entstanden aus 1.585 kWh einmal 10.024.
    /// </param>
    public async Task<Ergebnis> NachziehenAsync(int growId, bool neuAufbauen = false, CancellationToken ct = default)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null)
        {
            return new Ergebnis(false, 0, 0, 0, null, null, $"Grow {growId} existiert nicht.");
        }

        var quelle = _seite.StromQuelle;
        var entityId = quelle.ZaehlerFuerZelt(grow.TentId);
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return new Ergebnis(false, 0, 0, 0, null, null,
                "Keine Strom-Quelle eingerichtet — kWh-Zaehler in den Kosten-Einstellungen waehlen.");
        }

        var jetzt = DateTime.UtcNow;
        var (vonUtc, bisUtc) = Zeitraum(grow, jetzt);

        await using var socket = await HomeAssistantSocket.OeffnenAsync(
            _haSettings.GetEffectiveHomeAssistantSettings(), ct);
        if (socket is null)
        {
            return new Ergebnis(false, 0, 0, 0, null, null,
                "Keine Verbindung zu Home Assistant — Adresse und Token in den Einstellungen pruefen.");
        }

        // Ein Tag VOR dem Start mit abfragen: dessen Wert gilt am Ende jenes
        // Tages, also genau zu Beginn des ersten Grow-Tags.
        var antwort = await socket.BefehlAsync("recorder/statistics_during_period", new Dictionary<string, object?>
        {
            ["start_time"] = vonUtc.AddDays(-1).ToString("o", CultureInfo.InvariantCulture),
            ["end_time"] = bisUtc.ToString("o", CultureInfo.InvariantCulture),
            ["statistic_ids"] = new[] { entityId },
            ["period"] = "day",
            ["types"] = new[] { "state" },
        }, ct);

        if (!antwort.Erfolg || antwort.Ergebnis is not { } ergebnis)
        {
            return new Ergebnis(false, 0, 0, 0, null, null,
                antwort.Fehler ?? "Home Assistant hat keine Statistik geliefert.");
        }

        if (!ergebnis.TryGetProperty(entityId, out var reihe) || reihe.ValueKind != JsonValueKind.Array)
        {
            return new Ergebnis(false, 0, 0, 0, null, null,
                $"Fuer {entityId} liegt keine Langzeitstatistik vor. Der Sensor braucht eine state_class "
                + "(total oder total_increasing), sonst legt Home Assistant keine an.");
        }

        var werte = new List<Tageswert>();
        foreach (var eintrag in reihe.EnumerateArray())
        {
            if (Ende(eintrag) is not { } ende) continue;
            if (Zahl(eintrag, "state") is not { } kwh) continue;
            werte.Add(new Tageswert(ende, kwh));
        }

        var plan = Planen(grow, entityId, quelle, _kosten.GetZaehlerstaende(), werte, neuAufbauen, jetzt);

        foreach (var id in plan.Loeschen) _kosten.DeleteZaehlerstand(id);
        foreach (var stand in plan.Neu) stand.Id = _kosten.CreateZaehlerstand(stand);

        var angelegt = plan.Neu.Count;
        var hinweis = angelegt == 0
            ? plan.Uebersprungen > 0
                ? $"Nichts nachzutragen — fuer alle {plan.Uebersprungen} Tage lag bereits ein Stand vor."
                : plan.Unstimmig > 0
                    ? "Nichts nachgetragen."
                    : "Home Assistant hat fuer den Zeitraum keine Tageswerte geliefert."
            : $"{angelegt} Zaehlerstaende nachgetragen"
                + (plan.Uebersprungen > 0 ? $", {plan.Uebersprungen} Tage waren schon vorhanden" : string.Empty)
                + ".";

        if (plan.Unstimmig > 0)
        {
            hinweis += $" {plan.Unstimmig} Tageswerte passten nicht zwischen die vorhandenen Staende "
                + "(der Zaehler waere rueckwaerts gelaufen) und wurden ausgelassen.";
        }

        if (plan.Loeschen.Count > 0)
        {
            hinweis = $"Reihe neu aufgebaut: {plan.Loeschen.Count} alte Staende verworfen, " + hinweis;
        }

        return new Ergebnis(true, angelegt, plan.Uebersprungen, plan.Loeschen.Count,
            plan.Neu.FirstOrDefault()?.ZeitpunktUtc, plan.Neu.LastOrDefault()?.ZeitpunktUtc, hinweis, plan.Unstimmig);
    }

    /// <summary>
    /// Der Zeitraum eines Grows in UTC: vom Beginn seines ersten bis zum Ende
    /// seines letzten Ortstags (laufend: bis heute einschließlich).
    /// </summary>
    public static (DateTime VonUtc, DateTime BisUtc) Zeitraum(GrowRun grow, DateTime jetztUtc)
    {
        var heute = jetztUtc.ToLocalTime().Date;
        var letzterTag = grow.EndDate?.Date ?? heute;
        return (StromAufteilung.TagesbeginnUtc(grow.StartDate.Date), StromAufteilung.TagesbeginnUtc(letzterTag.AddDays(1)));
    }

    /// <summary>Plant den Import — welche Staende neu kommen, welche weichen.</summary>
    /// <remarks>
    /// <para><b>Nur der Zaehler dieses Grows (02.10.2026).</b> Vorher zaehlte jeder
    /// Stand ohne Grow aus jeder Zeit als „schon vorhanden". Der Worker schreibt
    /// solche Staende taeglich, solange kein Grow laeuft — beim zweiten Grow lag
    /// der erste vorhandene Stand deshalb Monate zurueck, und jeder HA-Tageswert
    /// wurde uebersprungen („Nichts nachzutragen"). Und der Neuaufbau loeschte
    /// alles im Zeitraum, auch die Staende eines anderen Zaehlers.</para>
    ///
    /// <para><b>Ein Stand je Ortstag.</b> Liegt fuer einen Tag schon ein Stand
    /// dieses Zaehlers vor (Worker, Grow-Start, Phasenwechsel), bleibt er. Fuer
    /// alle anderen Tage im Zeitraum kommt der HA-Wert dazu — nicht mehr nur VOR
    /// dem ersten vorhandenen Stand.</para>
    ///
    /// <para><b>Der Zeitpunkt ist das Tagesende.</b> Home Assistant liefert je
    /// Tag den Stand am ENDE des Zeitraums (letzte Stunde des Tages), bezeichnet
    /// mit dessen Beginn. Bis forkai.98 wurde der Wert mit dem Beginn abgelegt —
    /// einen Tag zu frueh. Ein Wert, der neben einen echten Worker-Stand fiel,
    /// lag dadurch VOR einem kleineren Stand, und <c>KwhZwischen</c> las den
    /// Ruecksprung als Zaehlerwechsel. Genau so entstanden einmal 10.024 statt
    /// 1.585 kWh. Abgelegt wird deshalb das Ende; was trotzdem rueckwaerts
    /// springen wuerde, bleibt draussen und wird gemeldet.</para>
    ///
    /// <para><b>Der Neuaufbau verwirft nur Staende dieses Zaehlers im Zeitraum
    /// dieses Grows</b> — nicht den Tag davor (das Ende des Vorgaengers) und
    /// nichts von einem anderen Zaehler.</para>
    /// </remarks>
    public static Plan Planen(
        GrowRun grow, string zaehler, StromQuelle quelle, IReadOnlyList<Zaehlerstand> alle,
        IReadOnlyList<Tageswert> werte, bool neuAufbauen, DateTime jetztUtc)
    {
        var (vonUtc, bisUtc) = Zeitraum(grow, jetztUtc);
        var reihe = alle
            .Where(s => string.Equals(quelle.ZaehlerVonStand(s), zaehler, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var loeschen = neuAufbauen
            ? reihe.Where(s => s.ZeitpunktUtc >= vonUtc && s.ZeitpunktUtc <= bisUtc).Select(s => s.Id).ToList()
            : [];

        var bleibt = reihe
            .Where(s => !loeschen.Contains(s.Id))
            .OrderBy(s => s.ZeitpunktUtc).ThenBy(s => s.Id)
            .ToList();
        var belegteTage = bleibt.Select(s => s.ZeitpunktUtc.ToLocalTime().Date).ToHashSet();

        var neu = new List<Zaehlerstand>();
        var uebersprungen = 0;
        var unstimmig = 0;

        foreach (var wert in werte.OrderBy(w => w.EndeUtc))
        {
            if (wert.EndeUtc < vonUtc || wert.EndeUtc > bisUtc) continue;
            // Der laufende Tag ist noch nicht zu Ende — sein „Endstand" laege in
            // der Zukunft. Den Stand von heute haelt der Worker.
            if (wert.EndeUtc > jetztUtc) continue;

            var tag = wert.EndeUtc.ToLocalTime().Date;
            if (belegteTage.Contains(tag))
            {
                uebersprungen++;
                continue;
            }

            var vorher = bleibt.LastOrDefault(s => s.ZeitpunktUtc < wert.EndeUtc);
            var nachher = bleibt.FirstOrDefault(s => s.ZeitpunktUtc > wert.EndeUtc);
            if ((vorher is not null && vorher.Kwh > wert.Kwh) || (nachher is not null && nachher.Kwh < wert.Kwh))
            {
                unstimmig++;
                continue;
            }

            var stand = new Zaehlerstand
            {
                ZeitpunktUtc = wert.EndeUtc,
                Kwh = wert.Kwh,
                Anlass = ZaehlerAnlass.Tag,
                // Notiz fuer die Tabelle; gerechnet wird ueber den Zaehler.
                GrowId = grow.Id,
                // Die Phase des Tages, den dieser Stand abschliesst.
                Phase = GrowStageResolver.Resolve(grow, tag.AddDays(-1)).ToString(),
                ZaehlerEntityId = zaehler,
            };

            neu.Add(stand);
            belegteTage.Add(tag);
            var stelle = bleibt.FindIndex(s => s.ZeitpunktUtc > wert.EndeUtc);
            bleibt.Insert(stelle < 0 ? bleibt.Count : stelle, stand);
        }

        return new Plan(neu, loeschen, uebersprungen, unstimmig);
    }

    /// <summary>
    /// Das Ende eines Statistik-Zeitraums — HA liefert ms seit Epoche oder ISO-Text.
    /// Fehlt <c>end</c>, gilt Beginn plus ein Ortstag.
    /// </summary>
    private static DateTime? Ende(JsonElement eintrag)
    {
        if (Zeitpunkt(eintrag, "end") is { } ende) return ende;
        return Zeitpunkt(eintrag, "start") is { } start
            ? StromAufteilung.TagesbeginnUtc(start.ToLocalTime().Date.AddDays(1))
            : null;
    }

    private static DateTime? Zeitpunkt(JsonElement eintrag, string feld)
    {
        if (!eintrag.TryGetProperty(feld, out var start)) return null;

        if (start.ValueKind == JsonValueKind.Number && start.TryGetInt64(out var ms))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }

        if (start.ValueKind == JsonValueKind.String
            && DateTime.TryParse(start.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var geparst))
        {
            return geparst;
        }

        return null;
    }

    private static double? Zahl(JsonElement eintrag, string feld)
        => eintrag.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.Number
            ? wert.GetDouble()
            : null;
}
