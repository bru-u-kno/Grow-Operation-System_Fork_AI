using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Die Nachtabsenkung eines Grows — Plan ansehen, ein- und ausschalten.
/// </summary>
/// <remarks>
/// Der Plan ist abrufbar, BEVOR etwas geschrieben wird. Eine Automatik, deren
/// Wirkung man erst am Chiller merkt, hat in einer Anlage nichts verloren.
/// <para>Fork AI (A-003, 03.10.2026): Verwaltung, nicht Grow-Planung. Dasselbe
/// Speichern setzt am Zelt das Zielgerät in Home Assistant und die
/// Kühler-Steuerung (Steckdose, Ein/Aus, Taktgrenzen), nach der der
/// Kühler-Worker selbständig schaltet. Im Zweifel die höhere Stufe.</para>
/// </remarks>
[ApiController]
[KiStufe(KiStufe.Verwaltung)]
[Route("api/grows/{growId:int}/night-ramp")]
[Produces("application/json")]
public sealed class NightRampApiController : ApiControllerBase
{
    private readonly GrowRepository _grows;
    private readonly NachtabsenkungWriter _absenkung;
    private readonly AppSettingsRepository _einstellungen;
    private readonly SystemAuditRepository _protokoll;

    public NightRampApiController(
        GrowRepository grows, NachtabsenkungWriter absenkung, AppSettingsRepository einstellungen,
        SystemAuditRepository protokoll)
    {
        _grows = grows;
        _absenkung = absenkung;
        _einstellungen = einstellungen;
        _protokoll = protokoll;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(NightRampDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<NightRampDto> Get(int growId)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");

        return Ok(Bauen(grow, _absenkung.PlanFuer(grow, DateTime.Now, vorschau: true)));
    }

    [HttpPut("")]
    [ProducesResponseType(typeof(NightRampDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<NightRampDto> Put(int growId, [FromBody] NightRampRequest request)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");

        if (request.FloorC is { } boden && boden < NachtabsenkungService.AbsoluteUntergrenzeC)
        {
            return BadRequestError(
                "floor_too_low",
                $"Die Untergrenze darf nicht unter {NachtabsenkungService.AbsoluteUntergrenzeC:0.#} °C liegen — "
                    + "darunter schadet die Kühlung mehr, als der Stress bringt.");
        }

        /* SAGEN, was nicht gespeichert werden kann — statt es wegzuwerfen.
           Zielgeraet und Kuehler haengen am ZELT. Hat der Grow keines (nach
           einem Import ein ganz normaler Zustand), fiel bis zum 02.09.2026 der
           ganze Block still aus und die Antwort war trotzdem 200. Die
           Oberflaeche schreibt die Antwort ins Formular zurueck: das Feld lief
           leer, daneben stand "Gespeichert.", und die Voraussetzungskette
           forderte genau das wieder an, was der Nutzer eben eingetragen hatte.
           Eine geschlossene Schleife. */
        var willZelteinstellung = !string.IsNullOrWhiteSpace(request.TargetEntityId)
                                  || (request.Chiller is { } c && (c.Enabled || !string.IsNullOrWhiteSpace(c.SwitchEntityId)));
        if (willZelteinstellung && grow.TentId is null)
        {
            return BadRequestError(
                "grow_without_tent",
                "Zielgeraet und Kuehler haengen am Zelt, und dieser Grow ist keinem zugeordnet. "
                + "Ordne ihn unter „Grow bearbeiten\" einem Zelt zu — danach lassen sich beide "
                + "einstellen.");
        }

        /* Und die DOMAENE der Kennung.
           Der Kuehler-Worker schaltet ueber switch.turn_on. Wer
           light.zelt_kuehler oder input_boolean.kuehler eintraegt — beides
           Kennungen, die es in einer Home-Assistant-Installation gibt —, bekam
           "Gespeichert" und eine Steuerung, die nie schaltet. */
        if (request.Chiller?.SwitchEntityId is { } schalter && !string.IsNullOrWhiteSpace(schalter)
            && !schalter.Trim().StartsWith("switch.", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequestError(
                "chiller_not_a_switch",
                $"„{schalter.Trim()}\" ist keine Steckdose. Der Kuehler wird ueber switch.turn_on "
                + "geschaltet; die Kennung muss deshalb mit „switch.\" beginnen.");
        }

        // Das Zielgeraet haengt am Zelt, wird aber hier gepflegt: der Nutzer
        // trifft beide Entscheidungen an derselben Stelle, statt sie sich aus
        // zwei Formularen zusammenzusuchen.
        if (request.TargetEntityId is not null && grow.TentId is { } tentId)
        {
            var zelt = _grows.GetTents(includeArchived: true).FirstOrDefault(t => t.Id == tentId);
            if (zelt is not null)
            {
                zelt.WaterTargetEntityId = string.IsNullOrWhiteSpace(request.TargetEntityId)
                    ? null
                    : request.TargetEntityId.Trim();
                _grows.UpdateTent(zelt);
            }
        }

        grow.NightRampEnabled = request.Enabled;
        // Gedeckelt beim SPEICHERN, nicht erst beim Rechnen. Vorher stand eine
        // Untergrenze von 8 roh in der Kachel („8 °C · Von dir gesetzt"), waehrend
        // die Rampentabelle danebendran bei 12 endete — die Rechnung deckelt seit
        // jeher, die Anzeige las den ungedeckelten Wert.
        grow.NightRampFloorC = request.FloorC is { } bodenWunsch
            ? Math.Max(bodenWunsch, NachtabsenkungService.AbsoluteUntergrenzeC)
            : null;
        // Die Kuehler-Einstellungen haengen am ZELT, nicht am Grow: ein
        // Kompressor gehoert zur Anlage und ueberlebt jeden Lauf.
        if (request.Chiller is { } kuehler && grow.TentId is { } zeltId)
        {
            var zelt = _grows.GetTents(includeArchived: true).FirstOrDefault(t => t.Id == zeltId);
            if (zelt is not null)
            {
                zelt.ChillerControlEnabled = kuehler.Enabled;
                zelt.ChillerSwitchEntityId = string.IsNullOrWhiteSpace(kuehler.SwitchEntityId)
                    ? null
                    : kuehler.SwitchEntityId.Trim();

                // Wer 0 schickt, meint nicht „ohne Totband" — er hat das Feld
                // leergeraeumt. Eine erste Fassung deckelte das auf das MINIMUM
                // (0,1 °C, 1 Minute) und waehlte damit ausgerechnet die
                // Einstellung, die den Kompressor am haerteste taktet. Unter dem
                // erlaubten Bereich gilt deshalb der Standard, nicht die Kante.
                zelt.ChillerHysteresisC = ZahlOderStandard(
                    kuehler.HysteresisC, 0.1, 3.0, KuehlerService.StandardHystereseC, zelt.ChillerHysteresisC);
                zelt.ChillerMinRunMinutes = (int)ZahlOderStandard(
                    kuehler.MinRunMinutes, 1, 60, KuehlerService.StandardMindestlaufMinuten, zelt.ChillerMinRunMinutes);
                zelt.ChillerMinPauseMinutes = (int)ZahlOderStandard(
                    kuehler.MinPauseMinutes, 1, 60, KuehlerService.StandardMindestpauseMinuten, zelt.ChillerMinPauseMinutes);
                zelt.ChillerMaxReadingAgeMinutes = (int)ZahlOderStandard(
                    kuehler.MaxReadingAgeMinutes, 1, 120, KuehlerService.StandardHoechstalterMinuten, zelt.ChillerMaxReadingAgeMinutes);

                _grows.UpdateTent(zelt);
            }
        }

        _grows.UpdateGrow(grow);

        return Ok(Bauen(grow, _absenkung.PlanFuer(grow, DateTime.Now)));
    }

    /// <summary>
    /// Einen geschickten Wert uebernehmen — oder, wenn er unter der erlaubten
    /// Untergrenze liegt, den Standard statt der Kante.
    /// </summary>
    /// <remarks>
    /// <b>Warum nicht einfach <c>Math.Clamp</c>.</b> Eine 0 aus einem
    /// leergeraeumten Feld ist keine Absicht, sondern eine fehlende Angabe.
    /// Sie auf das Minimum zu deckeln setzt Totband 0,1 °C und Mindestpause
    /// 1 Minute — die haerteste Taktung, die dieser Regler hergibt, an einem
    /// Geraet, das daran kaputtgeht. Nach OBEN wird weiter gedeckelt: wer 90
    /// Minuten Mindestlauf tippt, meint eine lange Zeit und keine fehlende.
    /// </remarks>
    private static double ZahlOderStandard(
        double? gewuenscht, double min, double max, double standard, double bisher)
    {
        if (gewuenscht is not { } wert) return bisher;
        if (wert < min) return standard;
        return Math.Min(wert, max);
    }

    private static double ZahlOderStandard(
        int? gewuenscht, double min, double max, double standard, double bisher)
        => ZahlOderStandard((double?)gewuenscht, min, max, standard, bisher);

    private NightRampDto Bauen(GrowRun grow, Absenkplan plan)
    {
        var zelt = grow.TentId is { } tentId ? _grows.GetTents(includeArchived: true).FirstOrDefault(t => t.Id == tentId) : null;
        return new NightRampDto(
            grow.NightRampEnabled,
            grow.NightRampFloorC,
            NachtabsenkungService.AbsoluteUntergrenzeC,
            zelt?.WaterTargetEntityId,
            plan,
            zelt is null ? null : new KuehlerDto(
                zelt.ChillerControlEnabled,
                zelt.ChillerSwitchEntityId,
                zelt.ChillerHysteresisC,
                zelt.ChillerMinRunMinutes,
                zelt.ChillerMinPauseMinutes,
                zelt.ChillerMaxReadingAgeMinutes,
                KuehlerWorker.LetzteSchaltung(_einstellungen, zelt.Id)),
            StandBauen(grow, zelt, plan));
    }

    /// <summary>
    /// „Läuft es gerade?" — die Kette der Voraussetzungen, jede einzeln.
    /// </summary>
    /// <remarks>
    /// <b>Der letzte Schreibvorgang ist der eigentliche Beleg.</b> Alle Haken
    /// gesetzt heisst „müsste laufen"; eine Zeile aus dem Anlagen-Protokoll
    /// heisst „hat gelaufen". Die Rampe schreibt nur an den Lichtflanken, also
    /// zweimal am Tag — ohne diesen Zeitpunkt bleibt offen, ob je etwas ankam.
    /// </remarks>
    private Steuerungsstand StandBauen(GrowRun grow, Tent? zelt, Absenkplan plan)
    {
        var einstellungen = _grows.GetEffectiveHomeAssistantSettings();

        // KEIN Kuehler-Urteil hier. Eine erste Fassung baute dafuer eine
        // KuehlerLage mit lauter null zusammen — der Regler antwortete
        // folgerichtig „Keine Wassertemperatur gemessen", und dieser Satz stand
        // dann neben einem gruenen Haken. Ein erfundener Zustand liefert einen
        // erfundenen Grund.
        //
        // Diese Seite beantwortet „ist es eingerichtet und aktiv". WAS der
        // Regler gerade tut, steht auf der Live-Seite — dort mit der echten
        // Lage aus Messwert und Steckdose.

        /* Der letzte ERFOLGREICHE Eintrag, nicht der letzte ueberhaupt: bis zum
           02.09.2026 wurde EIN Eintrag geholt und danach auf Erfolg gefiltert.
           Ein einziger Fehlversuch — Home Assistant kurz weg — loeschte damit
           die Angabe, obwohl die Rampe seit Wochen zweimal taeglich schreibt. */
        var letzterSollwert = NightRampAuskunft.LetzterErfolgUtc(_protokoll, "night-ramp");
        var letzteSchaltung = zelt is null
            ? null
            : KuehlerWorker.LetzteSchaltung(_einstellungen, zelt.Id);

        return SteuerungsstandBauer.Bauen(
            grow, zelt, plan, einstellungen.IsConfigured, DemoData.IsEnabled,
            letzterSollwert, letzteSchaltung);
    }

    public sealed class NightRampRequest
    {
        public bool Enabled { get; set; }
        public double? FloorC { get; set; }

        /// <summary>Die HA-Entität, die den Sollwert annimmt; leer schaltet das Schreiben ab.</summary>
        public string? TargetEntityId { get; set; }

        /// <summary>Die Kühler-Steuerung. Fehlt sie, bleibt sie unverändert.</summary>
        /// <remarks>
        /// Ausdrücklich <c>null</c>-fähig: ein Teil-Speichern der Rampe darf die
        /// Kühler-Einstellungen nicht zurücksetzen. Dieselbe Klasse Fehler, gegen
        /// die <c>SettingsApiController</c> die Felder aus <c>existing</c> rettet.
        /// </remarks>
        public KuehlerRequest? Chiller { get; set; }
    }

    public sealed class KuehlerRequest
    {
        public bool Enabled { get; set; }
        public string? SwitchEntityId { get; set; }
        public double? HysteresisC { get; set; }
        public int? MinRunMinutes { get; set; }
        public int? MinPauseMinutes { get; set; }
        public int? MaxReadingAgeMinutes { get; set; }
    }
}

/// <param name="TargetEntityId">Wohin geschrieben wird; null heisst: nur Plan, kein Eingriff.</param>
public sealed record NightRampDto(
    bool Enabled,
    double? FloorC,
    double HardFloorC,
    string? TargetEntityId,
    Absenkplan Plan,
    KuehlerDto? Chiller,
    Steuerungsstand Stand);

/// <summary>Die Kühler-Steuerung dieses Zelts.</summary>
/// <param name="SwitchEntityId">Die smarte Steckdose, an der der Kühler hängt.</param>
/// <param name="HysteresisC">Halbes Totband: an bei Soll + h, aus bei Soll − h.</param>
/// <param name="LastSwitchUtc">
/// Wann zuletzt geschaltet wurde. Daraus rechnet die Oberfläche, wie lange die
/// Kompressor-Sperre noch läuft — sonst sieht ein Nutzer nur, dass nichts
/// passiert, und nicht warum.
/// </param>
public sealed record KuehlerDto(
    bool Enabled,
    string? SwitchEntityId,
    double HysteresisC,
    int MinRunMinutes,
    int MinPauseMinutes,
    int MaxReadingAgeMinutes,
    DateTime? LastSwitchUtc);
