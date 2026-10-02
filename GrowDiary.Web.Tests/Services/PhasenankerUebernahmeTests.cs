using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die einmalige Übernahme: nach ihr liefert der Phasenanker für den Bestand
/// dieselbe Phase und Woche wie vorher die alte Rechnung.
/// </summary>
/// <remarks>
/// <para><b>Das Orakel.</b> <see cref="AlteLogik"/> ist der
/// <c>GrowStageResolver</c> und <c>MischplanService.WocheInPhase</c> im
/// Wortlaut von Commit 9bd8aaf, ohne Kommentare. Er ist bewusst NICHT die
/// eingefrorene Funktion aus <see cref="PhasenankerUebernahme"/> — die
/// Übernahme rechnet mit Formeln, das Orakel spielt die alte Rechnung Tag für
/// Tag durch.</para>
///
/// <para><b>Welche alte Woche.</b> Die alte App hatte drei Vegi-Wochen: der
/// Resolver (und der Zeitstrahl) begannen die Vegi nach 14 Tagen Sämling, der
/// Mischplan zählte ab dem Startdatum, die Plan-Auswertung ab Start + 7.
/// Verglichen wird mit dem Resolver: er war „die bisherige Logik", die eine
/// Phase „erreicht sah", und seine Beginne stimmen mit Zeitstrahl, Kopfzeile
/// und Kosten je Phase überein. Die Blütewoche stimmte bei allen überein und
/// wird zusätzlich gegen den alten Mischplan geprüft. Die Abweichung des alten
/// Mischplans in der Vegi steht als eigener Test da
/// (<see cref="DerAlteMischplanZaehlteDieAnzuchtInDieVegiWochen"/>).</para>
/// </remarks>
public sealed class PhasenankerUebernahmeTests : IDisposable
{
    private static readonly DateTime Heute = new(2026, 10, 2);

    /// <summary>Ein typischer Bestands-Grow; <c>Abweichung</c> nur mit ausgeschriebenem Grund.</summary>
    public sealed record Fall(string Name, GrowRun Grow, string? Abweichung = null);

    private static GrowRun G(int tageSeitStart, Action<GrowRun> anpassen)
    {
        var g = new GrowRun
        {
            Id = 1,
            Name = "Bestand",
            StartDate = Heute.AddDays(-tageSeitStart),
            SeedType = SeedType.Feminized,
            StartMaterial = StartMaterial.Seed,
            EntryPoint = GrowEntryPoint.Germination,
            Status = GrowStatus.Running,
        };
        anpassen(g);
        return g;
    }

