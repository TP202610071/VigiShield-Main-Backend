using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Controllers;

[ApiController]
[Route("api/media")]
[Authorize]
public class MediaController : ControllerBase
{
    private readonly R2Service _r2;

    public MediaController(R2Service r2) => _r2 = r2;

    /// <summary>Upload an app screenshot to R2; returns its public URL.</summary>
    [HttpPost("screenshot")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> UploadScreenshot(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Archivo de imagen vacío" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png")) ext = ".jpg";

        await using var stream = file.OpenReadStream();
        var url = await _r2.UploadAsync(
            stream, file.ContentType ?? "image/jpeg", "screenshots", ext);

        if (url is null)
            return StatusCode(503, new { error = "Almacenamiento de medios no configurado" });

        return Ok(new { url });
    }
}
