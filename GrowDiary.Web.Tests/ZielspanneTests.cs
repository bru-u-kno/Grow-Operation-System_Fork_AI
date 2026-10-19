using System.Globalization;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Ein Zielbereich als Text: eine Spanne ohne Breite ist ein einzelner Wert
/// („1,4-1,4“ las sich wie ein Fehler).
/// </summary>
public sealed class ZielspanneTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    [Fact]
    public void EineSpanneMitBreite_ZeigtBeideGrenzen()
    {
        Assert.Equal("1,0-1,2", Zielspanne.Text(1.0, 1.2, "0.0", "-", De));
        Assert.Equal("300–500", Zielspanne.Text(300, 500, "0", "–", De));
    }

    [Fact]
    public void GleicheGrenzen_SindEinWert()
    {
        Assert.Equal("1,4", Zielspanne.Text(1.4, 1.4, "0.0", "-", De));
    }

    [Fact]
    public void Verglichen_WirdDerGeschriebeneText_NichtDieZahl()
    {
        // 1,35 und 1,44 stehen mit einer Nachkommastelle beide als „1,4“ da.
        Assert.Equal("1,4", Zielspanne.Text(1.35, 1.44, "0.0", "-", De));
        // Mit zwei Nachkommastellen sind sie verschieden und bleiben eine Spanne.
        Assert.Equal("1,35-1,44", Zielspanne.Text(1.35, 1.44, "0.00", "-", De));
    }

    [Fact]
    public void EinseitigeGrenzen_UndLeer()
    {
        Assert.Equal("ab 5", Zielspanne.Text(5, null, "0.##", "-", De));
        Assert.Equal("bis 7", Zielspanne.Text(null, 7, "0.##", "-", De));
        Assert.Equal("—", Zielspanne.Text(null, null, "0.##", "-", De));
    }
}
