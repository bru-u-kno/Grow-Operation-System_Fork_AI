namespace GrowDiary.Web.Services;

/// <summary>
/// Neuling-Test 17.10.2026: Ein Steuerungs-Dienst, der seinen Helfern nach dem Anlegen ihren Startwert gibt
/// (<see cref="SteuerungStartwerteService"/>).
/// </summary>
/// <remarks>
/// Ein Zahl-Helfer entsteht ohne Startwert, Home Assistant nimmt dann sein Minimum — beim Wasserkühler 4 °C statt der 20/18 °C
/// der Einstellungen, beim Entfeuchter 30 % und 15 °C. Jeder Dienst führt eigene Einstellungen und weiß, welche Helfer er
/// schreibt; dieser Vertrag lässt den gemeinsamen Ablauf (festhalten, anlegen, nur Neues schreiben) darauf zugreifen.
/// </remarks>
public interface IStartwertDienst
{
    /// <summary>Die Steuerung, für die der Dienst zuständig ist (Kennung des Moduls).</summary>
    string StartModul { get; }

    /// <summary>
    /// Den Stand des Fork festhalten, <b>bevor</b> Helfer angelegt werden — sonst übernähme der Dienst beim ersten Lesen die
    /// eben angelegten Mindestwerte aus Home Assistant als eigene Einstellungen.
    /// </summary>
    Task<object> StandFesthaltenAsync(CancellationToken ct);

    /// <summary>
    /// Die neu angelegten Helfer beschreiben: nur diese, nicht die Regelung, nichts, was es schon gab.
    /// </summary>
    /// <param name="stand">Das Ergebnis von <see cref="StandFesthaltenAsync"/>.</param>
    /// <param name="neuAngelegt">Die Entity-IDs der neu angelegten Helfer (in beiden Kennungsräumen).</param>
    /// <returns>Wie viele Helfer geschrieben wurden und ob Home Assistant alle angenommen hat.</returns>
    Task<(int Geschrieben, bool Alles)> StartwerteSchreibenAsync(object stand, IReadOnlySet<string> neuAngelegt, CancellationToken ct);
}

/// <summary>Gemeinsame reine Hilfen für das Schreiben einzelner Helfer.</summary>
public static class Startwerte
{
    /// <summary>
    /// Aus einer Schreibliste nur die Helfer, die gewollt sind: alle (<paramref name="nur"/> = <c>null</c>, wie beim Speichern
    /// der Einstellungen) oder nur die genannten (frisch angelegte). Eine reine Entscheidung ohne Home Assistant — damit prüfbar,
    /// dass ein bestehender Helfer nicht überschrieben wird.
    /// </summary>
    public static IReadOnlyList<(string Entity, double Wert)> Nur(IReadOnlyList<(string Entity, double Wert)> liste, IReadOnlySet<string>? nur)
        => nur is null ? liste : liste.Where(z => nur.Contains(z.Entity)).ToArray();
}