    public static IEnumerable<Fall> Faelle()
    {
        yield return new("Samen, Tag 30, nie etwas eingetragen", G(30, _ => { }));
        yield return new("Samen, Tag 10, noch Sämling", G(10, _ => { }));
        yield return new("Samen, Keimung am Tag 3 bestätigt, Tag 40", G(40, g => g.GerminatedAt = g.StartDate.AddDays(3).AddHours(12)));
        yield return new("Samen, Vegi bestätigt, geflippt", G(70, g => { g.GerminatedAt = g.StartDate; g.VegStartedAt = g.StartDate.AddDays(9).AddHours(12); g.FlipDate = Heute.AddDays(-30); g.BreederFlowerWeeksMax = 9; }));
        yield return new("Samen, nie Vegi eingetragen, vor 20 Tagen geflippt", G(60, g => g.FlipDate = Heute.AddDays(-20)));
        yield return new("Samen, Flip geplant in 5 Tagen, Tag 30", G(30, g => g.FlipDate = Heute.AddDays(5)));
        yield return new("Samen, Einstieg Sämling mit 5 Tagen, Tag 20", G(20, g => { g.EntryPoint = GrowEntryPoint.Seedling; g.GerminatedAt = g.StartDate; g.DaysAlreadyInPhase = 5; }));
        yield return new("Samen, Einstieg Vegi ohne mitgebrachte Tage", G(25, g => { g.EntryPoint = GrowEntryPoint.Veg; g.GerminatedAt = g.StartDate; }));
        yield return new("Samen, abgeschlossen, nie Vegi eingetragen", G(130, g =>
        {
            g.GerminatedAt = g.StartDate;
            g.FlipDate = g.StartDate.AddDays(45);
            g.FinishStartedAt = g.StartDate.AddDays(100).AddHours(12);
            g.EndDate = g.StartDate.AddDays(110);
            g.Status = GrowStatus.Completed;
        }));
        yield return new("Samen, abgebrochen im Sämling", G(60, g => { g.EndDate = g.StartDate.AddDays(9); g.Status = GrowStatus.Aborted; }));
        yield return new("Steckling, bewurzelt angelegt, Tag 20", G(20, g => { g.StartMaterial = StartMaterial.Clone; g.CloneIsRooted = true; g.RootedAt = g.StartDate; }));
        yield return new("Steckling, unbewurzelt, Tag 5", G(5, g => g.StartMaterial = StartMaterial.Clone));
        yield return new("Steckling, geflippt", G(60, g => { g.StartMaterial = StartMaterial.Clone; g.CloneIsRooted = true; g.RootedAt = g.StartDate; g.FlipDate = Heute.AddDays(-25); }));
        yield return new("Autoflower, Tag 40, nie etwas eingetragen", G(40, g => { g.SeedType = SeedType.Autoflower; g.GerminatedAt = g.StartDate; }));
        yield return new("Autoflower, Tag 20", G(20, g => { g.SeedType = SeedType.Autoflower; g.GerminatedAt = g.StartDate; }));
        yield return new("Autoflower, Einstieg mit 30 Keimtagen, vor 10 Tagen angelegt", G(10, g => { g.SeedType = SeedType.Autoflower; g.AutoflowerDaysSinceGermination = 30; }));
        yield return new("Autoflower, Vegi bestätigt, Tag 50", G(50, g => { g.SeedType = SeedType.Autoflower; g.GerminatedAt = g.StartDate; g.VegStartedAt = g.StartDate.AddDays(11).AddHours(12); }));
        yield return new("Autoflower, abgeschlossen", G(120, g => { g.SeedType = SeedType.Autoflower; g.GerminatedAt = g.StartDate; g.FinishStartedAt = g.StartDate.AddDays(70).AddHours(12); g.EndDate = g.StartDate.AddDays(80); g.Status = GrowStatus.Completed; }));

        // --- Abweichungen, jede mit Grund ---
        yield return new("Samen, Einstieg Blüte (Keimdatum vom Formular)", G(5, g => { g.EntryPoint = GrowEntryPoint.Flower; g.GerminatedAt = g.StartDate; }),
            Abweichung: "Fehler behoben: die alte Rechnung sah den Einstieg nur ohne Keimdatum und nannte den blühenden Grow Veg.");
        yield return new("Steckling, Bewurzelung am Tag 8 bestätigt, Tag 30", G(30, g => { g.StartMaterial = StartMaterial.Clone; g.RootedAt = g.StartDate.AddDays(8).AddHours(12); g.CloneIsRooted = true; }),
            Abweichung: "Fehler behoben: die alte Rechnung nannte mit gesetztem RootedAt jeden Tag Veg, auch die Bewurzelungstage davor — Woche und Geschichte zählten sie mit.");
    }

    public static TheoryData<string> FallNamen()
    {
        var daten = new TheoryData<string>();
        foreach (var f in Faelle()) daten.Add(f.Name);
        return daten;
    }

    private static Fall FallMit(string name) => Faelle().Single(f => f.Name == name);

    private static DateTime Bezug(GrowRun g) => g.EndDate?.Date is { } e && e < Heute ? e : Heute;

    private static GrowRun Uebernommen(GrowRun g)
    {
        var (veg, bluete) = PhasenankerUebernahme.Vorschlag(g, Heute);
        if (veg is { } v) g.VegStartedAt = v.AddHours(12);
        if (bluete is { } b) g.FlipDate = b;
        return g;
    }

    [Fact]
    public void DieFallListeSiehtIhreGrundmenge()
    {
        // Mengenwächter: ohne Fälle liefe die Theorie null Mal und wäre grün.
        var faelle = Faelle().ToList();
        Assert.True(faelle.Count >= 18, $"nur {faelle.Count} Fälle");
        Assert.Contains(faelle, f => f.Grow.SeedType == SeedType.Autoflower && f.Grow.EndDate is not null);
        Assert.Contains(faelle, f => f.Grow.StartMaterial == StartMaterial.Clone);
        Assert.Contains(faelle, f => f.Grow.EndDate is not null && f.Grow.SeedType != SeedType.Autoflower);
        // Und die Übernahme hat überhaupt etwas zu tun.
        Assert.True(faelle.Count(f => PhasenankerUebernahme.Vorschlag(f.Grow, Heute) != (null, null)) >= 8);
    }

