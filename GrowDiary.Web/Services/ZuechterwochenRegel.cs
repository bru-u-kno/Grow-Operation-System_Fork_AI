using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Wem gehören die Züchterwochen eines Grows — dem Grow selbst oder der Sorte?
/// </summary>
/// <remarks>
/// Bis forkai.226 kopierte das Grow die Wochen der Sorte beim Anlegen und zog sie
/// nie nach (Mimosa EVO, 16.10.2026: Sorte 9–10, Grow 8–9, Finish eine Woche zu
/// früh). Jetzt gilt: eigener Wert nur, wenn er bewusst gesetzt ist, sonst die
/// Sorte. Eingesetzt wird beim Lesen (<c>GrowCoreRepository.WirksameZuechterwochen</c>),
/// gespeichert wird nur der eigene Wert.
/// </remarks>
public static class ZuechterwochenRegel
{
    /// <summary>
    /// Ob das Grow einen eigenen Wert trägt — beim Speichern über Formular oder API.
    /// </summary>
    /// <param name="angabe">Was der Aufrufer ausdrücklich gesagt hat; <c>null</c> bei älteren Clients.</param>
    /// <param name="grow">Das zu speichernde Grow mit den geschickten Wochen.</param>
    /// <param name="sorte">Die Sorte des Grows, falls es eine gibt.</param>
    /// <param name="bisher">Das Grow, wie es vor dem Speichern gelesen wurde (nur beim Ändern).</param>
    /// <remarks>
    /// <para><b>Ausdrücklich gesagt gilt.</b> <c>true</c> = eigener Wert, <c>false</c> =
    /// folgt der Sorte; die Zahlen daneben werden bei <c>false</c> nicht gelesen.
    /// Genau das schützt ein veraltetes Formular: Es kennt die Sorte von vor einer
    /// Stunde, schickt „folgt" mit den alten Zahlen — und würde sie, wenn die
    /// Zahlen zählten, als „eigen" festschreiben, also den Fehler zurückbringen,
    /// den diese Änderung beheben soll. Wer Wochen setzen will, schickt
    /// <c>true</c>; die Oberfläche tut das, sobald jemand „Eigene Werte eintragen"
    /// wählt oder tippt.</para>
    ///
    /// <para><b>Ohne Angabe (ältere Clients)</b> wird abgeleitet: gleich der Sorte
    /// oder leer heißt „folgt", ebenso unveränderte Zahlen eines folgenden Grows;
    /// abweichende neue Zahlen gelten als bewusst gesetzt, denn eine Eingabe wird
    /// nie still verworfen.</para>
    /// </remarks>
    public static bool Eigen(bool? angabe, GrowRun grow, Strain? sorte, GrowRun? bisher = null)
    {
        // Ohne Sorte nichts zu folgen. Die Autoflower rechnet nicht mit
        // Züchterwochen und bleibt dabei, auch wenn die Sorte welche trägt.
        if (grow.StrainId is null || sorte is null || grow.SeedType == SeedType.Autoflower)
        {
            return true;
        }

        if (angabe is { } ausdruecklich)
        {
            return ausdruecklich;
        }

        var hatWochen = grow.BreederFlowerWeeksMin is not null || grow.BreederFlowerWeeksMax is not null;
        if (!hatWochen)
        {
            return false;
        }

        if (grow.BreederFlowerWeeksMin == sorte.FlowerWeeksMin && grow.BreederFlowerWeeksMax == sorte.FlowerWeeksMax)
        {
            return false;
        }

        // Dieselben Zahlen wie beim letzten Lesen eines folgenden Grows: nichts
        // angefasst, die Sorte hat sich inzwischen nur weiterbewegt.
        if (bisher is { BreederWochenEigen: false }
            && grow.BreederFlowerWeeksMin == bisher.BreederFlowerWeeksMin
            && grow.BreederFlowerWeeksMax == bisher.BreederFlowerWeeksMax)
        {
            return false;
        }

        // Ohne Angabe, neue Zahlen, die von der Sorte abweichen: bewusst gesetzt.
        return true;
    }
}
