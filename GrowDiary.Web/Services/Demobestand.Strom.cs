using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public static partial class Demobestand
{
    /// <summary>Strompreis im Testbestand, ct/kWh.</summary>
    private const double StrompreisCent = 32;

    /// <summary>Wie weit die Zählerstände im Testbestand zurückreichen, in Tagen.</summary>
    /// <remarks>
    /// Bis vor den Start des letzten abgeschlossenen Laufs (Gorilla Glue, vor
    /// 183 Tagen), aber nicht bis zum ersten (Northern Lights, geerntet vor 190):
    /// so zeigt das Archiv beide Fälle — einen Lauf mit gemessenem Strom und
    /// einen, für den nur die Schätzung bleibt.
    /// </remarks>
    private const int ZaehlerTage = 186;

    /// <summary>Der Name des zweiten Blütezelts — eine Stelle, damit <see cref="DemoData.LageFuer"/> ihn nicht abtippt.</summary>
    internal const string ZweitesBluetezeltName = "Blütezelt 2 (Testdaten)";

    /// <summary>
    /// Ein zweites Blütezelt mit eigenem laufenden Grow — am selben Zähler wie das erste.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (02.10.2026).</b> Der Testbestand hatte genau einen
    /// laufenden Grow. Dass ein zweiter, gleichzeitig laufender Grow auf der
    /// Kostenseite nie einen Cent Strom bekam, konnte daran niemand sehen — dieselbe
    /// Falle wie „ein Gerät, obwohl der Nutzer sieben hat".</para>
    ///
    /// <para><b>Warum kein eigener Zähler.</b> Beide Zelte hängen an einer
    /// Steckdosenleiste mit einem Zähler — so sieht die Kostenseite den Fall, in
    /// dem sie teilen muss. Den eigenen Zähler je Zelt prüfen die Tests ohne
    /// Datenbank (<c>KostenMehrereGrowsTests</c>), und er lässt sich in der
    /// laufenden App über „Strom-Quelle einstellen" eintragen.</para>
    ///
    /// <para><b>In der Blüte, unter demselben Licht.</b> Der Testbestand liefert
    /// jedem Zelt denselben Lichtsensor (12/12). Ein Grow im Wachstum darunter
    /// widerspräche der eigenen Regel der App
    /// (<see cref="LightCycleLearner.Mismatch"/>).</para>
    /// </remarks>
    private static GrowRun ZweitesZeltAnlegen(
        GrowRepository grows, HydroSetupRepository hydro, SetupRepository setups, int sorteId)
    {
        var zelt = grows.CreateTent(new Tent
        {
            Name = ZweitesBluetezeltName,
            TentType = TentType.Production,
            Status = TentStatus.Active,
            WidthCm = 80,
            DepthCm = 80,
            TentHeightCm = 180,
            LightType = "LED",
            LightWatt = DemoData.LedZelt2W,
            DisplayOrder = 4,
            Notes = "Testdaten — dieses Zelt gibt es nicht. Hängt am selben Zähler wie das Blütezelt.",
        });

        grows.CreateLightSchedule(new LightSchedule
        {
            TentId = zelt.Id,
            Name = "Blüte 12/12 (Testdaten)",
            IsActive = true,
            LightsOnTime = Demoverlauf.LichtAnUhr,
            LightsOffTime = Demoverlauf.LichtAusUhr,
            Source = LightSource.Manual,
        });

        var system = hydro.CreateHydroSetup(new GrowSystem
        {
            TentId = zelt.Id,
            Name = "DWC 2er Blütezelt 2 (Testdaten)",
            HydroStyle = Models.HydroStyle.DWC.ToString(),
            PotCount = 2,
            PotSizeLiters = 19,
            ReservoirLiters = 38,
            // Dieselben Werte, die die Normalisierung für DWC erzwingt — siehe ZweiterAufbauAnlegen.
            LayoutType = HydroSetupLayoutType.SingleBucket,
            ReservoirPosition = ReservoirPosition.None,
            Status = HydroSetupStatus.Active,
            HasCirculationPump = false,
            HasAirPump = true,
            AirPumpLitersPerHour = 1800,
            AirStoneCount = 2,
            HasChiller = false,
            DisplayOrder = 3,
        });

        // Gestartet VOR der White Widow (Tag 85 gegen 73). Mehrere Stellen der App
        // nehmen bei mehreren laufenden Grows einfach „den neuesten"
        // (GetActiveGrows sortiert nach Start absteigend; so wählen Zielwerte und
        // die CRUD-Rundweg-Prüfung). Wäre dieser hier der neueste, stünde auf
        // /zielwerte das leere zweite Zelt. Das ist ein Befund über die App, nicht
        // über die Kosten — der Bestand hält den Haupt-Grow dort, wo er war.
        // Flip vor 57 Tagen: über den zehn Übergangstagen, vor dem Finish (bei
        // 11 Wochen Blüte ab Tag 63 nach dem Flip) — und in Blütewoche 9. Das
        // Programm des Testbestands (Demobestand.Programm) führt acht Blütewochen:
        // dieser Lauf blüht länger als sein Programm, sein Plan bekommt eine
        // angehängte Woche (Planwochen.Anhaengen). Ohne diesen Fall stünde die
        // verlängerte laufende Woche nirgends im Bestand (bis 02.10.2026: Tag 50).
        var grow = new GrowRun
        {
            TentId = zelt.Id,
            SystemId = system.Id,
            Name = "Gorilla Glue Zelt 2 (Testdaten)",
            Strain = "Gorilla Glue #4",
            StrainId = sorteId,
            Breeder = "GG Strains",
            Status = GrowStatus.Running,
            MediumType = MediumType.Hydro,
            HydroStyle = Models.HydroStyle.DWC,
            IrrigationType = IrrigationType.ActiveHydro,
            Environment = GrowEnvironment.Indoor,
            WaterSource = WaterSource.Tap,
            SeedType = Models.SeedType.Feminized,
            StartMaterial = StartMaterial.Seed,
            EntryPoint = GrowEntryPoint.Germination,
            PlantCount = 2,
            StartDate = Tag(85),
            GerminatedAt = Tag(85),
            VegStartedAt = Tag(78),
            FlipDate = Tag(57),
            BreederFlowerWeeksMin = 9,
            BreederFlowerWeeksMax = 11,
            Light = $"LED {DemoData.LedZelt2W} W",
            ReservoirSize = "38 L",
            Notes = "Testdaten: zweiter laufender Lauf, gleichzeitig mit dem im Blütezelt.",
        };
        grow.Id = grows.CreateGrow(grow);

        // So viele Pflanzen, wie der Grow behauptet — je Topf eine.
        for (var topf = 1; topf <= grow.PlantCount; topf++)
        {
            setups.CreatePlant(new PlantInstance
            {
                GrowId = grow.Id,
                StrainId = sorteId,
                SiteIndex = topf,
                Label = $"Pflanze {topf}",
                PlantRole = PlantRole.Production,
                PlantStatus = PlantStatus.Active,
                StartedAt = Tag(85),
                Notes = "Testdaten.",
            });
        }

        return grow;
    }

    /// <summary>Strom-Quelle, Strompreis und tägliche Zählerstände.</summary>
    /// <remarks>
    /// <para>Ein Stand je Tag um Mitternacht Ortszeit, wie ihn der Worker
    /// festhält, aus <see cref="DemoData.StromZaehlerKwh"/> — derselben Funktion,
    /// die der Worker im Testbetrieb liest. Die Stände gehören dem Zähler; Grow
    /// und Phase am Stand sind die Notiz, die der Worker dazuschreibt (der älteste
    /// Grow, der an dem Tag läuft).</para>
    /// <para>Gesetzt über dieselben Wege wie in der App
    /// (<see cref="KostenSeiteService.StromQuelleSchreiben"/>,
    /// <see cref="GrowCostService.StrompreisSchreiben"/>) — keine abgetippten Schlüssel.</para>
    /// </remarks>
    private static int StromAnlegen(AppSettingsRepository einstellungen, KostenRepository kosten, GrowRepository grows)
    {
        KostenSeiteService.StromQuelleSchreiben(einstellungen, new StromQuelle
        {
            ZaehlerEntityId = DemoData.StromZaehler,
            LeistungEntityId = DemoData.StromLeistung,
            Zelte = [],
        });
        GrowCostService.StrompreisSchreiben(einstellungen, StrompreisCent);

        var alle = grows.GetAllGrows();
        var heute = DateTime.Today;
        var angelegt = 0;
        for (var vorTagen = ZaehlerTage; vorTagen >= 0; vorTagen--)
        {
            var tag = heute.AddDays(-vorTagen);
            var zeitpunkt = DateTime.SpecifyKind(tag, DateTimeKind.Local).ToUniversalTime();
            var laufend = alle
                .Where(g => StromAufteilung.Laufzeit(g, heute) is { } lz && tag >= lz.Von && tag <= lz.Bis)
                .OrderBy(g => g.StartDate)
                .FirstOrDefault();

            kosten.CreateZaehlerstand(new Zaehlerstand
            {
                ZeitpunktUtc = zeitpunkt,
                Kwh = Math.Round(DemoData.StromZaehlerKwh(zeitpunkt), 3),
                Anlass = vorTagen == ZaehlerTage ? ZaehlerAnlass.Manuell : ZaehlerAnlass.Tag,
                GrowId = laufend?.Id,
                Phase = laufend is null ? null : GrowStageResolver.Resolve(laufend, tag).ToString(),
                ZaehlerEntityId = DemoData.StromZaehler,
            });
            angelegt++;
        }

        return angelegt;
    }
}
