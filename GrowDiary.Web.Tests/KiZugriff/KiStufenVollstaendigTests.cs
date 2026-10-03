using System.Reflection;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Jede schreibende Aktion ist für den KI-Zugriff eingestuft (A-003).
/// </summary>
/// <remarks>
/// <para><b>Warum eine Zählung.</b> Über einen Schlüssel ist eine schreibende
/// Aktion ohne Einstufung gesperrt — vergessen sperrt, statt zu öffnen. Das ist
/// sicher, aber still: der Assistent bekommt <c>ki_nicht_eingestuft</c>, und
/// niemand merkt, dass der neue Weg nie freigegeben werden kann. Eine
/// handgeschriebene Liste der Wege könnte nur an dem scheitern, was schon
/// draufsteht; diese Zählung geht über die Reflexion der geladenen Assembly
/// und sieht auch den Controller, den es erst morgen gibt.</para>
///
/// <para><b>Die Grundmenge.</b> Alle Controller der Assembly
/// <c>GrowDiary.Web</c> (API und MVC), alle öffentlichen Aktionen mit
/// mindestens einem HTTP-Verb ausser GET/HEAD/OPTIONS — auch über
/// <c>[AcceptVerbs]</c> und <c>[HttpPatch]</c>. Aktionen ganz ohne
/// Verb-Attribut zählen nicht: über einen Schlüssel kommt an sie nur ein GET
/// heran, jedes andere Verb bleibt ohne Einstufung gesperrt.</para>
///
/// <para><b>Wirksam heisst:</b> das Attribut an der Aktion gewinnt, sonst das
/// an der Klasse — beides auch geerbt. <c>KeinKiZugriff</c> zählt nur mit
/// ausgeschriebenem Grund.</para>
/// </remarks>
public sealed class KiStufenVollstaendigTests
{
    /// <summary>
    /// So viele schreibende Aktionen gab es beim Bau der Prüfung (03.10.2026:
    /// 163) — knapp darunter angesetzt. Fällt die Grundmenge darunter, sieht die
    /// Erkennung der Verben nichts mehr, und die Prüfung wäre grundlos grün.
    /// </summary>
    private const int MindestensSchreibende = 155;

