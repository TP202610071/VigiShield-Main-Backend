using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.Admin;
using VigiShield.Application.Services;
using VigiShield.Common.Exceptions;
using VigiShield.Common.Security;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Controllers;

/// <summary>
/// Panel de administración → «Notificaciones»: enviar una notificación push a una
/// cuenta o a un hogar (iPhone por APNs y Android por FCM). Solo rol Admin.
/// </summary>
[ApiController]
[Route("api/admin/push")]
[Authorize]
[SoloAdmin]
public class AdminPushController(PushService push) : ControllerBase
{
    // Pantallas de la app que se pueden abrir al tocar la notificación.
    private static readonly Dictionary<string, string> Rutas = new()
    {
        ["inicio"] = "/dashboard",
        ["camara"] = "/camera",
        ["historial"] = "/history",
        ["ajustes"] = "/settings",
    };

    [HttpGet("dispositivos")]
    public async Task<IActionResult> Dispositivos(CancellationToken ct) => Ok(await push.DispositivosPorUsuarioAsync(ct));

    [HttpPost("enviar")]
    public async Task<IActionResult> Enviar([FromBody] AdminPushRequest r, CancellationToken ct)
    {
        var nivel = (r.Nivel ?? MensajePush.Normal).Trim().ToLowerInvariant();
        if (nivel is not (MensajePush.Urgente or MensajePush.Normal or MensajePush.Silencioso))
            throw new AppException("Nivel no válido.");
        var imagen = string.IsNullOrWhiteSpace(r.ImagenUrl) ? null : r.ImagenUrl.Trim();
        if (imagen is not null && (!Uri.TryCreate(imagen, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new AppException("La imagen debe ser una URL https.");

        // La app recibe el destino en "eventId": un id de evento o una ruta que empieza con «/».
        var datos = new Dictionary<string, string> { ["type"] = "Admin" };
        var abrir = (r.Abrir ?? "inicio").Trim().ToLowerInvariant();
        if (abrir == "evento")
        {
            if (r.EventoId is not Guid evento) throw new AppException("Falta el id del evento.");
            datos["eventId"] = evento.ToString();
        }
        else if (Rutas.TryGetValue(abrir, out var ruta))
        {
            datos["eventId"] = ruta;
        }

        var mensaje = new MensajePush(r.Titulo.Trim(), string.IsNullOrWhiteSpace(r.Subtitulo) ? null : r.Subtitulo.Trim(),
            r.Cuerpo.Trim(), imagen, nivel, r.Sonido, r.Insignia, datos);
        return Ok(await push.EnviarAdminAsync(r.Destino.Trim().ToLowerInvariant(), r.Id, mensaje, ct));
    }
}
