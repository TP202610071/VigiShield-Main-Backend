using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.Events;
using VigiShield.Application.Services;
using VigiShield.Common.Extensions;

namespace VigiShield.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly EventService _eventService;
    private readonly IConfiguration _config;

    public EventsController(EventService eventService, IConfiguration config)
    {
        _eventService = eventService;
        _config = config;
    }

    /// <summary>Internal endpoint — called only by the Python AI service.</summary>
    [HttpPost("ingest")]
    public async Task<IActionResult> IngestEvent([FromBody] IngestEventRequest request)
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (apiKey != _config["InternalApi:Key"])
            return Unauthorized(new { error = "API key inválida" });

        var result = await _eventService.IngestEventAsync(request);
        if (result is null)
            return NoContent(); // monitoring is paused for this household — event suppressed
        return CreatedAtAction(nameof(GetEvent), new { id = result.Id }, result);
    }

    /// <summary>Internal endpoint — the Python AI service attaches a recorded clip URL.</summary>
    [HttpPost("{id:guid}/clip")]
    public async Task<IActionResult> AttachClip(Guid id, [FromBody] AttachClipRequest request)
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (apiKey != _config["InternalApi:Key"])
            return Unauthorized(new { error = "API key inválida" });

        await _eventService.AttachClipAsync(id, request.VideoClipPath);
        return NoContent();
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<EventListResponse>> GetEvents(
        [FromQuery] string? type,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        return Ok(await _eventService.GetEventsAsync(
            User.GetHouseholdId(), type, from, to, page, pageSize));
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<EventDto>> GetEvent(Guid id)
    {
        return Ok(await _eventService.GetEventByIdAsync(User.GetHouseholdId(), id));
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteEvent(Guid id)
    {
        if (!User.IsPrimaryResident())
            return Forbid();

        await _eventService.DeleteEventAsync(User.GetHouseholdId(), id);
        return NoContent();
    }

    [HttpDelete("bulk")]
    [Authorize]
    public async Task<IActionResult> BulkDeleteEvents([FromBody] BulkDeleteRequest request)
    {
        if (!User.IsPrimaryResident())
            return Forbid();

        await _eventService.BulkDeleteEventsAsync(User.GetHouseholdId(), request.EventIds);
        return NoContent();
    }
}
