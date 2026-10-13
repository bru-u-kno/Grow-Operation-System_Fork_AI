using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-021, forkai.212): „Arbeitsbereich aus dem Plan" am Home Assistant aus Papier — was beim Speichern und beim
/// Wochenwechsel wirklich in die Helfer geschrieben wird. Die Rechnung selbst steht in <see cref="ArbeitsbereichAusPlanTests"/>.
/// </summary>
public sealed class ArbeitsbereichAusPlanPruefstandTests
{
    private const string FeuchteEinTag = "input_number.trotec_feuchte_ein_tag";
    private static string Helfer(ZusatzPruefstand p, string entitaet) =>
        p.Schreibaufrufe().LastOrDefault(a => a.Entitaet == entitaet && a.Dienst == "input_number/set_value").Daten["value"]?.ToString() ?? "(nicht geschrieben)";

    private static ZusatzPruefstand BruAmWochenwechsel(string rhObergrenze)
    {
        var p = new ZusatzPruefstand();
        p.Setze(EntfeuchterSteuerungService.Entitaeten.RhObergrenze, rhObergrenze);
        return p;
    }

    private static EntfeuchterEinstellungen PlanEinstellungen() => new()
    {
        ArbeitsbereichQuelle = ArbeitsbereichQuellen.Plan,
        VorlaufProzent = 4,
        HystereseProzent = 4,
        EinschaltverzoegerungMin = 2,
        WartezeitAussenluftMin = 5,
        MindestlaufzeitMin = 20,
        FeuchteEinTag = 50, FeuchteAusTag = 46, FeuchteEinNacht = 50, FeuchteAusNacht = 46,
    };

    [Fact]
    public async Task BeimSpeichernGehenDieGerechnetenSchwellenNachHomeAssistant_nichtDieAltenFesten()
    {
        using var p = BruAmWochenwechsel("54");
        var dienst = p.Entfeuchter();
        var e = PlanEinstellungen();
        e.FeuchteEinTag = e.FeuchteEinNacht = 58; // alte feste Werte — dürfen nicht gewinnen
        e.FeuchteAusTag = e.FeuchteAusNacht = 54;

        var (gespeichert, fehler, _) = await dienst.SpeichernAsync(e, CancellationToken.None);

        Assert.Empty(fehler);
        Assert.Equal("50", Helfer(p, FeuchteEinTag));
        Assert.Equal("46", Helfer(p, "input_number.trotec_feuchte_aus_tag"));
        Assert.Equal("50", Helfer(p, "input_number.trotec_feuchte_ein"));
        Assert.Equal("46", Helfer(p, "input_number.trotec_feuchte_aus"));
        // Auch der gespeicherte Stand trägt sie — die Seite zeigt nach dem Speichern, was in Home Assistant liegt.
        Assert.Equal(50, gespeichert!.FeuchteEinTag);
        Assert.Equal(46, gespeichert.FeuchteAusNacht);
    }

    [Fact]
    public async Task FestSchreibtWieBisher_dieGespeichertenWerte()
    {
        using var p = BruAmWochenwechsel("54");
        var e = PlanEinstellungen();
        e.ArbeitsbereichQuelle = ArbeitsbereichQuellen.Fest;
        e.FeuchteEinTag = e.FeuchteEinNacht = 58;
        e.FeuchteAusTag = e.FeuchteAusNacht = 54;

        await p.Entfeuchter().SpeichernAsync(e, CancellationToken.None);

        Assert.Equal("58", Helfer(p, FeuchteEinTag));
        Assert.Equal("54", Helfer(p, "input_number.trotec_feuchte_aus_tag"));
    }

    [Fact]
    public async Task OhneObergrenzeImPlanBleibenDieFestenWerte()
    {
        using var p = new ZusatzPruefstand();
        // Der Wochenplan hat noch nichts nach Home Assistant geschrieben: kein Wert für die Obergrenze.
        p.Zustaende.Remove(EntfeuchterSteuerungService.Entitaeten.RhObergrenze);
        var e = PlanEinstellungen();
        e.FeuchteEinTag = e.FeuchteEinNacht = 52;
        e.FeuchteAusTag = e.FeuchteAusNacht = 48;

        await p.Entfeuchter().SpeichernAsync(e, CancellationToken.None);

        Assert.Equal("52", Helfer(p, FeuchteEinTag));
    }

    [Fact]
    public async Task DerWochenwechselZiehtDieSchwellenNach_undSchreibtSonstNichts()
    {
        using var p = BruAmWochenwechsel("54");
        var dienst = p.Entfeuchter();
        await dienst.SpeichernAsync(PlanEinstellungen(), CancellationToken.None);

        // Neue Woche: die Plan-Obergrenze sinkt auf 50 %, die Helfer stehen noch auf der alten Woche.
        p.Setze(EntfeuchterSteuerungService.Entitaeten.RhObergrenze, "50");
        p.Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteEinTag, "50");
        p.Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteAusTag, "46");
        p.Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteEinNacht, "50");
        p.Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteAusNacht, "46");
        p.Vergessen();

        var geschrieben = await p.Entfeuchter().PlanNachziehenAsync(CancellationToken.None);

        Assert.Equal(4, geschrieben);
        Assert.Equal("46", Helfer(p, FeuchteEinTag));
        Assert.Equal("42", Helfer(p, "input_number.trotec_feuchte_aus_tag"));
        // Der gespeicherte Stand folgt, sonst zeigte die Seite noch die alte Woche.
        var stand = p.Repo.GetEinstellungen<EntfeuchterEinstellungen>(EntfeuchterSteuerungService.Modul)!;
        Assert.Equal(46, stand.FeuchteEinTag);
        Assert.Equal(42, stand.FeuchteAusNacht);

        // Ein zweiter Lauf an derselben Woche schreibt nichts mehr.
        p.Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteEinTag, "46");
        p.Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteAusTag, "42");
        p.Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteEinNacht, "46");
        p.Setze(EntfeuchterSteuerungService.Entitaeten.FeuchteAusNacht, "42");
        p.Vergessen();
        Assert.Equal(0, await p.Entfeuchter().PlanNachziehenAsync(CancellationToken.None));
        Assert.Empty(p.Schreibaufrufe());
    }

    [Fact]
    public async Task BeiFestZiehtDerWochenwechselDieLuftfeuchteNichtNach()
    {
        using var p = BruAmWochenwechsel("54");
        var e = PlanEinstellungen();
        e.ArbeitsbereichQuelle = ArbeitsbereichQuellen.Fest;
        await p.Entfeuchter().SpeichernAsync(e, CancellationToken.None);
        p.Setze(EntfeuchterSteuerungService.Entitaeten.RhObergrenze, "40");
        p.Vergessen();

        Assert.Equal(0, await p.Entfeuchter().PlanNachziehenAsync(CancellationToken.None));
        Assert.DoesNotContain(p.Schreibaufrufe(), a => a.Entitaet.Contains("feuchte_ein") || a.Entitaet.Contains("feuchte_aus"));
    }
}
