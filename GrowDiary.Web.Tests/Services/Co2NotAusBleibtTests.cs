using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Wer die CO₂-Automation in Home Assistant ausschaltet, bekommt sie nicht
/// ungefragt zurück.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (29.09.2026).</b> Der stündliche Abgleich schrieb neben
/// den Sollwerten auch <c>automation.turn_on</c>. Ein Not-Aus in Home Assistant
/// — Ventil klemmt, Arbeit im Zelt, Flaschentausch — hielt so höchstens eine
/// Stunde. Dasselbe beim Speichern der CO₂-Seite: sie kennt den Zustand der
/// Automation nicht, und jedes Speichern eines ppm-Werts schaltete sie mit
/// ein.</para>
/// </remarks>
public sealed class Co2NotAusBleibtTests
{
    private static Co2Einstellungen Einstellungen(bool automatik = true) => new() { AutomatikAktiv = automatik };

    [Fact]
    public void StuendlicherAbgleich_FasstDieAutomationNichtAn()
    {
        var liste = Co2SteuerungService.Schalterliste(Einstellungen(), mitAutomatik: false);

        Assert.DoesNotContain(liste, s => s.Domain == "automation");
        Assert.DoesNotContain(liste, s => s.Entity == Co2SteuerungService.Entitaeten.Automatik);
    }

    [Fact]
    public void Selbsttest_MitAutomatikSchaltetDieListeSieWirklich()
    {
        var an = Co2SteuerungService.Schalterliste(Einstellungen(automatik: true), mitAutomatik: true);
        var aus = Co2SteuerungService.Schalterliste(Einstellungen(automatik: false), mitAutomatik: true);

        Assert.Contains(("automation", "turn_on", Co2SteuerungService.Entitaeten.Automatik), an);
        Assert.Contains(("automation", "turn_off", Co2SteuerungService.Entitaeten.Automatik), aus);
    }

    [Fact]
    public void StuendlicherAbgleich_SchreibtDieUebrigenSchalterWeiter()
    {
        var liste = Co2SteuerungService.Schalterliste(Einstellungen(), mitAutomatik: false);

        Assert.Contains(liste, s => s.Entity == Co2SteuerungService.Entitaeten.Autokalibrierung);
        Assert.Contains(liste, s => s.Entity == Co2SteuerungService.Entitaeten.AbluftDrosseln);
    }

    [Theory]
    [InlineData(true, true, false)]   // ppm geändert, Schalter nicht → Automation bleibt, wie sie in HA ist
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]   // Schalter umgelegt → schalten
    [InlineData(true, false, true)]
    public void Speichern_SchaltetDieAutomationNurWennDerSchalterUmgelegtWurde(bool vorher, bool neu, bool schalten)
    {
        Assert.Equal(schalten, Co2SteuerungService.AutomatikSchalten(Einstellungen(vorher), Einstellungen(neu)));
    }

    [Fact]
    public void ErstesSpeichern_SchaltetDieAutomation()
    {
        Assert.True(Co2SteuerungService.AutomatikSchalten(null, Einstellungen()));
    }
}
