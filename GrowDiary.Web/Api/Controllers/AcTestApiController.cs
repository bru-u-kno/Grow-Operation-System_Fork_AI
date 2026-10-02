using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Der Versuchsaufbau „Zelt (AC-Test)" — Geräte eintragen und ihre Stufe stellen.
/// </summary>
/// <remarks>
/// Siehe <see cref="AcTest"/> für das Warum. Kurz: es geht um die Frage, ob
/// Grow OS die Zentrale sein kann, von der aus der ganze Grow läuft — und die
/// beantwortet ein Nutzer, kein Entwurf.
/// </remarks>
[ApiController]
[Route("api/ac-test")]
public sealed class AcTestApiController : ControllerBase
{
    private readonly GrowRepository _grows;
    private readonly AppSettingsRepository _einstellungen;
    private readonly HomeAssistantService _homeAssistant;
    private readonly SystemAuditRepository _protokoll;
    private readonly AcSchreiber _schreiber;
    private readonly ILogger<AcTestApiController> _logger;

    public AcTestApiController(
        GrowRepository grows,
        AppSettingsRepository einstellungen,
        HomeAssistantService homeAssistant,
        SystemAuditRepository protokoll,
        AcSchreiber schreiber,
        ILogger<AcTestApiController> logger)
    {
        _grows = grows;
        _einstellungen = einstellungen;
        _homeAssistant = homeAssistant;
        _protokoll = protokoll;
        _schreiber = schreiber;
        _logger = logger;
    }

