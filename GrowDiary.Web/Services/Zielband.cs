using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;

namespace GrowDiary.Web.Services;

/// <summary>
/// Das Zielband eines Grows — dieselbe Antwort für alle, die danach fragen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (01.09.2026).</b> Der Schalter „Diese Wochen-Ziele auch
/// auf dem Bildschirm verwenden" (Feedchart) wirkte nur im
/// <see cref="GrowDashboardComposer"/>. Messprotokoll, Diagnose und
/// Empfehlungen rechneten weiter mit dem Profil.</para>
///
/// <para>Ein Grow mit Athena Blended in Blütewoche 4: das Chart nennt EC 2,6,
/// das Profil <c>rdwc-default</c> für Flower 1,0–1,2. Bei gemessenem EC 2,60
/// sagte die Live-Kachel „im Ziel", das Messprotokoll derselben Messung „weit
/// über dem Ziel". Zwei Auskünfte über einen Wert, nebeneinander auf dem
/// Schirm.</para>
///
/// <para><b>Die Kette, in dieser Reihenfolge:</b></para>
/// <list type="number">
///   <item>Profil: Grow → System → Anbaustil (<see cref="SetpointProfileResolver"/>)</item>
///   <item>Die Sollwerte dieses Profils für die Phase</item>
///   <item>Feedchart-Ziele der Woche, <b>wenn der Grow sie will</b></item>
///   <item>Eigene Grenzwerte des Nutzers — die gewinnen immer</item>
/// </list>
///
/// <para>Schritt 4 zuletzt, weil eine selbst eingetragene Grenze keine
/// Empfehlung mehr ist, sondern eine Ansage.</para>
/// </remarks>
public static class Zielband
{
    /// <summary>Das Band für einen Grow in einer Phase; <c>null</c> ohne Profil.</summary>
    /// <param name="targetValues">Die Sollwerte aus dem Wissen.</param>
    /// <param name="wissen">Für die Feedchart-Ziele; <c>null</c> lässt Schritt 3 aus.</param>
    /// <param name="grow">Der Lauf.</param>
    /// <param name="stage">Seine Phase.</param>
    /// <param name="systemProfileId">Das Profil des Hydro-Systems, falls es eins hat.</param>
    /// <param name="eigeneGrenzen">Die Grenzwert-Regeln des Zelts.</param>
    public static HydroTargetValues? FuerGrow(
        TargetValueService targetValues,
        KnowledgeBaseLoader? wissen,
        GrowRun grow,
        GrowStage stage,
        string? systemProfileId,
        IReadOnlyList<TentAlertRule>? eigeneGrenzen)
    {
        var profil = SetpointProfileResolver.Resolve(
            grow.SetpointProfileId, systemProfileId, grow.HydroStyle);

        var band = targetValues.GetTargets(profil.ProfileId, stage);
        if (band is null) return null;

        // Will der Grow die Wochen-Ziele seines Feedcharts, gelten sie — sonst
        // stuende beim Mischen EC 2,6 und auf dem Bildschirm etwas anderes.
        if (wissen is not null
            && MischplanService.ZielSpalteFuerGrow(grow, wissen.NutrientPrograms) is { } chartZiel)
        {
            band = MischplanService.MitFeedchart(band, chartZiel.Spalte);
        }

        return UserTargets.Overlay(band, eigeneGrenzen);
    }

    /// <summary>
    /// Das Band EINER Messgroesse — die Lesart, die auch auf der Kachel steht.
    /// </summary>
    /// <param name="key">Kennung der Messgroesse, z. B. <c>reservoir-ph</c>.</param>
    /// <param name="t">Die Sollwerte der Phase/Woche.</param>
    /// <param name="rampenBodenC">
    /// Der Wert, auf den die Nachtabsenkung faehrt — er zieht die Untergrenze
    /// der Wassertemperatur mit nach unten. Ohne ihn meldete die Kachel die
    /// eigene Regelung der App als Abweichung.
    /// </param>
    /// <remarks>
    /// <para><b>Warum hier und nicht im Dashboard (10.09.2026).</b> Diese
    /// Umrechnung stand als private Methode im
    /// <see cref="GrowDashboardComposer"/>. Solange nur die Kacheln sie
    /// brauchten, war das richtig. Seit die Alarme ihre Grenzen aus demselben
    /// Band ableiten koennen (<see cref="Planzielgrenzen"/>), braucht sie ein
    /// zweiter Leser — und eine Abschrift waere genau der Fehler, den diese
    /// Klasse behebt: zwei Auskuenfte ueber denselben Messwert.</para>
    ///
    /// <para>Beim pH steht bewusst der HANDLUNGSBEREICH und nicht das
    /// Anmischziel: <c>(PhMin, PhMax)</c> ist der Wert, auf den man anmischt,
    /// kein Band, an dem man misst. Bei der Wassertemperatur steht der
    /// ARBEITSBEREICH und nicht das Tag/Nacht-Paar — in der Veg-Phase sind
    /// beide Werte gleich, das Band waere null breit.</para>
    /// </remarks>
    public static (double? Min, double? Max) FuerMetrik(
        string key, HydroTargetValues? t, double? rampenBodenC = null)
    {
        if (t is null) return (null, null);
        return key switch
        {
            "reservoir-ph" => DeviationAnalyzerService.PhHandlungsbereich(t, eigene: null),
            "reservoir-ec" => (t.EcMin, t.EcMax),
            "orp" => (t.OrpMin, t.OrpMax),
            "vpd" => (t.VpdMin, t.VpdMax),
            "ppfd" => (t.PpfdMin, t.PpfdMax),
            "co2" => (t.Co2Min, t.Co2Max),
            "reservoir-temp" => Wasserband.Grenzen(t, rampenBodenC, null),
            _ => (null, null),
        };
    }
}
