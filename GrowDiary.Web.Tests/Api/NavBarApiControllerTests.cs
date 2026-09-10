using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI: die Leiste am oberen Rand überlebt alles, was ihr zugemutet wird.
/// </summary>
/// <remarks>
/// <para><b>Warum das geprüft wird.</b> Die Navigation ist der einzige Teil der
/// Oberfläche, ohne den man nirgends mehr hinkommt — auch nicht dorthin, wo man
/// den Fehler wieder geradeziehen würde. Ein kaputter Eintrag in der Datenbank
/// darf deshalb nie zu einer leeren Leiste führen, sondern immer nur zur
/// Werkseinstellung.</para>
///
/// <para>Die Liste erlaubter Pfade steht bewusst nur im Frontend, siehe
/// <see cref="NavBarApiController"/>. Hier wird deshalb nicht geprüft, ob ein
/// Pfad existiert — nur, dass Doppelte, Leere und Übermaß nicht durchkommen.</para>
/// </remarks>
public sealed class NavBarApiControllerTests : IDisposable
{
    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly AppSettingsRepository _einstellungen;
    private readonly NavBarApiController _controller;

    public NavBarApiControllerTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "NavBar_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(_pfade);
        _einstellungen = new AppSettingsRepository(_pfade);
        _controller = new NavBarApiController(_einstellungen);
    }

    private static NavBarDto Auslesen(ActionResult<NavBarDto> antwort)
    {
        var ok = Assert.IsType<OkObjectResult>(antwort.Result);
        return Assert.IsType<NavBarDto>(ok.Value);
    }

    [Fact]
    public void OhneVorgabe_meldetWerkseinstellung()
    {
        var dto = Auslesen(_controller.Get());

        Assert.Null(dto.Items);
        // Das Ruecksprungziel hat immer einen Wert — sonst zeigt das Haus-Zeichen
        // ins Leere, und der Weg zurueck nach Home Assistant waere weg.
        Assert.False(string.IsNullOrWhiteSpace(dto.DashboardPath));
    }

    [Fact]
    public void GespeicherteReihenfolge_kommtInDerselbenFolgeZurueck()
    {
        _controller.Save(new SaveNavBarRequest { Items = new List<string> { "/kosten", "/", "/aufgaben" } });

        var dto = Auslesen(_controller.Get());

        Assert.Equal(new[] { "/kosten", "/", "/aufgaben" }, dto.Items);
    }

    [Fact]
    public void Doppelte_werdenEinmalGespeichert()
    {
        // Zweimal derselbe Eintrag stuende zweimal in der Leiste und truege in
        // React denselben Schluessel.
        var dto = Auslesen(_controller.Save(new SaveNavBarRequest { Items = new List<string> { "/", "/", "/kosten" } }));

        Assert.Equal(new[] { "/", "/kosten" }, dto.Items);
    }

    [Fact]
    public void MehrAlsFuenf_werdenAbgeschnitten()
    {
        var dto = Auslesen(_controller.Save(new SaveNavBarRequest
        {
            Items = new List<string> { "/", "/messungen", "/wissen", "/kosten", "/aufgaben", "/grows", "/sensoren" },
        }));

        Assert.Equal(NavBarApiController.MaxItems, dto.Items!.Count);
        Assert.DoesNotContain("/grows", dto.Items!);
    }

    [Fact]
    public void LeereListe_setztAufWerkseinstellungZurueck()
    {
        _controller.Save(new SaveNavBarRequest { Items = new List<string> { "/kosten" } });

        var dto = Auslesen(_controller.Save(new SaveNavBarRequest { Items = new List<string>() }));

        Assert.Null(dto.Items);
        Assert.Null(_einstellungen.GetValue(NavBarApiController.SettingsKey));
    }

    [Fact]
    public void NurLeereEintraege_werdenAbgelehnt()
    {
        var antwort = _controller.Save(new SaveNavBarRequest { Items = new List<string> { "", "   " } });

        Assert.IsType<BadRequestObjectResult>(antwort.Result);
    }

    [Fact]
    public void KaputterEintrag_liefertWerkseinstellungStattFehler()
    {
        // Genau der Fall, um den es hier geht: waere das eine Ausnahme, stuende
        // der Nutzer vor einer App ohne jede Navigation.
        _einstellungen.SetValue(NavBarApiController.SettingsKey, "{kein json");

        var dto = Auslesen(_controller.Get());

        Assert.Null(dto.Items);
    }

    [Fact]
    public void DashboardPfad_aenderbarOhneDieReihenfolgeZuVerlieren()
    {
        _controller.Save(new SaveNavBarRequest { Items = new List<string> { "/kosten", "/" } });

        var dto = Auslesen(_controller.Save(new SaveNavBarRequest { DashboardPath = "/dashboard-grow/0" }));

        Assert.Equal("/dashboard-grow/0", dto.DashboardPath);
        Assert.Equal(new[] { "/kosten", "/" }, dto.Items);
    }

    [Fact]
    public void LeererDashboardPfad_faelltAufDieVoreinstellungZurueck()
    {
        _controller.Save(new SaveNavBarRequest { DashboardPath = "/dashboard-grow/0" });

        var dto = Auslesen(_controller.Save(new SaveNavBarRequest { DashboardPath = "  " }));

        Assert.Equal(NavBarApiController.DashboardDefault, dto.DashboardPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_wurzel, recursive: true);
        }
        catch (IOException)
        {
            // Aufraeumen ist Kuer; ein gehaltener Dateizeiger darf keinen Test roten.
        }
    }
}
