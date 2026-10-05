using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public static partial class Demobestand
{
    /// <summary>
    /// Die Dünger des Programms als Verbrauchsartikel, dazu Leitungswasser und ein Wasserprofil.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (A-006, 05.10.2026).</b> Der Wasserwechsel-Ablauf
    /// schlägt je Zeile des Mischplans eine Menge vor und bucht sie auf den
    /// passenden Kosten-Artikel. Der Bestand hatte nur eine CO₂-Flasche — jede
    /// Zeile hätte „kein Artikel" gezeigt, und der Weg „Vorschlag → Buchung"
    /// wäre in der Testdaten-App nie gelaufen. Ohne Wasserprofil gäbe es
    /// keinen Wasser-EC und keine CalMag-Regel zu sehen.</para>
    ///
    /// <para><b>Die Namen</b> sind die Komponenten von
    /// <see cref="Programm"/> (SKX Canna Aqua) — so findet der Vorschlag sie
    /// über <see cref="MischplanVorschlagRechnung.ArtikelFuer"/>, ohne Zuordnung
    /// von Hand. pH- und Purolyt stehen nicht im Plan: sie kommen über
    /// „+ Produkt".</para>
    ///
    /// <para><b>Das Wasserprofil</b> hat mittlere Härte mit Calcium — damit
    /// greift „Leitungswasser bringt Calcium mit, kein CalMag". Die Zahlen sind
    /// Testdaten in der Größenordnung eines deutschen Stadtwerks, kein echter
    /// Bericht.</para>
    /// </remarks>
    private static void DuengerUndWasserAnlegen(KostenRepository kosten, WaterProfileStore wasserprofil, int laufenderGrowId)
    {
        var artikel = new (string Name, string Hersteller, string Produkt, string Einheit, double Gebinde, double Preis)[]
        {
            ("CalMag Agent", "Canna", "CalMag Agent", "ml", 1000, 14.90),
            ("Aqua Flores A", "Canna", "Aqua Flores A", "ml", 5000, 39.90),
            ("Aqua Flores B", "Canna", "Aqua Flores B", "ml", 5000, 39.90),
            ("PK 13/14", "Canna", "PK 13/14", "ml", 1000, 16.90),
            ("Cannaboost", "Canna", "Cannaboost Accelerator", "ml", 1000, 44.90),
            ("pH- Pro Bloom", "Canna", "pH- Pro Bloom", "ml", 1000, 8.90),
            ("Purolyt", "Purolyt", "Purolyt Konzentrat", "ml", 5000, 49.90),
            (WasserwechselVorgangRepository.LeitungswasserArtikel, "Stadtwerke", "Trinkwasser", "L", 1000, 4.50),
        };

        foreach (var (name, hersteller, produkt, einheit, gebinde, preis) in artikel)
        {
            var id = kosten.CreateArtikel(new Verbrauchsartikel
            {
                Name = name,
                Hersteller = hersteller,
                Produkt = produkt,
                Einheit = einheit,
                Gebinde = gebinde,
                PreisEur = preis,
                Aktiv = true,
                AufGrowBuchen = true,
                Notiz = "Testdaten.",
            });

            kosten.CreateNachfuellung(new Nachfuellung
            {
                ArtikelId = id,
                ZeitpunktUtc = Tag(32).ToUniversalTime(),
                Menge = gebinde,
                KostenEur = preis,
                GrowId = laufenderGrowId,
                Notiz = "Testdaten.",
            });
        }

        wasserprofil.Save(new WaterProfile
        {
            SourceLabel = "Testdaten-Stadtwerk",
            ConductivityUsCm = 500,
            Ph = 7.6,
            TotalHardnessDh = 11.2,
            CarbonateHardnessDh = 8.0,
            CalciumMgL = 66.7,
            MagnesiumMgL = 8.1,
        });
    }
}
