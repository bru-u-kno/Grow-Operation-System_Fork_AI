using System.Globalization;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (A-010, Etappe 2, 07.10.2026): Die Rechnung hinter dem Kenntnisstand — ohne Datenbank, Home Assistant und KI.
/// </summary>
/// <remarks>
/// <para><b>Was „im Ziel" heißt.</b> Luftfeuchte und Temperatur liegen im Ziel, wenn sie das Maximum nicht übersteigen. Das VPD liegt
/// im Ziel, wenn es im Band des Plans liegt — mit <see cref="VpdToleranz"/> nach beiden Seiten: Brus Plan führt Unten = Oben (1,4 bis 1,4),
/// und ein Band der Breite null hielte nie jemand ein. „Annähernd" ist das Ziel des Nutzers, nicht „exakt".</para>
/// <para><b>Urteil.</b> Ab <see cref="ErreichbarAb"/> Prozent der Zeit im Ziel „erreichbar", ab <see cref="KnappAb"/> „knapp", darunter „Lücke";
/// unter <see cref="MindestMinuten"/> Messminuten in der Lichtphase „unbekannt" — wer fast nichts gemessen hat, urteilt nicht.</para>
/// <para><b>Wirkung</b> kommt nur aus Läufen, deren Eingriff mindestens <see cref="MindestEingriffMinuten"/> dauerte; ein Lauf von
/// 14 Sekunden (Neustart-Test) sagt nichts über ein Gerät.</para>
/// </remarks>
public static class KenntnisstandRechner
{
    public const double ErreichbarAb = 90;
    public const double KnappAb = 70;
    public const int MindestMinuten = 60;
    public const double VpdToleranz = 0.1;
    public const double MindestEingriffMinuten = 2;

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static string Z(double wert, string format = "0.##") => wert.ToString(format, De);

    // ------------------------------------------------------------- Zielabgleich

    public static IReadOnlyList<ZielZeile> Zielabgleich(IReadOnlyList<ZeltMinute> serie, Zielbaender ziele)
    {
        return
        [
            new("Luftfeuchte",
                Phase(serie, true, ziele.Tag.FeuchteMax, m => m.Feuchte, v => v <= ziele.Tag.FeuchteMax, "≤ {0} %"),
                Phase(serie, false, ziele.Nacht.FeuchteMax, m => m.Feuchte, v => v <= ziele.Nacht.FeuchteMax, "≤ {0} %")),
            new("Temperatur",
                Phase(serie, true, ziele.Tag.TempMax, m => m.Temp, v => v <= ziele.Tag.TempMax, "≤ {0} °C"),
                Phase(serie, false, ziele.Nacht.TempMax, m => m.Temp, v => v <= ziele.Nacht.TempMax, "≤ {0} °C")),
            new("VPD",
                VpdPhase(serie, true, ziele.Tag),
                VpdPhase(serie, false, ziele.Nacht)),
        ];
    }

    private static ZielPhase Phase(
        IReadOnlyList<ZeltMinute> serie, bool tag, double? ziel, Func<ZeltMinute, double?> wert, Func<double, bool> imZiel, string zielText)
    {
        if (ziel is null) return new ZielPhase(null, null, ZielUrteil.Unbekannt, 0);
        var punkte = serie.Where(m => m.Tag == tag && wert(m) is not null).Select(m => wert(m)!.Value).ToList();
        return Urteil(zielText.Replace("{0}", Z(ziel.Value)), punkte.Count, punkte.Count(imZiel));
    }

    private static ZielPhase VpdPhase(IReadOnlyList<ZeltMinute> serie, bool tag, ZielBand ziel)
    {
        if (ziel.VpdMin is null && ziel.VpdMax is null) return new ZielPhase(null, null, ZielUrteil.Unbekannt, 0);
        var unten = ziel.VpdMin - VpdToleranz;
        var oben = ziel.VpdMax + VpdToleranz;
        var punkte = serie.Where(m => m.Tag == tag && m.Vpd is not null).Select(m => m.Vpd!.Value).ToList();
        var text = ziel.VpdMin is { } a && ziel.VpdMax is { } b
            ? (Math.Abs(a - b) < 1e-9 ? $"≈ {Z(a)} kPa" : $"{Z(a)}–{Z(b)} kPa")
            : ziel.VpdMin is { } nur ? $"≥ {Z(nur)} kPa" : $"≤ {Z(ziel.VpdMax!.Value)} kPa";
        return Urteil(text, punkte.Count, punkte.Count(v => (unten is null || v >= unten) && (oben is null || v <= oben)));
    }

