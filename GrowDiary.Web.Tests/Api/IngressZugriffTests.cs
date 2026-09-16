using System.Net;
using System.Net.Http.Json;
using GrowDiary.Web.Api.Contracts;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Der Wächter an der echten App: wer darf Verwaltungswege benutzen?
/// </summary>
/// <remarks>
/// Fehlerregister F-011 (16.09.2026). Die Einzeltests in
/// <c>AdminAccessPolicyTests</c> prüfen die Regel; hier wird geprüft, dass sie
/// in der laufenden Kette auch greift — Middleware-Reihenfolge eingeschlossen.
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class IngressZugriffTests
{
    private readonly IntegrationsApp _app;

    public IngressZugriffTests(IntegrationsApp app) => _app = app;

    [Fact]
    public async Task EinNachbarAddonMitGefaelschtemKopfDarfKeinBackupAnlegen()
    {
        var client = _app.ClientVon("172.30.33.7", mitIngressKopf: true);

        var antwort = await client.PostAsync("/api/system/backup", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, antwort.StatusCode);
    }

    [Fact]
    public async Task EinNachbarAddonDarfDieBackupListeNichtLesen_ProduktdatenAberSchon()
    {
        var client = _app.ClientVon("172.30.33.7", mitIngressKopf: false);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/system/backup")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/grows")).StatusCode);
    }

    [Fact]
    public async Task UeberIngressGehtAnlegenUndAuflisten()
    {
        var client = _app.IngressClient();

        var anlegen = await client.PostAsync("/api/system/backup", content: null);
        Assert.Equal(HttpStatusCode.Created, anlegen.StatusCode);
        var manifest = await anlegen.Content.ReadFromJsonAsync<BackupManifestDto>();

        var liste = await client.GetFromJsonAsync<BackupListDto>("/api/system/backup");
        Assert.Contains(liste!.Backups, b => b.FileName == manifest!.FileName);
    }

    [Fact]
    public async Task EsGibtKeinenLoeschwegUeberHttp()
    {
        // Bewusst (siehe CrudVollstaendigTests): eine Sicherung loescht nur die Aufbewahrung.
        var client = _app.IngressClient();
        var manifest = await (await client.PostAsync("/api/system/backup", content: null))
            .Content.ReadFromJsonAsync<BackupManifestDto>();

        var antwort = await client.DeleteAsync(manifest!.DownloadUrl);

        Assert.False(antwort.IsSuccessStatusCode);
        var liste = await client.GetFromJsonAsync<BackupListDto>("/api/system/backup");
        Assert.Contains(liste!.Backups, b => b.FileName == manifest.FileName);
    }

    [Fact]
    public async Task HomeAssistantOhneKopfDarfEinBackupNurLesen()
    {
        // So holt der Node-RED-Flow „Fork-Daten bereitstellen" die Datei ab.
        var ingress = _app.IngressClient();
        var manifest = await (await ingress.PostAsync("/api/system/backup", content: null))
            .Content.ReadFromJsonAsync<BackupManifestDto>();

        var hostNetz = _app.ClientVon("172.30.32.1", mitIngressKopf: false);
        Assert.Equal(HttpStatusCode.OK, (await hostNetz.GetAsync(manifest!.DownloadUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hostNetz.PostAsync("/api/system/backup", content: null)).StatusCode);
    }
}
