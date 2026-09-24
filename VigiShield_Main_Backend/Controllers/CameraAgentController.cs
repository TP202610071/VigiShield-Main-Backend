using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Controllers;

/// <summary>
/// Endpoints que consume el agente que corre en la casa del usuario.
///
/// Existe porque el backend vive en la nube y no alcanza una IP privada: sin
/// algo dentro de la vivienda, los ajustes de imagen de la cámara solo se
/// pueden cambiar con el teléfono en el wifi de casa. El agente convive con el
/// relay de FFmpeg que el usuario ya deja corriendo.
///
/// Se autentica con la clave interna (X-Api-Key), igual que el backend de IA.
/// </summary>
[ApiController]
[Route("api/agent")]
[AllowAnonymous]
public class CameraAgentController : ControllerBase
{
    private readonly CameraAgentBroker _broker;
    private readonly IConfiguration _config;

    public CameraAgentController(CameraAgentBroker broker, IConfiguration config)
    {
        _broker = broker;
        _config = config;
    }

    private bool ClaveValida() =>
        Request.Headers["X-Api-Key"].FirstOrDefault() == _config["InternalApi:Key"];

    /// <summary>
    /// Consulta larga: devuelve las órdenes pendientes para esa vivienda. Si no
    /// hay ninguna, deja la petición abierta unos segundos en vez de contestar
    /// vacío al instante, para que el agente reaccione rápido sin martillear.
    /// </summary>
    [HttpGet("camera-commands")]
    public async Task<IActionResult> Recoger([FromQuery] Guid householdId, CancellationToken ct)
    {
        if (!ClaveValida()) return Unauthorized();
        if (householdId == Guid.Empty) return BadRequest(new { error = "householdId requerido" });

        var ordenes = await _broker.RecogerAsync(householdId, ct);
        return Ok(ordenes);
    }

    /// <summary>Resultado de una orden ya ejecutada contra la cámara.</summary>
    [HttpPost("camera-commands/{commandId:guid}")]
    public IActionResult Responder(Guid commandId, [FromQuery] Guid householdId,
        [FromBody] CameraAgentResult resultado)
    {
        if (!ClaveValida()) return Unauthorized();

        // Falso significa que la app ya dejó de esperar. No es un error del
        // agente, así que se acepta igual y no se le hace reintentar.
        _broker.Responder(householdId, commandId, resultado);
        return NoContent();
    }
}
