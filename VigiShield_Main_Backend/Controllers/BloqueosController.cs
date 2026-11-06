using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VigiShield.Application.Services;
using VigiShield.Common.Security;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Controllers;

/// <summary>Lo que la app y la IA necesitan saber de los bloqueos del administrador.</summary>
[ApiController]
public class BloqueosController(BloqueosService bloqueos, AppDbContext db, IConfiguration config) : ControllerBase
{
    /// <summary>
    /// Para la app: si la cuenta o este teléfono están bloqueados y qué cámaras
    /// del hogar no se analizan por decisión del administrador.
    /// </summary>
    [Authorize]
    [HttpGet("api/bloqueos/estado")]
    public async Task<IActionResult> Estado()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var inst = await bloqueos.InstantaneaAsync();
        var hogar = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.HouseholdId).FirstOrDefaultAsync();
        var deviceId = Request.Headers["X-Device-Id"].FirstOrDefault();
        var ip = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString();
        var cuenta = inst.Cuentas.Contains(userId) || inst.Hogares.Contains(hogar);
        var dispositivo = (deviceId is not null && inst.Dispositivos.Contains(deviceId))
            || (ip is not null && inst.Ips.Contains(ip));
        var camaras = await db.CameraConfigs.AsNoTracking().Where(c => c.HouseholdId == hogar)
            .Select(c => c.Id).ToListAsync();
        return Ok(new
        {
            cuenta,
            dispositivo,
            camaras = camaras.Where(inst.Camaras.Contains).ToList(),
            contacto = config["Soporte:Correo"],
        });
    }

    /// <summary>Para la IA: ids de las cámaras que no debe analizar.</summary>
    [HttpGet("api/internal/ai-bloqueos")]
    public async Task<IActionResult> ParaIa()
    {
        if (Request.Headers["X-Api-Key"].FirstOrDefault() != config["InternalApi:Key"])
            return Unauthorized(new { error = "API key inválida" });
        return Ok((await bloqueos.InstantaneaAsync()).Camaras);
    }
}

/// <summary>Panel: crear, listar y quitar bloqueos; ver teléfonos compartidos entre cuentas.</summary>
[ApiController]
[Route("api/admin/bloqueos")]
[Authorize]
[SoloAdmin]
public class AdminBloqueosController(BloqueosService bloqueos) : ControllerBase
{
    public sealed record NuevoBloqueo(string Tipo, string Valor, string? Motivo);

    [HttpGet]
    public async Task<IActionResult> Listar() => Ok(await bloqueos.ListarAsync());

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] NuevoBloqueo b)
    {
        var tipo = (b.Tipo ?? string.Empty).Trim().ToLowerInvariant();
        var valor = (b.Valor ?? string.Empty).Trim();
        if (!BloqueosService.Tipos.Contains(tipo)) return BadRequest(new { error = "Tipo inválido" });
        if (valor.Length is 0 or > 128) return BadRequest(new { error = "Valor inválido" });
        if (tipo is "camara" or "cuenta" && !Guid.TryParse(valor, out _))
            return BadRequest(new { error = "Se esperaba un id" });
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId);
        var motivo = b.Motivo?.Trim();
        return Ok(await bloqueos.CrearAsync(tipo, valor, motivo is { Length: > 0 } ? motivo[..Math.Min(300, motivo.Length)] : null, adminId));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Quitar(Guid id) => await bloqueos.QuitarAsync(id) ? NoContent() : NotFound();

    [HttpGet("dispositivos")]
    public async Task<IActionResult> Dispositivos([FromQuery] string? buscar) => Ok(await bloqueos.DispositivosAsync(buscar));
}
