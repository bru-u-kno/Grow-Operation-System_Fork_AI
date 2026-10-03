using System.Reflection;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Ein Controller nur für die Tests des Schlüsselwegs.
/// </summary>
/// <remarks>
/// Die echten Controller stufen zwei andere Arbeitsstränge ein; hier liegt je
/// Fall genau eine Aktion, damit die Sperre unabhängig von deren Stand geprüft
/// wird. Er wird nur in <see cref="KiZugriffApp"/> eingehängt — die gemeinsame
/// Integrations-App und alle Zählungen über die Routen sehen ihn nicht.
/// </remarks>
[ApiController]
[Route("api/ki-test")]
public sealed class KiTestController : ControllerBase
{
    [HttpGet("lesen")]
    public IActionResult Lesen() => Ok(new { gelesen = true });

    [HttpPost("dokumentieren")]
    [KiStufe(KiStufe.Dokumentieren)]
    public IActionResult Dokumentieren() => Ok(new { ausgefuehrt = "dokumentieren" });

    [HttpPost("schalten")]
    [KiStufe(KiStufe.GeraeteSchalten)]
    public IActionResult Schalten() => Ok(new { ausgefuehrt = "schalten" });

    [HttpPost("stopp")]
    [KiStufe(KiStufe.GeraeteSchalten)]
    [KiOhneHoechstwert("Nur für den Test: wie der Pumpen-Stopp.")]
    public IActionResult Stopp() => Ok(new { ausgefuehrt = "stopp" });

    [HttpPost("licht")]
    [KiStufe(KiStufe.Verwaltung | KiStufe.GeraeteSchalten)]
    public IActionResult Licht() => Ok(new { ausgefuehrt = "licht" });

    [HttpPost("wirft")]
    [KiStufe(KiStufe.Dokumentieren)]
    public IActionResult Wirft() => throw new InvalidOperationException("Absichtlich, für den Test.");

    [HttpPost("nie")]
    [KeinKiZugriff("Nur für den Test.")]
    public IActionResult Nie() => Ok(new { ausgefuehrt = "nie" });

    [HttpPost("uneingestuft")]
    public IActionResult Uneingestuft() => Ok(new { ausgefuehrt = "uneingestuft" });

    [HttpPost("sicherung")]
    [KiStufe(KiStufe.Verwaltung)]
    [KiSicherungVorher]
    public IActionResult MitSicherung()
    {
        Aufrufe.MitSicherung++;
        return Ok(new { ausgefuehrt = "sicherung" });
    }

    /// <summary>Zählt, ob die Aktion hinter der Sicherung wirklich lief.</summary>
    public static class Aufrufe
    {
        public static int MitSicherung;
    }
}

/// <summary>Einstufung am Controller; eine Aktion überschreibt sie.</summary>
[ApiController]
[Route("api/ki-test-planen")]
[KiStufe(KiStufe.GrowPlanen)]
public sealed class KiTestPlanenController : ControllerBase
{
    [HttpPost("vom-controller")]
    public IActionResult VomController() => Ok(new { ausgefuehrt = "vom-controller" });

    [HttpPost("von-der-aktion")]
    [KiStufe(KiStufe.Dokumentieren)]
    public IActionResult VonDerAktion() => Ok(new { ausgefuehrt = "von-der-aktion" });
}

/// <summary>Genau diese zwei Controller als Teil der Anwendung — nicht die ganze Testassembly.</summary>
internal sealed class KiTestControllerTeil : ApplicationPart, IApplicationPartTypeProvider
{
    public override string Name => "KiTestController";

    public IEnumerable<TypeInfo> Types =>
    [
        typeof(KiTestController).GetTypeInfo(),
        typeof(KiTestPlanenController).GetTypeInfo(),
    ];
}
