using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public static partial class Demobestand
{
    /// <summary>Ein Mutter- und ein Quarantäne-Bereich — jeder in seinem eigenen Zelt.</summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.10.2026).</b> <c>GET /api/setups</c> lieferte im
    /// Testbestand <c>[]</c>. Die Bereichs-Karte auf der Zeltseite rendert aber
    /// nur, wenn es Bereiche gibt — sie war im Bestand nie zu sehen. Die
    /// Prüfung vom 01.10.2026 hat ihre rohen Wörter („Mother", „Active",
    /// „Pending") repariert, ohne dass jemand die Karte ansehen konnte.</para>
    ///
    /// <para><b>Warum zwei neue Zelte statt des Blütezelts.</b> Das Blütezelt ist
    /// <see cref="TentType.Production"/>, und
    /// <see cref="SetupTentCompatibilityPolicy"/> lässt dort nur
    /// Production-Bereiche zu. Es auf <see cref="TentType.MultiPurpose"/>
    /// umzustellen hätte die Regel umgangen, aber nicht die Sache: eine Mutter
    /// unter 12/12 geht in die Blüte. Mutterpflanzen stehen unter 18/6, und eine
    /// Quarantäne steht getrennt von den Müttern — sonst schützt sie nichts.</para>
    ///
    /// <para><b>Die Stecklinge gehen den Weg der App.</b>
    /// <see cref="SetupRepository.CreateCloneFromMother"/> zählt
    /// <c>CloneCounterTotal</c> hoch und setzt <c>LastCloneCutAt</c> — genau
    /// wie <c>POST /api/plants/clone-from-mother</c>. Von Hand gesetzte Zähler
    /// könnten den Stecklingen widersprechen, die wirklich dastehen. Als Ziel
    /// erlaubt die App nur einen Quarantäne-Bereich; dorthin gehen sie.</para>
    ///
    /// <para><b>Ein Zugang ohne Sorte.</b> Ein Steckling von außen, dessen Sorte
    /// niemand sicher weiß — der Fall „ohne Sorte" auf der Karte, und der
    /// eigentliche Grund für eine Quarantäne.</para>
    /// </remarks>
    private static void BereicheAnlegen(
        GrowRepository grows, SetupRepository setups, int hauptSorteId, int zweiteSorteId)
    {
        var mutterzelt = grows.CreateTent(new Tent
        {
            Name = "Mutterzelt (Testdaten)",
            TentType = TentType.Mother,
            Status = TentStatus.Active,
            WidthCm = 60,
            DepthCm = 60,
            TentHeightCm = 160,
            LightType = "LED",
            LightWatt = 100,
            DisplayOrder = 2,
            Notes = "Testdaten — dieses Zelt gibt es nicht. Mütter unter 18/6.",
        });

        var quarantaenezelt = grows.CreateTent(new Tent
        {
            Name = "Quarantänezelt (Testdaten)",
            TentType = TentType.Quarantine,
            Status = TentStatus.Active,
            WidthCm = 40,
            DepthCm = 40,
            TentHeightCm = 120,
            LightType = "LED",
            LightWatt = 35,
            DisplayOrder = 3,
            Notes = "Testdaten — dieses Zelt gibt es nicht. Getrennt von den Müttern.",
        });

        // 18/6 für beide. Licht aus fällt auf dieselbe Stunde wie im Blütezelt:
        // DemoData liefert JEDEM Zelt denselben Lichtsensor (Demoverlauf, 12/12),
        // und der Sensor schlägt den Plan (LightClock.Resolve). Mit gleichem
        // Ende widersprechen sich Plan und Sensor nur morgens, nicht abends.
        foreach (var (zeltId, name) in new[]
                 {
                     (mutterzelt.Id, "Mütter 18/6 (Testdaten)"),
                     (quarantaenezelt.Id, "Quarantäne 18/6 (Testdaten)"),
                 })
        {
            grows.CreateLightSchedule(new LightSchedule
            {
                TentId = zeltId,
                Name = name,
                IsActive = true,
                LightsOnTime = $"{(Demoverlauf.LichtAus - 18 + 24) % 24:00}:00",
                LightsOffTime = Demoverlauf.LichtAusUhr,
                Source = LightSource.Manual,
            });
        }

        // --- Mutter-Bereich ---------------------------------------------------
        var muetter = setups.CreateSetup(new Setup
        {
            TentId = mutterzelt.Id,
            Name = "Mütter (Testdaten)",
            SetupType = SetupType.Mother,
            Status = SetupStatus.Active,
            // Einer der drei Werte, die SetupsApiController annimmt.
            MotherHealthStatus = "Stable",
            Notes = "Testdaten: zwei Mütter, alle drei Wochen zurückgeschnitten.",
        });

        var mutterWhiteWidow = setups.CreatePlant(new PlantInstance
        {
            SetupId = muetter.Id,
            StrainId = hauptSorteId,
            Label = "Mutter White Widow",
            PlantRole = PlantRole.Mother,
            PlantStatus = PlantStatus.Active,
            PhenoLabel = "Pheno A",
            StartedAt = Tag(140),
            Notes = "Testdaten.",
        });

        setups.CreatePlant(new PlantInstance
        {
            SetupId = muetter.Id,
            StrainId = zweiteSorteId,
            Label = "Mutter Gorilla Glue",
            PlantRole = PlantRole.Mother,
            PlantStatus = PlantStatus.Active,
            StartedAt = Tag(160),
            Notes = "Testdaten.",
        });

        // --- Quarantäne-Bereich -----------------------------------------------
        // Vierzehn Tage, davon sechs um: der Bereich ist mitten in der Prüfung,
        // das Ergebnis steht noch aus.
        var geschnitten = Tag(6);
        var quarantaene = setups.CreateSetup(new Setup
        {
            TentId = quarantaenezelt.Id,
            Name = "Quarantäne (Testdaten)",
            SetupType = SetupType.Quarantine,
            Status = SetupStatus.Active,
            QuarantineStartedAt = geschnitten,
            QuarantinePlannedEndAt = geschnitten.AddDays(14),
            // Einer der drei Werte, die SetupsApiController annimmt.
            QuarantineResult = "Pending",
            Notes = "Testdaten: Gelbtafeln hängen, täglich Blattunterseiten ansehen.",
        });

        for (var nummer = 1; nummer <= 3; nummer += 1)
        {
            setups.CreateCloneFromMother(new PlantInstance
            {
                StrainId = mutterWhiteWidow.StrainId,
                SetupId = quarantaene.Id,
                ParentPlantId = mutterWhiteWidow.Id,
                Label = $"Steckling White Widow {nummer}",
                PlantRole = PlantRole.Clone,
                PlantStatus = PlantStatus.Active,
                PhenoLabel = mutterWhiteWidow.PhenoLabel,
                StartedAt = geschnitten,
                Notes = "Testdaten: bewurzelt im Steinwollwürfel.",
            }, muetter.Id, geschnitten);
        }

        setups.CreatePlant(new PlantInstance
        {
            SetupId = quarantaene.Id,
            StrainId = null,
            Label = "Zugang Tauschsteckling",
            PlantRole = PlantRole.Quarantine,
            PlantStatus = PlantStatus.Active,
            StartedAt = geschnitten,
            Notes = "Testdaten: von außen, Sorte unbekannt — deshalb die Quarantäne.",
        });
    }
}
