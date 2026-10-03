using System.Reflection;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure.KiZugriff;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Der Pumpen-Stopp scheitert nie an der Stundengrenze.
/// </summary>
/// <remarks>
/// Die Sperre selbst ist mit einem Test-Controller belegt (Kern). Hier steht nur
/// die Frage, ob die ECHTE Aktion die Markierung trägt — beim Zusammenführen
/// der Stränge war sie die eine Stelle, die keiner der beiden Agenten anfassen
/// durfte.
/// </remarks>
public sealed class KiPumpenStoppTests
{
    [Fact]
    public void Der_Pumpen_Stopp_ist_von_der_Stundengrenze_ausgenommen()
    {
        var stopp = typeof(DosingApiController).GetMethod(nameof(DosingApiController.Stop))!;

        var markierung = stopp.GetCustomAttribute<KiOhneHoechstwertAttribute>();

        Assert.NotNull(markierung);
        Assert.False(string.IsNullOrWhiteSpace(markierung!.Grund));
    }
}
