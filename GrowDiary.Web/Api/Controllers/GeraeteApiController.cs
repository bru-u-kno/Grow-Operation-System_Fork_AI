using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Fork AI (forkai.22): Die Geräteliste — alles, was der Fork an Entitäten benutzt,
/// nach Geräten sortiert.
/// </summary>
/// <remarks>
/// <para>Diese Etappe liest nur. Geändert wird weiter an den bisherigen Stellen;
/// die Liste sagt dafür, wo das ist — je Entität steht, wofür sie benutzt wird und
/// aus welcher Quelle das kommt.</para>
/// <para>Fork AI (A-003, 03.10.2026): Zuordnen, Umbenennen, Rubriken — reine
/// Stammdaten des Forks, nichts schaltet. Über einen Schlüssel Verwaltung.</para>
/// </remarks>
[ApiController]
[KiStufe(KiStufe.Verwaltung)]
[Route("api/geraete")]
[Produces("application/json")]
public sealed class GeraeteApiController : ApiControllerBase
{
    private readonly GeraeteUebersichtService _geraete;
    private readonly GeraeteRepository _repo;
    private readonly SteuerungGeraeteService _rollen;
    private readonly SteuerungAbsicherungService _absicherung;
    private readonly HomeAssistantSettingsRepository _haSettings;

    public GeraeteApiController(GeraeteUebersichtService geraete, GeraeteRepository repo, SteuerungGeraeteService rollen,
        SteuerungAbsicherungService absicherung, HomeAssistantSettingsRepository haSettings)
    {
        _geraete = geraete;
        _repo = repo;
        _rollen = rollen;
        _absicherung = absicherung;
        _haSettings = haSettings;
    }

