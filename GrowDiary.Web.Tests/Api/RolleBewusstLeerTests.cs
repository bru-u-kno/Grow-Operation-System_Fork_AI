using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (Chiller-Ansteuerung): Eine optionale Rolle mit Vorgabe muss sich
/// leeren lassen. Vorher fiel sie still auf die Vorgabe zurück — und die
/// Vorgaben sind die Geräte einer bestimmten Anlage.
/// </summary>
public sealed class RolleBewusstLeerTests : IDisposable
{
    private readonly string _wurzel;
    private readonly SteuerungRepository _repo;
    private readonly SteuerungGeraeteService _geraete;

    public RolleBewusstLeerTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "RolleLeer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        var pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(pfade);
        _repo = new SteuerungRepository(pfade);
        _geraete = new SteuerungGeraeteService(_repo);
    }

    [Fact]
    public void EineGeleerteSteckdoseBleibtLeer()
    {
        var fehler = _geraete.Speichern("chiller", new Dictionary<string, string?> { ["steckdose"] = "" });

        Assert.Empty(fehler);
        Assert.Null(_geraete.EntitiesFuerModul("chiller")["steckdose"]);
        Assert.Null(_geraete.Entity("chiller", "steckdose"));
    }

    [Fact]
    public void EineGeleerteRolleLaesstSichNeuBelegen()
    {
        _geraete.Speichern("chiller", new Dictionary<string, string?> { ["steckdose"] = "switch.eine_steckdose" });
        _geraete.Speichern("chiller", new Dictionary<string, string?> { ["steckdose"] = "" });
        Assert.Null(_geraete.EntitiesFuerModul("chiller")["steckdose"]);

        _geraete.Speichern("chiller", new Dictionary<string, string?> { ["steckdose"] = "switch.eine_steckdose" });
        Assert.Equal("switch.eine_steckdose", _geraete.EntitiesFuerModul("chiller")["steckdose"]);
    }

    [Fact]
    public void EineAltMarkeInDerDatenbankWirdWeiterAlsLeerGelesen()
    {
        // Frühere Fassungen schrieben bei einer geleerten optionalen Rolle die Marke „-". Sie steht noch in
        // vorhandenen Datenbanken und darf nicht als Entität gelten.
        _repo.SetGeraet("chiller", "steckdose", SteuerungGeraeteRollen.BewusstLeer);

        Assert.Null(_geraete.EntitiesFuerModul("chiller")["steckdose"]);
        Assert.Null(_geraete.Entity("chiller", "steckdose"));
    }

    [Fact]
    public void LeerenSchreibtKeineMarkeMehr()
    {
        _geraete.Speichern("chiller", new Dictionary<string, string?> { ["steckdose"] = "switch.x" });
        _geraete.Speichern("chiller", new Dictionary<string, string?> { ["steckdose"] = "" });

        Assert.False(_geraete.Gespeichert("chiller").ContainsKey("steckdose"));
    }

    [Fact]
    public void EineRolleOhneVorgabeBleibtOhneMarkeLeer()
    {
        _geraete.Speichern("chiller", new Dictionary<string, string?> { ["kuehler_sollwert"] = "" });

        Assert.Null(_geraete.EntitiesFuerModul("chiller")["kuehler_sollwert"]);
        Assert.False(_geraete.Gespeichert("chiller").ContainsKey("kuehler_sollwert"));
    }

    [Fact]
    public void EinePflichtrolleLaesstSichNichtLeeren()
    {
        var fehler = _geraete.Speichern("chiller", new Dictionary<string, string?> { ["wasser_temp"] = "" });
        Assert.Contains("wasser_temp", fehler.Keys);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch (IOException) { }
    }
}
