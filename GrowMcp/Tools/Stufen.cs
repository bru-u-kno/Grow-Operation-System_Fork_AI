namespace GrowMcp.Tools;

/// <summary>
/// Die Stufen des Zugriffs für KI-Assistenten, wie Grow OS sie nennt.
/// </summary>
/// <remarks>
/// <para>Über die Leitung gehen die Namen aus <c>KiStufe</c> („GrowPlanen"),
/// in der Oberfläche stehen die Anzeigenamen („Grow planen",
/// <c>KiZugriffDienst.Anzeigename</c>). Beide stehen hier, weil die
/// Werkzeugbeschreibungen sie als Konstanten brauchen — und
/// <c>ForkWegeTests</c> hält sie per Reflexion gegen das Enum in Grow OS.</para>
///
/// <para>Die Reihenfolge ist die der Bits (1, 2, 4, 8). An ihr hängt „Rückfrage
/// ab Stufe …": ab Grow planen heisst Grow planen und alles danach.</para>
/// </remarks>
public static class Stufen
{
    public const string Dokumentieren = "Dokumentieren";
    public const string GrowPlanen = "GrowPlanen";
    public const string GeraeteSchalten = "GeraeteSchalten";
    public const string Verwaltung = "Verwaltung";

    /// <summary>Anzeigenamen — für Beschreibungen, die ein Mensch liest.</summary>
    public const string DokumentierenText = "Dokumentieren";
    public const string GrowPlanenText = "Grow planen";
    public const string GeraeteSchaltenText = "Geräte schalten";
    public const string VerwaltungText = "Verwaltung";

    /// <summary>Alle Stufen in der Reihenfolge ihrer Bits.</summary>
    public static IReadOnlyList<(string Name, string Text)> Alle { get; } =
    [
        (Dokumentieren, DokumentierenText),
        (GrowPlanen, GrowPlanenText),
        (GeraeteSchalten, GeraeteSchaltenText),
        (Verwaltung, VerwaltungText),
    ];

    /// <summary>Der Anzeigename zu einem Leitungsnamen; Unbekanntes bleibt, wie es ist.</summary>
    public static string Text(string name)
        => Alle.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).Text ?? name;

    /// <summary>Die Stelle in der Reihenfolge, oder -1.</summary>
    public static int Rang(string name)
    {
        for (var i = 0; i < Alle.Count; i++)
        {
            if (string.Equals(Alle[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}

/// <summary>
/// Dieses Werkzeug braucht einen Fork-Schlüssel (<c>gok_…</c>).
/// </summary>
/// <remarks>
/// <para>Mit dem MCP-Schlüssel antwortet es mit dem Hinweis, wo es den Schlüssel
/// gibt, und fragt Grow OS gar nicht erst. <c>SchreibWerkzeugeTests</c> zählt über
/// alle Werkzeuge mit diesem Merkmal und verlangt genau das.</para>
///
/// <para><see cref="Stufe"/> ist die Stufe, die Grow OS verlangt — nur zur
/// Beschreibung; entschieden wird in Grow OS. <c>null</c> heisst: lesend, ein
/// gültiger Schlüssel genügt.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class BrauchtForkSchluesselAttribute(string? stufe = null) : Attribute
{
    public string? Stufe { get; } = stufe;
}

/// <summary>
/// Ein plausibler Wert für diesen Parameter — nur für die Werkzeug-Zählung in den Tests.
/// </summary>
/// <remarks>
/// Ohne ihn füllte die Zählung jeden Text mit „test" — und ein Werkzeug mit
/// fester Auswahl („keimung", „veg" …) wiese das ab, bevor es Grow OS je fragt.
/// Die Zählung sähe dann nie, ob der Weg nach Grow OS überhaupt stimmt.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class BeispielAttribute(string wert) : Attribute
{
    public string Wert { get; } = wert;
}
