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
    private readonly DatabaseInitializer _initialisierer;

    public SystemApiController(AppPaths paths, GrowRepository repository, SystemAuditRepository auditRepository,
        DatabaseInitializer? initialisierer = null)
    {
        _paths = paths;
        _repository = repository;
        _auditRepository = auditRepository;
        // Fork AI (forkai.157): fuer die Wiederherstellung — nach dem Dateitausch
        // laeuft, was sonst nur beim Start laeuft. Ohne DI (Tests) derselbe Weg.
        _initialisierer = initialisierer
            ?? new DatabaseInitializer(paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance);
    }

}
