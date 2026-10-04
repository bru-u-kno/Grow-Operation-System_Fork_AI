using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Tests.Api;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Jeder zeitabhängige Demowert liegt zu JEDER Minute des Tages in dem Ziel, das
/// die App selbst auf die Live-Kachel legt.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (04.10.2026).</b> CI-Lauf 37179321959 wurde um 07:16
/// Ortszeit rot: „Blütezelt (Testdaten): reservoir-ph = 5.79 (Ziel 5.8–6.2)" in
/// <c>e2e/demobestand-im-ziel.spec.ts</c>. Am Vortag war dieselbe Prüfung auf
/// <c>main</c> einmal rot und danach wieder grün. Die E2E-Prüfung misst einen
/// Augenblick — wann, entscheidet der Zeitplan des Tors. Die pH-Kurve
/// (<see cref="Demoverlauf.Ph"/>) begann bei 5,78 und lag damit in der halben
/// Nacht unter dem Planziel. Diese Prüfung misst deshalb nicht einen
/// Augenblick, sondern alle 1440.</para>
///
/// <para><b>Eine Wahrheit je Zahl.</b> Kein Ziel ist hier abgetippt. Die App wird
/// gebaut wie die Testdaten-App (<see cref="Demobestand.Anlegen"/> und
/// <see cref="Demobestand.PlaeneAnlegen"/>, dieselbe Methode wie in
/// <c>Program.cs</c>), die Werte kommen über <see cref="HomeAssistantService.DemoZustaende"/>
/// (derselbe Weg wie <c>GetStatesAsync</c> im Testdatenmodus), und die Ziele legt
/// <see cref="GrowDashboardComposer.BuildTentMetrics"/> darauf — derselbe Aufruf wie
/// <c>/api/live/tents/{id}</c>. Geprüft wird wie in der E2E-Prüfung: jede Kachel
/// mit Bereichs- oder Höchstziel; ein Einzelwert-Ziel (Luft 23 °C) trifft keine
/// echte Kurve auf die Kommastelle und zählt nicht.</para>
///
/// <para><b>Warum ein Tag genügt.</b> Der Verlauf hängt vom Datum nur über
/// „Tage bis heute" ab (Sägezahn von EC und pH, Kühlerausfall). Der Live-Wert
/// steht immer auf heute — für ihn ist das Alter der Reihe immer dasselbe, und
/// es bleibt allein die Uhrzeit. Die zurückliegenden Tage sind Verlauf, und dort
/// darf der EC am sechsten Tag absichtlich knapp über dem Plan stehen (genau dann
/// ist der Wasserwechsel fällig). Damit ein Datumswechsel trotzdem nichts kippt,
/// hält die pH-Kurve ihr Ziel über den ganzen Dosierzyklus — das prüft
/// <see cref="Der_pH_haelt_sein_Ziel_ueber_den_ganzen_Dosierzyklus"/>.</para>
///
/// <para><b>Eigene App.</b> <see cref="Demobestand.PlaeneAnlegen"/> setzt ein
/// Düngeprogramm — die anderen Integrationsfälle rechnen mit einem Bestand ohne.
/// In der Sammlung, damit sie nicht gleichzeitig mit den anderen läuft; das
/// Plan-Register ist prozessweit und wird beim Aufräumen geleert.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class DemowerteImZielTests : IClassFixture<DemowerteImZielTests.DemoApp>
{
    private const int MinutenJeTag = 24 * 60;

    private readonly DemoApp _app;

    public DemowerteImZielTests(DemoApp app) => _app = app;

    public sealed class DemoApp : IDisposable
    {
        public IntegrationsApp App { get; } = new();
        private readonly List<int> _growIds = [];

        public DemoApp()
        {
            using var bereich = App.Services.CreateScope();
            var (plaene, _) = Demobestand.PlaeneAnlegen(bereich.ServiceProvider);
            Assert.True(plaene >= 2, $"Nur {plaene} Pläne angelegt — ohne Plan gäbe es keine Planziele zu prüfen.");
            _growIds.AddRange(bereich.ServiceProvider.GetRequiredService<GrowRepository>().GetActiveGrows().Select(g => g.Id));
        }

        public void Dispose()
        {
            foreach (var id in _growIds) GrowPlanRegister.Entfernen(id);
            App.Dispose();
        }
    }

    private readonly record struct Treffer(string Zelt, string Kachel, double Wert, double? Min, double? Max);

    [Fact]
    public void Jede_Live_Kachel_mit_Bereichsziel_liegt_zu_jeder_Minute_des_Tages_darin()
    {
        var dienste = _app.App.Services;
        var grows = dienste.GetRequiredService<GrowRepository>();
        var ha = dienste.GetRequiredService<HomeAssistantService>();
        var composer = dienste.GetRequiredService<GrowDashboardComposer>();

        var zelte = grows.GetTents().Select(z => grows.GetTent(z.Id)!).Where(z => z.ActiveGrows.Count > 0).ToList();
        Assert.True(zelte.Count >= 2, $"Nur {zelte.Count} Zelte mit laufendem Grow — die Prüfung sähe ihre Grundmenge nicht.");

        var heute = DateTime.Today;
        var geprueft = new HashSet<string>(StringComparer.Ordinal);
        var minuten = 0;
        var daneben = new Dictionary<string, List<(string Uhr, Treffer T)>>(StringComparer.Ordinal);

        foreach (var zelt in zelte)
        {
            var messungen = grows.GetMeasurementsForTent(zelt.Id);
            for (var minute = 0; minute < MinutenJeTag; minute++)
            {
                var ortszeit = heute.AddMinutes(minute);
                var zustaende = ha.DemoZustaende(zelt, DateTime.SpecifyKind(ortszeit, DateTimeKind.Local).ToUniversalTime());
                minuten++;

                foreach (var kachel in composer.BuildTentMetrics(zelt, zustaende, messungen))
                {
                    if (kachel.NumericValue is not { } wert) continue;
                    var (unten, oben) = (kachel.TargetMin, kachel.TargetMax);
                    if (unten is null && oben is null) continue;
                    if (unten is { } u && oben is { } o && Math.Abs(o - u) < 1e-9) continue;   // Einzelwert

                    geprueft.Add($"{zelt.Name}/{kachel.Key}");
                    if ((unten is { } min && wert < min) || (oben is { } max && wert > max))
                    {
                        var schluessel = $"{zelt.Name}/{kachel.Key}";
                        if (!daneben.TryGetValue(schluessel, out var liste)) daneben[schluessel] = liste = [];
                        liste.Add((ortszeit.ToString("HH:mm", CultureInfo.InvariantCulture), new Treffer(zelt.Name, kachel.Key, wert, unten, oben)));
                    }
                }
            }
        }

        // Mengenwächter: jede Minute für jedes Zelt, und genug Kacheln mit Bereichsziel.
        Assert.Equal(zelte.Count * MinutenJeTag, minuten);
        Assert.True(geprueft.Count >= 8, $"Nur {geprueft.Count} Kacheln mit Bereichsziel: {string.Join(", ", geprueft)}");
        // Der Wert aus dem Befund wird wirklich geprüft — in jedem Zelt mit Grow.
        Assert.All(zelte, z => Assert.Contains($"{z.Name}/reservoir-ph", geprueft));

        var befund = daneben
            .Select(e => $"{e.Key}: {e.Value.Count} von {MinutenJeTag} Minuten daneben, "
                         + $"erstmals {e.Value[0].Uhr} mit {e.Value[0].T.Wert.ToString(CultureInfo.InvariantCulture)} "
                         + $"(Ziel {e.Value[0].T.Min?.ToString(CultureInfo.InvariantCulture) ?? "…"}–{e.Value[0].T.Max?.ToString(CultureInfo.InvariantCulture) ?? "…"})")
            .ToList();
        Assert.True(befund.Count == 0,
            "Der Demobestand verfehlt seine eigenen Ziele — dann ist der Bestand falsch, nicht die Kachel:\n" + string.Join("\n", befund));
    }

    /// <summary>
    /// Der pH bleibt über den ganzen Dosierzyklus im Ziel der Kachel — damit auch ein
    /// Datumswechsel bei laufender App (der Sägezahn rückt einen Tag weiter) nichts kippt.
    /// </summary>
    /// <remarks>
    /// Das Ziel kommt aus der Kachel um 12:00 heute, der Wert aus
    /// <see cref="Demoverlauf.Wert"/> zu jeder Minute der letzten
    /// <see cref="Demoverlauf.DosierAlleTage"/> Tage — gerundet wie die Kachel.
    /// </remarks>
    [Fact]
    public void Der_pH_haelt_sein_Ziel_ueber_den_ganzen_Dosierzyklus()
    {
        var dienste = _app.App.Services;
        var grows = dienste.GetRequiredService<GrowRepository>();
        var ha = dienste.GetRequiredService<HomeAssistantService>();
        var composer = dienste.GetRequiredService<GrowDashboardComposer>();
        var zelt = grows.GetTents().Select(z => grows.GetTent(z.Id)!).First(z => z.ActiveGrows.Count > 0);

        var mittag = DateTime.Today.AddHours(12);
        var kachel = composer.BuildTentMetrics(zelt, ha.DemoZustaende(zelt, mittag.ToUniversalTime()), grows.GetMeasurementsForTent(zelt.Id))
            .Single(k => k.Key == "reservoir-ph");
        Assert.True(kachel.TargetMin is not null && kachel.TargetMax is not null, "Die pH-Kachel hat kein Bereichsziel — der Fall prüfte nichts.");

        var werte = Enumerable.Range(0, Demoverlauf.DosierAlleTage * MinutenJeTag)
            .Select(m => DateTime.Today.AddDays(-Demoverlauf.DosierAlleTage + 1).AddMinutes(m))
            .Select(t => (Zeit: t, Wert: Demoverlauf.Wert("reservoir-ph", t)!.Value))
            .ToList();
        Assert.Equal(Demoverlauf.DosierAlleTage * MinutenJeTag, werte.Count);
        // Der Sägezahn kommt vor: über drei Tage mindestens 0,15 Spanne.
        Assert.True(werte.Max(w => w.Wert) - werte.Min(w => w.Wert) >= 0.15, "Kein Sägezahn — der Fall sähe nur einen Tag.");

        var daneben = werte.Where(w => w.Wert < kachel.TargetMin || w.Wert > kachel.TargetMax).ToList();
        Assert.True(daneben.Count == 0,
            $"{daneben.Count} Minuten außerhalb {kachel.TargetMin}–{kachel.TargetMax}, etwa {daneben.FirstOrDefault().Zeit:dd.MM. HH:mm} mit {daneben.FirstOrDefault().Wert}.");
    }
}
