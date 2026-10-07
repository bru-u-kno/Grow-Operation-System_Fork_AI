using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (A-010, 07.10.2026): Die reine Rechnung des Probelaufs — ohne Datenbank und ohne Home Assistant.
/// </summary>
/// <remarks>
/// <para><b>Warum rein.</b> Ob ein Lauf abgebrochen wird, ist die Sicherheitsentscheidung des ganzen
/// Probelaufs. Sie steht hier, wo sie sich mit einer Handvoll Zahlen prüfen lässt, und nicht im Dienst,
/// der Uhr, Datenbank und Geräte mitbringt.</para>
///
/// <para><b>Fühler ohne Wert.</b> Meldet ein Fühler nichts, wird der Wert nicht geprüft — aber nur
/// kurz: ab einer Minute ohne Wert gilt das als Verletzung. Ein Lauf ohne Sicht aufs Zelt ist keiner.
/// Das gilt nur für Werte, für die eine Grenze gesetzt ist.</para>
/// </remarks>
public static class ProbelaufBewertung
{
    /// <summary>Unterhalb dieser Abweichung vom Start gilt ein Wert als unverändert (Messrauschen) — dort gibt es keine Erholungszeit.</summary>
    public static double MindestAbweichung(string groesse) => groesse switch { "Feuchte" => 1.0, "Temperatur" => 0.3, _ => 0.05 };

    /// <summary>Wie viel der Abweichung vom Start wieder abgebaut sein muss, damit ein Wert als erholt gilt.</summary>
    public const double ErholungsAnteil = 0.9;

    /// <summary>Ab dieser Dauer ohne Wert bricht der Lauf ab.</summary>
    public static readonly TimeSpan FuehlerLosGrenze = TimeSpan.FromSeconds(60);

    public static ProbelaufVerletzung? Pruefen(ProbelaufGrenzen grenzen, ProbelaufMesswerte werte, TimeSpan fuehlerLosSeit)
    {
        var fuehlerLos = fuehlerLosSeit > FuehlerLosGrenze;

        if (grenzen.FeuchteMax is { } fMax)
        {
            if (werte.Feuchte is null) { if (fuehlerLos) return FuehlerAus("Feuchte"); }
            else if (werte.Feuchte > fMax)
                return new("Feuchte", $"Luftfeuchte {Z(werte.Feuchte.Value)} % liegt über der Grenze {Z(fMax)} %.");
        }

        if (grenzen.TempMax is { } tMax)
        {
            if (werte.Temp is null) { if (fuehlerLos) return FuehlerAus("Temperatur"); }
            else if (werte.Temp > tMax)
                return new("Temperatur", $"Temperatur {Z(werte.Temp.Value)} °C liegt über der Grenze {Z(tMax)} °C.");
        }

        if (grenzen.VpdMin is not null || grenzen.VpdMax is not null)
        {
            if (werte.Vpd is null) { if (fuehlerLos) return FuehlerAus("VPD"); }
            else if (grenzen.VpdMin is { } vMin && werte.Vpd < vMin)
                return new("VPD", $"VPD {Z(werte.Vpd.Value)} kPa liegt unter der Grenze {Z(vMin)} kPa.");
            else if (grenzen.VpdMax is { } vMax && werte.Vpd > vMax)
                return new("VPD", $"VPD {Z(werte.Vpd.Value)} kPa liegt über der Grenze {Z(vMax)} kPa.");
        }

        return null;
    }

    /// <summary>Spielraum der vorgeschlagenen Grenzen über dem Pflanzenziel: Luftfeuchte in Prozentpunkten.</summary>
    public const double SpielraumFeuchte = 4;

    /// <summary>… Temperatur in Kelvin.</summary>
    public const double SpielraumTemp = 1.5;

    /// <summary>… VPD in kPa, nach beiden Seiten.</summary>
    public const double SpielraumVpd = 0.5;

