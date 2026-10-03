using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class TasksApiController : ApiControllerBase
{
    private readonly GrowRepository _growRepository;
    private readonly TaskRepository _taskRepository;
    private readonly AuditRepository _auditRepository;

    public TasksApiController(
        GrowRepository growRepository,
        TaskRepository taskRepository,
        AuditRepository auditRepository)
    {
        _growRepository = growRepository;
        _taskRepository = taskRepository;
        _auditRepository = auditRepository;
    }

    [HttpGet("grows/{growId:int}/tasks")]
    [ProducesResponseType(typeof(IReadOnlyList<GrowTaskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<GrowTaskDto>> List(int growId)
    {
        var grow = _growRepository.GetGrow(growId);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        }

        return Ok(_taskRepository.GetForGrow(growId).Select(task => task.ToDto()).ToList());
    }

    [HttpGet("tasks/{taskId:int}")]
    [ProducesResponseType(typeof(GrowTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowTaskDto> Detail(int taskId)
    {
        var task = _taskRepository.Get(taskId);
        if (task is null)
        {
            return NotFoundError("task_not_found", $"Aufgabe mit Id {taskId} existiert nicht.");
        }

        return Ok(task.ToDto());
    }

    [HttpPost("grows/{growId:int}/tasks")]
    [ProducesResponseType(typeof(GrowTaskDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowTaskDto> Create(int growId, [FromBody] GrowTaskCreateRequest request)
    {
        var grow = _growRepository.GetGrow(growId);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        try
        {
            var model = request.ToModel(growId);
            model.Id = _taskRepository.Create(model);
            _auditRepository.LogTaskCreated(growId, model.Id, model.Title);
            return CreatedAtAction(nameof(Detail), new { taskId = model.Id }, model.ToDto());
        }
        catch
        {
            ModelState.AddModelError(nameof(request.DueAtLocal), "Faelligkeitsdatum konnte nicht gelesen werden.");
            return ValidationError();
        }
    }

    [HttpPatch("tasks/{taskId:int}/status")]
    [ProducesResponseType(typeof(GrowTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowTaskDto> SetStatus(int taskId, [FromBody] TaskStatusUpdateRequest request)
    {
        var task = _taskRepository.Get(taskId);
        if (task is null)
        {
            return NotFoundError("task_not_found", $"Aufgabe mit Id {taskId} existiert nicht.");
        }

        _taskRepository.SetStatus(taskId, request.Status);
        task.Status = request.Status;
        task.CompletedAtUtc = request.Status == GrowTaskStatus.Open ? null : DateTime.UtcNow;
        _auditRepository.LogTaskStatusChanged(task.GrowId, taskId, task.Title, request.Status);

        return Ok(task.ToDto());
    }

    [HttpDelete("tasks/{taskId:int}")]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Delete(int taskId)
    {
        var task = _taskRepository.Get(taskId);
        if (task is null)
        {
            return NotFoundError("task_not_found", $"Aufgabe mit Id {taskId} existiert nicht.");
        }

        _taskRepository.Delete(taskId);
        _auditRepository.LogTaskDeleted(task.GrowId, taskId, task.Title);

        return NoContent();
    }
}
