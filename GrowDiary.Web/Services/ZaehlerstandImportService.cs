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
/// <see cref="ZaehlerAnlass.Tag"/> an. Damit rechnet
/// <c>KostenSeiteService.StromBerechnen</c> unveraendert weiter — es sammelt
/// ohnehin nur die Staende, die in die Laufzeit des Grows fallen.</para>
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
    public sealed record Ergebnis(
        bool Erfolg,
        int Angelegt,
        int Uebersprungen,
        DateTime? VonUtc,
        DateTime? BisUtc,
        string Hinweis);

    /// <summary>Die Staende eines Grows aus der HA-Statistik nachziehen.</summary>
    public async Task<Ergebnis> NachziehenAsync(int growId, CancellationToken ct = default)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null)
        {
            return new Ergebnis(false, 0, 0, null, null, $"Grow {growId} existiert nicht.");
        }

        var entityId = _seite.StromQuelle.ZaehlerEntityId;
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return new Ergebnis(false, 0, 0, null, null,
                "Keine Strom-Quelle eingerichtet — kWh-Zaehler in den Kosten-Einstellungen waehlen.");
        }

        // Einen Tag vor dem Start mitnehmen: der Verbrauch des ersten Tages ist
        // die Differenz zum Stand davor. Ohne ihn faengt die Rechnung erst am
        // zweiten Tag an.
        var von = grow.StartDate.Date.AddDays(-1).ToUniversalTime();
        var bis = (grow.EndDate?.Date.AddDays(1) ?? DateTime.UtcNow.Date.AddDays(1)).ToUniversalTime();

        await using var socket = await HomeAssistantSocket.OeffnenAsync(
            _haSettings.GetEffectiveHomeAssistantSettings(), ct);
        if (socket is null)
        {
            return new Ergebnis(false, 0, 0, null, null,
                "Keine Verbindung zu Home Assistant — Adresse und Token in den Einstellungen pruefen.");
        }

        var antwort = await socket.BefehlAsync("recorder/statistics_during_period", new Dictionary<string, object?>
        {
            ["start_time"] = von.ToString("o", CultureInfo.InvariantCulture),
            ["end_time"] = bis.ToString("o", CultureInfo.InvariantCulture),
            ["statistic_ids"] = new[] { entityId },
            ["period"] = "day",
            ["types"] = new[] { "state" },
        }, ct);

        if (!antwort.Erfolg || antwort.Ergebnis is not { } ergebnis)
        {
            return new Ergebnis(false, 0, 0, null, null,
                antwort.Fehler ?? "Home Assistant hat keine Statistik geliefert.");
        }

        if (!ergebnis.TryGetProperty(entityId, out var reihe) || reihe.ValueKind != JsonValueKind.Array)
        {
            return new Ergebnis(false, 0, 0, null, null,
                $"Fuer {entityId} liegt keine Langzeitstatistik vor. Der Sensor braucht eine state_class "
                + "(total oder total_increasing), sonst legt Home Assistant keine an.");
        }

        // forkai.97: Die Dublettenpruefung laeuft ueber das ORTSDATUM, nicht ueber
        // das UTC-Datum. Home Assistant beginnt seine Tagesbuckets um lokale
        // Mitternacht — in Europe/Berlin also 22:00 oder 23:00 UTC des Vortags.
        // Wer in UTC vergleicht, liegt systematisch einen Tag daneben, laesst
        // Ueberlappungen durch und mischt die Reihe.
        //
        // Und eine durchmischte Reihe ist hier nicht nur unschoen: KwhZwischen
        // deutet einen Rueckwaertssprung als Zaehlerwechsel und addiert dann den
        // VOLLEN Zaehlerstand. Ein einziger falsch einsortierter Stand hebt die
        // Summe damit um mehrere tausend kWh.
        var alle = _kosten.GetZaehlerstaende();
        var vorhanden = alle
            .Select(s => s.ZeitpunktUtc.ToLocalTime().Date)
            .ToHashSet();

        // Nur VOR dem ersten vorhandenen Stand nachtragen. Innerhalb eines
        // Zeitraums, den der Worker schon abdeckt, wird nichts eingefuegt — dort
        // liegen die echten Messzeitpunkte, und ein Importwert daneben erzeugt
        // genau den Rueckwaertssprung, den KwhZwischen falsch deutet.
        var ersterVorhandener = alle
            .Where(s => s.GrowId == grow.Id || s.GrowId is null)
            .OrderBy(s => s.ZeitpunktUtc)
            .Select(s => (DateTime?)s.ZeitpunktUtc)
            .FirstOrDefault();

        var angelegt = 0;
        var uebersprungen = 0;
        DateTime? ersterTag = null;
        DateTime? letzterTag = null;

        foreach (var eintrag in reihe.EnumerateArray())
        {
            if (Zeitpunkt(eintrag) is not { } zeitpunkt) continue;
            if (Zahl(eintrag, "state") is not { } kwh) continue;
            if (zeitpunkt.Date < von.Date || zeitpunkt.Date > bis.Date) continue;

            if (ersterVorhandener is { } grenze && zeitpunkt >= grenze)
            {
                uebersprungen++;
                continue;
            }

            if (!vorhanden.Add(zeitpunkt.ToLocalTime().Date))
            {
                uebersprungen++;
                continue;
            }

            // Die Phase kommt aus dem Grow selbst, nicht aus einem HA-Helfer:
            // GrowStageResolver leitet sie aus StartDate und FlipDate ab und ist
            // damit auch rueckwirkend richtig.
            var phase = GrowStageResolver.Resolve(grow, zeitpunkt);

            _kosten.CreateZaehlerstand(new Zaehlerstand
            {
                ZeitpunktUtc = zeitpunkt,
                Kwh = kwh,
                Anlass = ZaehlerAnlass.Tag,
                GrowId = grow.Id,
                Phase = phase.ToString(),
            });

            angelegt++;
            ersterTag ??= zeitpunkt;
            letzterTag = zeitpunkt;
        }

        var hinweis = angelegt == 0
            ? uebersprungen > 0
                ? $"Nichts nachzutragen — fuer alle {uebersprungen} Tage lag bereits ein Stand vor."
                : "Home Assistant hat fuer den Zeitraum keine Tageswerte geliefert."
            : $"{angelegt} Zaehlerstaende nachgetragen"
                + (uebersprungen > 0 ? $", {uebersprungen} Tage waren schon vorhanden" : string.Empty)
                + ".";

        return new Ergebnis(true, angelegt, uebersprungen, ersterTag, letzterTag, hinweis);
    }

    /// <summary>Der Zeitstempel einer Statistikzeile — HA liefert ms seit Epoche oder ISO-Text.</summary>
    private static DateTime? Zeitpunkt(JsonElement eintrag)
    {
        if (!eintrag.TryGetProperty("start", out var start)) return null;

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
