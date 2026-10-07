using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.Services;
using VigiShield.Common.Security;

namespace VigiShield.Controllers;

/// <summary>
/// Interruptor «Grabar evidencia» (ver <see cref="EvidenciaService"/>).
/// El panel de administración lo lee y lo cambia; el grabador de la VM de IA
/// consulta qué hogares grabar con la clave interna.
/// </summary>
[ApiController]
public class EvidenciaController(EvidenciaService evidencia, IConfiguration config) : ControllerBase
{
    public record CambiarRequest(bool Grabar);

    [HttpGet("api/admin/evidencia")]
    [Authorize]
    [SoloAdmin]
    public async Task<IActionResult> Listar(CancellationToken ct) => Ok(await evidencia.ListarAsync(ct));

    [HttpPut("api/admin/evidencia/{hogarId:guid}")]
    [Authorize]
    [SoloAdmin]
    public async Task<IActionResult> Cambiar(Guid hogarId, [FromBody] CambiarRequest request, CancellationToken ct) =>
        Ok(await evidencia.CambiarAsync(hogarId, request.Grabar, ct));

    [HttpGet("api/internal/evidencia/hogares")]
    public async Task<IActionResult> ParaGrabador(CancellationToken ct)
    {
        var clave = config["InternalApi:Key"];
        if (string.IsNullOrEmpty(clave) || Request.Headers["X-Api-Key"].FirstOrDefault() != clave)
            return Unauthorized();
        var (grabar, existentes) = await evidencia.ParaGrabadorAsync(ct);
        return Ok(new { grabar, existentes });
    }
}
