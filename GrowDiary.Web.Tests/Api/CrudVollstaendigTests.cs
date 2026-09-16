using System.Reflection;
using GrowDiary.Web.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Wer etwas anlegen kann, muss es auch wieder entfernen können.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (25.08.2026).</b> Der Nutzer: „CRUD ist grundlegend und
/// das befolgst du nicht." Er hatte recht, und zwar breiter als der eine Fall,
/// der ihn darauf brachte: Pflanzen liessen sich anlegen und ändern, aber
/// nirgends entfernen. Gezählt waren es <b>13 von 24</b> Routen mit POST ohne
/// jedes DELETE.</para>
///
/// <para><b>Die Grundmenge ist nicht „POST".</b> Ein POST kann auch eine
/// Handlung sein — <c>flip-to-flower</c>, <c>watchdog/test</c>,
/// <c>level-calibration/start</c>. Gezählt wird deshalb, was der Controller
/// selbst als <b>201 Created</b> ausschreibt: genau das ist die Zusage, eine
/// neue Sache angelegt zu haben. Steht sie da, muss es einen Weg zurück geben
/// — oder einen ausgeschriebenen Grund.</para>
///
/// <para><b>Warum eine Zählung und keine Liste.</b> Eine handgeschriebene
/// Liste kann nur an dem scheitern, was schon draufsteht. Diese hier geht über
/// die Reflexion der geladenen Assembly und sieht deshalb auch den Controller,
/// den es erst morgen gibt.</para>
/// </remarks>
public sealed class CrudVollstaendigTests
{
    /// <summary>
    /// Was anlegen darf, ohne entfernen zu können — mit ausgeschriebenem Grund.
    /// </summary>
    /// <remarks>
    /// Der Schlüssel ist der Controller-Name. Ein Eintrag ohne Grund ist keine
    /// Ausnahme, sondern eine Lücke mit Deckel.
    /// </remarks>
    private static readonly Dictionary<string, string> Ausnahmen = new(StringComparer.Ordinal)
    {
        ["RiskEventsApiController"] =
            "Ein Risiko hat einen Lebenslauf statt eines Loeschwegs: offen -> "
            + "bestaetigt (acknowledge) -> erledigt (resolve). Beide Wege gibt es, "
            + "und beide behalten den Befund. Ein Loeschen waere das Verschwinden "
            + "eines Befundes, den die App selbst erhoben hat — genau das, was ein "
            + "Waechter nicht koennen darf.",

        ["SystemApiController"] =
            "201 steht an der Sicherung (Backup). Eine Sicherung wird nicht ueber die "
            + "API geloescht — sie liegt als Datei im Add-on-Ordner, und ein "
            + "Loeschweg ueber HTTP waere ein Weg, sich seine eigene Rettung zu nehmen. "
            + "Seit 16.09.2026 (F-012) raeumt stattdessen die App selbst auf: "
            + "BackupAufbewahrung behaelt je Art die fuenf neuesten.",
    };

    private static IReadOnlyList<Type> Controller()
        => typeof(PlantsApiController).Assembly
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<MethodInfo> Aktionen(Type controller)
        => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    /// <summary>Legt dieser Controller etwas an — sagt er selbst 201 Created?</summary>
    private static bool LegtAn(Type controller)
        => Aktionen(controller).Any(m =>
            m.GetCustomAttributes<HttpPostAttribute>().Any()
            && m.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Any(a => a.StatusCode == StatusCodes.Status201Created));

    private static bool KannEntfernen(Type controller)
        => Aktionen(controller).Any(m => m.GetCustomAttributes<HttpDeleteAttribute>().Any());

    /// <summary>
    /// Der Mengenwächter: sieht die Zählung ihre Grundmenge überhaupt?
    /// </summary>
    /// <remarks>
    /// Ohne ihn liefe die Zählung bei einer leeren Menge null Mal durch und
    /// wäre grün — die Falle, die in CLAUDE.md ausgeschrieben steht.
    /// </remarks>
    [Fact]
    public void DieZaehlungSiehtIhreGrundmenge()
    {
        var controller = Controller();
        Assert.True(controller.Count >= 40,
            $"Nur {controller.Count} Controller gefunden — die Zaehlung laeuft ins Leere.");

        var anlegend = controller.Where(LegtAn).ToList();
        Assert.True(anlegend.Count >= 15,
            $"Nur {anlegend.Count} Controller legen etwas an — das kann nicht stimmen. "
            + "Vermutlich wird 201 Created nicht mehr ausgeschrieben, und die Zaehlung "
            + "prueft nichts mehr.");

        // Und die Gegenprobe: mindestens einer kann auch entfernen. Sonst
        // misst die Erkennung von DELETE nichts.
        Assert.Contains(anlegend, KannEntfernen);
    }

    [Fact]
    public void WerAnlegenKannMussAuchEntfernenKoennen()
    {
        var luecken = Controller()
            .Where(LegtAn)
            .Where(t => !KannEntfernen(t))
            .Where(t => !Ausnahmen.ContainsKey(t.Name))
            .Select(t => t.Name)
            .ToList();

        Assert.True(luecken.Count == 0,
            $"{luecken.Count} Controller legen etwas an, das niemand wieder entfernen kann:\n  "
            + string.Join("\n  ", luecken)
            + "\n\nEntweder ein HttpDelete dazu, oder ein Eintrag in Ausnahmen MIT Grund.");
    }

    /// <summary>
    /// Eine Ausnahme, die es nicht mehr braucht, ist eine Luege im Code.
    /// </summary>
    [Fact]
    public void KeineAusnahmeZeigtInsLeere()
    {
        var namen = Controller().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var (name, grund) in Ausnahmen)
        {
            Assert.True(namen.Contains(name),
                $"Ausnahme fuer '{name}' — diesen Controller gibt es nicht (mehr).");

            var typ = Controller().Single(t => t.Name == name);
            Assert.True(LegtAn(typ),
                $"Ausnahme fuer '{name}', aber der Controller legt gar nichts an. Weg damit.");
            Assert.False(KannEntfernen(typ),
                $"Ausnahme fuer '{name}', aber er kann inzwischen entfernen. Weg damit.");

            Assert.True(grund.Length >= 80,
                $"Der Grund fuer '{name}' ist zu kurz, um einer zu sein: \"{grund}\"");
        }
    }
}
