using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

public sealed partial class SystemApiController
{
    /// <summary>
    /// Der stabile Weg auf das Handy.
    /// </summary>
    /// <remarks>
    /// Cultivate Control wird über den Ingress ausgeliefert. Dieser Pfad trägt ein Token,
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
    public async Task<IActionResult> MobileAccess(
        [FromServices] SupervisorInfoService supervisor,
        [FromServices] IngressPanelService? ingressPanele,
        CancellationToken cancellationToken)
    {
        var slug = await supervisor.GetAddonSlugAsync(cancellationToken);
        var panelPath = SupervisorInfoService.PanelPath(slug);

        // Gibt es für diese App schon ein Ingress-Panel ohne HA-Titelleiste (hass_ingress, ui_mode normal)?
        // Dieselbe Abfrage, mit der die Push-Meldungen ihren Link wählen; die Antwort ist zehn Minuten gemerkt.
        // Die Oberfläche zeigt damit die Hilfe „Ohne HA-Titelleiste“ passend zum Stand.
        var ingressPanel = await IngressPanelService.PanelOderNullAsync(
            ingressPanele, _repository.GetEffectiveHomeAssistantSettings(), slug, cancellationToken);

        return Ok(new
        {
            available = panelPath is not null,
            slug,
            panelPath,
            ingressPanel,
            reason = panelPath is null
                ? "Cultivate Control läuft hier nicht als Home-Assistant-Add-on — es gibt keinen Panel-Pfad, auf den ein Handy zeigen könnte."
                : null,
        });
    }
}
