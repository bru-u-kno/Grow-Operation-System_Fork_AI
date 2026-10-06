using GrowDiary.Web.Infrastructure.KiZugriff;
using System.IO.Compression;
using System.Text;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Der Lagebericht zum Mitnehmen — für einen eigenen KI-Agenten.
/// </summary>
/// <remarks>
/// In der App selbst steckt keine KI, und das bleibt so. Wer trotzdem einen
/// Assistenten fragen will, bekommt hier eine Datei und entscheidet selbst, wem
/// er sie vorlegt. Das ist der ehrlichere Weg: Grow OS braucht keinen Schlüssel,
/// verschickt nichts, und was den Rechner verlässt, verlässt ihn, weil jemand
/// auf „Herunterladen" gedrückt hat.
/// </remarks>
[ApiController]
[Route("api/agent-export")]
[NurMitKi]
public sealed class AgentExportApiController : ApiControllerBase
{
    private readonly AgentContextBuilder _builder;
    private readonly AgentPackageBuilder _package;

    public AgentExportApiController(AgentContextBuilder builder, AgentPackageBuilder package)
    {
        _builder = builder;
        _package = package;
    }

    /// <summary>Der Lagebericht als Text — zum Ansehen in der App.</summary>
    [HttpGet("grows/{growId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Preview(int growId)
    {
        if (_builder.Build(growId, DateTime.UtcNow) is not { } context)
        {
            return NotFoundError("grow_not_found", $"Grow {growId} existiert nicht.");
        }

        return Ok(new { growName = context.GrowName, markdown = AgentContextBuilder.ToMarkdown(context) });
    }

    /// <summary>Dieselbe Datei zum Herunterladen.</summary>
    [HttpGet("grows/{growId:int}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Download(int growId)
    {
        if (_builder.Build(growId, DateTime.UtcNow) is not { } context)
        {
            return NotFoundError("grow_not_found", $"Grow {growId} existiert nicht.");
        }

        var markdown = AgentContextBuilder.ToMarkdown(context);
        var name = $"grow-os-lagebericht-{Dateiname(context.GrowName)}-{DateTime.Now:yyyy-MM-dd}.md";
        return File(Encoding.UTF8.GetBytes(markdown), "text/markdown; charset=utf-8", name);
    }

    /// <summary>
    /// Was in der Mappe steht — zum Ansehen, ohne sie herunterzuladen.
    /// </summary>
    /// <remarks>
    /// Die Anweisung und die Prüffragen sind das, was den Berater begrenzt.
    /// Sie in einem ZIP zu verstecken hiesse, dass niemand sie liest — und
    /// ungelesene Grenzen sind keine.
    /// </remarks>
    [HttpGet("mappe")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Folder()
        => Ok(new
        {
            anweisung = AgentPromptTexts.Systemanweisung,
            pruefungen = AgentPruefung.Alle,
            dateien = new[]
            {
                new { name = "LIESMICH.md", inhalt = "Was zu tun ist, in fünf Schritten." },
                new { name = "00-anweisung.md", inhalt = "Die Rolle und die Grenzen. Als Systemanweisung einsetzen." },
                new { name = "10-lagebericht.md", inhalt = "Deine Anlage im Moment des Exports." },
                new { name = "20-wissen-ablaeufe.md", inhalt = "Die Abläufe mit allen Schritten und Verzweigungen." },
                new { name = "21-wissen-behandlungen.md", inhalt = "Behandlungen mit Dosierung, Verboten und Konflikten." },
                new { name = "22-wissen-symptome.md", inhalt = "Symptome mit Ursachen, dazu die Erreger." },
                new { name = "23-wissen-regeln.md", inhalt = "Kurze Regeln aus dem Quellmaterial." },
                new { name = "24-wissen-sollwerte.md", inhalt = "Sollwerte je Phase und Nährstoffprogramme." },
                new { name = "90-pruefragen.md", inhalt = "Der Selbsttest mit Musterlösungen." },
            },
        });

    /// <summary>
    /// Die ganze Berater-Mappe als ZIP: Anweisung, Lage, Wissen, Selbsttest.
    /// </summary>
    /// <remarks>
    /// Der Lagebericht allein macht aus einem Assistenten noch keinen Berater —
    /// er kennt dann die Messwerte, aber nicht das Material, an dem sie zu
    /// messen sind. Erst mit den Abläufen, Behandlungen und Regeln nennt er
    /// Kürzel statt Meinungen.
    /// </remarks>
    [HttpGet("grows/{growId:int}/paket")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Package(int growId)
    {
        var nowUtc = DateTime.UtcNow;
        if (_package.Build(growId, nowUtc) is not { } paket)
        {
            return NotFoundError("grow_not_found", $"Grow {growId} existiert nicht.");
        }

        using var speicher = new MemoryStream();
        // In einem eigenen Block, damit das Archiv geschlossen ist, bevor die
        // Bytes gelesen werden — sonst fehlt das Verzeichnis am Ende.
        using (var archiv = new ZipArchive(speicher, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var datei in paket.Files)
            {
                var eintrag = archiv.CreateEntry(datei.Name, CompressionLevel.Optimal);
                using var strom = eintrag.Open();
                using var schreiber = new StreamWriter(strom, new UTF8Encoding(false));
                schreiber.Write(datei.Markdown);
            }
        }

        var name = $"grow-os-berater-{Dateiname(paket.GrowName)}-{DateTime.Now:yyyy-MM-dd}.zip";
        return File(speicher.ToArray(), "application/zip", name);
    }

    /// <summary>
    /// Ein Grow-Name darf alles Mögliche enthalten — ein Dateiname nicht.
    /// </summary>
    private static string Dateiname(string name)
    {
        var sauber = new string(name
            .Select(zeichen => char.IsLetterOrDigit(zeichen) ? char.ToLowerInvariant(zeichen) : '-')
            .ToArray())
            .Trim('-');

        while (sauber.Contains("--")) sauber = sauber.Replace("--", "-");
        return string.IsNullOrEmpty(sauber) ? "grow" : sauber;
    }
}
