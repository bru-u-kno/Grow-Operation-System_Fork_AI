using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/strains")]
[Produces("application/json")]
[KiStufe(KiStufe.GrowPlanen)]
public sealed class StrainsApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;

    public StrainsApiController(GrowRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StrainDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<StrainDto>> List()
        => Ok(_repository.GetStrains().Select(strain => strain.ToDto()).ToList());

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(StrainDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<StrainDto> Detail(int id)
    {
        var strain = _repository.GetStrain(id);
        return strain is null
            ? NotFoundError("strain_not_found", $"Strain mit Id {id} existiert nicht.")
            : Ok(strain.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(StrainDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<StrainDto> Create([FromBody] CreateStrainRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        ValidateStrain(request.Name, request.FlowerWeeksMin, request.FlowerWeeksMax, request.VpdPreferenceShift, request.NutrientDemandFactor, request.StretchFactor);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var strain = _repository.CreateStrain(request.ToModel());
        return CreatedAtAction(nameof(Detail), new { id = strain.Id }, strain.ToDto());
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(StrainDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<StrainDto> Update(int id, [FromBody] UpdateStrainRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var strain = _repository.GetStrain(id);
        if (strain is null)
        {
            return NotFoundError("strain_not_found", $"Strain mit Id {id} existiert nicht.");
        }

        ValidateStrain(request.Name, request.FlowerWeeksMin, request.FlowerWeeksMax, request.VpdPreferenceShift, request.NutrientDemandFactor, request.StretchFactor);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        request.ApplyTo(strain);
        _repository.UpdateStrain(strain);
        return Ok(_repository.GetStrain(id)!.ToDto());
    }

    /// <summary>
    /// Multiplicative traits (feeding appetite, stretch) scale a baseline, so they must be
    /// above zero. The VPD preference is a <em>shift</em> in kPa — a strain that likes it
    /// more humid legitimately sits below zero, so it is validated as a range instead.
    /// </summary>
    private void ValidateStrain(string name, int? flowerWeeksMin, int? flowerWeeksMax, double? vpdPreferenceShift, params double?[] multipliers)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(CreateStrainRequest.Name), "Name darf nicht leer sein.");
        }

        if (flowerWeeksMin.HasValue && flowerWeeksMax.HasValue && flowerWeeksMin.Value > flowerWeeksMax.Value)
        {
            ModelState.AddModelError(nameof(CreateStrainRequest.FlowerWeeksMin), "FlowerWeeksMin darf nicht groesser als FlowerWeeksMax sein.");
        }

        if (multipliers.Any(factor => factor.HasValue && factor.Value <= 0))
        {
            ModelState.AddModelError("Factors", "Naehrstoffbedarf und Streckung muessen groesser als 0 sein.");
        }

        if (vpdPreferenceShift is { } shift && Math.Abs(shift) > 1)
        {
            ModelState.AddModelError(
                nameof(CreateStrainRequest.VpdPreferenceShift),
                "VPD-Vorliebe ist eine Verschiebung in kPa und sollte zwischen -1 und +1 liegen.");
        }
    }

    /// <summary>Eine Sorte entfernen.</summary>
    /// <remarks>
    /// <para><b>Warum es das erst seit dem 25.08.2026 gibt.</b> Sorten liessen
    /// sich anlegen und aendern, aber nirgends entfernen — wer sich vertippte,
    /// behielt den Eintrag fuer immer in jeder Auswahlliste. Gezaehlt hat das
    /// <c>CrudVollstaendigTests</c>: neun Controller mit demselben Loch.</para>
    ///
    /// <para><b>Der Waechter.</b> <c>PlantInstances.StrainId</c> und
    /// <c>CuringJars.StrainId</c> haengen mit <c>ON DELETE SET NULL</c> an der
    /// Sorte. Ohne diese Pruefung naehme ein Loeschen den betroffenen Pflanzen
    /// und Glaesern wortlos ihre Sorte.</para>
    /// </remarks>
    [HttpDelete("{id:int}")]
    [KiStufe(KiStufe.Verwaltung)]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Delete(int id)
    {
        var sorte = _repository.GetStrain(id);
        if (sorte is null)
        {
            return NotFoundError("strain_not_found", $"Sorte mit Id {id} existiert nicht.");
        }

        var pflanzen = _repository.CountPlantsWithStrain(id);
        var glaeser = _repository.CountCuringJarsWithStrain(id);
        // Grows.StrainId hat keinen Fremdschluessel — der Verweis bliebe stehen
        // und zeigte auf eine Sorte, die es nicht mehr gibt.
        var grows = _repository.CountGrowsWithStrain(id);
        if (pflanzen > 0 || glaeser > 0 || grows > 0)
        {
            var wo = new List<string>();
            if (pflanzen > 0) wo.Add($"{pflanzen} Pflanzen");
            if (glaeser > 0) wo.Add($"{glaeser} Aushärte-Gläser");
            if (grows > 0) wo.Add($"{grows} Grows");
            return ValidationError(
                $"'{sorte.Name}' wird noch benutzt: {string.Join(" und ", wo)}. "
                + "Sonst verlören die ihre Sorte, ohne dass es jemand merkt.");
        }

        _repository.DeleteStrain(id);
        return NoContent();
    }

}
