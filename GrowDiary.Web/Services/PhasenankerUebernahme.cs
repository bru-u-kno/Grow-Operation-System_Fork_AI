using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Einmalig: wo die bisherige Rechnung einen Phasenbeginn nur geschätzt hat,
/// wird die Schätzung als bestätigter Beginn eingetragen.
/// </summary>
/// <remarks>
/// <para><b>Warum.</b> Seit dem 02.10.2026 beginnt die Vegi (und bei der
/// Autoflower die Blüte) erst mit einer Bestätigung — siehe
/// <see cref="Phasenanker"/>. Ein laufender Grow, den die alte Rechnung längst
/// in der Vegi sah, stünde nach dem Update plötzlich wieder in der Anzucht; ein
/// abgeschlossener verlöre seine Vegi-Wochen in der Auswertung. Deshalb wird
/// für jeden Grow, dessen Beginn nie bestätigt wurde, EINMAL der Tag
/// eingetragen, den die alte Rechnung genannt hätte — aber nur, wenn sie die
/// Phase am Stichtag (heute, bei abgeschlossenen Läufen der Erntetag) schon
/// erreicht sah. Jeder Eintrag bekommt eine Zeile im Journal.</para>
///
/// <para><b>Wann.</b> Beim Start, direkt nach dem Schema, vor allem, was
/// Phasen liest. Das Merkmal steht in den AppSettings der Datenbank selbst:
/// eine zurückgespielte ältere Sicherung hat es nicht und wird beim nächsten
/// Start ebenfalls übernommen. Eine neue Datenbank setzt es beim ersten Start
/// (es gibt nichts zu übernehmen) — der Demobestand entsteht danach und trägt
/// seine Beginne selbst.</para>
/// </remarks>
public static class PhasenankerUebernahme
{
    public const string Merkmal = "phasenanker:schaetzung-uebernommen";

    /// <summary>Was übernommen würde — rein, ohne Datenbank.</summary>
    /// <returns>Der einzutragende Vegi-Beginn und/oder Blütebeginn; beide null, wenn nichts zu tun ist.</returns>
    public static (DateTime? VegAb, DateTime? BlueteAb) Vorschlag(GrowRun grow, DateTime heute)
    {
        var bezug = grow.EndDate?.Date is { } ende && ende < heute.Date ? ende : heute.Date;
        var stand = Phasenanker.Fuer(grow, bezug);

        DateTime? bluete = null;
        if (stand.BlueteAb is null && AlterBluetenBeginn(grow) is { } b && b <= bezug)
        {
            bluete = b;
        }

        DateTime? veg = null;
        if (stand.VegAb is null && AlterVegBeginn(grow) is { } v && v <= bezug)
        {
            var obergrenze = stand.BlueteAb ?? bluete;
            veg = obergrenze is { } o && o < v ? o : v;
        }

        return (veg, bluete);
    }

    /// <summary>Übernimmt die Schätzungen — einmal je Datenbank.</summary>
    /// <returns>Wie viele Grows einen Beginn bekommen haben.</returns>
    public static int Ausfuehren(
        GrowRepository grows, JournalRepository journal, AppSettingsRepository einstellungen, DateTime heute)
    {
        if (einstellungen.GetValue(Merkmal) is not null) return 0;

        var anzahl = 0;
        foreach (var id in grows.GetAllGrows().Select(g => g.Id).ToList())
        {
            // Einzeln nachladen: UpdateGrow ersetzt die ganze Zeile.
            if (grows.GetGrow(id) is not { } grow) continue;
            var (veg, bluete) = Vorschlag(grow, heute);
            if (veg is null && bluete is null) continue;

            if (veg is { } vegAb)
            {
                // Mittags: VegStartedAt geht über ToStorageUtc in die Datenbank,
                // ein lokales Mitternachtsdatum rutschte dort einen Tag zurück.
                grow.VegStartedAt = vegAb.Date.AddHours(12);
            }
            if (bluete is { } blueteAb)
            {
                grow.FlipDate = blueteAb.Date;
            }
            grows.UpdateGrow(grow);

            if (veg is { } v)
            {
                journal.Create(new JournalEntry
                {
                    GrowId = id,
                    EntryType = JournalEntryType.VegStarted,
                    Body = $"Vegi-Beginn {v:dd.MM.yyyy} aus der bisherigen Schätzung übernommen.",
                    Source = ValueOrigin.Derived,
                    OccurredAtUtc = v.Date.AddHours(12).ToUniversalTime(),
                });
            }
            if (bluete is { } bl)
            {
                journal.Create(new JournalEntry
                {
                    GrowId = id,
                    EntryType = JournalEntryType.FlipToFlower,
                    Body = $"Blütebeginn {bl:dd.MM.yyyy} aus der bisherigen Schätzung übernommen (Autoflower).",
                    Source = ValueOrigin.Derived,
                    OccurredAtUtc = bl.Date.AddHours(12).ToUniversalTime(),
                });
            }
            anzahl++;
        }

        einstellungen.SetValue(Merkmal, DateTime.UtcNow.ToString("O"));
        return anzahl;
    }

    // ------------------------------------------------------------------
    // Die alte Schätzung, eingefroren auf den Stand vom 02.10.2026
    // (GrowStageResolver vor dem Phasenanker). NUR für diese Übernahme —
    // die Zahlen stehen hier bewusst als eigene Literale: ändert jemand den
    // Richtwert der Erinnerung, darf sich die Übernahme nicht mitändern.
    // ------------------------------------------------------------------

    /// <summary>
    /// Ab welchem Tag die alte Rechnung „Veg" sagte, wenn niemand etwas
    /// bestätigt hatte; null, wo sie nie von selbst umschaltete (Klon).
    /// </summary>
    private static DateTime? AlterVegBeginn(GrowRun grow)
    {
        if (grow.StartMaterial == StartMaterial.Clone) return null;

        if (grow.SeedType == SeedType.Autoflower)
        {
            return AlteAutoflowerKeimung(grow).AddDays(14);
        }

        // Alter Pfad 5: Basis Bewurzelung/Keimung/Start, 14 Sämlingstage,
        // mitgebrachte Tage verkürzen sie.
        var basis = grow.RootedAt?.Date ?? grow.GerminatedAt?.Date ?? grow.StartDate.Date;
        var mitgebracht = grow.EntryPoint is GrowEntryPoint.Germination or GrowEntryPoint.Seedling
            ? grow.DaysAlreadyInPhase ?? 0
            : 0;
        return basis.AddDays(14 - mitgebracht);
    }

    /// <summary>Ab welchem Tag die alte Rechnung eine Autoflower blühen ließ.</summary>
    private static DateTime? AlterBluetenBeginn(GrowRun grow)
        => grow.SeedType == SeedType.Autoflower ? AlteAutoflowerKeimung(grow).AddDays(28) : null;

    private static DateTime AlteAutoflowerKeimung(GrowRun grow)
        => (grow.GerminatedAt?.Date ?? grow.StartDate.Date).AddDays(-(grow.AutoflowerDaysSinceGermination ?? 0));
}
