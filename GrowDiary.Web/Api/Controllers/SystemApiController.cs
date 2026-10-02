using System.IO.Compression;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
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

    /// <remarks>
    /// <paramref name="schema"/> kommt im Betrieb aus dem Container (Singleton aus
    /// Program.cs). Ohne Angabe — direkt gebaute Controller in Tests — entsteht
    /// einer für denselben Pfad; der Initialisierer hat keinen eigenen Zustand.
    /// </remarks>
    public SystemApiController(AppPaths paths, GrowRepository repository, SystemAuditRepository auditRepository,
        DatabaseInitializer? schema = null)
    {
        _paths = paths;
        _repository = repository;
        _auditRepository = auditRepository;
        _schema = schema ?? new DatabaseInitializer(paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance);
    }

}
