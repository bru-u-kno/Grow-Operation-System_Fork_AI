using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Die Phase, wie sie der Mensch liest — an einer Stelle.
/// </summary>
/// <remarks>
/// <para>Stand bis forkai.66 als öffentliche Methode im
/// <c>GeltendeZieleApiController</c>, weil die CO₂-Steuerung dieselbe
/// Schreibweise brauchte. Mit dem Wegfall jenes Controllers hätte die
/// Übersetzung mitgehen müssen — und die Zielwerte-Seite hatte sich inzwischen
/// ohnehin eine zweite gebaut („Vegi“ statt „Vegetativ“).</para>
///
/// <para>Zwei Schreibweisen für dieselbe Phase sind genau die Sorte
/// Doppelauskunft, gegen die <see cref="Zielband"/> gebaut wurde: dieselbe
/// Anlage heisst auf zwei Bildschirmen anders, und niemand weiss, ob damit
/// dasselbe gemeint ist.</para>
/// </remarks>
public static class Phasenname
{
    public static string Fuer(GrowStage phase) => phase switch
    {
        GrowStage.Seedling => "Sämling",
        GrowStage.Clone => "Steckling",
        GrowStage.Veg => "Vegetativ",
        GrowStage.Transition => "Transition",
        GrowStage.Flower => "Blüte",
        GrowStage.Finish => "Finish",
        _ => phase.ToString(),
    };
}
