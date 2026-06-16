using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.Auth;
using VigiShield.Application.Services;
using VigiShield.Common.Extensions;

namespace VigiShield.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly IWebHostEnvironment _env;

    public AuthController(AuthService authService, IWebHostEnvironment env)
    {
        _authService = authService;
        _env = env;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        return CreatedAtAction(nameof(Me), result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        return Ok(await _authService.LoginAsync(request));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> Me()
    {
        return Ok(await _authService.GetProfileAsync(User.GetUserId()));
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        return Ok(await _authService.UpdateProfileAsync(User.GetUserId(), request));
    }

    [HttpPost("avatar")]
    [Authorize]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<UserProfileDto>> UploadAvatar(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Archivo de imagen vacío" });
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { error = "La imagen supera el límite de 5 MB" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) ext = ".jpg";

        var userId = User.GetUserId();
        var dir = Path.Combine(_env.ContentRootPath, "wwwroot", "avatars");
        Directory.CreateDirectory(dir);

        // Replace any previous avatar for this user (possibly a different extension).
        foreach (var old in Directory.GetFiles(dir, userId + ".*"))
        {
            try { System.IO.File.Delete(old); } catch { /* ignore */ }
        }

        var fileName = $"{userId}{ext}";
        await using (var stream = System.IO.File.Create(Path.Combine(dir, fileName)))
            await file.CopyToAsync(stream);

        // Cache-bust so the app reloads the new picture even though the name is stable.
        var relative = $"/avatars/{fileName}?v={DateTime.UtcNow.Ticks}";
        return Ok(await _authService.SetAvatarAsync(userId, relative));
    }

    [HttpPut("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        await _authService.ChangePasswordAsync(User.GetUserId(), request);
        return NoContent();
    }

    [HttpPost("recover-password")]
    public async Task<IActionResult> RecoverPassword([FromBody] RecoverPasswordRequest request)
    {
        await _authService.RecoverPasswordAsync(request);
        return Ok(new { message = "Si el correo existe, recibirás un enlace de recuperación" });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await _authService.ResetPasswordAsync(request);
        return Ok(new { message = "Contraseña restablecida exitosamente" });
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        // JWT is stateless — client discards the token
        return NoContent();
    }
}
