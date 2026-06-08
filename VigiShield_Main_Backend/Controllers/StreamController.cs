using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.Stream;
using VigiShield.Application.Services;
using VigiShield.Common.Extensions;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Controllers;

[ApiController]
[Route("api/stream")]
public class StreamController : ControllerBase
{
    private readonly CameraService _cameraService;
    private readonly FaceService _faceService;
    private readonly CameraControlService _cameraControl;
    private readonly IConfiguration _config;

    public StreamController(
        CameraService cameraService,
        FaceService faceService,
        CameraControlService cameraControl,
        IConfiguration config)
    {
        _cameraService = cameraService;
        _faceService = faceService;
        _cameraControl = cameraControl;
        _config = config;
    }

    // ── Multi-camera CRUD ─────────────────────────────────────────────────────

    /// <summary>List all cameras configured for this household.</summary>
    [HttpGet("cameras")]
    [Authorize]
    public async Task<ActionResult<List<CameraConfigDto>>> GetCameras()
        => Ok(await _cameraService.GetCamerasAsync(User.GetHouseholdId()));

    /// <summary>Get a specific camera by ID.</summary>
    [HttpGet("cameras/{cameraId:guid}")]
    [Authorize]
    public async Task<ActionResult<CameraConfigDto>> GetCamera(Guid cameraId)
        => Ok(await _cameraService.GetCameraAsync(User.GetHouseholdId(), cameraId));

    /// <summary>Add a new camera. Primary residents only.</summary>
    [HttpPost("cameras")]
    [Authorize]
    public async Task<ActionResult<CameraConfigDto>> CreateCamera([FromBody] UpdateCameraConfigRequest request)
    {
        if (!User.IsPrimaryResident()) return Forbid();
        var dto = await _cameraService.CreateCameraAsync(User.GetHouseholdId(), request);
        return CreatedAtAction(nameof(GetCamera), new { cameraId = dto.Id }, dto);
    }

    /// <summary>Update a specific camera. Primary residents only.</summary>
    [HttpPut("cameras/{cameraId:guid}")]
    [Authorize]
    public async Task<ActionResult<CameraConfigDto>> UpdateCamera(Guid cameraId, [FromBody] UpdateCameraConfigRequest request)
    {
        if (!User.IsPrimaryResident()) return Forbid();
        return Ok(await _cameraService.UpdateCameraAsync(User.GetHouseholdId(), cameraId, request));
    }

    /// <summary>Delete a camera. Primary residents only.</summary>
    [HttpDelete("cameras/{cameraId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteCamera(Guid cameraId)
    {
        if (!User.IsPrimaryResident()) return Forbid();
        await _cameraService.DeleteCameraAsync(User.GetHouseholdId(), cameraId);
        return NoContent();
    }

    // ── Live camera image/video controls (hi3510 CGI) ─────────────────────────

    /// <summary>Read the camera's current image/video settings (brightness, etc.).</summary>
    [HttpGet("cameras/{cameraId:guid}/control")]
    [Authorize]
    public async Task<IActionResult> GetCameraControls(Guid cameraId)
        => Ok(await _cameraControl.GetSettingsAsync(User.GetHouseholdId(), cameraId));

    /// <summary>Apply image/video settings to the camera. Primary residents only.</summary>
    [HttpPut("cameras/{cameraId:guid}/control")]
    [Authorize]
    public async Task<IActionResult> UpdateCameraControls(
        Guid cameraId, [FromBody] Dictionary<string, string> settings)
    {
        if (!User.IsPrimaryResident()) return Forbid();
        await _cameraControl.ApplySettingsAsync(User.GetHouseholdId(), cameraId, settings);
        return NoContent();
    }

    // ── Backwards-compat: default camera ─────────────────────────────────────

    /// <summary>Returns HLS URL of the default camera (Flutter stream tab).</summary>
    [HttpGet("url")]
    [Authorize]
    public async Task<IActionResult> GetStreamUrl()
    {
        var cam = await _cameraService.GetDefaultCameraAsync(User.GetHouseholdId());
        return Ok(new { url = cam?.HlsViewUrl });
    }

    // ── Python AI Backend ─────────────────────────────────────────────────────

    /// <summary>Returns RTSP config for all cameras of a household (Python AI service).</summary>
    [HttpGet("ai-config")]
    public async Task<IActionResult> GetAiConfig([FromQuery] Guid householdId)
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (apiKey != _config["InternalApi:Key"])
            return Unauthorized(new { error = "API key inválida" });

        var cameras = await _cameraService.GetAiConfigAsync(householdId);
        return Ok(cameras);
    }

    /// <summary>Returns RTSP config for ALL cameras across ALL households (Python AI service).</summary>
    [HttpGet("ai-config/all")]
    public async Task<IActionResult> GetAllAiConfig()
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (apiKey != _config["InternalApi:Key"])
            return Unauthorized(new { error = "API key inválida" });

        var cameras = await _cameraService.GetAllAiConfigAsync();
        return Ok(cameras);
    }

    /// <summary>Returns authorized faces for a household (Python AI service for DeepFace).</summary>
    [HttpGet("ai-faces")]
    public async Task<IActionResult> GetAiFaces([FromQuery] Guid householdId)
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (apiKey != _config["InternalApi:Key"])
            return Unauthorized(new { error = "API key inválida" });

        var faces = await _faceService.GetFacesAsync(householdId);
        return Ok(faces);
    }
}
