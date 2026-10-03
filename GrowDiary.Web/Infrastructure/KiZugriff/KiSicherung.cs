using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Legt vor einer <see cref="KiSicherungVorherAttribute"/>-Aktion eine Sicherung an.
/// </summary>
/// <remarks>
/// Eine Schnittstelle nur, damit ein Test das Scheitern nachstellen kann —
/// „schlägt die Sicherung fehl, läuft nichts" lässt sich anders nicht belegen.
/// </remarks>
public interface IKiSicherung
{
    /// <summary>Der Dateiname der angelegten Sicherung, oder null, wenn keine entstand.</summary>
    string? Anlegen(IServiceProvider dienste);
}

/// <summary>
/// Dieselbe Sicherung wie der Knopf in der Oberfläche: <c>POST /api/system/backup</c>.
/// </summary>
/// <remarks>
/// <para>Es gibt keinen eigenen Sicherungsdienst — das Anlegen steht in
/// <see cref="SystemApiController.CreateBackup"/>, und das Zurückspielen ruft
/// es für seine Vorab-Sicherung genauso auf. Hier also derselbe Aufruf, nicht
/// ein Nachbau: eine zweite Stelle, die eine Sicherung zusammenstellt, liefe
/// beim nächsten neuen Ordner auseinander.</para>
///
/// <para>Der Controller wird dafür nur gebaut, nicht über HTTP gerufen; seine
/// Abhängigkeiten sind Singletons.</para>
/// </remarks>
public sealed class KiSicherungUeberSystemApi : IKiSicherung
{
    public string? Anlegen(IServiceProvider dienste)
    {
        try
        {
            var system = ActivatorUtilities.CreateInstance<SystemApiController>(dienste);
            var ergebnis = system.CreateBackup();
            return ergebnis.Result is CreatedResult { Value: BackupManifestDto manifest } ? manifest.FileName : null;
        }
        catch (Exception ex)
        {
            // Keine Sicherung heisst: nichts ausführen. Der Grund gehört ins Log —
            // die Antwort an den Assistenten sagt nur, dass es nicht ging.
            dienste.GetService<ILogger<KiSicherungUeberSystemApi>>()?.LogError(ex, "KI-Zugriff: Sicherung vor der Aktion fehlgeschlagen.");
            return null;
        }
    }
}