    /// <summary>Die eingetragenen Geräte samt aktueller Stufe.</summary>
    [HttpGet("{zeltId:int}")]
    public async Task<ActionResult<AcTestStand>> Stand(int zeltId, CancellationToken ct)
    {
        if (_grows.GetTent(zeltId) is null)
        {
            return NotFound(ApiErrorFactory.NotFound("zelt_nicht_gefunden", $"Zelt {zeltId} gibt es nicht."));
        }

        var einstellungen = _grows.GetEffectiveHomeAssistantSettings();
        var geraete = AcTest.Lesen(_einstellungen, zeltId);
        var stand = new List<AcGeraetStand>();

        foreach (var geraet in geraete)
        {
            /* EIN Geraet, das nicht antwortet, nimmt nicht die ganze Seite mit.
               Bis zum 02.09.2026 lief jeder Aufruf ungeschuetzt: eine
               Basisadresse ohne Schema (der Nutzer traegt "192.168.1.50:8123"
               statt "http://192.168.1.50:8123" ein) laesst GetEntityStateAsync
               werfen, und GET /api/ac-test/{id} brach mit 500 ab — obwohl
               direkt darunter der Satz bereitsteht, der genau das erklaeren
               soll. */
            AcGeraetStand zeile;
            try
            {
                // Jede Entität EINZELN holen: das Wörterbuch aus GetStatesAsync
                // kennt nur Metrik-Kennungen, nie Entitäts-Kennungen. Genau daran
                // ist der Kühler-Regler schon einmal gescheitert.
                var leistung = await _homeAssistant.GetEntityStateAsync(einstellungen, geraet.LeistungEntityId, ct);
                var modus = string.IsNullOrWhiteSpace(geraet.ModusEntityId)
                    ? null
                    : await _homeAssistant.GetEntityStateAsync(einstellungen, geraet.ModusEntityId, ct);

                // Die Zeiten werden GELESEN, nicht angenommen. Was die Seite anzeigt,
                // muss der Controller melden — sonst steht dort der Wunsch und nicht
                // die Wirklichkeit, und genau das war der teure Fehler beim Kuehler.
                var einZeit = string.IsNullOrWhiteSpace(geraet.EinZeitEntityId)
                    ? null
                    : await _homeAssistant.GetEntityStateAsync(einstellungen, geraet.EinZeitEntityId, ct);
                var ausZeit = string.IsNullOrWhiteSpace(geraet.AusZeitEntityId)
                    ? null
                    : await _homeAssistant.GetEntityStateAsync(einstellungen, geraet.AusZeitEntityId, ct);

                zeile = new AcGeraetStand(
                    geraet,
                    leistung?.NumericValue,
                    modus?.State,
                    AcTest.AlsHhMm(einZeit?.State),
                    AcTest.AlsHhMm(ausZeit?.State),
                    leistung is null
                        ? $"{geraet.LeistungEntityId} antwortet nicht — gibt es die Entität?"
                        : null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception fehler)
            {
                _logger.LogWarning(fehler, "AC-Test: {Entitaet} nicht lesbar.", geraet.LeistungEntityId);
                zeile = new AcGeraetStand(
                    geraet, null, null, null, null,
                    $"{geraet.LeistungEntityId} liess sich nicht lesen: {fehler.Message} "
                    + "— steht in den Einstellungen eine vollstaendige Adresse mit http:// ?");
            }

            stand.Add(zeile);
        }

        // Der Vorschlag fuer den Zeitplan kommt aus dem Lichtplan des Zelts —
        // derselben Quelle, aus der der Waechter gegen Lichteinbruch liest.
        // Erfunden wird hier nichts.
        var plan = _grows.GetActiveLightScheduleForTent(zeltId);
        var ein = AcTest.AlsHhMm(plan?.LightsOnTime);
        var aus = AcTest.AlsHhMm(plan?.LightsOffTime);
        var lichtplan = plan is not null && ein is not null && aus is not null
            ? new AcLichtplan(plan.Name, ein, aus)
            : null;

        return Ok(new AcTestStand(
            zeltId, stand, einstellungen.IsConfigured, DemoData.IsEnabled, lichtplan));
    }

    /// <summary>Die Geräte eintragen oder ändern.</summary>
    [HttpPut("{zeltId:int}")]
    public ActionResult<IReadOnlyList<string>> Speichern(
        int zeltId, [FromBody] List<AcGeraet> geraete)
    {
        if (_grows.GetTent(zeltId) is null)
        {
            return NotFound(ApiErrorFactory.NotFound("zelt_nicht_gefunden", $"Zelt {zeltId} gibt es nicht."));
        }

        var maengel = AcTest.Speichern(_einstellungen, zeltId, geraete);
        if (maengel.Count > 0)
        {
            // Der FEHLERVERTRAG, keine rohe Liste: die Oberflaeche liest
            // ApiError.Message. Eine rohe Liste hat kein message-Feld, und der
            // Nutzer sah "API request failed with status 400" — der deutsche
            // Satz war da und kam nie an.
            return BadRequest(ApiErrorFactory.Validation(string.Join(" ", maengel)));
        }

        return Ok(Array.Empty<string>());
    }

    /// <summary>Eine Stufe stellen — der eigentliche Versuch.</summary>
    /// <remarks>
    /// <b>Nur auf Klick.</b> Hier läuft kein Hintergrunddienst und nichts regelt
    /// sich von selbst. Jeder Aufruf steht im Anlagen-Protokoll, auch der
    /// erfolgreiche: bei einem Versuch will man hinterher wissen, wer wann was
    /// gestellt hat.
    /// </remarks>
    [HttpPost("{zeltId:int}/stufe")]
    public async Task<IActionResult> Stufe(
        int zeltId, [FromBody] StufeRequest request, CancellationToken ct)
    {
        var zelt = _grows.GetTent(zeltId);
        if (zelt is null)
        {
            return NotFound(ApiErrorFactory.NotFound("zelt_nicht_gefunden", $"Zelt {zeltId} gibt es nicht."));
        }

        var geraet = AcTest.Lesen(_einstellungen, zeltId)
            .FirstOrDefault(g => g.LeistungEntityId == request.EntityId);
        if (geraet is null)
        {
            // Kein blindes Schreiben auf eine beliebige Entität: gestellt wird
            // nur, was der Nutzer vorher hier eingetragen hat.
            return BadRequest(ApiErrorFactory.BadRequest(
                "geraet_nicht_eingetragen",
                $"{request.EntityId} ist für dieses Zelt nicht eingetragen."));
        }

        if (!AcTest.StufeErlaubt(request.Stufe))
        {
            return BadRequest(ApiErrorFactory.Validation(
                $"Stufe {request.Stufe} liegt ausserhalb von {AcTest.StufeMin}–{AcTest.StufeMax}."));
        }

        var einstellungen = _grows.GetEffectiveHomeAssistantSettings();
        var domain = geraet.LeistungEntityId.Split('.', 2)[0];

        var ergebnisse = await _schreiber.SchreibenAsync(einstellungen,
        [
            new AcSchreibschritt(
                geraet.LeistungEntityId, domain, "set_value",
                new Dictionary<string, object> { ["value"] = request.Stufe },
                request.Stufe.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)),
        ], ct: ct);

        return Antworten(zelt.Name, geraet.Name, $"Stufe {request.Stufe:0.#}", ergebnisse);
    }

