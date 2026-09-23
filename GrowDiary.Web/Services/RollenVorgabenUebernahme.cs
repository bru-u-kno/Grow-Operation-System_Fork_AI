using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.138, F-034): Übernimmt die früheren Werksvorgaben der Rollen
/// einmalig als feste Zuordnung — aber nur, wo es die Entität in Home Assistant
/// wirklich gibt.
/// </summary>
/// <remarks>
/// <para><b>Warum.</b> Die Vorgaben waren die Geräte einer einzelnen Anlage. In
/// dieser Anlage ändert sich durch die Übernahme nichts; in jeder anderen bleibt
/// eine Rolle leer, statt still auf eine fremde Kennung zu zeigen.</para>
/// <para><b>Wann.</b> Erst, wenn Home Assistant antwortet — ohne Entitätenliste
/// lässt sich nicht prüfen, was existiert. Bis dahin gilt der alte Rückfall weiter
/// (<see cref="SteuerungGeraeteService"/>). Danach nie wieder.</para>
/// </remarks>
public sealed class RollenVorgabenUebernahme : BackgroundService
{
    private readonly IServiceProvider _dienste;
    private readonly ILogger<RollenVorgabenUebernahme> _log;

    public RollenVorgabenUebernahme(IServiceProvider dienste, ILogger<RollenVorgabenUebernahme> log)
    {
        _dienste = dienste;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _dienste.CreateScope();
                var einstellungen = scope.ServiceProvider.GetRequiredService<AppSettingsRepository>();
                if (einstellungen.GetValue(SteuerungGeraeteService.UebernahmeSchluessel) is not null) return;

                var ha = scope.ServiceProvider.GetRequiredService<HomeAssistantService>();
                var haSettings = scope.ServiceProvider.GetRequiredService<HomeAssistantSettingsRepository>()
                    .GetEffectiveHomeAssistantSettings();
                if (haSettings.IsConfigured)
                {
                    var entities = await ha.GetEntitiesAsync(haSettings, stoppingToken);
                    if (entities.Count > 0)
                    {
                        var repo = scope.ServiceProvider.GetRequiredService<SteuerungRepository>();
                        var vorhanden = entities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var anzahl = 0;
                        foreach (var (modul, rolle, entity) in Uebernehmen(
                                     SteuerungGeraeteRollen.Alle,
                                     m => repo.GetGeraete(m).Select(g => g.Rolle),
                                     vorhanden))
                        {
                            repo.SetGeraet(modul, rolle, entity);
                            anzahl++;
                        }

                        einstellungen.SetValue(SteuerungGeraeteService.UebernahmeSchluessel, DateTime.UtcNow.ToString("O"));
                        _log.LogInformation("Rollen-Vorgaben übernommen: {Anzahl} Zuordnungen fest gespeichert.", anzahl);
                        return;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Übernahme der Rollen-Vorgaben fehlgeschlagen — nächster Versuch in einer Minute.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Was übernommen wird — rein, ohne Datenbank: jede Rolle mit bisheriger
    /// Vorgabe, die noch keine eigene Zuordnung hat und deren Entität existiert.
    /// </summary>
    public static IEnumerable<(string Modul, string Rolle, string Entity)> Uebernehmen(
        IEnumerable<GeraeteRolle> rollen,
        Func<string, IEnumerable<string>> gespeicherteRollen,
        IReadOnlySet<string> vorhandeneEntities)
    {
        var cache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var rolle in rollen)
        {
            if (string.IsNullOrWhiteSpace(rolle.BisherigeVorgabe)) continue;
            if (!cache.TryGetValue(rolle.Modul, out var belegt))
            {
                belegt = gespeicherteRollen(rolle.Modul).ToHashSet(StringComparer.OrdinalIgnoreCase);
                cache[rolle.Modul] = belegt;
            }
            if (belegt.Contains(rolle.Schluessel)) continue;
            if (!vorhandeneEntities.Contains(rolle.BisherigeVorgabe)) continue;
            yield return (rolle.Modul, rolle.Schluessel, rolle.BisherigeVorgabe);
        }
    }
}