    private static readonly HashSet<string> LesendeVerben = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS",
    };

    /// <summary>Eine schreibende Aktion, wie sie in der Fehlermeldung steht.</summary>
    private sealed record Aktion(Type Controller, MethodInfo Methode, IReadOnlyList<string> Wege)
    {
        public override string ToString()
            => $"{Controller.Name}.{Methode.Name} ({string.Join("; ", Wege)})";
    }

    private static IReadOnlyList<Type> ControllerDerApp()
        => typeof(PlantsApiController).Assembly
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Alle schreibenden Aktionen der übergebenen Controller.</summary>
    private static IReadOnlyList<Aktion> SchreibendeAktionen(IEnumerable<Type> controller)
    {
        var ergebnis = new List<Aktion>();
        foreach (var typ in controller)
        {
            var routeKlasse = typ.GetCustomAttributes<RouteAttribute>(inherit: true)
                .Select(r => r.Template)
                .FirstOrDefault() ?? string.Empty;

            foreach (var methode in typ.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (methode.IsSpecialName) continue;
                if (methode.DeclaringType is not { } deklariert
                    || !deklariert.IsSubclassOf(typeof(ControllerBase))
                    || deklariert.Assembly == typeof(ControllerBase).Assembly)
                {
                    continue;
                }

                if (methode.GetCustomAttribute<NonActionAttribute>(inherit: true) is not null) continue;

                var wege = new List<string>();
                foreach (var verbAttribut in methode.GetCustomAttributes(inherit: true).OfType<IActionHttpMethodProvider>())
                {
                    var schreibend = verbAttribut.HttpMethods.Where(v => !LesendeVerben.Contains(v)).ToList();
                    if (schreibend.Count == 0) continue;

                    var vorlage = (verbAttribut as IRouteTemplateProvider)?.Template;
                    wege.Add($"{string.Join(",", schreibend)} {Route(routeKlasse, vorlage)}");
                }

                if (wege.Count > 0)
                {
                    ergebnis.Add(new Aktion(typ, methode, wege));
                }
            }
        }

        return ergebnis;
    }

    private static string Route(string klasse, string? aktion)
    {
        if (string.IsNullOrEmpty(aktion)) return klasse;
        if (aktion.StartsWith("~/", StringComparison.Ordinal)) return aktion[2..];
        if (aktion.StartsWith('/')) return aktion[1..];
        return string.IsNullOrEmpty(klasse) ? aktion : $"{klasse}/{aktion}";
    }

    /// <summary>Das wirksame Attribut: an der Aktion vor dem an der Klasse.</summary>
    private static (KiStufeAttribute? Stufe, KeinKiZugriffAttribute? Kein) Wirksam(Aktion aktion)
    {
        var stufe = aktion.Methode.GetCustomAttribute<KiStufeAttribute>(inherit: true);
        var kein = aktion.Methode.GetCustomAttribute<KeinKiZugriffAttribute>(inherit: true);
        if (stufe is not null || kein is not null) return (stufe, kein);

        return (aktion.Controller.GetCustomAttribute<KiStufeAttribute>(inherit: true),
                aktion.Controller.GetCustomAttribute<KeinKiZugriffAttribute>(inherit: true));
    }

    private static bool KeinZugriffMitGrund(KeinKiZugriffAttribute? kein)
        => kein is not null && !string.IsNullOrWhiteSpace(kein.Grund);

    /// <summary>Schreibende Aktionen, denen eine wirksame Einstufung fehlt.</summary>
    private static IReadOnlyList<Aktion> OhneEinstufung(IEnumerable<Type> controller)
        => SchreibendeAktionen(controller)
            .Where(a =>
            {
                var (stufe, kein) = Wirksam(a);
                if (kein is not null) return !KeinZugriffMitGrund(kein);
                return stufe is null;
            })
            .ToList();

    /// <summary>DELETE-Aktionen ohne Sicherung vorher und ohne Ausschluss.</summary>
    private static IReadOnlyList<Aktion> LoeschenOhneSicherung(IEnumerable<Type> controller)
        => SchreibendeAktionen(controller)
            .Where(a => a.Methode.GetCustomAttributes<HttpDeleteAttribute>(inherit: true).Any())
            .Where(a => a.Methode.GetCustomAttribute<KiSicherungVorherAttribute>(inherit: true) is null
                        && a.Controller.GetCustomAttribute<KiSicherungVorherAttribute>(inherit: true) is null)
            .Where(a => !KeinZugriffMitGrund(Wirksam(a).Kein))
            .ToList();

    private static string Meldung(string kopf, IReadOnlyList<Aktion> fehlend, string fuss)
        => $"{fehlend.Count} {kopf}:\n  "
           + string.Join("\n  ", fehlend.Select(a => a.ToString()).OrderBy(s => s, StringComparer.Ordinal))
           + "\n\n" + fuss;

    /// <summary>
    /// Der Mengenwächter: sieht die Zählung ihre Grundmenge überhaupt?
    /// </summary>
    [Fact]
    public void Die_Zaehlung_sieht_ihre_Grundmenge()
    {
        var controller = ControllerDerApp();
        Assert.True(controller.Count >= 40,
            $"Nur {controller.Count} Controller gefunden — die Zählung läuft ins Leere.");

        var schreibend = SchreibendeAktionen(controller);
        Assert.True(schreibend.Count >= MindestensSchreibende,
            $"Nur {schreibend.Count} schreibende Aktionen gefunden (erwartet mindestens "
            + $"{MindestensSchreibende}). Vermutlich erkennt die Prüfung die Verben nicht mehr.");

        // Jede Verb-Art muss vorkommen — sonst misst die Erkennung eine davon nicht.
        foreach (var verb in new[] { "POST", "PUT", "PATCH", "DELETE" })
        {
            Assert.Contains(schreibend, a => a.Wege.Any(w => w.StartsWith(verb, StringComparison.Ordinal)
                                                             || w.Contains("," + verb, StringComparison.Ordinal)));
        }

        // MVC-Controller unter Controllers/ gehören zur Grundmenge.
        Assert.Contains(schreibend, a => a.Controller.Namespace == "GrowDiary.Web.Controllers");
    }

    [Fact]
    public void Jede_schreibende_Aktion_ist_eingestuft()
    {
        var fehlend = OhneEinstufung(ControllerDerApp());

        Assert.True(fehlend.Count == 0, Meldung(
            "schreibende Aktionen ohne Einstufung für den KI-Zugriff",
            fehlend,
            "Je Aktion [KiStufe(KiStufe.…)] (oder an der Klasse), oder [KeinKiZugriff(\"Grund\")]. "
            + "Ohne Einstufung ist die Aktion über einen Schlüssel gesperrt. Siehe docs/ki-zugriff.md."));
    }

    [Fact]
    public void Jedes_Loeschen_legt_vorher_eine_Sicherung_an()
    {
        var fehlend = LoeschenOhneSicherung(ControllerDerApp());

        Assert.True(fehlend.Count == 0, Meldung(
            "DELETE-Aktionen ohne [KiSicherungVorher]",
            fehlend,
            "Ein Löschen über einen Schlüssel lässt sich nicht von Hand zurückdrehen. "
            + "[KiSicherungVorher] an die Aktion, oder [KeinKiZugriff(\"Grund\")]."));
    }

    // ----------------------------------------------------------- Bissnachweis

    /// <summary>
    /// Die Prüfung findet eine absichtlich nicht eingestufte Aktion — und lässt
    /// die eingestuften in Ruhe.
    /// </summary>
    /// <remarks>
    /// Die Prüflinge sind private, verschachtelte Typen: MVC nimmt nur
    /// öffentliche Klassen der obersten Ebene als Controller, und sie liegen
    /// ausserhalb der App-Assembly — die echte Grundmenge sehen sie nie.
    /// </remarks>
    [Fact]
    public void Die_Pruefung_findet_eine_nicht_eingestufte_Aktion()
    {
        var fehlend = OhneEinstufung([typeof(PrueflingOhneStufe), typeof(PrueflingMitStufe), typeof(PrueflingKeinZugriff)])
            .Select(a => $"{a.Controller.Name}.{a.Methode.Name}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "PrueflingKeinZugriff.OhneGrund",
                "PrueflingMitStufe.KlasseUeberschrieben",
                "PrueflingOhneStufe.Anlegen",
                "PrueflingOhneStufe.Patchen",
                "PrueflingOhneStufe.Verben",
            ],
            fehlend);
    }

    [Fact]
    public void Die_Pruefung_findet_ein_Loeschen_ohne_Sicherung()
    {
        var fehlend = LoeschenOhneSicherung([typeof(PrueflingOhneStufe), typeof(PrueflingMitStufe), typeof(PrueflingKeinZugriff)])
            .Select(a => $"{a.Controller.Name}.{a.Methode.Name}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["PrueflingMitStufe.LoeschenOhneSicherung"], fehlend);
    }

    [Route("pruefling")]
    private sealed class PrueflingOhneStufe : ControllerBase
    {
        [HttpGet] public IActionResult Lesen() => Ok();
        [HttpPost] public IActionResult Anlegen() => Ok();
        [HttpPatch("{id:int}")] public IActionResult Patchen(int id) => Ok(id);
        [AcceptVerbs("GET", "POST")] public IActionResult Verben() => Ok();
        [AcceptVerbs("GET", "HEAD")] public IActionResult NurLesend() => Ok();
        [NonAction] [HttpPost] public IActionResult KeineAktion() => Ok();
    }

    [Route("pruefling-mit")]
    [KiStufe(KiStufe.Dokumentieren)]
    private sealed class PrueflingMitStufe : ControllerBase
    {
        [HttpPost] public IActionResult VonDerKlasse() => Ok();
        [HttpPut, KiStufe(KiStufe.GrowPlanen)] public IActionResult EigeneStufe() => Ok();
        [HttpDelete("a"), KiSicherungVorher] public IActionResult LoeschenMitSicherung() => Ok();
        [HttpDelete("b")] public IActionResult LoeschenOhneSicherung() => Ok();
        [HttpPost("leer"), KeinKiZugriff(" ")] public IActionResult KlasseUeberschrieben() => Ok();
    }

    [Route("pruefling-kein")]
    [KeinKiZugriff("Nur für Menschen in der Oberfläche.")]
    private sealed class PrueflingKeinZugriff : ControllerBase
    {
        [HttpPost] public IActionResult MitGrund() => Ok();
        [HttpDelete] public IActionResult LoeschenOhneSicherungAberGesperrt() => Ok();
        [HttpPut, KeinKiZugriff("")] public IActionResult OhneGrund() => Ok();
    }
}
