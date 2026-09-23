using System.Text.Json;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.150): Sperre und Freigabe der CO₂-Steuerung im Fork —
/// Notbremse und Canopy-Obergrenze folgen der Plan-Woche, die neuen Klima-Werte
/// werden erst geschrieben, wenn sie im Fork gespeichert sind.
/// </summary>
/// <remarks>
/// Die Planwerte sind die von SKX Canna Aqua: Blütewoche 5 RH max 55 %, Luft
/// 25 °C; Blütewoche 7 RH max 40 %, Luft 20 °C; Flush Luft 16 °C.
/// </remarks>
public sealed class Co2KlimaSperreTests
{
    private static Co2Einstellungen PlanGekoppelt() => new()
    {
        RhObergrenzeProzent = 60,
        RhNotbremseModus = GrenzModus.Plan,
        RhNotbremseAbstandProzent = 10,
        CanopyObergrenzeModus = GrenzModus.Plan,
        CanopyObergrenzeAbstandK = 5,
        CanopyObergrenzeC = 30,
    };

    [Theory]
    [InlineData(55, 65)]   // Blütewoche 5 — derselbe Wert wie bisher fest
    [InlineData(50, 60)]   // Blütewoche 6
    [InlineData(40, 50)]   // Blütewoche 7
    public void NotbremseFolgtDerPlanObergrenze(double rhPlan, double erwartet)
        => Assert.Equal(erwartet, Co2SteuerungService.WirksameNotbremse(PlanGekoppelt(), rhPlan));

    [Fact]
    public void NotbremseBleibtImWertebereichDesHelfers()
    {
        // Der Flush nennt 30 % RH max — 30 + 10 liegt im Bereich; 15 + 10 nicht.
        Assert.Equal(Co2SteuerungService.NotbremseMin, Co2SteuerungService.WirksameNotbremse(PlanGekoppelt(), 15));
        Assert.Equal(Co2SteuerungService.NotbremseMax, Co2SteuerungService.WirksameNotbremse(PlanGekoppelt(), 85));
    }

    [Fact]
    public void NotbremseFestOhneGespeichertenWertIstNull()
        => Assert.Null(Co2SteuerungService.WirksameNotbremse(new Co2Einstellungen(), 55));

    [Theory]
    [InlineData(25.0, 30.0)]  // Blütewoche 5
    [InlineData(20.0, 25.0)]  // Blütewoche 7
    [InlineData(16.0, 21.0)]  // Flush — vorher lag 21 °C unter dem Helferbereich (22)
    public void CanopyObergrenzeFolgtDerPlanLuft(double planLuft, double erwartet)
        => Assert.Equal(erwartet, Co2SteuerungService.WirksameCanopyObergrenze(PlanGekoppelt(), planLuft));

    [Fact]
    public void CanopyObergrenzeOhnePlanLuftGiltFest()
        => Assert.Equal(30, Co2SteuerungService.WirksameCanopyObergrenze(PlanGekoppelt(), null));

    [Fact]
    public void NieGespeicherteKlimaWerteWerdenNichtGeschrieben()
    {
        // Ein Update darf keinen Helfer überschreiben, den jemand in Home
        // Assistant von Hand gesetzt hat.
        var liste = Co2SteuerungService.Schreibliste(new Co2Einstellungen(), 960, 840, 660, []);
        var entitaeten = liste.Select(z => z.Entity).ToList();

        Assert.True(entitaeten.Count >= 10);
        Assert.DoesNotContain(Co2SteuerungService.Entitaeten.KlimaToleranz, entitaeten);
        Assert.DoesNotContain(Co2SteuerungService.Entitaeten.RhNotbremse, entitaeten);
        Assert.DoesNotContain(Co2SteuerungService.Entitaeten.T6Klima, entitaeten);
    }

    [Fact]
    public void GespeicherteKlimaWerteGehenMitDemPlanNachHomeAssistant()
    {
        var e = PlanGekoppelt();
        e.KlimaToleranzMinuten = 7;
        e.T6StufeKlima = 6;

        var liste = Co2SteuerungService.Schreibliste(e, 960, 840, 660, [], rhObergrenzeWirksam: 50, planLuftTagC: 23)
            .ToDictionary(z => z.Entity, z => z.Wert);

        Assert.Equal(7, liste[Co2SteuerungService.Entitaeten.KlimaToleranz]);
        Assert.Equal(6, liste[Co2SteuerungService.Entitaeten.T6Klima]);
        Assert.Equal(60, liste[Co2SteuerungService.Entitaeten.RhNotbremse]);
        Assert.Equal(28, liste[Co2SteuerungService.Entitaeten.CanopyObergrenze]);
    }

