using System.Globalization;
using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.141, F-037): Überträgt die Grenzwerte des Forks (pH, EC,
/// Wassertemperatur) an die Alarmgrenzen des Bluelab Guardian.
/// </summary>
/// <remarks>
/// <para><b>Eine Quelle.</b> Gepflegt wird unter „Grenzwerte" — dem Plan folgend oder
/// fest. Das Gerät bekommt dieselben Zahlen, damit es nicht mit veralteten Grenzen
/// Alarm schlägt (am 23.09.2026 stand EC am Gerät auf 0,8–1,3 bei Planwert 1,6).</para>
/// <para><b>Ein Band fürs Gerät.</b> Der Guardian kennt nur eine Unter- und eine
/// Obergrenze. Hat die Regel ein Nachtband, bekommt das Gerät die weitere Spanne
/// aus Tag und Nacht — so meldet es nachts nichts Falsches; genau nach Tag und
/// Nacht unterscheidet weiter der Fork.</para>
/// <para><b>Sanft zur Cloud.</b> Geschrieben wird nur, was abweicht, nacheinander,
/// und derselbe Wert höchstens alle 30 Minuten — die Cloud übernimmt Änderungen
/// mit Verzögerung, und ohne Sperre schriebe jeder Lauf dieselbe Zahl erneut.</para>
/// </remarks>
public sealed class BluelabGrenzenService
{
    public const string Modul = "bluelab";

    /// <summary>Rolle → Messgröße der Fork-Regel, Seite (unten/oben), Edenic-Schlüssel.</summary>
    public static readonly IReadOnlyList<(string Rolle, string Metrik, bool Oben, string Schluessel)> Zuordnung = new[]
    {
        ("ph_low", "reservoir-ph", false, "setting.ph_low_alarm"),
        ("ph_high", "reservoir-ph", true, "setting.ph_high_alarm"),
        ("ec_low", "reservoir-ec", false, "setting.ec_low_alarm"),
        ("ec_high", "reservoir-ec", true, "setting.ec_high_alarm"),
        ("temp_low", "reservoir-temp", false, "setting.temp_low_alarm"),
        ("temp_high", "reservoir-temp", true, "setting.temp_high_alarm"),
    };

    private const string ZuletztSchluessel = "fork-ai:bluelab:zuletzt";
    private const string SperrePraefix = "fork-ai:bluelab:geschrieben:";
    private static readonly TimeSpan Sperre = TimeSpan.FromMinutes(30);

    private readonly SteuerungGeraeteService _geraete;
    private readonly GrowRepository _grows;
    private readonly AlertEvaluationService _alarme;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly AppSettingsRepository _einstellungen;
    private readonly ILogger<BluelabGrenzenService> _log;

    public BluelabGrenzenService(
        SteuerungGeraeteService geraete, GrowRepository grows, AlertEvaluationService alarme,
        HomeAssistantService ha, HomeAssistantSettingsRepository haSettings,
        AppSettingsRepository einstellungen, ILogger<BluelabGrenzenService> log)
    {
        _geraete = geraete;
        _grows = grows;
        _alarme = alarme;
        _ha = ha;
        _haSettings = haSettings;
        _einstellungen = einstellungen;
        _log = log;
    }

    /// <summary>Stand für die Anzeige unter „Grenzwerte".</summary>
    public sealed record Stand(
        bool Eingerichtet,
        DateTime? ZuletztUtc,
        int Geschrieben,
        string? Fehler,
        IReadOnlyList<Grenze> Grenzen);

    public sealed record Grenze(string Rolle, double? Soll, double? Geraet);

    /// <summary>
    /// Das Band fürs Gerät — rein, ohne Home Assistant: je Messgröße die weitere
    /// Spanne aus Tag und Nacht, auf 0,1 nach außen gerundet.
    /// </summary>
    public static Dictionary<string, double> Sollwerte(IEnumerable<TentAlertRule> wirksam)
    {
        var ergebnis = new Dictionary<string, double>(StringComparer.Ordinal);
        var regeln = wirksam.ToList();
        foreach (var (rolle, metrik, oben, _) in Zuordnung)
        {
            var regel = regeln.FirstOrDefault(r => string.Equals(r.MetricKey, metrik, StringComparison.OrdinalIgnoreCase));
            if (regel is null) continue;

            var werte = oben
                ? new[] { regel.MaxValue, regel.NightMaxValue }
                : new[] { regel.MinValue, regel.NightMinValue };
            var vorhanden = werte.Where(w => w is not null).Select(w => w!.Value).ToList();
            if (vorhanden.Count == 0) continue;

            var wert = oben ? vorhanden.Max() : vorhanden.Min();
            ergebnis[rolle] = oben ? Math.Ceiling(wert * 10 - 1e-9) / 10 : Math.Floor(wert * 10 + 1e-9) / 10;
        }
        return ergebnis;
    }

    /// <summary>Das Zelt, dessen Grenzwerte gelten: das erste mit Reservoir-Regeln.</summary>
    private Tent? ZeltMitRegeln(out IReadOnlyList<TentAlertRule> regeln)
    {
        foreach (var zelt in _grows.GetTents())
        {
            var wirksam = _alarme.WirksameRegeln(zelt);
            if (wirksam.Any(r => r.MetricKey.StartsWith("reservoir-", StringComparison.OrdinalIgnoreCase)))
            {
                regeln = wirksam;
                return zelt;
            }
        }
        regeln = Array.Empty<TentAlertRule>();
        return null;
    }

    public async Task<Stand> StandAsync(CancellationToken ct)
    {
        var rollen = _geraete.EntitiesFuerModul(Modul);
        var eingerichtet = rollen.TryGetValue("skript", out var skript) && skript is not null
                           && Zuordnung.Any(z => rollen.TryGetValue(z.Rolle, out var e) && e is not null);
        ZeltMitRegeln(out var regeln);
        var soll = Sollwerte(regeln);

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var zustaende = settings.IsConfigured
            ? (await _ha.GetEntitiesAsync(settings, ct)).ToDictionary(e => e.EntityId, e => e.State, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var grenzen = Zuordnung.Select(z =>
        {
            double? geraet = rollen.TryGetValue(z.Rolle, out var id) && id is not null && zustaende.TryGetValue(id, out var s)
                && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            return new Grenze(z.Rolle, soll.TryGetValue(z.Rolle, out var w) ? w : null, geraet);
        }).ToList();

        var zuletzt = LeseZuletzt();
        return new Stand(eingerichtet, zuletzt.Zeit, zuletzt.Anzahl, zuletzt.Fehler, grenzen);
    }

    /// <summary>Abgleich: schreibt, was am Gerät abweicht. Liefert die Zahl der Schreibvorgänge.</summary>
    public async Task<int> AbgleichenAsync(CancellationToken ct)
    {
        var rollen = _geraete.EntitiesFuerModul(Modul);
        if (!rollen.TryGetValue("skript", out var skript) || skript is null) return 0;
        if (!skript.StartsWith("script.", StringComparison.OrdinalIgnoreCase)) return 0;

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return 0;

        ZeltMitRegeln(out var regeln);
        var soll = Sollwerte(regeln);
        if (soll.Count == 0) return 0;

        var zustaende = (await _ha.GetEntitiesAsync(settings, ct))
            .ToDictionary(e => e.EntityId, e => e.State, StringComparer.OrdinalIgnoreCase);
        if (zustaende.Count == 0) return 0;

        var geschrieben = 0;
        string? fehler = null;
        foreach (var (rolle, _, _, schluessel) in Zuordnung)
        {
            if (!soll.TryGetValue(rolle, out var wert)) continue;
            if (!rollen.TryGetValue(rolle, out var entity) || entity is null) continue;
            if (!zustaende.TryGetValue(entity, out var ist) || !double.TryParse(ist, NumberStyles.Float, CultureInfo.InvariantCulture, out var istWert)) continue;
            if (Math.Abs(istWert - wert) < 0.05) continue;
            if (KuerzlichGeschrieben(rolle, wert)) continue;

            var ok = await _ha.CallServiceAsync(settings, "script", skript["script.".Length..],
                new Dictionary<string, object> { ["setting_key"] = schluessel, ["value"] = wert }, ct);
            if (ok)
            {
                geschrieben++;
                _einstellungen.SetValue(SperrePraefix + rolle,
                    JsonSerializer.Serialize(new { Wert = wert, Zeit = DateTime.UtcNow }));
                _log.LogInformation("Bluelab-Grenze {Rolle}: {Ist} → {Soll}", rolle, istWert, wert);
            }
            else
            {
                fehler = $"{rolle} konnte nicht geschrieben werden";
            }

            // Nacheinander, mit Abstand: die Cloud verwirft parallele Schreibvorgänge.
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch (OperationCanceledException) { break; }
        }

        if (geschrieben > 0 || fehler is not null)
        {
            _einstellungen.SetValue(ZuletztSchluessel,
                JsonSerializer.Serialize(new { Zeit = DateTime.UtcNow, Anzahl = geschrieben, Fehler = fehler }));
        }
        return geschrieben;
    }

    private bool KuerzlichGeschrieben(string rolle, double wert)
    {
        try
        {
            var roh = _einstellungen.GetValue(SperrePraefix + rolle);
            if (roh is null) return false;
            using var doc = JsonDocument.Parse(roh);
            var w = doc.RootElement.GetProperty("Wert").GetDouble();
            var t = doc.RootElement.GetProperty("Zeit").GetDateTime();
            return Math.Abs(w - wert) < 0.05 && DateTime.UtcNow - t < Sperre;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private (DateTime? Zeit, int Anzahl, string? Fehler) LeseZuletzt()
    {
        try
        {
            var roh = _einstellungen.GetValue(ZuletztSchluessel);
            if (roh is null) return (null, 0, null);
            using var doc = JsonDocument.Parse(roh);
            var r = doc.RootElement;
            return (r.GetProperty("Zeit").GetDateTime(), r.GetProperty("Anzahl").GetInt32(),
                r.TryGetProperty("Fehler", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return (null, 0, null);
        }
    }
}

/// <summary>Fork AI (forkai.141): gleicht die Bluelab-Grenzen alle fünf Minuten ab.</summary>
public sealed class BluelabGrenzenWorker : BackgroundService
{
    private readonly IServiceProvider _dienste;
    private readonly ILogger<BluelabGrenzenWorker> _log;

    public BluelabGrenzenWorker(IServiceProvider dienste, ILogger<BluelabGrenzenWorker> log)
    {
        _dienste = dienste;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(120), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _dienste.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BluelabGrenzenService>().AbgleichenAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Abgleich der Bluelab-Grenzen fehlgeschlagen.");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
