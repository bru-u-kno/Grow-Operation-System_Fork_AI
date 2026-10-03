namespace GrowOsAccess;

/// <summary>
/// Woher der Fork-Schlüssel der laufenden Anfrage kommt.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026): Ein Assistent, der mit einem Schlüssel aus
/// Grow OS (<c>gok_…</c>, „Zugriff für KI-Assistenten") verbunden ist, darf
/// eintragen und schalten — so weit, wie der Betreiber es dort angehakt hat.
/// Entschieden wird das allein in Grow OS. Diese Bibliothek reicht den Schlüssel
/// nur durch, und zwar bei <b>jeder</b> Anfrage, lesend wie schreibend.</para>
///
/// <para>Die Quelle wird je Anfrage gefragt und hält nichts fest. Der Schlüssel
/// steht damit nie in einem Feld, einer Datei oder einem Protokoll dieses
/// Programms — er lebt so lange wie die Anfrage, die ihn mitgebracht hat.</para>
/// </remarks>
public interface IForkSchluesselQuelle
{
    /// <summary>Der <c>gok_</c>-Schlüssel der laufenden Anfrage — oder <c>null</c>.</summary>
    string? Schluessel { get; }
}

/// <summary>Kein Schlüssel: nur lesen, wie bisher.</summary>
public sealed class KeinForkSchluessel : IForkSchluesselQuelle
{
    public static KeinForkSchluessel Instanz { get; } = new();

    public string? Schluessel => null;
}

/// <summary>Was einen Fork-Schlüssel ausmacht.</summary>
public static class ForkSchluessel
{
    /// <summary>
    /// Die Vorsilbe, an der Grow OS einen Schlüssel erkennt.
    /// </summary>
    /// <remarks>
    /// Steht in Grow OS als <c>KiZugriffDienst.Vorsilbe</c>;
    /// <c>ForkWegeTests</c> liest den Wert dort per Reflexion nach, statt ihn
    /// abzutippen.
    /// </remarks>
    public const string Vorsilbe = "gok_";

    /// <summary>
    /// Sieht das wie ein Fork-Schlüssel aus?
    /// </summary>
    /// <remarks>
    /// Nur die Form, nicht die Gültigkeit — die kennt allein Grow OS. Verlangt
    /// werden die Vorsilbe und dahinter mindestens acht Zeichen base64url (Grow OS
    /// erzeugt 43, die ersten acht zeigt es als Präfix). Leerzeichen oder
    /// Steuerzeichen kommen so gar nicht erst in einen Kopf nach Grow OS.
    /// </remarks>
    public static bool HatForm(string? wert)
    {
        if (string.IsNullOrEmpty(wert) || !wert.StartsWith(Vorsilbe, StringComparison.Ordinal)) return false;
        var rest = wert.AsSpan(Vorsilbe.Length);
        if (rest.Length < 8 || rest.Length > 128) return false;
        foreach (var zeichen in rest)
        {
            if (!(char.IsAsciiLetterOrDigit(zeichen) || zeichen is '-' or '_')) return false;
        }
        return true;
    }
}
