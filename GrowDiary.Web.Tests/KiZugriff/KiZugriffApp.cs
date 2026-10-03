using System.Net.Http.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Tests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Die echte App mit dem Test-Controller des Schlüsselwegs.
/// </summary>
/// <remarks>
/// Eine eigene Instanz statt der gemeinsamen: der Test-Controller soll in keiner
/// anderen Prüfung auftauchen, und die Sicherung muss sich zum Scheitern bringen
/// lassen. Die Fälle laufen trotzdem in der Sammlung <see cref="IntegrationsSammlung"/>,
/// also nie gleichzeitig mit den übrigen Integrationsfällen.
/// </remarks>
public sealed class KiZugriffApp : IDisposable
{
    public IntegrationsApp App { get; }
    public UmschaltbareSicherung Sicherung { get; } = new();

    public KiZugriffApp()
    {
        App = new IntegrationsApp
        {
            // Wie im Add-on: mit Fehlerbehandler statt Entwicklerseite — sonst
            // liefe die Wiederholung nach /api/error nie (siehe Ausnahme-Fall).
            Umgebung = "Production",
            Zusatzdienste = dienste =>
            {
                dienste.AddControllers().ConfigureApplicationPartManager(teile => teile.ApplicationParts.Add(new KiTestControllerTeil()));
                dienste.RemoveAll<IKiSicherung>();
                dienste.AddSingleton<IKiSicherung>(Sicherung);
            },
        };
    }

    public HttpClient Oberflaeche() => App.IngressClient();

    /// <summary>Hauptschalter setzen, Höchstwerte wahlweise mit.</summary>
    public async Task SchalterAsync(bool aktiv, int maxSchaltbefehle = 1000)
    {
        var antwort = await Oberflaeche().PutAsJsonAsync("/api/settings/ki-zugriff", new
        {
            aktiv,
            rueckfrageAbStufe = "GrowPlanen",
            hoechstwerte = new { maxDosisMlJeBefehl = 10.0, maxSchaltbefehleJeStunde = maxSchaltbefehle },
        });
        antwort.EnsureSuccessStatusCode();
    }

    /// <summary>Einen Schlüssel über die Oberfläche anlegen; zurück kommt der Klartext.</summary>
    public async Task<(int Id, string Klartext)> SchluesselAsync(string name, params string[] stufen)
    {
        var antwort = await Oberflaeche().PostAsJsonAsync("/api/settings/ki-zugriff/schluessel", new { name, stufen });
        Assert.Equal(System.Net.HttpStatusCode.Created, antwort.StatusCode);
        var angelegt = await antwort.Content.ReadFromJsonAsync<KiSchluesselAngelegtDto>();
        return (angelegt!.Schluessel.Id, angelegt.Klartext);
    }

    public void Dispose() => App.Dispose();
}

/// <summary>Die echte Sicherung — oder auf Wunsch keine.</summary>
public sealed class UmschaltbareSicherung : IKiSicherung
{
    private readonly KiSicherungUeberSystemApi _echt = new();

    public bool Versagen { get; set; }

    public string? Anlegen(IServiceProvider dienste) => Versagen ? null : _echt.Anlegen(dienste);
}
