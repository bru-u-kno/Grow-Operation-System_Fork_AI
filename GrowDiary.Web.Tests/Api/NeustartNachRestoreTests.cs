using System.Net;
using System.Net.Http.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Nach dem Zurückspielen einer Sicherung startet Grow OS neu.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (offene Punkte 03.10.2026, B8).</b> Die Datenbank wird
/// unter dem laufenden Prozess getauscht. Rund acht Stellen halten danach den
/// Stand der vorigen Datenbank im Speicher — Grow-Plan, Wissensbasis,
/// Wochenwerte, Schema-Merker, Übernahmen beim Start. Nur ein Neustart erfasst
/// alle.</para>
/// </remarks>
public sealed class NeustartNachRestoreTests : IDisposable
{
    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly GrowRepository _grows;
    private readonly SystemAuditRepository _audit;
    private readonly string? _tokenVorher;

    public NeustartNachRestoreTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Neustart_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(_pfade);
        _grows = new GrowRepository(_pfade);
        _audit = new SystemAuditRepository(_pfade);
        _tokenVorher = Environment.GetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable);
        Environment.SetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable, _tokenVorher);
        try { Directory.Delete(_wurzel, recursive: true); } catch { }
    }

    private sealed class MitschreibenderNeustart(bool kann) : IAppNeustart
    {
        public List<string> Gruende { get; } = new();

        public bool Planen(string grund)
        {
            Gruende.Add(grund);
            return kann;
        }
    }

    private BackupRestoreResultDto Zurueckspielen(IAppNeustart? neustart)
    {
        var controller = new SystemApiController(_pfade, _grows, _audit, neustart: neustart);
        var sicherung = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(controller.CreateBackup().Result).Value);
        return Assert.IsType<BackupRestoreResultDto>(Assert.IsType<OkObjectResult>(controller.RestoreBackup(sicherung.FileName).Result).Value);
    }

    [Fact]
    public void NachDemZurueckspielen_WirdDerNeustartGeplant()
    {
        var neustart = new MitschreibenderNeustart(kann: true);

        var ergebnis = Zurueckspielen(neustart);

        Assert.Single(neustart.Gruende);
        Assert.True(ergebnis.NeustartGeplant);
        Assert.Contains(ergebnis.Warnings, w => w.Contains("startet in wenigen Sekunden neu", StringComparison.Ordinal));
    }

    /// <summary>Die Hinweise des Trockenlaufs gehören nicht ins Ergebnis eines echten Zurückspielens.</summary>
    [Fact]
    public void DasErgebnisBehauptetKeinenTrockenlauf()
    {
        var ergebnis = Zurueckspielen(new MitschreibenderNeustart(kann: true));

        Assert.DoesNotContain(ergebnis.Warnings, w => w.Contains("Dry-Run", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ein abgewiesenes Zurückspielen startet nichts neu.</summary>
    [Fact]
    public void GeblocktesZurueckspielen_PlantKeinenNeustart()
    {
        var neustart = new MitschreibenderNeustart(kann: true);
        var controller = new SystemApiController(_pfade, _grows, _audit, neustart: neustart);
        var sicherung = Assert.IsType<BackupManifestDto>(Assert.IsType<CreatedResult>(controller.CreateBackup().Result).Value);
        var pfad = Directory.EnumerateFiles(_wurzel, sicherung.FileName, SearchOption.AllDirectories).Single();
        // Eine leere Sicherung: ohne Datenbank darin lehnt der Plan ab.
        File.Delete(pfad);
        using (System.IO.Compression.ZipFile.Open(pfad, System.IO.Compression.ZipArchiveMode.Create)) { }

        var antwort = controller.RestoreBackup(sicherung.FileName);

        Assert.IsType<BadRequestObjectResult>(antwort.Result);
        Assert.Empty(neustart.Gruende);
    }

    /// <summary>Ohne Add-on: kein stiller Erfolg, sondern der Satz, dass es neu gestartet werden muss.</summary>
    [Fact]
    public void OhneAddon_SagtDasErgebnisDassEinNeustartNoetigIst()
    {
        var ergebnis = Zurueckspielen(new MitschreibenderNeustart(kann: false));

        Assert.False(ergebnis.NeustartGeplant);
        Assert.Contains(ergebnis.Warnings, w => w.Contains("bitte jetzt neu starten", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OhneToken_StartetNichtsNeu()
    {
        var http = new RecordingHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var neustart = new SupervisorNeustart(new StubHttpClientFactory(http), NullLogger<SupervisorNeustart>.Instance, TimeSpan.Zero);

        Assert.False(neustart.Planen("Prüfung"));
        await Task.Delay(200);
        Assert.Empty(http.Requests);
    }

    /// <summary>Mit Token: genau ein POST auf den eigenen Neustart, mit dem Token.</summary>
    [Fact]
    public async Task AlsAddon_BittetDenSupervisorUmDenEigenenNeustart()
    {
        Environment.SetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable, "geheim");
        string? kopf = null;
        var http = new RecordingHttpHandler((anfrage, _) =>
        {
            kopf = anfrage.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var neustart = new SupervisorNeustart(new StubHttpClientFactory(http), NullLogger<SupervisorNeustart>.Instance, TimeSpan.FromMilliseconds(10));

        Assert.True(neustart.Planen("Prüfung"));
        for (var i = 0; i < 50 && http.Requests.Count == 0; i++) await Task.Delay(50);

        var anfrage = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, anfrage.Method);
        Assert.Equal("http://supervisor/addons/self/restart", anfrage.Uri.ToString());
        Assert.Equal("Bearer geheim", kopf);
    }

    /// <summary>
    /// Die laufende App gibt den Neustart wirklich in den Controller — über den
    /// echten Weg (HTTP, Ingress), nicht über einen von Hand gebauten Controller.
    /// </summary>
    /// <remarks>
    /// Der Parameter ist optional: käme der Dienst nicht beim Controller an,
    /// bliebe er still <c>null</c>. Dieser Fall belegt den Weg vom Container in
    /// den Controller; dass Program.cs den echten Dienst registriert, belegt
    /// <see cref="ProgramRegistriertDenSupervisorNeustart"/>.
    /// </remarks>
    [Fact]
    public async Task DieAppGibtDenNeustartInDenController()
    {
        var neustart = new MitschreibenderNeustart(kann: true);
        using var app = new IntegrationsApp
        {
            Zusatzdienste = dienste => dienste.AddSingleton<IAppNeustart>(neustart),
        };
        var client = app.IngressClient();

        var sicherung = await client.PostAsync("/api/system/backup", null);
        Assert.Equal(HttpStatusCode.Created, sicherung.StatusCode);
        var name = (await sicherung.Content.ReadFromJsonAsync<BackupManifestDto>())!.FileName;

        var antwort = await client.PostAsync($"/api/system/backup/{name}/restore", null);
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);

        Assert.Single(neustart.Gruende);
        Assert.True((await antwort.Content.ReadFromJsonAsync<BackupRestoreResultDto>())!.NeustartGeplant);
    }

    /// <summary>Und die echte Registrierung ist der Supervisor-Weg.</summary>
    [Fact]
    public void ProgramRegistriertDenSupervisorNeustart()
    {
        using var app = new IntegrationsApp();
        Assert.IsType<SupervisorNeustart>(app.Services.GetRequiredService<IAppNeustart>());
    }
}
