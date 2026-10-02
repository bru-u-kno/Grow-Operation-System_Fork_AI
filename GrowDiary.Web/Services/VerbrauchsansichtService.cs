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
    /// <param name="growId">Der Grow, den die Seite zeigt — nur für <see cref="Spanne.DieserGrow"/>.</param>
    public Ansicht Zusammenstellen(
        int artikelId, Spanne spanne, DateTime? von, DateTime? bis, int? growId)
        => Zusammenstellen(
            artikelId, _kosten.GetArtikel(artikelId), _kosten.GetNachfuellungen(artikelId), _kosten.GetVerbraeuche(artikelId),
            spanne, von, bis, growId, DateTime.UtcNow, TimeZoneInfo.Local);

    /// <summary>Die reine Rechnung — ohne Datenbank und mit ausdrücklicher Zeitzone, damit sie prüfbar ist.</summary>
    /// <remarks>
    /// <para><b>Tage sind Ortstage (02.10.2026).</b> Die 7- und 30-Tage-Grenze
    /// und das Datum der Zeile waren UTC-Tage: eine Buchung um 00:30 Ortszeit
    /// stand am Vortag, und „7 Tage" begann um 02:00 statt um Mitternacht. Die
    /// CO₂-Steuerung bucht abends nach Licht aus — im Sommer liegt das in Berlin
    /// zwei Stunden vor dem UTC-Tageswechsel, im Winter eine.</para>
    /// <para><b>„Dieser Grow" ohne Grow zeigt nichts.</b> Vorher fiel der Filter
    /// weg, und die Zeile hieß „Dieser Grow", zeigte aber jede Buchung.</para>
    /// </remarks>
    public static Ansicht Zusammenstellen(
        int artikelId, Verbrauchsartikel? artikel, IReadOnlyList<Nachfuellung> fuellungenRoh, IReadOnlyList<Verbrauch> alle,
        Spanne spanne, DateTime? von, DateTime? bis, int? growId, DateTime jetztUtc, TimeZoneInfo zone)
    {
        var name = artikel?.Name ?? $"Artikel {artikelId}";
        var einheit = artikel?.Einheit ?? string.Empty;

        var (start, ende) = Grenzen(spanne, von, bis, jetztUtc, zone);

        // Preis je Einheit aus den Füllungen: die jüngste Füllung vor der
        // Buchung hat sie bezahlt. Der Artikelpreis ist nur der Rückfall.
        var fuellungen = fuellungenRoh
            .OrderBy(n => n.ZeitpunktUtc)
            .ToList();

        var zeilen = new List<Zeile>();
        var fehlenderPreis = false;

        foreach (var v in alle)
        {
            if (start is { } s && v.ZeitpunktUtc < s) continue;
            if (ende is { } e && v.ZeitpunktUtc >= e) continue;
            if (spanne == Spanne.DieserGrow && (growId is null || v.GrowId != growId)) continue;

            var preis = PreisJeEinheit(fuellungen, v.ZeitpunktUtc, artikel);
            if (preis is null) fehlenderPreis = true;

            zeilen.Add(new Zeile(
                v.Id,
                Ortsdatum(v.ZeitpunktUtc, zone).ToString("yyyy-MM-dd"),
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

    /// <summary>Die Grenzen einer Spanne, in UTC — Tage sind Ortstage des Add-ons.</summary>
    public static (DateTime? Von, DateTime? Bis) Grenzen(Spanne spanne, DateTime? von, DateTime? bis)
        => Grenzen(spanne, von, bis, DateTime.UtcNow, TimeZoneInfo.Local);

    /// <summary>Die Grenzen einer Spanne, in UTC, gerechnet in <paramref name="zone"/>.</summary>
    public static (DateTime? Von, DateTime? Bis) Grenzen(Spanne spanne, DateTime? von, DateTime? bis, DateTime jetztUtc, TimeZoneInfo zone)
    {
        var heute = Ortsdatum(jetztUtc, zone);
        return spanne switch
        {
            Spanne.SiebenTage => (TagesbeginnUtc(heute.AddDays(-6), zone), null),
            Spanne.DreissigTage => (TagesbeginnUtc(heute.AddDays(-29), zone), null),
            Spanne.DieserGrow => (null, null),
            Spanne.Alles => (null, null),
            Spanne.Eigen => (von, bis),
            _ => (null, null),
        };
    }

    private static DateTime Ortsdatum(DateTime utc, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).Date;

    private static DateTime TagesbeginnUtc(DateTime ortsdatum, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(ortsdatum.Date, DateTimeKind.Unspecified), zone);

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
        // Unabhängig von der Reihenfolge des Aufrufers: die jüngste Füllung bis
        // zum Zeitpunkt, sonst die älteste überhaupt. Vorher LastOrDefault auf
        // eine aufsteigende Liste — die Kostenseite reicht sie aber absteigend
        // herein (KostenRepository: ORDER BY ZeitpunktUtc DESC) und bewertete
        // damit jeden Verbrauch zum Preis der ÄLTESTEN Füllung.
        var passend = fuellungen.Where(n => n.ZeitpunktUtc <= zeitpunkt).MaxBy(n => n.ZeitpunktUtc)
                      ?? fuellungen.MinBy(n => n.ZeitpunktUtc);
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
