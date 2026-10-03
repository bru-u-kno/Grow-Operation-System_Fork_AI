using GrowDiary.Web.Services;
using System.IO.Compression;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

// Fork AI (A-003, 03.10.2026): Sicherungen, Restore-Plan, Upgrade-Vorprüfung —
// über einen Schlüssel Verwaltung. Das Zurückspielen trägt zusätzlich
// [KiSicherungVorher] (SystemApiController.BackupEndpoints.cs). Die
// Upgrade-Vorprüfung migriert nichts; sie legt selbst eine Sicherung an.
[ApiController]
[KiStufe(KiStufe.Verwaltung)]
[Route("api/system")]
[Produces("application/json")]
public sealed partial class SystemApiController : ApiControllerBase
{
    private readonly AppPaths _paths;
    private readonly GrowRepository _repository;
    private readonly SystemAuditRepository _auditRepository;

    /// <summary>
    /// Das Kern-Schema — derselbe Lauf wie beim Start (Program.cs), damit eine
    /// zurückgespielte Sicherung sofort alle Schema-Ergänzungen bekommt.
    /// </summary>
    private readonly DatabaseInitializer _schema;

    /// <summary>Der Neustart nach dem Zurückspielen einer Sicherung (siehe <see cref="SupervisorNeustart"/>).</summary>
    private readonly IAppNeustart? _neustart;

    /// <remarks>
    /// <paramref name="schema"/> kommt im Betrieb aus dem Container (Singleton aus
    /// Program.cs). Ohne Angabe — direkt gebaute Controller in Tests — entsteht
    /// einer für denselben Pfad; der Initialisierer hat keinen eigenen Zustand.
    /// <paramref name="neustart"/> fehlt in Tests — dann wird nichts neu
    /// gestartet, und das Ergebnis sagt, dass ein Neustart nötig ist.
    /// </remarks>
    public SystemApiController(AppPaths paths, GrowRepository repository, SystemAuditRepository auditRepository,
        DatabaseInitializer? schema = null, IAppNeustart? neustart = null)
    {
        _neustart = neustart;
        _paths = paths;
        _repository = repository;
        _auditRepository = auditRepository;
        _schema = schema ?? new DatabaseInitializer(paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance);
    }

}