    /// <summary>Den Zeitplan stellen — Ein-Zeit, Aus-Zeit, Modus.</summary>
    /// <remarks>
    /// <b>Der Grund fuer die Reihenfolge steht in der Karte des Testers:</b> die
    /// AC-Infinity-Cloud verwirft parallele Updates. Drei gleichzeitige Aufrufe
    /// ergaben <c>Unable to update device controls</c> und im besten Fall EINE
    /// uebernommene Aenderung. <see cref="AcSchreiber"/> schreibt deshalb
    /// nacheinander, wartet dazwischen und liest jedes Mal nach.
    ///
    /// Der Modus kommt ZULETZT. Ein Geraet im Zeitplan-Modus mit noch alten
    /// Zeiten schaltet nach dem alten Plan — das waere schlimmer als gar nichts.
    /// </remarks>
    [HttpPost("{zeltId:int}/zeitplan")]
    public async Task<IActionResult> Zeitplan(
        int zeltId, [FromBody] ZeitplanRequest request, CancellationToken ct)
    {
        var zelt = _grows.GetTent(zeltId);
        if (zelt is null)
        {
            return NotFound(ApiErrorFactory.NotFound("zelt_nicht_gefunden", $"Zelt {zeltId} gibt es nicht."));
        }

        var geraet = AcTest.Lesen(_einstellungen, zeltId)
            .FirstOrDefault(g => g.LeistungEntityId == request.EntityId);
        if (geraet is null)
        {
            return BadRequest(ApiErrorFactory.BadRequest(
                "geraet_nicht_eingetragen",
                $"{request.EntityId} ist für dieses Zelt nicht eingetragen."));
        }

        if (string.IsNullOrWhiteSpace(geraet.EinZeitEntityId)
            || string.IsNullOrWhiteSpace(geraet.AusZeitEntityId))
        {
            return BadRequest(ApiErrorFactory.BadRequest(
                "zeit_entitaeten_fehlen",
                "Für dieses Gerät sind keine Zeit-Entitäten eingetragen. "
                + "Bei AC Infinity heissen sie Geplante Ein-Zeit und Geplante Aus-Zeit."));
        }

        var maengel = new List<string>();
        if (!AcTest.ZeitErlaubt(request.Ein)) maengel.Add($"Ein-Zeit: {request.Ein} ist keine Uhrzeit im Format HH:MM.");
        if (!AcTest.ZeitErlaubt(request.Aus)) maengel.Add($"Aus-Zeit: {request.Aus} ist keine Uhrzeit im Format HH:MM.");

        /* Und ob die beiden ZUSAMMEN einen Plan ergeben.
           20:00 bis 20:00 ging bis zum 02.09.2026 durch, und derselbe Aufruf
           zwang den Modus danach auf "Schedule" — das Geraet faehrt ab da einen
           Plan ohne Dauer. Ueber Mitternacht bleibt erlaubt: 12/12 in der
           Bluete heisst oft 20:00 bis 08:00. */
        if (maengel.Count == 0 && !AcTest.ZeitplanErlaubt(request.Ein, request.Aus))
        {
            maengel.Add($"Ein- und Aus-Zeit sind beide {request.Ein} — das ergibt keinen Plan. "
                        + "Wer durchgehend laufen lassen will, stellt den Modus auf Dauerlauf.");
        }

        if (maengel.Count > 0)
        {
            return BadRequest(ApiErrorFactory.Validation(string.Join(" ", maengel)));
        }

        var einstellungen = _grows.GetEffectiveHomeAssistantSettings();
        var schritte = new List<AcSchreibschritt>
        {
            new(geraet.EinZeitEntityId!, "time", "set_value",
                new Dictionary<string, object> { ["time"] = request.Ein + ":00" }, request.Ein),
            new(geraet.AusZeitEntityId!, "time", "set_value",
                new Dictionary<string, object> { ["time"] = request.Aus + ":00" }, request.Aus),
        };

        // Der Modus nur, wenn er eingetragen ist — und immer als Letztes.
        if (!string.IsNullOrWhiteSpace(geraet.ModusEntityId))
        {
            schritte.Add(new AcSchreibschritt(
                geraet.ModusEntityId!, "select", "select_option",
                new Dictionary<string, object> { ["option"] = AcModi.Zeitplan }, AcModi.Zeitplan));
        }

        var ergebnisse = await _schreiber.SchreibenAsync(einstellungen, schritte, ct: ct);
        return Antworten(zelt.Name, geraet.Name, $"Zeitplan {request.Ein}–{request.Aus}", ergebnisse);
    }