    /// <summary>
    /// Die vorgeschlagenen Grenzen: Pflanzenziel <b>plus Spielraum</b>.
    /// </summary>
    /// <remarks>
    /// Die Ziele des Plans sind Regelziele. Ein Probelauf soll sie bewusst überschreiten dürfen — sonst bräche
    /// er sofort ab (bei einem Ziel von 51 % Luftfeuchte und einem Istwert von 52,8 % schon vor dem Start) — und
    /// ein VPD-Band der Breite null (Unten = Oben = 1,4) ließe keinen Wert zu. Die Grenze ist die Sicherheit, nicht das Ziel.
    /// Fehlt ein Ziel (<c>null</c>), wird der Wert nicht überwacht.
    /// </remarks>
    public static ProbelaufGrenzen GrenzenAusZielen(double? rhMax, double? tempMax, double? vpdUnten, double? vpdOben)
        => new(
            FeuchteMax: rhMax + SpielraumFeuchte,
            TempMax: tempMax + SpielraumTemp,
            VpdMin: vpdUnten is { } u ? Math.Round(Math.Max(0.1, u - SpielraumVpd), 2) : null,
            VpdMax: vpdOben is { } o ? Math.Round(o + SpielraumVpd, 2) : null);

    private static ProbelaufVerletzung FuehlerAus(string groesse)
        => new(groesse, $"Der Fühler für {groesse} meldet seit über einer Minute nichts — ohne Sicht aufs Zelt läuft kein Probelauf weiter.");

    private static string Z(double wert) => wert.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

    // ------------------------------------------------------------ Kennzahlen

    public static IReadOnlyList<ProbelaufKennzahl> Kennzahlen(
        IReadOnlyList<ProbelaufMesswerte> vorher,
        IReadOnlyList<ProbelaufMesswerte> waehrend,
        IReadOnlyList<ProbelaufMesswerte> nachher)
    {
        var ergebnis = new List<ProbelaufKennzahl>();
        foreach (var (name, wahl) in new (string, Func<ProbelaufMesswerte, double?>)[]
                 { ("Feuchte", m => m.Feuchte), ("Temperatur", m => m.Temp), ("VPD", m => m.Vpd) })
        {
            var k = KennzahlFuer(name, wahl, vorher, waehrend, nachher);
            if (k is not null) ergebnis.Add(k);
        }
        return ergebnis;
    }

    private static ProbelaufKennzahl? KennzahlFuer(
        string name, Func<ProbelaufMesswerte, double?> wahl,
        IReadOnlyList<ProbelaufMesswerte> vorher, IReadOnlyList<ProbelaufMesswerte> waehrend, IReadOnlyList<ProbelaufMesswerte> nachher)
    {
        var w = waehrend.Where(m => wahl(m) is not null).OrderBy(m => m.ZeitUtc).ToList();
        if (w.Count == 0) return null;

        var start = wahl(w[0])!.Value;
        var ende = wahl(w[^1])!.Value;
        // „Spitze" ist der Wert mit dem größten Abstand zum Start — beim VPD kann das ein Tiefpunkt sein.
        var spitze = w.Select(m => wahl(m)!.Value).MaxBy(v => Math.Abs(v - start));

        var minuten = (w[^1].ZeitUtc - w[0].ZeitUtc).TotalMinutes;
        var proMinute = minuten > 0 ? (ende - start) / minuten : 0;

        // Erholt heißt: 90 % der Abweichung vom Startwert sind wieder abgebaut. Der Startwert (der Stand, in dem die Regelung
        // arbeitete) ist der Bezug — nicht der Mittelwert des Vorlaufs: der schwankt, und eine enge Toleranz darum wird nie erreicht
        // (am echten Zelt am 07.10.2026: Luftfeuchte 53,3 → 48,5 % schon im Vorlauf).
        double? erholung = null;
        var nachReihe = nachher.Where(m => wahl(m) is not null).OrderBy(m => m.ZeitUtc).ToList();
        var abweichung = spitze - start;
        if (Math.Abs(abweichung) < MindestAbweichung(name))
        {
            // Es gab nichts, wovon sich etwas erholen könnte — „0 Minuten" statt einer Zahl aus dem Messrauschen.
            erholung = 0;
        }
        else if (nachReihe.Count > 0)
        {
            var ziel = spitze - ErholungsAnteil * abweichung;
            var treffer = nachReihe.FirstOrDefault(m => abweichung > 0 ? wahl(m)!.Value <= ziel : wahl(m)!.Value >= ziel);
            if (treffer is not null) erholung = Math.Max(0, (treffer.ZeitUtc - w[^1].ZeitUtc).TotalMinutes);
        }

        return new ProbelaufKennzahl(name, start, spitze, ende, proMinute, erholung);
    }
}
