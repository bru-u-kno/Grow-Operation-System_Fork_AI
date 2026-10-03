using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Der Vergleich „meldet der Controller, was ich wollte?"
///
/// <para><b>Woher das kommt.</b> Der Tester hat seine Home-Assistant-Karte auf
/// v5 gehoben, und die Begründung steht in ihrer zweiten Zeile: <i>die
/// AC-Infinity-Cloud verwirft parallele Updates</i>. Sein Code schreibt seither
/// nacheinander, wartet dazwischen und liest nach — bis zu zwei
/// Wiederholungen, danach eine sichtbare Warnung.</para>
///
/// <para>Ohne diese Nachkontrolle meldet Home Assistant „gesendet" und die
/// Oberfläche sagt „gestellt", während am Gerät nichts passiert ist. Genau
/// diese Sorte Erfolgsmeldung ist in diesem Projekt schon mehrfach teuer
/// geworden.</para>
/// </summary>
public sealed class AcSchreiberTests
{
    [Theory]
    // Zahlen kommen je nach Entität unterschiedlich zurück. Ein Textvergleich
    // hielte das für einen Fehlschlag und schriebe endlos nach.
    [InlineData("7", "7", true)]
    [InlineData("7.0", "7", true)]
    // Ein deutsches Komma liest die Maschinen-Regel (Zahlenlesen.Maschine) als
    // Dezimalzeichen, wie das Frontend. Bis 03.10.2026 stand hier „false" —
    // NumberStyles.Any las „7,0" als 70.
    [InlineData("7,0", "7", true)]
    [InlineData("5,8", "58", false)]
    [InlineData("6", "7", false)]
    // Zeiten: gesetzt wird „18:00", gemeldet „18:00:00".
    [InlineData("18:00:00", "18:00", true)]
    [InlineData("18:00", "18:00:00", true)]
    [InlineData("06:00:00", "18:00", false)]
    // Auswahlwerte
    [InlineData("Schedule", "Schedule", true)]
    [InlineData("schedule", "Schedule", true)]
    [InlineData("On", "Schedule", false)]
    // Gar keine Antwort ist nie eine Bestätigung.
    [InlineData(null, "7", false)]
    public void Passt_erkennt_dieselbe_Zahl_in_anderer_Schreibweise(string? ist, string soll, bool erwartet)
    {
        Assert.Equal(erwartet, AcSchreiber.Passt(ist, soll));
    }

    [Fact]
    public void Eine_Null_ist_nicht_nichts()
    {
        // Stufe 0 heisst „aus" und ist ein gueltiger Sollwert. Wer hier auf
        // Wahrheitswerte prueft, haelt das Ausschalten fuer einen Fehlschlag.
        Assert.True(AcSchreiber.Passt("0", "0"));
        Assert.True(AcSchreiber.Passt("0.0", "0"));
        Assert.False(AcSchreiber.Passt("1", "0"));
    }

    [Fact]
    public void Die_Pruefung_beisst()
    {
        // Waere `Passt` immer wahr, liefe die Nachkontrolle leer und jeder
        // verworfene Auftrag gaelte als bestaetigt — der Fehler, gegen den
        // die ganze Klasse gebaut ist.
        Assert.False(AcSchreiber.Passt("3", "7"));
        Assert.False(AcSchreiber.Passt("", "7"));
        Assert.False(AcSchreiber.Passt("unavailable", "7"));
    }

    [Fact]
    public void Die_Zahlen_stammen_aus_der_Karte_des_Testers()
    {
        // Faustregeln mit Etikett: beide Werte sind seine Standardwerte
        // (`write_gap_ms` = 2000, `verify_seconds` = 20), keine
        // Herstellerangabe. Stehen sie eines Tages woanders, faellt das hier auf.
        Assert.Equal(TimeSpan.FromSeconds(2), AcSchreiber.Pause);
        Assert.Equal(TimeSpan.FromSeconds(20), AcSchreiber.Wartezeit);
        Assert.Equal(3, AcSchreiber.Versuche);

        // Der Takt muss in die Wartezeit passen und darf sie nicht ueberschreiten,
        // sonst wird genau einmal gefragt und die Obergrenze ist die Wartepflicht.
        Assert.True(AcSchreiber.Nachfragetakt < AcSchreiber.Wartezeit);

        // Und er darf NICHT gleich der Pause sein: sonst laesst sich in keinem
        // Test unterscheiden, ob zwischen zwei Schritten pausiert oder nur
        // nachgefragt wurde — und die Pause verschwand unbemerkt.
        Assert.NotEqual(AcSchreiber.Pause, AcSchreiber.Nachfragetakt);
    }
}
