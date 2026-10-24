using GrowDiary.Web.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Eine abgelehnte Eingabe — moeglichst mit dem Grund.</summary>
    /// <remarks>
    /// <para><b>Warum die Feldmeldungen NICHT in die Nachricht wandern.</b>
    /// Naheliegend waere es: <c>apiFetch</c> im Frontend liest ausschliesslich
    /// <c>message</c>, also sieht der Nutzer von <c>fieldErrors</c> nichts.
    /// Genau das war am 25.08.2026 der erste Anlauf — und der Pruefer hat ihn
    /// an der laufenden App widerlegt: auf <c>/aushaerten</c> stand danach
    /// „The field HumidityPercent must be between 0 and 100." Die Meldungen
    /// stammen naemlich zum grossen Teil gar nicht von uns, sondern aus
    /// DataAnnotations und dem Model-Binding — auf Englisch, 37 Attribute ohne
    /// eigenen Text. „Alles auf Deutsch" waere damit an 105 Stellen gebrochen
    /// gewesen.</para>
    ///
    /// <para><b>Stattdessen genau dort, wo es einen Satz gibt.</b> Wer einen
    /// eigenen, deutschen Grund kennt, uebergibt ihn — siehe
    /// <c>PlantsApiController.ValidateTopf</c>. Ohne den bleibt der bekannte
    /// Standardsatz.</para>
    /// </remarks>
    protected ActionResult ValidationError(string? message = null)
        => BadRequest(ApiErrorFactory.Validation(
            message ?? "Eingaben konnten nicht validiert werden.", ToFieldErrors(), TraceId));

    protected ActionResult BadRequestError(string code, string message)
        => BadRequest(ApiErrorFactory.BadRequest(code, message, TraceId));

    protected ActionResult NotFoundError(string code, string message)
        => NotFound(ApiErrorFactory.NotFound(code, message, TraceId));

    protected ActionResult ConflictError(string code, string message)
        => Conflict(ApiErrorFactory.Conflict(code, message, TraceId));

    protected ActionResult ForbiddenError(string code, string message)
        => StatusCode(StatusCodes.Status403Forbidden, ApiErrorFactory.Forbidden(code, message, TraceId));

    private string? TraceId => HttpContext?.TraceIdentifier;

    private IReadOnlyDictionary<string, string[]> ToFieldErrors()
        => ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => string.IsNullOrWhiteSpace(entry.Key) ? "$" : entry.Key,
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Ungültiger Wert." : error.ErrorMessage)
                    .Distinct()
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
}
