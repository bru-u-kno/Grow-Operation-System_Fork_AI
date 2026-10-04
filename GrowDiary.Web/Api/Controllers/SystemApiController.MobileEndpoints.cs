using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

public sealed partial class SystemApiController
{
    /// <summary>
    /// Der stabile Weg auf das Handy.
    /// </summary>
    /// <remarks>
    /// Grow OS wird über den Ingress ausgeliefert. Dieser Pfad trägt ein Token,
    /// das pro Anfrage wechselt — wer sich die Adresse aus der Adresszeile als
    /// Lesezeichen ablegt, hat morgen eine tote Kachel auf dem Startbildschirm.
    ///
    /// Stabil ist der Pfad <c>/app/&lt;slug&gt;</c> (siehe <see cref="SupervisorInfoService.PanelPath"/>). Zurückgegeben
    /// wird nur der Pfad, nicht die volle Adresse: welcher Name für Home
    /// Assistant gilt, weiss der Server nicht — er kennt sich selbst nur als
    /// <c>http://supervisor/core</c>. Der Browser weiss es, denn er ist gerade
    /// darüber verbunden.
    /// </remarks>
    [HttpGet("mobile-access")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MobileAccess([FromServices] SupervisorInfoService supervisor, CancellationToken cancellationToken)
    {
        var slug = await supervisor.GetAddonSlugAsync(cancellationToken);
        var panelPath = SupervisorInfoService.PanelPath(slug);

        return Ok(new
        {
            available = panelPath is not null,
            slug,
            panelPath,
            reason = panelPath is null
                ? "Grow OS läuft hier nicht als Home-Assistant-Add-on — es gibt keinen Panel-Pfad, auf den ein Handy zeigen könnte."
                : null,
        });
    }
}
