using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.Push;
using VigiShield.Application.Services;
using VigiShield.Common.Extensions;

namespace VigiShield.Controllers;

/// <summary>Registro de los teléfonos que reciben notificaciones push.</summary>
[ApiController]
[Route("api/push")]
public class PushController(PushService push) : ControllerBase
{
    /// <summary>El teléfono avisa su token (al entrar y cada vez que cambia).</summary>
    [HttpPost("dispositivos")]
    [Authorize]
    public async Task<IActionResult> Registrar([FromBody] RegistrarDispositivoRequest request, CancellationToken ct)
    {
        await push.RegistrarAsync(User.GetUserId(), request, ct);
        return NoContent();
    }

    /// <summary>
    /// Al cerrar sesión. Sin sesión a propósito: el token largo y aleatorio basta,
    /// y así funciona aunque el JWT ya se haya borrado.
    /// </summary>
    [HttpPost("dispositivos/baja")]
    [AllowAnonymous]
    public async Task<IActionResult> Baja([FromBody] BajaDispositivoRequest request, CancellationToken ct)
    {
        await push.DarDeBajaAsync(request.Token, ct);
        return NoContent();
    }

    [HttpGet("estado")]
    [Authorize]
    public async Task<IActionResult> Estado(CancellationToken ct) => Ok(await push.EstadoAsync(User.GetUserId(), ct));

    /// <summary>Envía una notificación de prueba a los teléfonos del usuario.</summary>
    [HttpPost("prueba")]
    [Authorize]
    public async Task<IActionResult> Prueba(CancellationToken ct) => Ok(await push.EnviarPruebaAsync(User.GetUserId(), ct));
}