    /// <summary>
    /// Aus den Ergebnissen eine ehrliche Antwort machen — und ins Protokoll.
    /// </summary>
    /// <remarks>
    /// <para><b>Teilerfolg ist kein Erfolg — aber „nicht bestätigt" ist auch
    /// kein Fehlschlag.</b> Die erste Fassung gab hier 502 zurück, sobald die
    /// Bestätigung ausblieb. Der Tester hat das sofort erlebt: „manchmal kommt
    /// 502 — aber das Schalten funktioniert." Beides stimmte. Die
    /// AC-Infinity-Integration meldet den neuen Wert oft erst Minuten später
    /// zurück, länger als die Nachkontrolle wartet.</para>
    ///
    /// <para>Deshalb drei Ausgänge statt zwei: alles bestätigt → 200 mit
    /// <c>ok=true</c>. Gesendet, aber (noch) nicht zurückgemeldet → 200 mit
    /// <c>ok=false</c> und Klartext — die Oberfläche zeigt das gelb und liest
    /// später nach. Nur wenn Home Assistant den Aufruf gar nicht ANNIMMT, ist
    /// es ein Fehler (502): dann wurde nichts geschaltet.</para>
    /// </remarks>
    private IActionResult Antworten(
        string zeltName, string geraetName, string was, IReadOnlyList<AcSchrittErgebnis> ergebnisse)
    {
        var antwort = AcStellAntwort.Bauen(ergebnisse);
        var abgelehnt = AcStellAntwort.SendungAbgelehnt(ergebnisse);

        _protokoll.Add(new SystemAuditEvent
        {
            EventType = AcTest.ProtokollTyp,
            Action = antwort.Ok ? "gestellt" : abgelehnt ? "abgelehnt" : "nicht-bestaetigt",
            Summary = AcStellAntwort.ProtokollZeile(zeltName, geraetName, was, ergebnisse),
            Severity = antwort.Ok ? "info" : "warning",
            Success = antwort.Ok,
        });

        if (abgelehnt)
        {
            return StatusCode(502, ApiErrorFactory.Create(
                "ha_nimmt_nicht_an",
                "Home Assistant hat den Aufruf nicht angenommen — es wurde nichts geschaltet. "
                + "Steht die Verbindung, und gibt es die Entität noch?",
                502));
        }

        return Ok(antwort);
    }

    public sealed class StufeRequest
    {
        public string EntityId { get; set; } = string.Empty;
        public double Stufe { get; set; }
    }

    public sealed class ZeitplanRequest
    {
        public string EntityId { get; set; } = string.Empty;

        /// <summary>Ein-Zeit als HH:MM.</summary>
        public string Ein { get; set; } = string.Empty;

        /// <summary>Aus-Zeit als HH:MM.</summary>
        public string Aus { get; set; } = string.Empty;
    }
}