    [Theory]
    [MemberData(nameof(FallNamen))]
    public void NachDerUebernahmeDieselbePhaseUndWocheWieVorher(string name)
    {
        var fall = FallMit(name);
        var alt = FallMit(name).Grow; // unverändert, für das Orakel
        var neu = Uebernommen(fall.Grow);
        var bezug = Bezug(neu);

        var alteStufe = AlteLogik.Resolve(alt, bezug);
        var stand = Phasenanker.Fuer(neu, bezug);
        var alteWoche = AlteLogik.WocheInPhase(alt, bezug);

        var gleich = alteStufe == stand.Stufe && (alteWoche is null || alteWoche == stand.WocheInPhase);
        if (fall.Abweichung is not null)
        {
            // Die Ausnahme muss stimmen — sonst ist ihr Grund überholt.
            Assert.False(gleich && GeschichteGleich(alt, neu), $"{name}: als Abweichung geführt, ist aber gleich — Ausnahme streichen.");
            return;
        }

        Assert.Equal(alteStufe, stand.Stufe);
        if (alteWoche is { } w) Assert.Equal(w, stand.WocheInPhase);

        // Blütewochen zusätzlich gegen den alten Mischplan.
        if (alteStufe is GrowStage.Transition or GrowStage.Flower)
        {
            Assert.Equal(AlteLogik.MischplanWocheInPhase(alt, "Flower", bezug), stand.WocheIn("Flower"));
        }
    }

    [Theory]
    [MemberData(nameof(FallNamen))]
    public void NachDerUebernahmeDieselbeGeschichte_TagFuerTag(string name)
    {
        // Auch rückwirkend: Messprotokoll und Kosten je Phase fragen nach
        // vergangenen Tagen. Keiner davon darf seine Phase wechseln.
        var fall = FallMit(name);
        if (fall.Abweichung is not null) return;
        Assert.True(GeschichteGleich(FallMit(name).Grow, Uebernommen(fall.Grow)), $"{name}: {Erster(FallMit(name).Grow, Uebernommen(FallMit(name).Grow))}");
    }

    private static bool GeschichteGleich(GrowRun alt, GrowRun neu) => Erster(alt, neu) is null;

    private static string? Erster(GrowRun alt, GrowRun neu)
    {
        for (var tag = alt.StartDate.Date; tag <= Bezug(neu); tag = tag.AddDays(1))
        {
            var a = AlteLogik.Resolve(alt, tag);
            var n = Phasenanker.Fuer(neu, tag).Stufe;
            if (a != n) return $"am {tag:dd.MM.yyyy} alt {a}, neu {n}";
        }
        return null;
    }

    [Fact]
    public void DerAlteMischplanZaehlteDieAnzuchtInDieVegiWochen()
    {
        // Dokumentierte Abweichung: für einen Samen-Grow ohne Eintrag zählte
        // der Mischplan die Vegi-Woche ab dem Start — Tag 30 war dort Woche 5,
        // obwohl die Phase erst seit Tag 14 Veg hieß. Der Anker (und der alte
        // Resolver, Zeitstrahl, Kosten) sagen Woche 3. Genau diesen Sprung hat
        // der Nutzer gemeldet: nach dem Sämling stand der Plan in Vegi-Woche 3.
        var alt = G(30, _ => { });
        var neu = Uebernommen(G(30, _ => { }));

        Assert.Equal(5, AlteLogik.MischplanWocheInPhase(alt, "Veg", Heute));
        Assert.Equal(3, Phasenanker.Fuer(neu, Heute).WocheInPhase);
    }

    [Fact]
    public void NurWoDieAlteRechnungDiePhaseErreichtSah()
    {
        // Tag 10: die alte Rechnung sah noch Sämling — es wird nichts eingetragen.
        Assert.Equal((null, null), PhasenankerUebernahme.Vorschlag(G(10, _ => { }), Heute));
        // Bestätigtes bleibt unangetastet.
        Assert.Equal((null, null), PhasenankerUebernahme.Vorschlag(G(40, g => g.VegStartedAt = Heute.AddDays(-20)), Heute));
        // Ein unbewurzelter Steckling schaltete nie von selbst um.
        Assert.Equal((null, null), PhasenankerUebernahme.Vorschlag(G(40, g => g.StartMaterial = StartMaterial.Clone), Heute));
    }

