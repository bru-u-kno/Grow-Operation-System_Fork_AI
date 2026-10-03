using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Einstellungen → „Zugriff für KI-Assistenten".
/// </summary>
/// <remarks>
/// <para>Die Schlüsselverwaltung liegt unter <c>/api/settings</c> — also auf einem
/// Verwaltungsweg, den ein Nachbar-Add-on nicht einmal lesen darf — <b>und</b>
/// trägt <see cref="KeinKiZugriffAttribute"/>. Doppelt, weil ein Schlüssel mit
/// Verwaltung sonst Einstellungen ändern dürfte und damit auch Schlüssel: sich
/// selbst weitere Stufen geben, einen zweiten erzeugen, den Hauptschalter
/// umlegen.</para>
///
/// <para>Ausnahme ist <see cref="Ich"/>: ein lesender Weg ausserhalb von
/// <c>/api/settings</c>, den nur ein Schlüssel sinnvoll ruft. Lesen braucht
/// keine Stufe, und <see cref="KeinKiZugriffAttribute"/> greift nur bei
/// schreibenden Aktionen und Verwaltungswegen (siehe
/// <see cref="KiZugriffSperre.Entscheiden"/>).</para>
/// </remarks>
[ApiController]
[Route("api/settings/ki-zugriff")]
[Produces("application/json")]
[KeinKiZugriff("Die Schlüsselverwaltung — sonst könnte sich ein Schlüssel selbst Stufen geben oder einen zweiten erzeugen.")]
public sealed class KiZugriffApiController : ApiControllerBase
{
    private readonly KiZugriffDienst _dienst;
    private readonly SystemAuditRepository _protokoll;

    // Grenzen für die Höchstwerte. Darüber ist es kein Höchstwert mehr, sondern ein Tippfehler.
    private const double HoechsteDosisMl = 1000;
    private const int HoechsteSchaltbefehle = 1000;

    // Feldnamen in den Fehlern: die Eigenschaftsnamen. Die Oberfläche ordnet nach dem letzten Namensteil zu.
    private const string FeldDosis = "Hoechstwerte.MaxDosisMlJeBefehl";
    private const string FeldSchaltbefehle = "Hoechstwerte.MaxSchaltbefehleJeStunde";
    private static readonly string DosisMeldung = $"Die Höchstdosis je Befehl muss über 0 und höchstens {HoechsteDosisMl:0} ml sein.";
    private static readonly string SchaltbefehleMeldung = $"Die Schaltbefehle je Stunde müssen eine ganze Zahl von 0 bis {HoechsteSchaltbefehle} sein (0 = keine).";

