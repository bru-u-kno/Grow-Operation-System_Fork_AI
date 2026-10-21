using System.Text.RegularExpressions;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Cultivate Control (cc.265): Meldungen, die eine API-Antwort dem Menschen zeigt, stehen mit echten Umlauten da
/// („ungültig“, nicht „ungueltig“).
/// </summary>
/// <remarks>
/// Gelesen wird der Quelltext der Controller: jede Zeile, die eine Fehler- oder Hinweismeldung erzeugt
/// (<c>AddModelError</c>, <c>ConflictError</c>, <c>BadRequestError</c>, <c>errors.Add</c>, <c>warnings.Add</c>).
/// Das erste Argument von <c>ConflictError</c>/<c>BadRequestError</c> ist ein maschinenlesbarer Code
/// (<c>datum_ungueltig</c>) und bleibt ASCII — er steht nicht in der Prüfung, weil nur Wörter mit Leerzeichen
/// drumherum gelten. Interpolationen in <c>{…}</c> sind ausgenommen.
/// </remarks>
public sealed class MeldungenMitUmlautenTests
{
    private static readonly Regex Senke = new(
        @"AddModelError|ConflictError|BadRequestError|NotFoundError|errors\.Add|warnings\.Add", RegexOptions.Compiled);

    private static readonly Regex Zeichenkette = new("\\$?\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    private static readonly Regex Interpolation = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    // Nur ganze Wörter, die im Deutschen sicher einen Umlaut tragen.
    private static readonly Regex AsciiUmlaut = new(
        @"(?<![A-Za-z_])(fuer|Fuer|ueber|Ueber|koennen|koennte|moeglich|muessen|duerfen|ungueltig|gueltig|gueltiges|" +
        @"geaendert|geloescht|loeschen|verknuepft|verknuepften|Oeffne|waehle|gewaehlten|groesser|enthaelt|gehoert|" +
        @"benoetigt|laeuft|eingeschraenkt|unterstuetzt|unterstuetzte|ausfuehrbaren|Abhaengigkeiten|Datensaetze|" +
        @"Naehrstoffbedarf|Keimungsbestaetigung|Pruefung|ueberein|uebergeben)(?![A-Za-z_])", RegexOptions.Compiled);

    [Fact]
    public void Meldungen_der_Controller_haben_echte_Umlaute()
    {
        var wurzel = Projektwurzel();
        var fehler = new List<string>();

        foreach (var datei in Directory.EnumerateFiles(Path.Combine(wurzel, "GrowDiary.Web", "Api"), "*.cs", SearchOption.AllDirectories))
        {
            var zeilen = File.ReadAllLines(datei);
            for (var i = 0; i < zeilen.Length; i++)
            {
                var zeile = zeilen[i];
                if (zeile.TrimStart().StartsWith("//") || !Senke.IsMatch(zeile)) continue;

                foreach (Match literal in Zeichenkette.Matches(zeile))
                {
                    var text = Interpolation.Replace(literal.Groups[1].Value, " ");
                    // Code wie "datum_ungueltig": ein Wort ohne Leerzeichen, nur Kleinbuchstaben und Unterstrich.
                    if (Regex.IsMatch(text, "^[a-z0-9_]+$")) continue;
                    var treffer = AsciiUmlaut.Match(text);
                    if (treffer.Success)
                    {
                        fehler.Add($"{Path.GetRelativePath(wurzel, datei)}:{i + 1}: „{treffer.Value}“ in „{text}“");
                    }
                }
            }
        }

        Assert.True(fehler.Count == 0, "Meldungen mit ASCII-Umlauten:\n" + string.Join("\n", fehler));
    }

    private static string Projektwurzel()
    {
        var ordner = AppContext.BaseDirectory;
        while (ordner is not null)
        {
            if (Directory.Exists(Path.Combine(ordner, "GrowDiary.Web", "Api"))) return ordner;
            ordner = Path.GetDirectoryName(ordner);
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
