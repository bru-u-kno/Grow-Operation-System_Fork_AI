using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Modi eines AC-Infinity-Geräts: eine Tabelle (<see cref="AcModi"/>), und
/// kein Modus steht roh auf Englisch auf dem Schirm.
/// </summary>
/// <remarks>
/// <b>Der Anlass (02.10.2026).</b> Die Modi standen an drei Stellen, und die
/// Steuerungs-Übersicht baute <c>$"Modus {licht.Modus} · Stufe …"</c> — dort
/// stand „Modus Auto", weil nur die Oberfläche übersetzen konnte.
/// </remarks>
public sealed class AcModiTests
{
    /// <summary>
    /// Die <c>options</c> von <c>select.rdwc_fan3_aktiver_modus</c> in der
    /// Anlage des Nutzers (02.10.2026) — die Liste von außen, gegen die die
    /// Tabelle gehalten wird. Nicht aus dem Kopf und nicht aus AcModi
    /// abgeschrieben: würde sie aus AcModi gelesen, belegte sich die Tabelle
    /// selbst.
    /// </summary>
    private static readonly string[] OptionenDerAnlage =
    {
        "Off", "On", "Auto", "Timer to On", "Timer to Off", "Cycle", "Schedule", "VPD",
        "CO2", "CO2 Fan", "Moisture", "Water Temp", "pH", "EC", "Water Detect",
    };

    /// <summary>Abkürzungen, die auch im Deutschen so heißen — nur sie dürfen gleich bleiben.</summary>
    private static readonly HashSet<string> GleichImDeutschen = new() { "VPD", "pH", "EC" };

    /// <summary>Alle Kennungen, die AcModi als Konstante führt — per Reflexion, nicht abgetippt.</summary>
    private static List<string> Konstanten() => typeof(AcModi)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    [Fact]
    public void Die_Tabelle_kennt_genau_die_Modi_der_Anlage()
    {
        Assert.Equal(15, OptionenDerAnlage.Length);
        Assert.Equal(
            OptionenDerAnlage.OrderBy(x => x, StringComparer.Ordinal),
            AcModi.Namen.Keys.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Jede_Konstante_hat_einen_Namen_und_jeder_Name_eine_Konstante()
    {
        var konstanten = Konstanten();
        // Mengenwächter: ohne ihn liefe die Prüfung bei leerer Reflexion grün.
        Assert.True(konstanten.Count >= 15, $"Nur {konstanten.Count} Konstanten gefunden — sieht die Reflexion die Klasse?");
        Assert.Equal(
            konstanten.OrderBy(x => x, StringComparer.Ordinal),
            AcModi.Namen.Keys.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Jeder_Modus_ist_uebersetzt_ausser_den_deutschen_Abkuerzungen()
    {
        foreach (var wert in OptionenDerAnlage)
        {
            if (GleichImDeutschen.Contains(wert)) Assert.Equal(wert, AcModi.Name(wert));
            else Assert.NotEqual(wert, AcModi.Name(wert));
        }
    }

    [Theory]
    [InlineData("On", "an")]
    [InlineData("on", "an")]
    [InlineData(" Timer to Off ", "Countdown bis aus")]
    [InlineData("CO2", "CO₂")]
    [InlineData(null, "–")]
    [InlineData("", "–")]
    [InlineData("unavailable", "nicht erreichbar")]
    [InlineData("unknown", "unbekannt")]
    [InlineData("Künftiger Modus", "Künftiger Modus")]
    public void Name_liest_jede_Schreibweise(string? kennung, string erwartet)
        => Assert.Equal(erwartet, AcModi.Name(kennung));

    /* ------------------------------------------------ Die Kurzzeile der Übersicht */

    private static LichtLive Licht(string? modus, int? stufe = 5, string? preset = null, string? ein = null, string? aus = null)
        => new(true, modus, stufe, ein, aus, true, true, preset, null, Array.Empty<string>(), Array.Empty<string>(), DateTime.UtcNow);

    [Fact]
    public void LichtKurz_nennt_keinen_Modus_roh_auf_Englisch()
    {
        // Die drei Modi mit eigener Zeile (Aus, Dauerlicht, Zeitplan) sind
        // unten einzeln geprüft — hier geht es um alle übrigen.
        var selten = OptionenDerAnlage.Where(m => m is not ("Off" or "On" or "Schedule")).ToList();
        Assert.True(selten.Count >= 12, "Die Grundmenge der seltenen Modi ist leer — dann prüft das nichts.");

        foreach (var modus in selten)
        {
            var zeile = SteuerungApiController.LichtKurz(Licht(modus));
            Assert.Equal($"Modus {AcModi.Name(modus)} · Stufe 5", zeile);
            if (!GleichImDeutschen.Contains(modus))
            {
                // Gross-/Kleinschreibung zählt: „automatisch" enthält „auto",
                // aber nicht das Wort „Auto".
                Assert.False(Regex.IsMatch(zeile, $@"(?<![\w]){Regex.Escape(modus)}(?![\w])"),
                    $"„{zeile}“ nennt den Modus roh: {modus}");
            }
        }
    }

    [Fact]
    public void LichtKurz_der_Anlass_Modus_Auto()
        => Assert.Equal("Modus automatisch · Stufe 5", SteuerungApiController.LichtKurz(Licht("Auto")));

    [Fact]
    public void LichtKurz_die_drei_eigenen_Zeilen()
    {
        Assert.Equal("Aus · Stufe 5", SteuerungApiController.LichtKurz(Licht(AcModi.Aus)));
        Assert.Equal("Dauerlicht · Stufe 5", SteuerungApiController.LichtKurz(Licht(AcModi.An)));
        Assert.Equal("Zeitplan Blüte 06:00 – 18:00 · Stufe 5",
            SteuerungApiController.LichtKurz(Licht(AcModi.Zeitplan, preset: "bluete", ein: "06:00", aus: "18:00")));
    }

    [Fact]
    public void LichtKurz_ohne_Modus_und_ohne_Stufe()
        => Assert.Equal("Modus – · Stufe –", SteuerungApiController.LichtKurz(Licht(null, stufe: null)));

    [Fact]
    public void Der_deutsche_Name_geht_mit_ueber_die_Leitung()
    {
        // Die Oberfläche liest `modusName` — kommt das Feld nicht im JSON an,
        // stünde dort nichts. Dieselben Einstellungen wie Program.cs (camelCase).
        var json = JsonSerializer.Serialize(Licht("Cycle"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"modus\":\"Cycle\"", json);
        Assert.Contains("\"modusName\":\"Zyklus\"", json);

        var ac = new AcGeraetStand(new AcGeraet("LED", "number.led", "select.led"), 5, "Schedule", null, null, null);
        Assert.Contains("\"modusName\":\"Zeitplan\"", JsonSerializer.Serialize(ac, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Null(new AcGeraetStand(new AcGeraet("LED", "number.led", null), 5, null, null, null, null).ModusName);
    }
}
