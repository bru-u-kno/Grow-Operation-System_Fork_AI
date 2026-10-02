using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Eine Woche der Rampe — was Tag und Nacht gelten sollen.</summary>
public sealed record AbsenkWoche(int BluetewocheAb1, double TagC, double NachtC, bool Erreicht);

/// <summary>Der ganze Plan plus der Wert, der heute gilt.</summary>
/// <param name="HeuteTagC">Sollwert für die Lichtphase, null wenn nichts gilt.</param>
/// <param name="HeuteNachtC">Sollwert für die Dunkelphase.</param>
/// <param name="AktuelleWoche">Blütewoche ab 1; null vor dem Flip.</param>
/// <param name="LueckeSchluessel">
/// Der maschinenlesbare Grund der Lücke (<see cref="KettenSchluessel"/>) —
/// daran hängt die Oberfläche den Knopf, der zur richtigen Stelle führt.
/// Der Text allein täte das nicht: eine Zuordnung am Wortlaut wäre beim
/// nächsten Umformulieren still tot.
/// </param>
public sealed record Absenkplan(
    IReadOnlyList<AbsenkWoche> Wochen,
    double? HeuteTagC,
    double? HeuteNachtC,
    int? AktuelleWoche,
    string Herkunft,
    string? Luecke,
    string? LueckeSchluessel = null);

/// <summary>
/// Die Nachtabsenkung: Crop Steering, wie es im RDWC tatsächlich geht.
/// </summary>
/// <remarks>
/// <para><b>Woher das kommt:</b> Von SKX, dem Autor der RDWC-Abläufe in dieser
/// Wissensbasis. Im Substrat steuert man über Trockenphasen — im Wasser gibt es
/// die nicht. Sein Hebel ist die Wassertemperatur: sie verengt das Tor, durch
/// das die Nährstoffe müssen, und der Stress geht in Harz statt in Blattmasse.
/// Die Methode heisst „Cold Morning Routine" und kommt aus dem Gemüsebau.</para>
///
/// <para><b>Die Regel in einem Satz:</b> die Nachttemperatur sinkt je Blütewoche
/// um ein Grad, bis eine Untergrenze erreicht ist. Der Tagwert bleibt, wo das
/// Phasenprofil ihn hat.</para>
///
/// <para><b>Warum eine Untergrenze und nicht endlos:</b> „jede Woche ein Grad"
/// ohne Boden führt nach neun Blütewochen auf Kühlschranktemperatur. Wo der Boden
/// liegt, sagt das Phasenprofil selbst — sein Finish-Nachtwert ist das Ziel, an
/// dem die Rampe stehen bleibt. Erfunden wird hier nichts; die Rampe geht
/// gleitend dorthin, wo die Profile heute springen.</para>
///
/// <para><b>Was diese Klasse NICHT tut:</b> schalten. Sie rechnet. Das Schreiben
/// nach Home Assistant und das Regeln stehen bewusst woanders — siehe
/// <see cref="NachtabsenkungWriter"/>.</para>
/// </remarks>
public static class NachtabsenkungService
{
    /// <summary>Ein Grad je Blütewoche — die Zahl aus der Quelle.</summary>
    public const double SchrittProWocheC = 1.0;

    /// <summary>Tiefer geht die Rampe nie, egal was gerechnet wird.</summary>
    /// <remarks>
    /// Harte Kante gegen Tippfehler und gegen mich selbst: unter 12 °C hat im
    /// Reservoir nichts mehr zu suchen, auch wenn jemand die Untergrenze
    /// versehentlich auf 4 stellt.
    /// </remarks>
    public const double AbsoluteUntergrenzeC = 12.0;

    /// <summary>Wie viele Wochen der Plan höchstens ausweist.</summary>
    private const int MaxWochen = 14;

