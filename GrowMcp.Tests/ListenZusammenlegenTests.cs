using GrowMcp.Tools;

namespace GrowMcp.Tests;

/// <summary>
/// Zwei Listen zusammenlegen, statt eine gegen die andere zu tauschen.
/// </summary>
/// <remarks>
/// <para><b>Zwei Fehler, die daran hingen.</b> „auch abgeschlossene Grows"
/// schaltete auf die Archiv-Liste UM, statt sie dazuzulegen — wer nach allen
/// Grows fragte, verlor genau die laufenden. Und die Technik eines Zeltes holte
/// Wartung und Kalibrierung ganz ohne Filter, also die Termine aller Zelte.</para>
///
/// <para>Beide Male kam eine plausible Antwort zurück, nur zur falschen Frage.
/// Eine KI, die damit arbeitet, kann den Unterschied nicht bemerken.</para>
/// </remarks>
public sealed class ListenZusammenlegenTests
{
    [Fact]
    public void TwoListsBecomeOne()
    {
        var zusammen = GrowTools.Verschmelzen("""[{"id":1},{"id":2}]""", """[{"id":9}]""");

        Assert.Equal("""[{"id":1},{"id":2},{"id":9}]""", zusammen);
    }

    [Fact]
    public void AnEmptyListAddsNothingAndLosesNothing()
    {
        Assert.Equal("""[{"id":1}]""", GrowTools.Verschmelzen("""[{"id":1}]""", "[]"));
        Assert.Equal("""[{"id":1}]""", GrowTools.Verschmelzen("[]", """[{"id":1}]"""));
    }

    [Fact]
    public void SomethingThatIsNotAListIsHandedBackUnchanged()
    {
        // Lieber die erste Antwort unveraendert durchreichen als eine
        // Fehlermeldung erfinden — Cultivate Control hat dann schon etwas gesagt.
        Assert.Equal("""{"fehler":"kaputt"}""", GrowTools.Verschmelzen("""{"fehler":"kaputt"}""", "[]"));
        Assert.Equal("kein json", GrowTools.Verschmelzen("kein json", "[]"));
    }

    [Fact]
    public void DeviceIdsAreReadFromTheDeviceList()
    {
        var ids = GrowTools.GeraeteIds("""[{"id":3,"name":"pH-Sonde"},{"id":7,"name":"EC-Sonde"}]""");

        Assert.Equal([3, 7], ids);
    }

    [Fact]
    public void ADeviceListWithoutIdsYieldsNothingRatherThanGuessing()
    {
        Assert.Empty(GrowTools.GeraeteIds("""[{"name":"ohne Id"}]"""));
        Assert.Empty(GrowTools.GeraeteIds("[]"));
        Assert.Empty(GrowTools.GeraeteIds("""{"fehler":"kaputt"}"""));
    }
}
