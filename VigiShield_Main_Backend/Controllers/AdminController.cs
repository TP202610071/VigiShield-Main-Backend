using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.Admin;
using VigiShield.Application.Services;
using VigiShield.Common.Security;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Controllers;

/// <summary>
/// Panel de administración (https://vigishield.app/admin). Solo rol Admin.
/// Para lanzar alertas el panel usa el endpoint existente POST /api/events/simulate,
/// y para gestionar administradores, los de /api/users/admins.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize]
[SoloAdmin]
public class AdminController(AdminService admin, HostMetrics metricas, IHttpClientFactory http) : ControllerBase
{
    [HttpGet("resumen")]
    public async Task<ActionResult<AdminResumenDto>> Resumen() => Ok(await admin.ResumenAsync());

    [HttpGet("usuarios")]
    public async Task<ActionResult<List<AdminUsuarioDto>>> Usuarios() => Ok(await admin.UsuariosAsync());

    [HttpGet("camaras")]
    public async Task<ActionResult<List<AdminCamaraDto>>> Camaras() => Ok(await admin.CamarasAsync());

    /// <summary>Activa o desactiva el análisis de IA de la cámara de cualquier participante.</summary>
    [HttpPut("camaras/{id:guid}/activa")]
    public async Task<ActionResult<AdminCamaraDto>> CambiarActiva(Guid id, [FromBody] AdminCambiarActivaRequest req)
        => Ok(await admin.CambiarActivaAsync(id, req.Activa));

    [HttpGet("eventos")]
    public async Task<ActionResult<AdminEventosPaginaDto>> Eventos([FromQuery] AdminFiltroEventos filtro)
        => Ok(await admin.EventosAsync(filtro));

    /// <summary>ZIP con eventos.csv, fotos y clips de los eventos del filtro.</summary>
    [HttpGet("eventos/exportar")]
    public async Task Exportar([FromQuery] AdminFiltroEventos filtro, CancellationToken ct)
    {
        // ZipArchive escribe la cabecera de cada archivo de forma síncrona.
        var control = HttpContext.Features.Get<IHttpBodyControlFeature>();
        if (control is not null) control.AllowSynchronousIO = true;
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"vigishield_eventos_{DateTime.UtcNow.AddHours(-5):yyyyMMdd_HHmm}.zip\"";
        var cliente = http.CreateClient("admin-export");
        await admin.ExportarAsync(filtro, Response.Body, cliente, ct);
    }

    /// <summary>CPU, RAM y disco de la VM del backend (Oracle).</summary>
    [HttpGet("metricas")]
    public IActionResult Metricas() => Ok(metricas.Leer());
}