    public KiZugriffApiController(KiZugriffDienst dienst, SystemAuditRepository protokoll)
    {
        _dienst = dienst;
        _protokoll = protokoll;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(KiZugriffSeiteDto), StatusCodes.Status200OK)]
    public ActionResult<KiZugriffSeiteDto> Seite() => Ok(SeiteBauen());

    [HttpPut("")]
    [ProducesResponseType(typeof(KiZugriffSeiteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<KiZugriffSeiteDto> Speichern([FromBody] KiZugriffSpeichernRequest? request)
    {
        if (request is null)
        {
            UnlesbareFelderUebersetzen();
            return ValidationError("Die Einstellungen für KI-Assistenten waren nicht lesbar.");
        }

        var bisher = _dienst.Einstellungen();

        KiStufe? rueckfrage = null;
        if (!string.IsNullOrWhiteSpace(request.RueckfrageAbStufe))
        {
            rueckfrage = KiZugriffDienst.EinzelneStufe(request.RueckfrageAbStufe);
            if (rueckfrage is null)
            {
                ModelState.AddModelError(nameof(KiZugriffSpeichernRequest.RueckfrageAbStufe),
                    $"Unbekannte Stufe „{request.RueckfrageAbStufe}“. Erlaubt: {ErlaubteStufen()} — oder leer für „nie“.");
            }
        }

        var hoechstwerte = request.Hoechstwerte ?? bisher.Hoechstwerte;
        // Dosis: mehr als 0 — eine Höchstdosis von 0 ml wäre ein verkleideter Schalter.
        // Schaltbefehle: 0 ist erlaubt und heisst „keine Schaltbefehle über einen Schlüssel".
        if (!double.IsFinite(hoechstwerte.MaxDosisMlJeBefehl)
            || hoechstwerte.MaxDosisMlJeBefehl <= 0
            || hoechstwerte.MaxDosisMlJeBefehl > HoechsteDosisMl)
        {
            ModelState.AddModelError(FeldDosis, DosisMeldung);
        }
        if (hoechstwerte.MaxSchaltbefehleJeStunde is < 0 or > HoechsteSchaltbefehle)
        {
            ModelState.AddModelError(FeldSchaltbefehle, SchaltbefehleMeldung);
        }

        if (!ModelState.IsValid)
        {
            return ValidationError("Die Einstellungen für KI-Assistenten konnten nicht gespeichert werden.");
        }

        _dienst.EinstellungenSpeichern(new KiZugriffEinstellungen(request.Aktiv, rueckfrage, hoechstwerte));
        if (bisher.Aktiv != request.Aktiv)
        {
            Protokollieren(request.Aktiv ? "ki-zugriff-eingeschaltet" : "ki-zugriff-ausgeschaltet",
                request.Aktiv ? "Zugriff für KI-Assistenten eingeschaltet." : "Zugriff für KI-Assistenten ausgeschaltet.");
        }
        return Ok(SeiteBauen());
    }

    [HttpPost("schluessel")]
    [ProducesResponseType(typeof(KiSchluesselAngelegtDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<KiSchluesselAngelegtDto> SchluesselAnlegen([FromBody] KiSchluesselRequest? request)
    {
        if (!Pruefen(request, neu: true, out var name, out var stufen))
        {
            if (request is null) UnlesbareFelderUebersetzen();
            return ValidationError("Der Schlüssel konnte nicht angelegt werden.");
        }

        var (schluessel, klartext) = _dienst.Anlegen(name, stufen);
        Protokollieren("ki-schluessel-angelegt",
            $"Schlüssel ‚{schluessel.Name}‘ ({schluessel.Praefix}…) angelegt, Stufen: {StufenText(schluessel.Stufen)}.");
        return StatusCode(StatusCodes.Status201Created, new KiSchluesselAngelegtDto(KiZugriffDienst.ZuDto(schluessel), klartext));
    }

    [HttpPut("schluessel/{id:int}")]
    [ProducesResponseType(typeof(KiSchluesselDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<KiSchluesselDto> SchluesselAendern(int id, [FromBody] KiSchluesselRequest? request)
    {
        if (_dienst.Hole(id) is null) return NichtGefunden(id);
        if (!Pruefen(request, neu: false, out var name, out var stufen))
        {
            if (request is null) UnlesbareFelderUebersetzen();
            return ValidationError("Der Schlüssel konnte nicht geändert werden.");
        }

        _dienst.Aendern(id, name, stufen);
        var geaendert = _dienst.Hole(id)!;
        Protokollieren("ki-schluessel-geaendert",
            $"Schlüssel ‚{geaendert.Name}‘ ({geaendert.Praefix}…) geändert, Stufen: {StufenText(geaendert.Stufen)}.");
        return Ok(KiZugriffDienst.ZuDto(geaendert));
    }

    [HttpPost("schluessel/{id:int}/sperren")]
    [ProducesResponseType(typeof(KiSchluesselDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<KiSchluesselDto> SchluesselSperren(int id)
    {
        if (!_dienst.Sperren(id)) return NichtGefunden(id);
        var gesperrt = _dienst.Hole(id)!;
        Protokollieren("ki-schluessel-gesperrt", $"Schlüssel ‚{gesperrt.Name}‘ ({gesperrt.Praefix}…) gesperrt.");
        return Ok(KiZugriffDienst.ZuDto(gesperrt));
    }

    [HttpDelete("schluessel/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult SchluesselLoeschen(int id)
    {
        var vorher = _dienst.Hole(id);
        if (vorher is null || !_dienst.Loeschen(id)) return NichtGefunden(id);
        Protokollieren("ki-schluessel-geloescht", $"Schlüssel ‚{vorher.Name}‘ ({vorher.Praefix}…) gelöscht.");
        return NoContent();
    }

    /// <summary>Was der anfragende Schlüssel darf — die erste Frage eines Assistenten.</summary>
    /// <remarks>Ohne Schlüssel (etwa aus der Oberfläche) gibt es hier nichts zu sagen: 401.</remarks>
    [HttpGet("/api/ki-zugriff/ich")]
    [ProducesResponseType(typeof(KiZugriffIchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public ActionResult<KiZugriffIchDto> Ich()
    {
        if (KiZugriffKontext.Aus(HttpContext) is not { } kontext)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, ApiErrorFactory.Create(
                "ki_schluessel_fehlt",
                "Diese Abfrage gilt einem Schlüssel: Authorization: Bearer gok_… mitschicken.",
                StatusCodes.Status401Unauthorized,
                traceId: HttpContext?.TraceIdentifier));
        }

        var einstellungen = _dienst.Einstellungen();
        return Ok(new KiZugriffIchDto(
            kontext.SchluesselName,
            KiZugriffDienst.StufenNamen(kontext.Stufen),
            einstellungen.RueckfrageAbStufe?.ToString(),
            kontext.Hoechstwerte));
    }

    // --------------------------------------------------------------- Hilfe

    private KiZugriffSeiteDto SeiteBauen()
    {
        var einstellungen = _dienst.Einstellungen();
        return new KiZugriffSeiteDto(
            einstellungen.Aktiv,
            einstellungen.RueckfrageAbStufe?.ToString(),
            einstellungen.Hoechstwerte,
            _dienst.Alle().Select(KiZugriffDienst.ZuDto).ToList());
    }

    /// <summary>
    /// Name und Stufen prüfen — dieselben Regeln wie die Oberfläche.
    /// </summary>
    /// <remarks>
    /// Name Pflicht, höchstens 60 Zeichen; mindestens eine Stufe, beim Anlegen wie
    /// beim Ändern. Nur ein neuer Schlüssel OHNE Stufen-Feld bekommt die
    /// Vorbelegung; eine ausdrücklich leere Liste ist ein Fehler — wer alle
    /// Häkchen entfernt, will keinen Schlüssel, der nichts darf, sondern hat
    /// sich vertan (oder will löschen).
    /// </remarks>
    private bool Pruefen(KiSchluesselRequest? request, bool neu, out string name, out KiStufe stufen)
    {
        name = request?.Name?.Trim() ?? string.Empty;
        stufen = KiStufe.Keine;
        if (request is null) return false;

        if (name.Length == 0)
        {
            ModelState.AddModelError(nameof(KiSchluesselRequest.Name), "Bitte einen Namen angeben, etwa „Claude am Telefon“.");
        }
        else if (name.Length > KiZugriffDienst.NameHoechstLaenge)
        {
            ModelState.AddModelError(nameof(KiSchluesselRequest.Name),
                $"Der Name darf höchstens {KiZugriffDienst.NameHoechstLaenge} Zeichen haben.");
        }

        if (request.Stufen is null)
        {
            if (neu)
            {
                stufen = KiZugriffDienst.VorgabeStufen;
            }
            else
            {
                ModelState.AddModelError(nameof(KiSchluesselRequest.Stufen), "Bitte mindestens eine Stufe anhaken.");
            }
        }
        else
        {
            stufen = KiZugriffDienst.StufenLesen(request.Stufen, out var unbekannt);
            if (unbekannt.Count > 0)
            {
                ModelState.AddModelError(nameof(KiSchluesselRequest.Stufen),
                    $"Unbekannte Stufe {string.Join(", ", unbekannt.Select(u => $"„{u}“"))}. Erlaubt: {ErlaubteStufen()}.");
            }
            else if (stufen == KiStufe.Keine)
            {
                ModelState.AddModelError(nameof(KiSchluesselRequest.Stufen), "Bitte mindestens eine Stufe anhaken.");
            }
        }

        return ModelState.IsValid;
    }

    /// <summary>
    /// Kam der Körper gar nicht erst an (unlesbares JSON, Kommazahl im Ganzzahlfeld),
    /// stehen im ModelState die englischen Sätze des Model-Bindings unter
    /// JSON-Pfaden wie <c>$.hoechstwerte.maxSchaltbefehleJeStunde</c>. Hier werden
    /// daraus die Eigenschaftsnamen und ein deutscher Satz.
    /// </summary>
    private void UnlesbareFelderUebersetzen()
    {
        var felder = ModelState.Where(e => e.Value?.Errors.Count > 0).Select(e => e.Key).ToList();
        ModelState.Clear();

        foreach (var schluessel in felder)
        {
            var feld = string.Join(".", schluessel.TrimStart('$')
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => char.ToUpperInvariant(t[0]) + t[1..]));
            var meldung = feld switch
            {
                FeldSchaltbefehle => SchaltbefehleMeldung,
                FeldDosis => DosisMeldung,
                _ => "Dieser Wert ist nicht lesbar.",
            };
            ModelState.AddModelError(feld.Length == 0 || feld == "Request" ? "$" : feld, meldung);
        }

        if (ModelState.ErrorCount == 0) ModelState.AddModelError("$", "Es kam nichts Lesbares an.");
    }

    private static string ErlaubteStufen() => string.Join(", ", KiZugriffDienst.AlleStufen);

    private static string StufenText(KiStufe stufen)
        => stufen == KiStufe.Keine ? "keine" : KiZugriffDienst.Anzeigename(stufen);

    private ActionResult NichtGefunden(int id)
        => NotFoundError("ki_schluessel_nicht_gefunden", $"Einen Schlüssel mit der Nummer {id} gibt es nicht.");

    private void Protokollieren(string aktion, string zusammenfassung)
    {
        try
        {
            _protokoll.Add(new SystemAuditEvent
            {
                EventType = "security",
                Action = aktion,
                Summary = zusammenfassung,
                Severity = "warning",
                Source = "ki-zugriff",
                RemoteAddress = HttpContext?.Connection.RemoteIpAddress?.ToString(),
                Success = true,
            });
        }
        catch
        {
            // Das Protokoll darf die Verwaltung nicht blockieren.
        }
    }
}
