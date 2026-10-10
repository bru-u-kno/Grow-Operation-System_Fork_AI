using System.Text.RegularExpressions;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-016, Etappe 6): Die Anleitung auf der Rollen-Seite nennt die Rollen und Edenic-Schlüssel, die der
/// Fork wirklich benutzt — beides steht in der Oberfläche als Liste. Weicht sie vom Dienst ab, sagt die Anleitung
/// dem Nutzer etwas anderes, als der Fork tut (der Schlüssel ginge ins Leere, ohne dass es jemand merkt).
/// </summary>
public sealed class BluelabAnleitungSpiegelTests
{
    private static string Quelle()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var pfad = Path.Combine(dir, "GrowDiary.React", "src", "features", "geraete", "bluelab-anleitung.ts");
            if (File.Exists(pfad)) return File.ReadAllText(pfad);
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("bluelab-anleitung.ts nicht gefunden.");
    }

    private static List<string> Liste(string text, string name)
    {
        var block = Regex.Match(text, name + @"\s*=\s*\[(?<inhalt>.*?)\]\s*as const", RegexOptions.Singleline);
        Assert.True(block.Success, $"{name} nicht gefunden — der Test sieht seine Quelle nicht.");
        return Regex.Matches(block.Groups["inhalt"].Value, "'([^']+)'").Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public void DieAlarmRollenDerOberflaecheSindDieDesDienstes()
    {
        var oberflaeche = Liste(Quelle(), "BLUELAB_ALARME");
        var dienst = BluelabGrenzenService.Zuordnung.Select(z => z.Rolle).ToList();

        Assert.Equal(dienst.Order(StringComparer.Ordinal), oberflaeche.Order(StringComparer.Ordinal));
        // ... und es gibt sie als Rollen des Moduls.
        Assert.All(oberflaeche, r => Assert.NotNull(SteuerungGeraeteRollen.Finden(BluelabGrenzenService.Modul, r)));
    }

    [Fact]
    public void DieEdenicSchluesselDerOberflaecheSindDieDesDienstes()
    {
        var oberflaeche = Liste(Quelle(), "BLUELAB_SCHLUESSEL");
        var dienst = BluelabGrenzenService.Zuordnung.Select(z => z.Schluessel).ToList();

        Assert.Equal(dienst.Order(StringComparer.Ordinal), oberflaeche.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DieSkriptRolleGibtEsUndIstOptional()
    {
        Assert.Contains("BLUELAB_SKRIPT = 'skript'", Quelle());
        var rolle = SteuerungGeraeteRollen.Finden(BluelabGrenzenService.Modul, "skript");
        Assert.NotNull(rolle);
        Assert.False(rolle!.Pflicht);
    }
}
