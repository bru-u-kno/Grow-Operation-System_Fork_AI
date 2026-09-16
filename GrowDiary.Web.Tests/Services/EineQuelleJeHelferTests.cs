using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.115, F-013): Jeder HA-Helfer hat genau EINE schreibende Stelle im Fork.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Am 15.09.2026 setzte der Wochenplan die
/// Feuchte-Obergrenze auf 60 %, der stündliche CO₂-Worker schrieb 38 Minuten
/// später seine gespeicherten 65 % zurück. Der Wochenplan hielt das für eine
/// Handänderung und gab den Helfer auf — die Obergrenze stand danach auf einem
/// alten Wert, und niemand hatte etwas verstellt.</para>
/// </remarks>
public class EineQuelleJeHelferTests
{
    private static readonly string Rh = Co2SteuerungService.Entitaeten.RhObergrenze;

    [Fact]
    public void Co2SchreibtDieVomWochenplanGefuehrteObergrenzeNicht()
    {
        var e = new Co2Einstellungen { RhObergrenzeProzent = 65 };

        var liste = Co2SteuerungService.Schreibliste(e, 960, 840, 660, [Rh]);

        Assert.DoesNotContain(liste, z => z.Entity == Rh);
        // Der Rest bleibt vollständig — nur die geführte Zahl fällt heraus.
        Assert.Contains(liste, z => z.Entity == Co2SteuerungService.Entitaeten.ZielWarm && z.Wert == 960);
        Assert.Contains(liste, z => z.Entity == Co2SteuerungService.Entitaeten.KlimaHysterese);
    }

    [Fact]
    public void OhneWochenplanSchreibtCo2DieObergrenzeWeiter()
    {
        // Gegenprobe: ohne laufenden Plan ist die CO₂-Seite die einzige Quelle.
        var e = new Co2Einstellungen { RhObergrenzeProzent = 65 };

        var liste = Co2SteuerungService.Schreibliste(e, 960, 840, 660, []);

        Assert.Contains(liste, z => z.Entity == Rh && z.Wert == 65);
    }

    [Fact]
    public void KuehlerSchreibtDasGefuehrteZielpaarNicht()
    {
        var e = new ChillerEinstellungen { ZielTagC = 21, ZielNachtC = 17, MindestlaufzeitMin = 5 };
        var gefuehrt = new[] { ChillerSteuerungService.Entitaeten.ZielTag, ChillerSteuerungService.Entitaeten.ZielNacht };

        var liste = ChillerSteuerungService.Schreibliste(e, gefuehrt);

        Assert.DoesNotContain(liste, z => z.Entity == ChillerSteuerungService.Entitaeten.ZielTag);
        Assert.DoesNotContain(liste, z => z.Entity == ChillerSteuerungService.Entitaeten.ZielNacht);
        Assert.Contains(liste, z => z.Entity == ChillerSteuerungService.Entitaeten.Mindestlaufzeit && z.Wert == 5);
    }

    [Fact]
    public void OhneWochenplanSchreibtDerKuehlerSeinZielpaar()
    {
        var e = new ChillerEinstellungen { ZielTagC = 21, ZielNachtC = 17 };

        var liste = ChillerSteuerungService.Schreibliste(e, []);

        Assert.Contains(liste, z => z.Entity == ChillerSteuerungService.Entitaeten.ZielTag && z.Wert == 21);
        Assert.Contains(liste, z => z.Entity == ChillerSteuerungService.Entitaeten.ZielNacht && z.Wert == 17);
    }

    [Fact]
    public void DasCo2ZielGehoertDerGespeichertenCo2Steuerung()
    {
        Assert.True(WochenplanSyncService.RolleBeiCo2Steuerung(WochenplanSyncService.Rollen.Co2Ziel, co2Gespeichert: true));

        // Nie gespeichert: dann zieht der Wochenplan das Ziel wie bisher nach.
        Assert.False(WochenplanSyncService.RolleBeiCo2Steuerung(WochenplanSyncService.Rollen.Co2Ziel, co2Gespeichert: false));

        // Die Feuchte-Obergrenze bleibt in jedem Fall beim Wochenplan.
        Assert.False(WochenplanSyncService.RolleBeiCo2Steuerung(WochenplanSyncService.Rollen.RhObergrenze, co2Gespeichert: true));
    }
}