    [Fact]
    public void StufeBeiKlimasperreLiegtZwischenDosierungUndNormal()
    {
        var zuTief = Co2SteuerungService.Pruefen(new Co2Einstellungen { T6StufeKlima = 4 });
        var zuHoch = Co2SteuerungService.Pruefen(new Co2Einstellungen { T6StufeKlima = 8 });
        var passt = Co2SteuerungService.Pruefen(new Co2Einstellungen { T6StufeKlima = 6 });

        Assert.True(zuTief.ContainsKey(nameof(Co2Einstellungen.T6StufeKlima)));
        Assert.True(zuHoch.ContainsKey(nameof(Co2Einstellungen.T6StufeKlima)));
        Assert.False(passt.ContainsKey(nameof(Co2Einstellungen.T6StufeKlima)));
    }

    [Fact]
    public void FesteNotbremseMussUeberDerObergrenzeLiegen()
    {
        var fehler = Co2SteuerungService.Pruefen(new Co2Einstellungen { RhObergrenzeProzent = 60, RhNotbremseFestProzent = 60 });
        Assert.True(fehler.ContainsKey(nameof(Co2Einstellungen.RhNotbremseFestProzent)));
    }

    [Fact]
    public void MittelungsfensterHatGrenzen()
    {
        Assert.True(Co2SteuerungService.Pruefen(new Co2Einstellungen { RhMittelMinuten = 0 }).ContainsKey(nameof(Co2Einstellungen.RhMittelMinuten)));
        Assert.False(Co2SteuerungService.Pruefen(new Co2Einstellungen { RhMittelMinuten = 5 }).ContainsKey(nameof(Co2Einstellungen.RhMittelMinuten)));
    }

    [Fact]
    public void FensterFelderSindDieDesFormularsGleitenderMittelwert()
    {
        var felder = SteuerungMittelwertService.FensterFelder(5, 1);
        var json = JsonSerializer.Serialize(felder);

        Assert.Equal("{\"window_size\":{\"hours\":0,\"minutes\":5,\"seconds\":0},\"type\":\"last\",\"precision\":1}", json);
    }

    [Fact]
    public void GenauigkeitKommtAusDemVorschlagDesFormulars()
    {
        // So antwortet Home Assistant auf das Öffnen des Optionen-Dialogs:
        // die aktuellen Optionen stehen als suggested_value im Schema.
        var formular = JsonDocument.Parse("""
            {"type":"form","step_id":"time_simple_moving_average","flow_id":"abc",
             "data_schema":[
               {"name":"type","default":"last"},
               {"name":"window_size","required":true,"description":{"suggested_value":{"hours":0,"minutes":5,"seconds":0}}},
               {"name":"precision","default":2,"description":{"suggested_value":1}}]}
            """).RootElement;

        Assert.Equal(1, SteuerungMittelwertService.VorschlagAusFormular(formular, "precision"));
        Assert.Null(SteuerungMittelwertService.VorschlagAusFormular(formular, "fehlt"));
    }

    [Fact]
    public void KatalogLegtDenMittelwertVorDerKlimaFreigabeAn()
    {
        var co2 = SteuerungBauteile.Alle.Where(b => b.Modul == "co2").ToList();
        var mittel = co2.FindIndex(b => b.EntityId == Co2SteuerungService.Entitaeten.RhMittel);
        var ueber = co2.FindIndex(b => b.EntityId == Co2SteuerungService.Entitaeten.FeuchteUeberGrenze);
        var klima = co2.FindIndex(b => b.EntityId == Co2SteuerungService.Entitaeten.KlimaOk);

        Assert.True(mittel >= 0 && ueber >= 0 && klima >= 0);
        Assert.True(mittel < klima && ueber < klima);
        Assert.Equal(BauteilArt.Mittelwert, co2[mittel].Art);

        var vorlage = co2[klima].Vorlage!;
        Assert.Contains("states('sensor.co2_sonden_rh_mittel') | float(rh)", vorlage);
        Assert.Contains("input_number.co2_rh_notbremse", vorlage);
        Assert.Contains("input_number.co2_klima_toleranz", vorlage);
    }

    [Fact]
    public void DieSeiteZeigtNieGespeicherteWerteAusHomeAssistant()
    {
        var live = new Co2Live(true, 800, 960, "plan", 1200, null, 960, 840, 660, 50, 910, true, true, false, true, true, 5, false,
            29, 53, 27, 51, 1.5, 10, 0.05, 0.07, 8.3, 7, null)
        {
            HaKlimaToleranzMinuten = 9,
            HaRhNotbremseProzent = 70,
            HaT6StufeKlima = 6,
        };

        var anzeige = Co2SteuerungService.MitWertenAusHomeAssistant(new Co2Einstellungen(), live);
        var gespeichert = Co2SteuerungService.MitWertenAusHomeAssistant(new Co2Einstellungen { KlimaToleranzMinuten = 4 }, live);

        Assert.Equal(9, anzeige.KlimaToleranzMinuten);
        Assert.Equal(70, anzeige.RhNotbremseFestProzent);
        Assert.Equal(6, anzeige.T6StufeKlima);
        Assert.Equal(4, gespeichert.KlimaToleranzMinuten);
    }
}
