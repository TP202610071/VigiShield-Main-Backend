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
    private readonly ConfigService _configService;
    private readonly IConfiguration _config;
    private readonly WhipPublishService _publish;

    public StreamController(
        CameraService cameraService,
        FaceService faceService,
        CameraControlService cameraControl,
        ConfigService configService,
        IConfiguration config,
        WhipPublishService publish)
    {
        _cameraService = cameraService;
        _faceService = faceService;
        _cameraControl = cameraControl;
        _configService = configService;
        _config = config;
        _publish = publish;
    }

    [HttpPost("cameras/{cameraId:guid}/publish")]
    [Authorize]
    [RequestSizeLimit(131072)]
    public async Task<ActionResult<PublishResponse>> Publish(Guid cameraId, [FromBody] PublishRequest request, CancellationToken ct)
    {
        if (!User.IsPrimaryResident()) return Forbid();
        return Ok(await _publish.PublishAsync(User.GetHouseholdId(), cameraId, request.Sdp, ct));
    }

    [HttpDelete("cameras/{cameraId:guid}/publish/{sessionId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeletePublish(Guid cameraId, Guid sessionId, CancellationToken ct)
    {
        if (!User.IsPrimaryResident()) return Forbid();
        await _publish.DeleteAsync(User.GetHouseholdId(), cameraId, sessionId, ct);
        return NoContent();
    }

    [HttpPatch("cameras/{cameraId:guid}/notifications")]
    [Authorize]
    public async Task<ActionResult<CameraConfigDto>> UpdateNotifications(Guid cameraId, [FromBody] UpdateNotificationsRequest request)
    {
        if (!User.IsPrimaryResident()) return Forbid();
        if (!request.Enabled.HasValue) return BadRequest();
        return Ok(await _cameraService.UpdateNotificationsAsync(User.GetHouseholdId(), cameraId, request.Enabled.Value));
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

    // ── Zonas de interés (ROI) ────────────────────────────────────────────────

    /// <summary>Guarda las zonas dibujadas por el usuario para una cámara. Residentes primarios.</summary>
    [HttpPut("cameras/{cameraId:guid}/zones")]
    [Authorize]
    public async Task<ActionResult<CameraConfigDto>> UpdateZones(Guid cameraId, [FromBody] UpdateZonesRequest request)
    {
        if (!User.IsPrimaryResident()) return Forbid();
        return Ok(await _cameraService.UpdateZonesAsync(User.GetHouseholdId(), cameraId, request));
    }

    // ── Video de ejemplo ──────────────────────────────────────────────────────

    /// <summary>Video de ejemplo en curso del hogar (204 si no hay ninguno).</summary>
    [HttpGet("sample-video")]
    [Authorize]
    public async Task<IActionResult> GetSampleVideo()
    {
        var dto = await _cameraService.GetSampleVideoAsync(User.GetHouseholdId());
        return dto is null ? NoContent() : Ok(dto);
    }

    /// <summary>
    /// Reproduce un video de ejemplo unos minutos para ver alertas y eventos
    /// sin provocar la escena en casa. Cualquier miembro del hogar. Con
    /// <c>otro=true</c> cambia al siguiente video aunque haya uno en curso.
    /// </summary>
    [HttpPost("sample-video")]
    [Authorize]
    public async Task<ActionResult<SampleVideoDto>> StartSampleVideo([FromQuery] bool otro = false)
        => Ok(await _cameraService.StartSampleVideoAsync(User.GetHouseholdId(), User.GetUserId(), otro));

    /// <summary>Termina antes de tiempo el video de ejemplo en curso.</summary>
    [HttpDelete("sample-video")]
    [Authorize]
    public async Task<IActionResult> StopSampleVideo()
    {
        await _cameraService.StopSampleVideoAsync(User.GetHouseholdId());
        return NoContent();
    }

    // ── Live camera image/video controls (hi3510 CGI) ─────────────────────────

    /// <summary>Read the camera's current image/video settings (brightness, etc.).</summary>
    [HttpGet("cameras/{cameraId:guid}/control")]
    [Authorize]
    public async Task<IActionResult> GetCameraControls(Guid cameraId)
        => Ok(await _cameraControl.GetSettingsAsync(User.GetHouseholdId(), cameraId));

    /// <summary>
    /// IP y credenciales de la cámara para controlarla desde la red local.
    /// El backend en la nube no alcanza una IP privada, así que el ajuste de
    /// imagen lo hace la app cuando el teléfono está en el wifi de casa.
    /// </summary>
    [HttpGet("cameras/{cameraId:guid}/lan-access")]
    [Authorize]
    public async Task<IActionResult> GetCameraLanAccess(Guid cameraId)
    {
        if (!User.IsPrimaryResident()) return Forbid();
        return Ok(await _cameraControl.GetLanAccessAsync(User.GetHouseholdId(), cameraId));
    }

    /// <summary>
    /// Enciende o apaga el procesamiento de IA de una cámara.
    /// Desactivarla es la única forma de que deje de consumir recursos.
    /// </summary>
    [HttpPatch("cameras/{cameraId:guid}/active")]
    [Authorize]
    public async Task<ActionResult<CameraConfigDto>> UpdateActive(Guid cameraId, [FromBody] UpdateNotificationsRequest request)
    {
        if (!User.IsPrimaryResident()) return Forbid();
        if (!request.Enabled.HasValue) return BadRequest();
        return Ok(await _cameraService.UpdateActiveAsync(User.GetHouseholdId(), cameraId, request.Enabled.Value));
    }

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

    /// <summary>Returns the household's alert toggle config (Python AI service — suppresses disabled event types).</summary>
    [HttpGet("ai-alert-config")]
    public async Task<IActionResult> GetAiAlertConfig([FromQuery] Guid householdId)
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (apiKey != _config["InternalApi:Key"])
            return Unauthorized(new { error = "API key inválida" });

        return Ok(await _configService.GetAlertConfigAsync(householdId));
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
