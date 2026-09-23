using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.ViewModels.Live;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Controllers;

[Route("tents")]
public sealed class TentsController : Controller
{
    private readonly GrowRepository _repository;
    private readonly HomeAssistantService _homeAssistantService;
    private readonly GrowDashboardComposer _composer;
    private readonly GrowAlertService _growAlertService;
    private readonly AppPaths _paths;
    private readonly NachtabsenkungWriter _absenkung;
    private readonly AppSettingsRepository _einstellungen;

    public TentsController(GrowRepository repository, HomeAssistantService homeAssistantService, GrowDashboardComposer composer, GrowAlertService growAlertService, AppPaths paths, NachtabsenkungWriter absenkung, AppSettingsRepository einstellungen)
    {
        _repository = repository;
        _homeAssistantService = homeAssistantService;
        _composer = composer;
        _growAlertService = growAlertService;
        _paths = paths;
        _absenkung = absenkung;
        _einstellungen = einstellungen;
    }

    [HttpGet("")]
    public IActionResult Index(int? selected, CancellationToken cancellationToken)
        => Redirect("/zelte");

    [HttpGet("{id:int}")]
    public IActionResult Details(int id, CancellationToken cancellationToken)
        => Redirect($"/zelte/{id}");

    [HttpGet("/api/live/tents/{id:int}")]
    public async Task<IActionResult> Live(int id, CancellationToken cancellationToken)
    {
        var tent = _repository.GetTent(id);
        if (tent is null)
        {
            return NotFound();
        }

        var settings = _repository.GetEffectiveHomeAssistantSettings();
        /* Tent.ActiveGrows wurde bis zum 01.09.2026 von NIEMANDEM ausser dieser
           Zeile und der in HomeController gefuellt. Daran hingen still zwei
           Dinge hier — die Alarmzeile des Zelts und die Zielbereiche der
           Kacheln — und sieben weitere in den Diensten, die kein Controller
           bedient: der Volumenfaktor der Dosierung blieb immer 1, und der
           Lichteinbruch-Waechter kehrte sofort zurueck.

           Jetzt fuellt GrowRepository.GetTent die Liste. Diese Zeile ist damit
           die zweite Wahrheit und faellt weg. */
        var measurements = _repository.GetMeasurementsForTent(id);
        var states = await _homeAssistantService.GetStatesAsync(settings, tent, cancellationToken);
        var metrics = _composer.BuildTentMetrics(tent, states, measurements);
        var alerts = tent.ActiveGrows
            .SelectMany(grow => _growAlertService.BuildAlertsForGrow(grow, maxCount: 2))
            .Take(8)
            .ToList();
        var tone = GrowAlertService.ResolveStateTone(alerts, settings.IsConfigured);

        return Json(new TentLivePayload
        {
            TentId = tent.Id,
            StateTone = tone,
            StateLabel = GrowAlertService.ResolveStateLabel(tone),
            /* Derselbe Weg, den die Oberflaeche an allen anderen Stellen nimmt.

               Bis zum 02.09.2026 stand hier Url.Action("CameraSnapshot",
               "Tents", …) — auf eine Aktion, die es seit dem Aufraeumen der
               Legacy-Kamera-Wege nicht mehr gibt. Url.Action wirft nicht, es
               gibt null zurueck: cameraUrl war dauerhaft leer, und auf der
               Zelt-Seite stand fuer immer „Kamera — Nicht eingerichtet", auch
               wenn die Kamera in Home Assistant sauber eingetragen war.

               Lokal faellt das nicht auf, weil ohne Home Assistant die
               Bedingung davor kurzschliesst — der Schaden trifft nur die echte
               Anlage. Gehalten von JedeUrlActionZeigtAufEineEchteAktionTests. */
            CameraUrl = settings.IsConfigured && !string.IsNullOrWhiteSpace(tent.CameraEntityId)
                ? $"/api/live/tents/{tent.Id}/camera?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                : null,
            RefreshedAtUtc = DateTime.UtcNow,
            Metrics = metrics.Select(metric => metric.ToPayload()).ToList(),
            Chiller = await KuehlerLageAsync(tent, settings, states, cancellationToken)
        });
    }

    /// <summary>
    /// Was der Kühler-Regler gerade tut — für die Live-Seite.
    /// </summary>
    /// <remarks>
    /// <b>Dieselbe Rechnung, die auch schaltet.</b> Lage und Urteil kommen aus
    /// <see cref="KuehlerWorker.LageLesen"/> und <see cref="KuehlerService.Entscheiden"/>;
    /// eine zweite Fassung fürs Anzeigen würde von der ersten abdriften. Nur
    /// der Zeitpunkt unterscheidet sich: der Worker sieht die Lage im
    /// Minutentakt, die Seite beim Aufruf.
    /// </remarks>
    private async Task<KuehlerLivePayload?> KuehlerLageAsync(
        Tent tent,
        HomeAssistantSettings settings,
        IReadOnlyDictionary<string, HomeAssistantState> states,
        CancellationToken cancellationToken)
    {
        // Ist die Steuerung aus, gibt es nichts zu zeigen. Eine Kachel, die
        // dauerhaft „nicht eingerichtet" sagt, waere Rauschen.
        // Fork AI (forkai.136): Crop Steering ist stillgelegt — der Kühler steht
        // unter Steuerung → Chiller, nicht doppelt auf der Live-Seite.
        if (!GrowDiary.Web.Infrastructure.ForkAiSchalter.CropSteeringAktiv
            || !tent.ChillerControlEnabled || string.IsNullOrWhiteSpace(tent.ChillerSwitchEntityId))
        {
            return null;
        }

        // Dieselbe Quelle wie im Worker: die Steckdose EINZELN, weil `states`
        // nur Metrik-Kennungen kennt.
        var steckdose = await _homeAssistantService.GetEntityStateAsync(
            settings, tent.ChillerSwitchEntityId!, cancellationToken);

        var lage = KuehlerWorker.LageLesen(
            _repository, _absenkung, _einstellungen, tent, states, steckdose);
        var urteil = KuehlerService.Entscheiden(lage, tent, DateTime.UtcNow);

        return new KuehlerLivePayload
        {
            SwitchEntityId = tent.ChillerSwitchEntityId!,
            SollC = lage.SollC,
            IstC = lage.IstC,
            MesswertAlterMinuten = lage.MesswertAlter is { } alter ? (int)Math.Round(alter.TotalMinutes) : null,
            Tagbetrieb = lage.Tagbetrieb,
            LaeuftGerade = lage.KuehlerLaeuftGerade,
            Schaltung = urteil.Schaltung switch
            {
                KuehlerSchaltung.Ein => "ein",
                KuehlerSchaltung.Aus => "aus",
                _ => "nichts",
            },
            Grund = urteil.Grund,
        };
    }

    /* Drei Legacy-Kamera-Aktionen standen hier bis zum 02.09.2026:
       "{id}/camera.jpg", "{id}/camera-stream" und "{id}/latest-snapshot".

       Die Zaehlung "jede Route hat einen Aufrufer" fand fuer keine einen — die
       Oberflaeche nimmt an allen vier Stellen "/api/live/tents/{id}/camera",
       den Endpunkt gleich hier darueber. "Belegt" waren sie nur durch das
       API-Verzeichnis, das sie auflistet, und durch AdminAccessPolicy, die sie
       schuetzt: ein Katalog und ein Waechter, kein Aufrufer.

       Drei Wege zu einem Bild sind drei Stellen, an denen der Kamera-Zwischen-
       speicher auseinanderlaufen kann. */
}