    private static ZielPhase Urteil(string zielText, int minuten, int imZiel)
    {
        if (minuten < MindestMinuten) return new ZielPhase(zielText, null, ZielUrteil.Unbekannt, minuten);
        var anteil = 100.0 * imZiel / minuten;
        var urteil = anteil >= ErreichbarAb ? ZielUrteil.Erreichbar : anteil >= KnappAb ? ZielUrteil.Knapp : ZielUrteil.Luecke;
        return new ZielPhase(zielText, Math.Round(anteil, 1), urteil, minuten);
    }

    // ------------------------------------------------------------------ Wirkung

    /// <summary>Wirkung je Gerät und Lichtphase: der Mittelwert über alle brauchbaren Läufe.</summary>
    public static IReadOnlyList<WirkungZeile> Wirkung(IReadOnlyList<ProbelaufLauf> laeufe, Func<string, string> titel)
    {
        var brauchbar = laeufe
            .Where(l => l.Status is ProbelaufStatus.Fertig or ProbelaufStatus.Abgebrochen
                        && l.TagPhaseBeiStart is not null && l.Auswertung is not null
                        && l.EingriffEndeUtc is { } ende && (ende - l.StartUtc).TotalMinutes >= MindestEingriffMinuten)
            .ToList();

        var zeilen = new List<WirkungZeile>();
        foreach (var gruppe in brauchbar.GroupBy(l => (l.Modul, Tag: l.TagPhaseBeiStart!.Value))
                     .OrderBy(g => g.Key.Modul, StringComparer.Ordinal).ThenByDescending(g => g.Key.Tag))
        {
            var werte = gruppe
                .SelectMany(l => l.Auswertung!.Kennzahlen)
                .GroupBy(k => k.Groesse)
                .Select(g =>
                {
                    var erholungen = g.Where(k => k.ErholungMinuten is not null).Select(k => k.ErholungMinuten!.Value).ToList();
                    return new WirkungWert(g.Key, Math.Round(g.Average(k => k.AenderungProMinute), 3), erholungen.Count > 0 ? Math.Round(erholungen.Average(), 1) : null);
                })
                .ToList();
            zeilen.Add(new WirkungZeile(gruppe.Key.Modul, titel(gruppe.Key.Modul), gruppe.Key.Tag, gruppe.Count(), werte));
        }
        return zeilen;
    }

    // ---------------------------------------------------------------- Abdeckung

    /// <summary>
    /// Je Gerät: wie viele brauchbare Läufe bei Licht an und aus. CO₂ wird nachts nicht dosiert — dort gibt es nichts zu messen.
    /// </summary>
    public static IReadOnlyList<AbdeckungZeile> Abdeckung(
        IReadOnlyList<ProbelaufLauf> laeufe, IEnumerable<string> module, Func<string, string> titel)
    {
        var wirkung = Wirkung(laeufe, titel);
        return module.Select(m => new AbdeckungZeile(
            m, titel(m),
            wirkung.Where(w => w.Modul == m && w.Tag).Sum(w => w.Laeufe),
            wirkung.Where(w => w.Modul == m && !w.Tag).Sum(w => w.Laeufe),
            TagMoeglich: true,
            NachtMoeglich: !string.Equals(m, "co2", StringComparison.OrdinalIgnoreCase))).ToList();
    }

    // ------------------------------------------------------------ Nächster Lauf

