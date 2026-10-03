using GrowDiary.Web.Infrastructure;

namespace GrowDiary.Web.Services;

/// <summary>Startet Grow OS neu — oder sagt, dass es das nicht kann.</summary>
public interface IAppNeustart
{
    /// <summary>
    /// Plant einen Neustart in wenigen Sekunden.
    /// </summary>
    /// <returns>
    /// <c>true</c>, wenn er geplant ist; <c>false</c>, wenn Grow OS nicht als
    /// Add-on läuft und sich deshalb nicht selbst neu starten kann.
    /// </returns>
    bool Planen(string grund);
}

/// <summary>
/// Neustart über den Supervisor von Home Assistant.
/// </summary>
/// <remarks>
/// <para><b>Wofür (offene Punkte 03.10.2026, B8).</b> Nach dem Zurückspielen
/// einer Sicherung liegt eine andere Datenbank unter dem laufenden Prozess. Die
/// Schema-Ergänzungen laufen sofort (<c>SystemApiController.RestoreBackup</c>),
/// aber rund acht Stellen halten den Stand der vorigen Datenbank im Speicher:
/// der Grow-Plan (<c>GrowPlanRegister</c>), die Wissensbasis (die Dateien werden
/// zurückgespielt, aber nicht neu geladen), die Wochenwert-Überlagerung, drei
/// <c>SchemaSteht</c>-Merker und die Übernahmen beim Start. Sie einzeln neu zu
/// laden wäre eine handgeschriebene Liste — die nächste Stelle, die jemand
/// einbaut, fehlte darin, und niemand merkte es. Ein Neustart erfasst alle.</para>
///
/// <para><b>Ohne neue Rechte.</b> <c>/addons/self/*</c> darf jedes Add-on
/// aufrufen, auch ohne <c>hassio_api</c> (Home Assistant Developer Docs, „App
/// communication"). Dasselbe Token wie <see cref="SupervisorInfoService"/>.</para>
///
/// <para><b>Warum verzögert.</b> Der Aufrufer soll seine Antwort noch bekommen
/// — mit dem Namen der Sicherheitskopie darin.</para>
/// </remarks>
public sealed class SupervisorNeustart : IAppNeustart
{
    private const string RestartUrl = "http://supervisor/addons/self/restart";

    /// <summary>So lange bekommt die laufende Anfrage, um fertig zu werden.</summary>
    public static readonly TimeSpan Verzoegerung = TimeSpan.FromSeconds(3);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SupervisorNeustart> _logger;
    private readonly TimeSpan _verzoegerung;

    /// <param name="verzoegerung">Nur Prüfungen setzen sie; sonst <see cref="Verzoegerung"/>.</param>
    public SupervisorNeustart(IHttpClientFactory httpClientFactory, ILogger<SupervisorNeustart> logger, TimeSpan? verzoegerung = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _verzoegerung = verzoegerung ?? Verzoegerung;
    }

    public bool Planen(string grund)
    {
        var token = HomeAssistantAddon.SupervisorToken;
        if (token is null) return false;

        _logger.LogWarning("Neustart in {Sekunden} s geplant: {Grund}", _verzoegerung.TotalSeconds, grund);
        _ = Task.Run(async () =>
        {
            await Task.Delay(_verzoegerung);
            try
            {
                using var client = _httpClientFactory.CreateClient(nameof(SupervisorNeustart));
                client.Timeout = TimeSpan.FromSeconds(10);
                using var anfrage = new HttpRequestMessage(HttpMethod.Post, RestartUrl);
                anfrage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                using var antwort = await client.SendAsync(anfrage);
                if (!antwort.IsSuccessStatusCode)
                {
                    _logger.LogError("Supervisor lehnte den Neustart ab ({Status}). Grow OS bitte von Hand neu starten — bis dahin gilt im Speicher der Stand vor dem Zurückspielen.", (int)antwort.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Neustart über den Supervisor gescheitert. Grow OS bitte von Hand neu starten.");
            }
        });
        return true;
    }
}
