using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public static partial class Demobestand
{
    /// <summary>Eine CO₂-Flasche, deren laufende Füllung gebuchten Verbrauch hat.</summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.10.2026).</b> Der Testbestand hatte keinen
    /// einzigen Verbrauchsartikel. Den Füllstand-Balken auf der Kostenseite —
    /// „Noch … %" und „leer ≈ … aus dem gebuchten Verbrauch" — hatte deshalb nie
    /// jemand gesehen, auch nicht der, der ihn zuletzt repariert hat.</para>
    ///
    /// <para><b>Woraus die Seite das rechnet</b>
    /// (<see cref="KostenSeiteService"/>, <c>ArtikelBerechnen</c>): Füllstand =
    /// 1 − gebuchter Verbrauch seit der offenen Füllung ÷ deren Menge; eine
    /// Prognose gibt es erst, wenn mindestens ein Zwanzigstel verbraucht ist UND
    /// zwei Wochen vergangen sind. Unter dieser Schwelle sagt die Seite selbst,
    /// dass zu wenig da ist.</para>
    ///
    /// <para><b>Die Mengen.</b> Eine 10-kg-Tauschflasche, seit 32 Tagen offen,
    /// vier Wochenbuchungen mit zusammen 5,0 kg. Je Woche rund 45 min Ventil am
    /// Tag bei 2 L/min — 90 L CO₂ zu je 1,98 g, also gut 1,2 kg die Woche. Die
    /// vorige Füllung hielt 69 Tage, darin auch die Wochen zwischen zwei
    /// Grows ohne Begasung.</para>
    ///
    /// <para><b>Gebucht wird aufs Durchgangskonto</b> (<c>AufGrowBuchen</c>):
    /// eine Flasche reicht über mehr als einen Grow, also trifft nur der
    /// Verbrauch den Lauf — so beschreibt es das Modell selbst für genau diesen
    /// Artikel.</para>
    ///
    /// <para><b>Nachfüllen wie die App.</b> <c>POST /api/kosten/nachfuellungen</c>
    /// schließt die offene Füllung zum neuen Zeitpunkt, ordnet dem laufenden
    /// Grow zu und schreibt einen Journal-Eintrag — der Bestand tut dasselbe,
    /// sonst stünde im Journal kein Wort von einer Flasche, die die Kostenseite
    /// kennt.</para>
    /// </remarks>
    private static void Co2FlascheAnlegen(
        KostenRepository kosten, JournalRepository journal, int zeltId, int laufenderGrowId, int vorigerGrowId)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        const double Flasche = 10;
        const double Preis = 39.00;

        var artikel = new Verbrauchsartikel
        {
            Name = "CO₂-Flasche (Testdaten)",
            Hersteller = "Linde",
            Produkt = "CO₂-Tauschflasche 10 kg",
            PreisEur = Preis,
            Einheit = "kg",
            Gebinde = Flasche,
            TentId = zeltId,
            Notiz = "Testdaten: Magnetventil mit Durchflussregler, 2 L/min.",
            Aktiv = true,
            AufGrowBuchen = true,
        };
        artikel.Id = kosten.CreateArtikel(artikel);

        var vorigeAm = Tag(101).ToUniversalTime();
        var laufendeAm = Tag(32).ToUniversalTime();

        kosten.CreateNachfuellung(new Nachfuellung
        {
            ArtikelId = artikel.Id,
            ZeitpunktUtc = vorigeAm,
            Menge = Flasche,
            KostenEur = Preis,
            GrowId = vorigerGrowId,
            // Leer genau dann, als die nächste kam — so schließt die App sie.
            LeerAmUtc = laufendeAm,
            Notiz = "Testdaten.",
        });

        kosten.CreateNachfuellung(new Nachfuellung
        {
            ArtikelId = artikel.Id,
            ZeitpunktUtc = laufendeAm,
            Menge = Flasche,
            KostenEur = Preis,
            GrowId = laufenderGrowId,
            Notiz = "Testdaten.",
        });

        var vorigeLaufzeit = Math.Round((laufendeAm - vorigeAm).TotalDays);
        foreach (var (growId, wann, text) in new[]
                 {
                     (vorigerGrowId, vorigeAm, $"{Flasche.ToString("0.##", de)} kg · {Preis.ToString("0.00", de)} €"),
                     (laufenderGrowId, laufendeAm,
                         $"{Flasche.ToString("0.##", de)} kg · {Preis.ToString("0.00", de)} € · vorherige Füllung hielt {vorigeLaufzeit} Tage"),
                 })
        {
            journal.Create(new JournalEntry
            {
                GrowId = growId,
                Title = $"{artikel.Name} nachgefüllt",
                Body = text,
                EntryType = JournalEntryType.Action,
                Source = ValueOrigin.Manual,
                OccurredAtUtc = wann,
            });
        }

        // Vier Wochenbuchungen, abends nach Licht aus. Die Mengen steigen
        // leicht: mit der Blüte wird das Ventil länger offen gehalten.
        var buchungen = new (int VorTagen, double Kg)[]
        {
            (25, 1.21),
            (18, 1.24),
            (11, 1.26),
            (4, 1.29),
        };

        foreach (var (vorTagen, kg) in buchungen)
        {
            kosten.CreateVerbrauch(new Verbrauch
            {
                ArtikelId = artikel.Id,
                GrowId = laufenderGrowId,
                ZeitpunktUtc = DateTime.Today.AddDays(-vorTagen).AddHours(Demoverlauf.LichtAus).AddMinutes(30).ToUniversalTime(),
                Menge = kg,
                Quelle = "manuell",
                Notiz = "Testdaten: Ventilzeit der Woche × 2 L/min, nicht gewogen.",
            });
        }
    }
}