    /// <summary>
    /// Der nächste sinnvolle Lauf: die Kombination aus Gerät und Lichtphase ohne Messung, die zu einer Lücke im Zielabgleich passt.
    /// </summary>
    public static NaechsterLauf? Naechster(IReadOnlyList<AbdeckungZeile> abdeckung, IReadOnlyList<ZielZeile> ziele, int dauer = 10)
    {
        NaechsterLauf? bester = null;
        var besterWert = -1;
        foreach (var zeile in abdeckung)
        {
            foreach (var tag in new[] { true, false })
            {
                if (tag ? !zeile.TagMoeglich || zeile.LaeufeTag > 0 : !zeile.NachtMoeglich || zeile.LaeufeNacht > 0) continue;

                var luecke = ziele
                    .Select(z => (z.Groesse, Phase: tag ? z.Tag : z.Nacht))
                    .Where(p => p.Phase.Urteil is ZielUrteil.Luecke or ZielUrteil.Knapp)
                    .OrderByDescending(p => p.Phase.Urteil == ZielUrteil.Luecke).ThenBy(p => p.Phase.AnteilProzent)
                    .Cast<(string Groesse, ZielPhase Phase)?>().FirstOrDefault();

                var wert = luecke is null ? 1 : luecke.Value.Phase.Urteil == ZielUrteil.Luecke ? 3 : 2;
                if (wert <= besterWert) continue;
                besterWert = wert;
                var phaseText = tag ? "bei Licht an" : "bei Licht aus";
                var begruendung = luecke is { } l
                    ? $"Schließt eine Lücke: {l.Groesse} {phaseText} nur {Z(l.Phase.AnteilProzent ?? 0, "0")} % der Zeit im Ziel — und für {zeile.Titel} gibt es hier noch keine Messung."
                    : $"Für {zeile.Titel} {phaseText} gibt es noch keine Messung.";
                bester = new NaechsterLauf(zeile.Modul, zeile.Titel, tag, dauer, begruendung);
            }
        }
        return bester;
    }

    // ----------------------------------------------------------------- Hinweise

    /// <summary>Die Zielzeile heißt „Luftfeuchte", die Kennzahl des Laufs „Feuchte" — dieselbe Größe, zwei Namen.</summary>
    private static string KennzahlName(string zielGroesse) => zielGroesse == "Luftfeuchte" ? "Feuchte" : zielGroesse;

    /// <summary>
    /// Sätze zu jeder Lücke — mit dem Gerät, dessen gemessene Wirkung dort am größten ist. Das sind Hinweise, keine Einstellungen:
    /// was geändert wird, entscheidet der Nutzer.
    /// </summary>
    public static IReadOnlyList<string> Hinweise(IReadOnlyList<ZielZeile> ziele, IReadOnlyList<WirkungZeile> wirkung)
    {
        var sätze = new List<string>();
        foreach (var zeile in ziele)
        {
            foreach (var (tag, phase) in new[] { (true, zeile.Tag), (false, zeile.Nacht) })
            {
                if (phase.Urteil is not (ZielUrteil.Luecke or ZielUrteil.Knapp)) continue;
                var phaseText = tag ? "bei Licht an" : "bei Licht aus";
                var satz = $"{zeile.Groesse} {phaseText}: nur {Z(phase.AnteilProzent ?? 0, "0")} % der Zeit im Ziel ({phase.ZielText}).";

                var staerkste = wirkung.Where(w => w.Tag == tag)
                    .SelectMany(w => w.Werte.Where(v => v.Groesse == KennzahlName(zeile.Groesse)).Select(v => (w.Titel, v.ProMinute)))
                    .OrderByDescending(x => Math.Abs(x.ProMinute)).Cast<(string Titel, double ProMinute)?>().FirstOrDefault();
                satz += staerkste is { } s
                    ? $" Gemessen wirkt {s.Titel} dort am stärksten ({(s.ProMinute > 0 ? "+" : "−")}{Z(Math.Abs(s.ProMinute), "0.###")} je Minute, wenn aus) — dort lässt sich ansetzen."
                    : " Für diese Lichtphase fehlt noch ein Probelauf, der zeigt, was dort wirkt.";
                sätze.Add(satz);
            }
        }
        return sätze;
    }
}
