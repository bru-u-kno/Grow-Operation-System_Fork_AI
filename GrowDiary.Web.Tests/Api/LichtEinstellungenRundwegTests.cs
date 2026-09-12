using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Licht-Einstellungen überstehen das Speichern vollständig.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (12.09.2026).</b> <c>RundwegVollstaendigTests</c> meldete
/// <c>LichtEinstellungen</c> als einzigen PUT-Vertrag ohne Rundweg — die
/// Licht-Steuerung kam in forkai.51 ohne einen einzigen Test. Der allgemeine
/// Rundweg kann sie nicht fahren: er füllt jedes Feld mit einer festen Probe
/// (1 bzw. true), und „1" ist keine Uhrzeit im Format HH:mm; das PUT lehnt sie
/// mit 400 ab. Ein Rundweg, der nur Ablehnungen einsammelt, prüft nichts.</para>
///
/// <para><b>Was hier geprüft wird.</b> Dass keins der neun Felder beim
/// Speichern verlorengeht — der eigentliche Zweck eines Rundwegs. Genau dieser
/// Fehler ist teuer: wer Zeiten einträgt, sieht die Oberfläche danach fröhlich
/// zurückspringen und weiß nicht, ob der Controller oder die Ablage schuld ist.
/// </para>
///
/// <para><b>Was hier NICHT geprüft wird.</b> Der Weg durch Controller und
/// <c>LichtSteuerungService</c> samt Prüfung der Zeitformate und dem Schreiben
/// an den AC-Infinity-Controller. Dafür braucht es einen gestellten Funk; das
/// ist ein eigener Test und steht noch aus.</para>
/// </remarks>
public sealed class LichtEinstellungenRundwegTests : IDisposable
{
    private readonly string _wurzel;
    private readonly SteuerungRepository _repo;

    public LichtEinstellungenRundwegTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "LichtRundweg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        var pfade = new AppPaths(_wurzel);
        // Ohne angelegte Datenbank kommt SQLite gar nicht erst an die Datei
        // („unable to open database file") — dieselbe Vorbereitung wie in den
        // uebrigen Repository-Tests.
        TestDatabase.Initialize(pfade);
        _repo = new SteuerungRepository(pfade);
    }

    [Fact]
    public void JedesFeldUeberlebtDasSpeichern()
    {
        // Bewusst überall ANDERE Werte als die Vorgaben: ein Feld, das beim
        // Speichern verlorengeht, fiele sonst auf den Standard zurück und der
        // Vergleich wäre trotzdem grün.
        var gesendet = new LichtEinstellungen
        {
            VeggieEin = "04:30",
            VeggieAus = "22:15",
            BlueteEin = "06:45",
            BlueteAus = "18:45",
            Stufe = 9,
            SchreibAbstandMs = 3500,
            VerifySekunden = 45,
            MaxWiederholungen = 4,
            HelferSpiegeln = true,
        };

        _repo.SetEinstellungen(LichtSteuerungService.Modul, gesendet);
        var gelesen = _repo.GetEinstellungen<LichtEinstellungen>(LichtSteuerungService.Modul);

        Assert.NotNull(gelesen);
        Assert.Equal("04:30", gelesen!.VeggieEin);
        Assert.Equal("22:15", gelesen.VeggieAus);
        Assert.Equal("06:45", gelesen.BlueteEin);
        Assert.Equal("18:45", gelesen.BlueteAus);
        Assert.Equal(9, gelesen.Stufe);
        Assert.Equal(3500, gelesen.SchreibAbstandMs);
        Assert.Equal(45, gelesen.VerifySekunden);
        Assert.Equal(4, gelesen.MaxWiederholungen);
        Assert.True(gelesen.HelferSpiegeln);
    }

    [Fact]
    public void ZweitesSpeichernErsetztDasErste()
    {
        // Die Ablage hält je Modul EINE Zeile. Hängt sie an, statt zu ersetzen,
        // liest man später die alten Zeiten zurück — und wundert sich, warum
        // eine Änderung „nicht ankommt".
        _repo.SetEinstellungen(LichtSteuerungService.Modul, new LichtEinstellungen { Stufe = 3 });
        _repo.SetEinstellungen(LichtSteuerungService.Modul, new LichtEinstellungen { Stufe = 8 });

        Assert.Equal(8, _repo.GetEinstellungen<LichtEinstellungen>(LichtSteuerungService.Modul)?.Stufe);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); }
        catch (IOException) { /* Aufräumen ist keine Zusicherung des Tests. */ }
    }
}
