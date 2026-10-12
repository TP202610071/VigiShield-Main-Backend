using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.Auth;
using VigiShield.Application.Services;
using VigiShield.Common.Extensions;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Controllers;

/// <summary>
/// Iniciar sesión con Apple. Va aparte de AuthController para no tocarlo,
/// pero cuelga de la misma ruta (/api/auth).
/// </summary>
[ApiController]
[Route("api/auth")]
public class LoginExternoController(LoginExternoService externos, AppleSignInService apple) : ControllerBase
{
    /// <summary>Entra (o se registra) con el token de Apple.</summary>
    [HttpPost("apple")]
    [AllowAnonymous]
    public async Task<IActionResult> ConApple([FromBody] AppleLoginRequest request, CancellationToken ct)
        => Ok(await externos.ConAppleAsync(request, ct));

    /// <summary>Con qué proveedores está vinculada la cuenta (la app decide cómo confirmar el borrado).</summary>
    [HttpGet("externo")]
    [Authorize]
    public async Task<IActionResult> Estado(CancellationToken ct)
        => Ok(await externos.EstadoAsync(User.GetUserId(), ct));

    /// <summary>Lo que la app necesita para ofrecer Apple en Android. Sin Services ID, Android no lo muestra.</summary>
    [HttpGet("externo/config")]
    [AllowAnonymous]
    public IActionResult Config()
        => Ok(new LoginExternoConfigDto(apple.ServiceId is { } s ? new AppleAndroidDto(s, apple.RedirectUri) : null));

    /// <summary>Borra la cuenta confirmando con Apple en vez de con la contraseña.</summary>
    [HttpPost("delete-account/apple")]
    [Authorize]
    public async Task<IActionResult> EliminarConApple([FromBody] EliminarConAppleRequest request, CancellationToken ct)
    {
        var r = await externos.EliminarConAppleAsync(User.GetUserId(), request, ct);
        return Ok(new { hogarEliminado = r.HogarEliminado, usuariosEliminados = r.UsuariosEliminados });
    }

    /// <summary>
    /// Vuelta del flujo web de Apple (Android): Apple envía el formulario aquí y
    /// se lo pasamos a la app por un intent, como pide sign_in_with_apple.
    /// </summary>
    [HttpPost("apple/callback")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    public IActionResult VueltaDeApple()
    {
        var consulta = string.Join("&", Request.Form.Select(c =>
            $"{Uri.EscapeDataString(c.Key)}={Uri.EscapeDataString(c.Value.ToString())}"));
        return RedirectPreserveMethod(
            $"intent://callback?{consulta}#Intent;package={apple.PaqueteAndroid};scheme=signinwithapple;end");
    }
}
