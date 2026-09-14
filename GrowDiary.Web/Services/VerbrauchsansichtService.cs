using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.77): Der Verbrauch eines Artikels über einen Zeitraum —
/// Menge, Kosten und woher die Buchung kam.
/// </summary>
/// <remarks>
/// <para><b>Warum hier und nicht in der Steuerung.</b> Die CO₂-Steuerung bucht
/// ihren Tagesverbrauch abends auf den Artikel. Dieselben Buchungen entstehen
/// von Hand oder aus dem Journal, für Dünger genauso wie für Gas. Eine Ansicht,
/// die nur CO₂ kann, müsste man für jeden weiteren Artikel neu bauen — also
/// steht sie bei den Kosten, wo die Buchungen ohnehin liegen.</para>
/// <para><b>Der laufende Tag fehlt hier absichtlich.</b> Er ist noch nicht
/// gebucht; sein Stand steht in der Steuerung. Zwei Zahlen mit zwei Wahrheiten
/// in eine Tabelle zu mischen macht beide unbrauchbar.</para>
/// <para><b>Der Preis kommt von der Füllung, nicht vom Artikel.</b> Eine
/// Nachfüllung kann teurer gewesen sein als die vorige; der Artikelpreis ist nur
/// der Rückfall für Altdaten, bei denen keine Füllung zuzuordnen ist.</para>
/// </remarks>
public sealed class VerbrauchsansichtService
{
    private readonly KostenRepository _kosten;

    public VerbrauchsansichtService(KostenRepository kosten) => _kosten = kosten;

    /// <summary>Ein Zeitraum, wie ihn die Seite anbietet.</summary>
    public enum Spanne
    {
        SiebenTage,
        DreissigTage,
        DieserGrow,
        Alles,
        Eigen,
    }

    public sealed record Zeile(
        /// <summary>
        /// forkai.104: Die Id der Buchung. Eine Zeile IST eine Buchung — ohne
        /// die Id kann die Oberflaeche sie nicht wieder entfernen, und der
        /// Loeschweg bleibt in der API haengen.
        /// </summary>
        int Id,
        string Datum,
        double Menge,
        double? Eur,
        string Quelle,
        string? Notiz,
        int? GrowId);

    public sealed record Ansicht(
        int ArtikelId,
        string Name,
        string Einheit,
        string Spanne,
        string? VonIso,
        string? BisIso,
        double SummeMenge,
        double SummeEur,
        int Buchungen,
        bool KostenVollstaendig,
        IReadOnlyList<Zeile> Zeilen);

    /// <summary>Den Verbrauch eines Artikels zusammenstellen.</summary>
    /// <param name="von">Untergrenze (UTC, einschließlich) — null für „von Anfang an".</param>
    /// <param name="bis">Obergrenze (UTC, ausschließlich) — null für „bis jetzt".</param>
    public Ansicht Zusammenstellen(
        int artikelId, Spanne spanne, DateTime? von, DateTime? bis, int? growId)
    {
        var artikel = _kosten.GetArtikel(artikelId);
        var name = artikel?.Name ?? $"Artikel {artikelId}";
        var einheit = artikel?.Einheit ?? string.Empty;

        var (start, ende) = Grenzen(spanne, von, bis);

        // Preis je Einheit aus den Füllungen: die jüngste Füllung vor der
        // Buchung hat sie bezahlt. Der Artikelpreis ist nur der Rückfall.
        var fuellungen = _kosten.GetNachfuellungen(artikelId)
            .OrderBy(n => n.ZeitpunktUtc)
            .ToList();

        var alle = _kosten.GetVerbraeuche(artikelId);
        var zeilen = new List<Zeile>();
        var fehlenderPreis = false;

        foreach (var v in alle)
        {
            if (start is { } s && v.ZeitpunktUtc < s) continue;
            if (ende is { } e && v.ZeitpunktUtc >= e) continue;
            if (spanne == Spanne.DieserGrow && growId is { } g && v.GrowId != g) continue;

            var preis = PreisJeEinheit(fuellungen, v.ZeitpunktUtc, artikel);
            if (preis is null) fehlenderPreis = true;

            zeilen.Add(new Zeile(
                v.Id,
                v.ZeitpunktUtc.ToString("yyyy-MM-dd"),
                v.Menge,
                preis is { } p ? Math.Round(v.Menge * p, 2) : null,
                v.Quelle,
                v.Notiz,
                v.GrowId));
        }

        return new Ansicht(
            artikelId,
            name,
            einheit,
            spanne.ToString(),
            start?.ToString("o"),
            ende?.ToString("o"),
            Math.Round(zeilen.Sum(z => z.Menge), 4),
            Math.Round(zeilen.Sum(z => z.Eur ?? 0), 2),
            zeilen.Count,
            !fehlenderPreis,
            zeilen);
    }

    /// <summary>Die Grenzen einer Spanne, in UTC.</summary>
    public static (DateTime? Von, DateTime? Bis) Grenzen(Spanne spanne, DateTime? von, DateTime? bis)
        => spanne switch
        {
            Spanne.SiebenTage => (DateTime.UtcNow.Date.AddDays(-6), null),
            Spanne.DreissigTage => (DateTime.UtcNow.Date.AddDays(-29), null),
            Spanne.DieserGrow => (null, null),
            Spanne.Alles => (null, null),
            Spanne.Eigen => (von, bis),
            _ => (null, null),
        };

    /// <summary>
    /// Was eine Einheit zum Zeitpunkt der Buchung gekostet hat.
    /// </summary>
    /// <remarks>
    /// Maßgeblich ist die jüngste Füllung, die zu dem Zeitpunkt schon im Lager
    /// lag — sie hat das Verbrauchte bezahlt. Gibt es keine, greift der
    /// Artikelpreis; gibt es auch den nicht, bleibt die Zeile ohne Betrag. Eine
    /// Null hinzuschreiben wäre schlimmer: Die Summe sähe vollständig aus und
    /// wäre zu niedrig.
    /// </remarks>
    public static double? PreisJeEinheit(
        IReadOnlyList<Nachfuellung> fuellungen, DateTime zeitpunkt, Verbrauchsartikel? artikel)
    {
        var passend = fuellungen.LastOrDefault(n => n.ZeitpunktUtc <= zeitpunkt) ?? fuellungen.FirstOrDefault();
        if (passend is { Menge: > 0 } f && f.KostenEur is { } kosten)
        {
            return kosten / f.Menge;
        }

        if (artikel is { PreisEur: { } preis, Gebinde: > 0 } a)
        {
            return preis / a.Gebinde!.Value;
        }

        return null;
    }
}
