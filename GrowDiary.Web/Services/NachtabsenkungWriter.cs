using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Schreibt den Wochenwert der Nachtabsenkung nach Home Assistant.
/// </summary>
/// <remarks>
/// <para><b>Cultivate Control plant, Home Assistant regelt.</b> Hier wird ein Sollwert
/// gesetzt — zweimal am Tag, bei Licht an und bei Licht aus. Die Regelschleife
/// mit ihrer Hysterese bleibt dort, wo Fühler und Relais sitzen. Cultivate Control taktet
/// keinen Chiller.</para>
///
/// <para><b>Warum das die sichere Aufteilung ist:</b> stürzt dieses Add-on ab
/// oder läuft ein Update, behält Home Assistant den zuletzt geschriebenen
/// Sollwert. Die Rampe pausiert, mehr passiert nicht. Wäre Cultivate Control der Regler,
/// könnte derselbe Absturz den Chiller dauerhaft an oder aus lassen — genau die
/// Klasse Fehler, gegen die beim Dosieren der Totmann steht.</para>
///
/// <para><b>Was hier bewusst nicht passiert:</b> raten. Ohne Zieleinheit, ohne
/// Lichtzustand oder ohne Blütewoche wird nichts geschrieben. Ein geratener
/// Sollwert verstellt eine echte Kühlung.</para>
/// </remarks>
public sealed class NachtabsenkungWriter
{
    private readonly GrowRepository _grows;
    private readonly TargetValueService _targets;
    private readonly HomeAssistantService _homeAssistant;
    private readonly SetpointProfileRepository _profiles;
    private readonly HydroSetupRepository _hydro;
    private readonly SystemAuditRepository _audit;
    private readonly ILogger<NachtabsenkungWriter> _logger;

    public NachtabsenkungWriter(
        GrowRepository grows,
        TargetValueService targets,
        HomeAssistantService homeAssistant,
        SetpointProfileRepository profiles,
        HydroSetupRepository hydro,
        SystemAuditRepository audit,
        ILogger<NachtabsenkungWriter> logger)
    {
        _grows = grows;
        _targets = targets;
        _homeAssistant = homeAssistant;
        _profiles = profiles;
        _hydro = hydro;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>Der Plan eines Grows — für die Anzeige und für den Schreibvorgang.</summary>
    public Absenkplan PlanFuer(GrowRun grow, DateTime heute, bool vorschau = false)
    {
        var systemProfil = grow.SystemId is { } id ? _hydro.GetSystem(id)?.SetpointProfileId : null;
        var profil = SetpointProfileResolver.Resolve(grow.SetpointProfileId, systemProfil, grow.HydroStyle);

        return NachtabsenkungService.Rechnen(
            grow,
            _targets.GetTargets(profil.ProfileId, GrowStage.Flower),
            _targets.GetTargets(profil.ProfileId, GrowStage.Finish),
            grow.NightRampFloorC,
            heute,
            vorschau);
    }

    /// <summary>
    /// Setzt den Sollwert für die gerade beginnende Phase.
    /// </summary>
    /// <param name="lichtAn">true bei Licht an, false bei Licht aus.</param>
    public async Task<bool> SchreibenAsync(
        Tent tent, bool lichtAn, DateTime heute, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tent.WaterTargetEntityId)) return false;

        var grow = _grows.GetActiveGrowsForTent(tent.Id).FirstOrDefault(g => g.NightRampEnabled);
        if (grow is null) return false;

        var plan = PlanFuer(grow, heute);
        var ziel = lichtAn ? plan.HeuteTagC : plan.HeuteNachtC;
        if (ziel is not { } wert) return false;

        var settings = _grows.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        // Die Domäne entscheidet den Dienst: ein Thermostat nimmt `temperature`,
        // ein Zahlenfeld `value`. Beides kommt in echten Anlagen vor.
        var domain = tent.WaterTargetEntityId.Split('.', 2)[0];
        var (service, feld) = domain switch
        {
            "climate" => ("set_temperature", "temperature"),
            "number" or "input_number" => ("set_value", "value"),
            _ => (string.Empty, string.Empty),
        };

        if (service.Length == 0)
        {
            _logger.LogWarning(
                "Nachtabsenkung: {Entity} ist weder climate noch number — es wird nichts geschrieben.",
                tent.WaterTargetEntityId);
            return false;
        }

        var ok = await _homeAssistant.CallEntityServiceAsync(
            settings, domain, service, tent.WaterTargetEntityId, cancellationToken,
            new Dictionary<string, object> { [feld] = wert });

        // Jeder Eingriff in die Anlage gehoert ins Protokoll — auch der, der
        // funktioniert hat. Sonst steht spaeter die Frage im Raum, warum das
        // Wasser ploetzlich kaelter war.
        _audit.Add(new SystemAuditEvent
        {
            EventType = "night-ramp",
            Action = ok ? "setpoint-written" : "setpoint-failed",
            Summary = $"{tent.Name}: {(lichtAn ? "Tag" : "Nacht")}-Sollwert {wert.ToString("0.#", AppCulture.German)} °C "
                + $"an {tent.WaterTargetEntityId} (Blütewoche {plan.AktuelleWoche}).",
            Severity = ok ? "info" : "warning",
            RelatedGrowId = grow.Id,
            Success = ok,
        });

        if (ok)
        {
            _logger.LogInformation(
                "Nachtabsenkung: {Phase}-Sollwert {Wert} °C an {Entity} (Zelt {TentId}, Blütewoche {Woche}).",
                lichtAn ? "Tag" : "Nacht", wert, tent.WaterTargetEntityId, tent.Id, plan.AktuelleWoche);
        }

        return ok;
    }
}
