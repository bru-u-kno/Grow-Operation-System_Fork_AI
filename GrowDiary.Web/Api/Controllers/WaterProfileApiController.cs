using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>The values a user copies out of the city's tap-water report.</summary>
/// <remarks>
/// One profile, app-wide — the water in the pipe is the same for every grow.
/// A grow opts in through its WaterSource (Tap/Mixed); RO grows ignore it.
/// </remarks>
[ApiController]
[Route("api/water-profile")]
[Produces("application/json")]
[KiStufe(KiStufe.GrowPlanen)]
public sealed class WaterProfileApiController : ApiControllerBase
{
    private readonly WaterProfileStore _store;
    private readonly WasserAmpelService _ampel;

    public WaterProfileApiController(WaterProfileStore store, WasserAmpelService ampel)
    {
        _store = store;
        _ampel = ampel;
    }

    /// <summary>Die Bewertung des Profils — was die Zahlen für den Grow bedeuten.</summary>
    /// <remarks>
    /// Eigener Endpunkt statt eines Feldes am Profil: das Profil ist das, was im
    /// Bericht der Stadt steht, und die Bewertung ist unsere Lesart davon. Wer
    /// das Profil speichert, soll nicht aus Versehen ein Urteil mitschicken.
    /// </remarks>
    [HttpGet("rating")]
    [ProducesResponseType(typeof(WasserAmpel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult<WasserAmpel> Rating()
        => _ampel.Aktuell() is { } ampel ? Ok(ampel) : NoContent();

    /// <summary>The stored profile — an empty one if none was saved yet.</summary>
    /// <remarks>
    /// 200 mit leerem Profil statt 404: fuer die Eingabeseite ist „noch nichts
    /// eingetragen" kein Fehler, sondern der Ausgangszustand des Formulars.
    /// </remarks>
    [HttpGet("")]
    [ProducesResponseType(typeof(WaterProfile), StatusCodes.Status200OK)]
    public ActionResult<WaterProfile> Get()
        => Ok(_store.Get() ?? new WaterProfile());

    [HttpPut("")]
    [ProducesResponseType(typeof(WaterProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<WaterProfile> Put([FromBody] WaterProfile profile)
    {
        // Nur das Grobe abfangen. Ob 276 µS/cm stimmt, weiss allein der Bericht —
        // aber ein negativer Wert oder ein pH von 25 ist sicher ein Tippfehler,
        // und der faellt besser hier auf als spaeter im Lagebericht.
        foreach (var (name, wert) in new (string, double?)[]
        {
            (nameof(profile.ConductivityUsCm), profile.ConductivityUsCm),
            (nameof(profile.TotalHardnessDh), profile.TotalHardnessDh),
            (nameof(profile.CarbonateHardnessDh), profile.CarbonateHardnessDh),
            (nameof(profile.CalciumMgL), profile.CalciumMgL),
            (nameof(profile.MagnesiumMgL), profile.MagnesiumMgL),
            (nameof(profile.SodiumMgL), profile.SodiumMgL),
            (nameof(profile.NitrateMgL), profile.NitrateMgL),
            (nameof(profile.SulfateMgL), profile.SulfateMgL),
            (nameof(profile.ChlorideMgL), profile.ChlorideMgL),
            (nameof(profile.TreatedConductivityUsCm), profile.TreatedConductivityUsCm),
        })
        {
            if (wert is < 0)
            {
                ModelState.AddModelError(name, "Der Wert kann nicht negativ sein.");
            }
        }

        if (profile.Ph is < 0 or > 14)
        {
            ModelState.AddModelError(nameof(profile.Ph), "pH liegt zwischen 0 und 14.");
        }

        if (profile.TreatedPh is < 0 or > 14)
        {
            ModelState.AddModelError(nameof(profile.TreatedPh), "pH liegt zwischen 0 und 14.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        _store.Save(profile);
        return Ok(profile);
    }
}
