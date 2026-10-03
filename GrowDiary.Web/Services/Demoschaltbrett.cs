using System.Collections.Concurrent;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Das Schaltbrett des Testbetriebs: was hier geschaltet wird, bleibt geschaltet.
/// </summary>
/// <remarks>
/// <para><b>Warum es das gibt.</b> Bis zum 24.08.2026 hat der Testbetrieb jeden
/// Schaltbefehl mit „Erfolg" beantwortet und nichts verändert — im Quelltext
/// stand sogar der Grund: <i>„damit der ganze Weg durchläuft"</i>. Der Weg lief
/// damit aber nur bis zur Antwort. Alles, was <b>danach</b> kommt, war im
/// Testbestand nicht prüfbar: die Nachkontrolle in
/// <see cref="AcSchreiber"/>, der Wächter über der Kühler-Steckdose, jede
/// Anzeige, die den Zustand eines geschalteten Geräts zeigt.</para>
///
/// <para>Und es war zusätzlich unehrlich: die Kühler-Karte zeigte „steht"
/// neben dem Satz „Kühler an", weil der Zustand aus einer Kurve kam und die
/// Entscheidung aus dem Regler. Zwei Wahrheiten für dieselbe Steckdose.</para>
///
/// <para><b>Was es NICHT tut.</b> Es rechnet keine Physik nach. Ein
/// eingeschalteter Kühler macht das Wasser hier nicht kälter — die Messkurven
/// bleiben, wie sie sind. Es hält nur fest, was jemand gestellt hat, und
/// antwortet beim nächsten Lesen damit. Genau das tut eine echte Steckdose
/// auch.</para>
///
/// <para><b>Nur im Testbetrieb.</b> Ohne <c>GROW_OS_DEMO=1</c> wird diese
/// Klasse nie angefasst.</para>
/// </remarks>
public static class Demoschaltbrett
{
    private static readonly ConcurrentDictionary<string, Eintrag> Stand =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record Eintrag(string Zustand, double? Zahl, DateTime WannUtc);

    /// <summary>Wie viele Entitäten das Brett gerade hält.</summary>
    /// <remarks>Für den Mengenwächter im Test: ein leeres Brett prüft nichts.</remarks>
    public static int Anzahl => Stand.Count;

    /// <summary>Alles vergessen — nur für Tests.</summary>
    public static void Leeren() => Stand.Clear();

    /// <summary>Einen Schaltbefehl festhalten.</summary>
    /// <returns>
    /// <c>true</c>, wenn der Befehl verstanden wurde. <c>false</c> heisst: diese
    /// Kombination aus Domäne und Dienst kennt das Brett nicht — dann meldet der
    /// Testbetrieb ehrlich einen Fehlschlag, statt Erfolg zu behaupten und
    /// nichts zu tun.
    /// </returns>
    public static bool Schalten(
        string domain, string dienst, string entityId, IReadOnlyDictionary<string, object>? daten)
    {
        var wert = Deuten(domain, dienst, entityId, daten);
        if (wert is null) return false;

        var zahl = Zahlenlesen.Maschine(wert);

        Stand[entityId] = new Eintrag(wert, zahl, DateTime.UtcNow);
        return true;
    }

    /// <summary>Was zuletzt gestellt wurde — oder <c>null</c>, wenn nie etwas.</summary>
    public static HomeAssistantState? Lesen(string entityId)
    {
        if (!Stand.TryGetValue(entityId, out var eintrag)) return null;

        return new HomeAssistantState
        {
            EntityId = entityId,
            State = eintrag.Zustand,
            NumericValue = eintrag.Zahl,
            FriendlyName = $"Testdaten · {entityId}",
            LastChanged = eintrag.WannUtc,
            LastUpdated = eintrag.WannUtc,
        };
    }

    /// <summary>Welcher Zustand folgt aus diesem Aufruf?</summary>
    /// <remarks>
    /// Die Zuordnung von Dienst zu Zustand ist die von Home Assistant. Sie steht
    /// hier ausgeschrieben und nicht geraten: ein <c>time.set_value</c> trägt
    /// seinen Wert unter <c>time</c>, ein <c>number.set_value</c> unter
    /// <c>value</c>, ein <c>select.select_option</c> unter <c>option</c>.
    /// </remarks>
    private static string? Deuten(
        string domain, string dienst, string entityId, IReadOnlyDictionary<string, object>? daten)
    {
        string? AusDaten(string schluessel)
            => daten is not null && daten.TryGetValue(schluessel, out var w)
                ? Convert.ToString(w, System.Globalization.CultureInfo.InvariantCulture)
                : null;

        return (domain.ToLowerInvariant(), dienst.ToLowerInvariant()) switch
        {
            (_, "turn_on") => "on",
            (_, "turn_off") => "off",
            (_, "toggle") => Lesen(entityId)?.State == "on" ? "off" : "on",
            ("number" or "input_number", "set_value") => AusDaten("value"),
            ("time" or "input_datetime", "set_value") => AusDaten("time"),
            ("select" or "input_select", "select_option") => AusDaten("option"),
            ("climate", "set_temperature") => AusDaten("temperature"),
            _ => null,
        };
    }
}
