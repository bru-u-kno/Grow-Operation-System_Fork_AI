using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[KeinKiZugriff("Fehlerbehandler der App — wird nur intern nach einer Ausnahme angesprungen, nie als Befehl aufgerufen.")]
[Route("api/error")]
[Produces("application/json")]
public sealed class ApiErrorController : ApiControllerBase
{
    [HttpGet]
    [HttpPost]
    [HttpPut]
    [HttpDelete]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status500InternalServerError)]
    public ActionResult<ApiError> Error()
    {
        var exceptionFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        var code = exceptionFeature?.Error is InvalidOperationException
            ? "invalid_operation"
            : "internal_server_error";

        return StatusCode(
            StatusCodes.Status500InternalServerError,
            ApiErrorFactory.ServerError(code, "Ein unerwarteter Backend-Fehler ist aufgetreten.", HttpContext.TraceIdentifier));
    }
}