    [HttpGet]
    [ProducesResponseType(typeof(GeraeteSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeraeteSeiteDto>> Liste(CancellationToken ct)
    {
        var stand = await _geraete.AlleAsync(ct);
        var geraete = stand.Geraete;

        var zeilen = geraete.Select(g => new GeraetDto(
            g.Schluessel,
            g.Name,
            g.ElternSchluessel,
            g.Anschluss,
            g.IstController,
            g.Modell,
            g.IstRubrik,
            g.IstUnzugeordnet,
            g.ElternVomNutzer,
            g.NameVomNutzer,
            g.AbgeleiteterEltern,
            g.TentId,
            g.HardwareItemId,
            g.Entitaeten.Select(e => new GeraetEntitaetDto(
                e.EntityId,
                e.Verwendungen.Select(v => new GeraetVerwendungDto(v.Zweck, v.Quelle)).ToList(),
                e.Verschoben,
                e.HerkunftName)).ToList()))
            .ToList();

        // Fork AI (forkai.44): Regeln tut Home Assistant. Weicht eine Rolle von dem
        // ab, was die Automation fest verdrahtet hat, meinen Anzeige und Regelung
        // Verschiedenes — das gehört auf die Seite, nicht in ein Protokoll.
        var verdrahtet = await _absicherung.HandgebauteVerdrahtungAsync(_haSettings.GetEffectiveHomeAssistantSettings(), ct);
        var hinweise = Co2SteuerungService.Abweichungen(_rollen.EntitiesFuerModul(Co2SteuerungService.Modul), verdrahtet)
            .Concat(stand.RegisterErreichbar
                ? GeraeteUebersichtService.Zuordnungshinweise(geraete)
                : ["Das Geräteregister von Home Assistant ist nicht erreichbar. Ohne es lässt sich kein Gerät belegen — deshalb steht alles unter „Nicht zugeordnet“."])
            .ToList();

        return Ok(new GeraeteSeiteDto(
            zeilen,
            // Eine Rubrik ist ein Fach, kein Geraet — sie faelschte die Zahl.
            zeilen.Count(z => !z.IstRubrik),
            zeilen.Sum(z => z.Entitaeten.Count),
            zeilen.Where(z => z.IstUnzugeordnet).Sum(z => z.Entitaeten.Count),
            // Korrekturen sind BEIDES: verschobene Entitaeten und Geraete, die der
            // Nutzer umgehaengt oder umbenannt hat. Zaehlte nur das erste, stuende
            // nach dem Verschieben eines Geraets weiter eine Null da.
            zeilen.Sum(z => z.Entitaeten.Count(e => e.Verschoben))
                + zeilen.Count(z => !z.IstRubrik && (z.ElternVomNutzer || z.NameVomNutzer)),
            hinweise));
    }

    /// <summary>
    /// Eine Entität einem Gerät zuschlagen — oder mit leerem Schlüssel die Zuordnung
    /// lösen, dann gilt wieder, was Home Assistant sagt.
    /// </summary>
    [HttpPut("entitaet")]
    [ProducesResponseType(typeof(GeraeteSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeraeteSeiteDto>> EntitaetZuordnen([FromBody] EntitaetZuordnenRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.EntityId))
        {
            return BadRequestError("entity_missing", "Ohne Entität geht es nicht.");
        }

        if (string.Equals(request.Schluessel?.Trim(), GeraeteSchluessel.Unzugeordnet, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequestError("unzugeordnet_fest", "„Nicht zugeordnet“ ist kein Ziel. Zum Lösen den Schlüssel leer lassen.");
        }

        _repo.EntitaetZuordnen(request.EntityId.Trim(), request.Schluessel?.Trim());
        return await Liste(ct);
    }

    /// <summary>
    /// Ein Gerät ändern: Name, an welchem Gerät es hängt, an welcher Steckstelle.
    /// <c>ElternSchluessel</c> leer heißt ausdrücklich „hängt an nichts" — so wird ein
    /// von Home Assistant geerbter Controller ausgehängt.
    /// </summary>
    [HttpPut("{schluessel}")]
    [ProducesResponseType(typeof(GeraeteSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeraeteSeiteDto>> Speichern(string schluessel, [FromBody] GeraetSpeichernRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequestError("leer", "Es wurde nichts übergeben.");

        if (string.Equals(schluessel, GeraeteSchluessel.Unzugeordnet, StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.ElternSchluessel?.Trim(), GeraeteSchluessel.Unzugeordnet, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequestError("unzugeordnet_fest", "„Nicht zugeordnet“ sammelt, was zu keinem Gerät gehört — es lässt sich nicht ändern und nichts hängt daran.");
        }

        if (!string.IsNullOrWhiteSpace(request.ElternSchluessel)
            && string.Equals(request.ElternSchluessel.Trim(), schluessel, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequestError("eltern_selbst", "Ein Gerät kann nicht an sich selbst hängen.");
        }

        _repo.GeraetSpeichern(new GespeichertesGeraet
        {
            Schluessel = schluessel,
            // Leerer Name heißt „nicht ändern" — wie beim Eltern-Schlüssel. Sonst
            // schriebe schon ein Aushängen den aktuellen Namen als Korrektur fest,
            // und ein späteres Umbenennen in Home Assistant käme nie mehr an.
            Name = request.Name?.Trim() ?? string.Empty,
            TentId = request.TentId,
            HardwareItemId = request.HardwareItemId,
            ElternSchluessel = request.ElternSchluessel?.Trim(),
            Anschluss = string.IsNullOrWhiteSpace(request.Anschluss) ? null : request.Anschluss.Trim(),
            // Ein Umbenennen darf aus einer Rubrik kein Gerät machen.
            IstRubrik = schluessel.StartsWith(GeraeteSchluessel.RubrikPraefix, StringComparison.OrdinalIgnoreCase),
        });

        return await Liste(ct);
    }

    /// <summary>
    /// Eine Rubrik anlegen — ein Fach ohne Entitäten, unter das Geräte gehängt
    /// werden können. Technisch ein Eltern-Gerät, nur ohne Entsprechung in Home
    /// Assistant.
    /// </summary>
    [HttpPost("rubrik")]
    [ProducesResponseType(typeof(GeraeteSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeraeteSeiteDto>> RubrikAnlegen([FromBody] RubrikRequest request, CancellationToken ct)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequestError("name_missing", "Eine Rubrik braucht einen Namen.");

        var schluessel = GeraeteSchluessel.RubrikSchluessel(name);
        _repo.GeraetSpeichern(new GespeichertesGeraet
        {
            Schluessel = schluessel,
            Name = name,
            IstRubrik = true,
        });

        return await Liste(ct);
    }

    /// <summary>Die Korrektur verwerfen — es gilt wieder, was abgeleitet wird.</summary>
    [HttpDelete("{schluessel}/korrektur")]
    [KiSicherungVorher]
    [ProducesResponseType(typeof(GeraeteSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeraeteSeiteDto>> Verwerfen(string schluessel, CancellationToken ct)
    {
        _repo.GeraetVerwerfen(schluessel);
        return await Liste(ct);
    }
}

/// <param name="Schluessel">Zielgerät; leer löst die Zuordnung.</param>
public sealed record EntitaetZuordnenRequest(string? EntityId, string? Schluessel);

public sealed record RubrikRequest(string? Name);

/// <param name="Name">Leer: der Name bleibt, wie er abgeleitet wird (Home Assistant, Inventar).</param>
/// <param name="ElternSchluessel">Leerer String: hängt ausdrücklich an nichts. Null: nicht ändern.</param>
public sealed record GeraetSpeichernRequest(
    string? Name,
    string? ElternSchluessel,
    string? Anschluss,
    int? TentId,
    int? HardwareItemId);

public sealed record GeraetVerwendungDto(string Zweck, string Quelle);

/// <param name="Verschoben">Von Hand diesem Gerät zugeschlagen.</param>
/// <param name="HerkunftName">Wohin Home Assistant sie zählt.</param>
public sealed record GeraetEntitaetDto(
    string EntityId,
    IReadOnlyList<GeraetVerwendungDto> Verwendungen,
    bool Verschoben,
    string? HerkunftName);

/// <param name="ElternSchluessel">Der Controller, in dessen Port das Gerät steckt.</param>
/// <param name="Anschluss">Die Steckstelle am Eltern-Gerät, etwa „Port 5".</param>
/// <param name="IstUnzugeordnet">Das Sammelfach für Entitäten, die zu keinem Gerät gehören.</param>
public sealed record GeraetDto(
    string Schluessel,
    string Name,
    string? ElternSchluessel,
    string? Anschluss,
    bool IstController,
    string? Modell,
    bool IstRubrik,
    bool IstUnzugeordnet,
    bool ElternVomNutzer,
    bool NameVomNutzer,
    string? AbgeleiteterEltern,
    int? TentId,
    int? HardwareItemId,
    IReadOnlyList<GeraetEntitaetDto> Entitaeten);

public sealed record GeraeteSeiteDto(
    IReadOnlyList<GeraetDto> Geraete,
    int AnzahlGeraete,
    int AnzahlEntitaeten,
    int AnzahlUnzugeordnet,
    int AnzahlVerschoben,
    /// <summary>Rollen, die nicht zu dem passen, was die HA-Automation wirklich benutzt.</summary>
    IReadOnlyList<string> Hinweise);
