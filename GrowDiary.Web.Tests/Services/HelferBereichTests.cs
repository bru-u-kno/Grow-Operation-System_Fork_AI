using System.Text.Json;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-022): Der Bereich eines Zahl-Helfers wird nur geweitet, nie verengt — der Fall, der „nicht alle Helfer übernommen" ausgelöst
/// hat: „Feuchte EIN Nacht" stand auf 50–85, gewollt war 48.
/// </summary>
public sealed class HelferBereichTests
{
    [Fact]
    public void WertImBereichBrauchtNichts()
    {
        Assert.Null(HelferBereichService.Weiten(50, 85, 50));
        Assert.Null(HelferBereichService.Weiten(50, 85, 70));
        Assert.Null(HelferBereichService.Weiten(50, 85, 85));
    }

    [Fact]
    public void ZuKleinerWertWeitetAufDenKatalogBereich()
    {
        // Der Fall des Nutzers: 48 in einem Helfer 50–85, Katalog 30–90.
        Assert.Equal((30d, 90d), HelferBereichService.Weiten(50, 85, 48, 30, 90));
    }

    [Fact]
    public void OhneKatalogWirdNurSoWeitGeweitetWieNoetig()
    {
        Assert.Equal((48d, 85d), HelferBereichService.Weiten(50, 85, 48));
        Assert.Equal((50d, 91d), HelferBereichService.Weiten(50, 85, 91));
    }

    [Fact]
    public void DerBereichWirdNieEnger()
    {
        // Katalog 30–90 liegt im Helfer 10–100: der Helfer behält seine Grenzen, nur der Wert muss hinein.
        Assert.Equal((5d, 100d), HelferBereichService.Weiten(10, 100, 5, 30, 90));
    }

    [Fact]
    public void UnsinnigeWerteWeitenNichts()
    {
        Assert.Null(HelferBereichService.Weiten(50, 85, double.NaN));
        Assert.Null(HelferBereichService.Weiten(50, 85, double.PositiveInfinity));
    }

    [Fact]
    public void LesenErkenntHelferUndKennungen()
    {
        var json = JsonDocument.Parse("""
            [
              {"id":"beispiel_feuchte_ein","name":"Beispiel Feuchte EIN Nacht","min":50,"max":85,"step":1,"mode":"box","unit_of_measurement":"%","icon":"mdi:water-plus"},
              {"id":"ohne_bereich","name":"kaputt"},
              {"id":"beispiel_feuchte_aus","name":"Beispiel Feuchte AUS Nacht","min":30.5,"max":90,"step":0.5,"mode":"slider"}
            ]
            """).RootElement;

        var h = HelferBereichService.Lesen(json);

        Assert.Equal(2, h.Count);
        var ein = h["input_number.beispiel_feuchte_ein"];
        Assert.Equal((50d, 85d, 1d, "box", "%"), (ein.Min, ein.Max, ein.Schritt, ein.Modus, ein.Einheit));
        Assert.Equal("Beispiel Feuchte EIN Nacht", ein.Name);
        Assert.Equal(30.5, h["input_number.beispiel_feuchte_aus"].Min);
        // Die Kennung findet man unabhängig von der Schreibweise.
        Assert.True(h.ContainsKey("INPUT_NUMBER.BEISPIEL_FEUCHTE_EIN"));
    }

    [Fact]
    public void LesenVertrageEineAndereAntwort()
    {
        Assert.Empty(HelferBereichService.Lesen(JsonDocument.Parse("{}").RootElement));
        Assert.Empty(HelferBereichService.Lesen(JsonDocument.Parse("[]").RootElement));
    }
}