    public static Absenkplan Rechnen(
        GrowRun grow,
        HydroTargetValues? bluete,
        HydroTargetValues? finish,
        double? untergrenzeC,
        DateTime heute,
        bool vorschau = false)
    {
        // Vorschau rechnet auch im ausgeschalteten Zustand: wer sich entscheiden
        // soll, muss vorher sehen, worauf er sich einlaesst.
        if (!grow.NightRampEnabled && !vorschau)
        {
            return Leer("Die Nachtabsenkung ist für diesen Grow nicht eingeschaltet.",
                KettenSchluessel.PlanAbgeschaltet);
        }

        if (bluete is null)
        {
            return Leer("Ohne Sollwerte für die Blüte gibt es keinen Startwert für die Rampe.",
                KettenSchluessel.PlanOhneProfil);
        }

        var start = bluete.WaterTempNightC;
        var tag = bluete.WaterTempDayC;

        // Der Boden kommt aus dem Profil, wenn der Nutzer keinen eigenen nennt:
        // der Finish-Nachtwert ist das Ziel, an dem die Profile heute springen.
        var boden = Math.Max(
            untergrenzeC ?? finish?.WaterTempNightC ?? start,
            AbsoluteUntergrenzeC);

        if (boden > start)
        {
            return Leer($"Die Untergrenze ({Zahl(boden)} °C) liegt über dem Blüte-Nachtwert ({Zahl(start)} °C) — so kann nichts absinken.",
                KettenSchluessel.PlanUntergrenzeZuHoch);
        }

        var wochen = new List<AbsenkWoche>();
        var amBoden = 0;
        for (var woche = 1; woche <= MaxWochen; woche++)
        {
            var wert = Math.Max(boden, start - (woche - 1) * SchrittProWocheC);
            var erreicht = Math.Abs(wert - boden) < 0.001;
            wochen.Add(new AbsenkWoche(woche, tag, wert, erreicht));

            // Noch eine Zeile nach dem Erreichen: sonst bricht die Tabelle genau
            // dort ab, wo sie zeigen soll, dass es NICHT weiter runtergeht.
            if (erreicht && ++amBoden >= 2) break;
        }

        var aktuelle = Bluetewoche(grow, heute);
        var heuteNacht = aktuelle is { } w
            ? wochen[Math.Min(w, wochen.Count) - 1].NachtC
            : (double?)null;

        return new Absenkplan(
            wochen,
            aktuelle is null ? null : tag,
            heuteNacht,
            aktuelle,
            $"Start {Zahl(start)} °C aus dem Blüte-Profil, {Zahl(SchrittProWocheC)} °C je Blütewoche tiefer bis {Zahl(boden)} °C. "
                + "Methode nach SKX („Cold Morning Routine“); die Zahlen kommen aus deinem Sollwert-Profil.",
            aktuelle is null ? "Noch keine Blüte — die Rampe beginnt mit dem Flip." : null,
            aktuelle is null ? KettenSchluessel.PlanVorDemFlip : null);
    }

    /// <summary>Die laufende Blütewoche ab 1, oder null vor dem Blütebeginn.</summary>
    /// <remarks>
    /// Der Blütebeginn kommt aus dem <see cref="Phasenanker"/>: der Flip, bei
    /// der Autoflower „Blüte beginnt". Ohne Bestätigung gibt es hier bewusst
    /// keine Schätzung: eine geratene Woche verstellt eine echte Kühlung.
    /// </remarks>
    public static int? Bluetewoche(GrowRun grow, DateTime heute)
    {
        if (Phasenanker.Fuer(grow, heute).BlueteAb is not { } bluete) return null;
        var tage = (heute.Date - bluete).Days;
        return tage < 0 ? null : tage / 7 + 1;
    }

    private static Absenkplan Leer(string luecke, string schluessel)
        => new([], null, null, null, string.Empty, luecke, schluessel);

    private static string Zahl(double wert)
        => wert.ToString("0.#", Infrastructure.AppCulture.German);
}
