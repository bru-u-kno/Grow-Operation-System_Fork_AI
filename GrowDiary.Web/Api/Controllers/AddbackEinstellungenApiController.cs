using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Die Vorgaben des Nachfüllens: ob und nach wie vielen Minuten der Fork die Nachmessung
/// selbst einträgt.
/// </summary>
/// <remarks>
/// <para>Eine Einstellung der Anlage, nicht des Geräts: wer „als meinen Standard merken" wählt,
/// soll am Handy dasselbe sehen wie am Rechner. Gespeichert in <c>AppSettings</c>
/// (<see cref="AppSettingsRepository"/>), je Wert ein Schlüssel — es gibt keine eigene Tabelle.</para>
/// <para>Die Vorgabe füllt nur das Formular vor; jedes einzelne Nachfüllen kann sie ändern
/// (<c>NachmessungMinuten</c> an <c>POST …/addback/vorgaenge</c>).</para>
/// </remarks>
[ApiController]
[Route("api/addback/einstellungen")]
[Produces("application/json")]
[KiStufe(KiStufe.GrowPlanen)]
public sealed class AddbackEinstellungenApiController : ApiControllerBase
{
    public const string SchluesselAutomatisch = "addback.nachmessung.automatisch";
    public const string SchluesselMinuten = "addback.nachmessung.minuten";

    /// <summary>Ohne Wahl: an, nach 15 Minuten — der Mischvorgang im Tank ist dann durch.</summary>
    public const int StandardMinuten = 15;

    private readonly AppSettingsRepository _einstellungen;

    public AddbackEinstellungenApiController(AppSettingsRepository einstellungen)
    {
        _einstellungen = einstellungen;
    }

    /// <summary>Die gespeicherte Vorgabe — oder der Standard, solange nichts gewählt wurde.</summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(AddbackEinstellungenDto), StatusCodes.Status200OK)]
    public ActionResult<AddbackEinstellungenDto> Get() => Ok(Lesen());

    /// <summary>Speichert die Vorgabe.</summary>
    [HttpPut("")]
    [ProducesResponseType(typeof(AddbackEinstellungenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<AddbackEinstellungenDto> Put([FromBody] AddbackEinstellungenDto anfrage)
    {
        if (anfrage.NachmessungMinuten is < 1 or > 240)
        {
            ModelState.AddModelError(nameof(anfrage.NachmessungMinuten), "Die Nachmessung kann nach 1 bis 240 Minuten erfolgen.");
            return ValidationError();
        }

        _einstellungen.SetValue(SchluesselAutomatisch, anfrage.NachmessungAutomatisch ? "1" : "0");
        _einstellungen.SetValue(SchluesselMinuten, anfrage.NachmessungMinuten.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Ok(Lesen());
    }

    private AddbackEinstellungenDto Lesen()
    {
        var automatisch = _einstellungen.GetValue(SchluesselAutomatisch) != "0";
        var minuten = int.TryParse(_einstellungen.GetValue(SchluesselMinuten), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var m) && m is >= 1 and <= 240
            ? m
            : StandardMinuten;
        return new AddbackEinstellungenDto(automatisch, minuten);
    }
}

/// <summary>Die Vorgabe der automatischen Nachmessung.</summary>
public sealed record AddbackEinstellungenDto(bool NachmessungAutomatisch, int NachmessungMinuten);