    // ------------------------------------------------ mit Datenbank

    private readonly string _temp;
    private readonly AppPaths _pfade;

    public PhasenankerUebernahmeTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "Phasenanker_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _pfade = new AppPaths(_temp);
        TestDatabase.InitializeWithDefaultTent(_pfade);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { /* Aufräumen ist Beigabe */ }
    }

    [Fact]
    public void LaeuftEinmal_TraegtEin_UndSchreibtInsJournal()
    {
        var grows = new GrowRepository(_pfade);
        var journal = new JournalRepository(_pfade);
        var einstellungen = new AppSettingsRepository(_pfade);

        var samen = G(30, g => { g.Name = "Samen"; g.Notes = "bleibt stehen"; });
        samen.Id = grows.CreateGrow(samen);
        var auto = G(40, g => { g.Name = "Auto"; g.SeedType = SeedType.Autoflower; g.GerminatedAt = g.StartDate.AddHours(12); });
        auto.Id = grows.CreateGrow(auto);
        var jung = G(5, g => g.Name = "Jung");
        jung.Id = grows.CreateGrow(jung);

        Assert.Equal(2, PhasenankerUebernahme.Ausfuehren(grows, journal, einstellungen, Heute));

        var s = grows.GetGrow(samen.Id)!;
        Assert.Equal(Heute.AddDays(-16), s.VegStartedAt?.Date);
        Assert.Equal("bleibt stehen", s.Notes);
        Assert.Equal(Ankerphase.Veg, Phasenanker.Fuer(s, Heute).Phase);
        Assert.Contains(journal.GetForGrow(samen.Id), j => j.EntryType == JournalEntryType.VegStarted
            && j.Body!.Contains("aus der bisherigen Schätzung übernommen"));

        var a = grows.GetGrow(auto.Id)!;
        Assert.Equal(Heute.AddDays(-12), a.FlipDate?.Date);
        Assert.Equal(Heute.AddDays(-26), a.VegStartedAt?.Date);
        Assert.Equal(GrowStage.Flower, GrowStageResolver.Resolve(a, Heute));
        Assert.Equal(2, journal.GetForGrow(auto.Id).Count);

        Assert.Null(grows.GetGrow(jung.Id)!.VegStartedAt);
        Assert.Empty(journal.GetForGrow(jung.Id));

        // Ein zweiter Start ändert nichts mehr — auch nicht an einem Grow, den
        // die alte Rechnung inzwischen in der Vegi sähe.
        Assert.Equal(0, PhasenankerUebernahme.Ausfuehren(grows, journal, einstellungen, Heute.AddDays(30)));
        Assert.Null(grows.GetGrow(jung.Id)!.VegStartedAt);
        Assert.NotNull(einstellungen.GetValue(PhasenankerUebernahme.Merkmal));
    }

    /// <summary>
    /// Die alte Rechnung im Wortlaut (Commit 9bd8aaf), ohne Kommentare — nur
    /// als Orakel für diese Tests.
    /// </summary>
    private static class AlteLogik
    {
        public static GrowStage Resolve(GrowRun grow, DateTime today)
        {
            var heute = today.Date;
            if (grow.FlipDate is { } flip && heute >= flip.Date)
            {
                return FlowerStageFor(grow, flip.Date, heute);
            }
            if (grow.SeedType == SeedType.Autoflower)
            {
                var keim = AutoflowerKeimBasis(grow);
                var tage = (heute - keim).Days;
                if (tage >= 28) return FlowerStageFor(grow, keim.AddDays(28), heute);
                return SeedlingOrVeg(grow, heute, tage);
            }
            var vegStart = grow.RootedAt?.Date ?? grow.GerminatedAt?.Date ?? grow.StartDate.Date;
            if (grow.StartMaterial == StartMaterial.Clone && !grow.CloneIsRooted && grow.RootedAt is null)
            {
                return GrowStage.Clone;
            }
            if (grow.StartMaterial == StartMaterial.Clone)
            {
                return GrowStage.Veg;
            }
            if (grow.StartMaterial == StartMaterial.Seed && grow.GerminatedAt is null)
            {
                switch (grow.EntryPoint)
                {
                    case GrowEntryPoint.Veg:
                        return GrowStage.Veg;
                    case GrowEntryPoint.Flower:
                        return GrowStage.Flower;
                    case GrowEntryPoint.Flush:
                        return GrowStage.Finish;
                }
            }
            var seitStart = (heute - vegStart).Days + (grow.DaysAlreadyInPhase ?? 0);
            return grow.EntryPoint is GrowEntryPoint.Germination or GrowEntryPoint.Seedling
                ? SeedlingOrVeg(grow, heute, seitStart)
                : GrowStage.Veg;
        }

        private static DateTime AutoflowerKeimBasis(GrowRun grow)
            => (grow.GerminatedAt?.Date ?? grow.StartDate.Date).AddDays(-(grow.AutoflowerDaysSinceGermination ?? 0));

        private static DateTime? AutoflowerBluetenStart(GrowRun grow)
            => grow.SeedType == SeedType.Autoflower ? AutoflowerKeimBasis(grow).AddDays(28) : null;

        private static GrowStage SeedlingOrVeg(GrowRun grow, DateTime heute, int tageSeitStart)
        {
            if (grow.VegStartedAt is { } vegAb)
            {
                return heute >= vegAb.Date ? GrowStage.Veg : GrowStage.Seedling;
            }
            return tageSeitStart < 14 ? GrowStage.Seedling : GrowStage.Veg;
        }

        private static GrowStage FlowerStageFor(GrowRun grow, DateTime flip, DateTime heute)
        {
            var tageInBluete = (heute - flip).Days;
            if (grow.FinishStartedAt is { } finish && heute >= finish.Date)
            {
                return GrowStage.Finish;
            }
            if (tageInBluete < 10)
            {
                return GrowStage.Transition;
            }
            var wochen = grow.BreederFlowerWeeksMax ?? grow.BreederFlowerWeeksMin;
            if (wochen is { } w && w > 0)
            {
                var ernte = flip.AddDays(w * 7);
                if (heute >= ernte.AddDays(-14))
                {
                    return GrowStage.Finish;
                }
            }
            return GrowStage.Flower;
        }

        /// <summary>Alter <c>MischplanService.WocheInPhase</c>, mit Stichtag statt „heute".</summary>
        public static int MischplanWocheInPhase(GrowRun grow, string chartStage, DateTime heute)
        {
            if (chartStage == "Flower" && (grow.FlipDate?.Date ?? AutoflowerBluetenStart(grow)) is { } bluetenStart)
            {
                return Math.Max(1, ((heute - bluetenStart).Days / 7) + 1);
            }
            var start = grow.VegStartedAt?.Date ?? grow.StartDate.Date;
            var ende = chartStage == "Flower" ? heute : (grow.FlipDate?.Date ?? heute);
            return Math.Max(1, ((ende - start).Days / 7) + 1);
        }

        /// <summary>
        /// Die Woche, die die alte Rechnung für ihre Phase am Stichtag ergab:
        /// gezählt ab dem ersten Tag, seit dem sie ohne Unterbrechung dieselbe
        /// Phase sagte. In der Anzucht null (sie kannte keinen Anzuchtbeginn),
        /// ebenso, wenn sie die Phase schon am Starttag sagte (ein fester
        /// Einstieg — dann zählt der alte Mischplan ab Start).
        /// </summary>
        public static int? WocheInPhase(GrowRun grow, DateTime stichtag)
        {
            var familie = Familie(Resolve(grow, stichtag));
            if (familie == "Anzucht") return null;
            var beginn = stichtag.Date;
            while (beginn > grow.StartDate.Date && Familie(Resolve(grow, beginn.AddDays(-1))) == familie)
            {
                beginn = beginn.AddDays(-1);
            }
            if (beginn == grow.StartDate.Date)
            {
                return familie == "Bluete" ? MischplanWocheInPhase(grow, "Flower", stichtag)
                    : familie == "Veg" ? MischplanWocheInPhase(grow, "Veg", stichtag)
                    : null;
            }
            return Math.Max(1, (stichtag.Date - beginn).Days / 7 + 1);
        }

        private static string Familie(GrowStage stufe) => stufe switch
        {
            GrowStage.Seedling or GrowStage.Clone => "Anzucht",
            GrowStage.Veg => "Veg",
            GrowStage.Transition or GrowStage.Flower => "Bluete",
            _ => "Finish",
        };
    }
}
