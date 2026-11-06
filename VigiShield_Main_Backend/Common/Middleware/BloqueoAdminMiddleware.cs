using System.Security.Claims;
using System.Text.RegularExpressions;
using VigiShield.Application.Services;

namespace VigiShield.Common.Middleware;

/// <summary>
/// 1) Registra desde qué teléfono (cabecera X-Device-Id) e IP entra cada cuenta.
/// 2) Si el administrador bloqueó la cámara, la cuenta, el teléfono o la IP,
///    rechaza con 403 lo que pone a trabajar a la VM de IA: agregar o editar
///    una cámara, activarla, transmitir desde el celular y pedir el video de
///    ejemplo. Lo demás (ver eventos, borrar cámaras, zonas, avisos) sigue igual.
/// </summary>
public partial class BloqueoAdminMiddleware(RequestDelegate next, BloqueosService bloqueos)
{
    public const string Codigo = "BLOQUEADO_ADMIN";

    [GeneratedRegex("^[A-Za-z0-9._:-]{8,128}$")]
    private static partial Regex DeviceIdValido();

    [GeneratedRegex("^/api/stream/(cameras(/(?<id>[0-9a-fA-F-]{36})(/(?<accion>publish|active))?)?|sample-video)/?$")]
    private static partial Regex RutaQueCarga();

    public async Task InvokeAsync(HttpContext context)
    {
        var deviceId = context.Request.Headers["X-Device-Id"].FirstOrDefault();
        if (deviceId is not null && !DeviceIdValido().IsMatch(deviceId)) deviceId = null;
        var ip = context.Connection.RemoteIpAddress?.MapToIPv4().ToString();
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);

        if (userId != Guid.Empty && deviceId is not null)
        {
            var plataforma = context.Request.Headers["X-Device-Platform"].FirstOrDefault();
            bloqueos.Registrar(userId, deviceId, ip, plataforma is { Length: <= 20 } ? plataforma : null);
        }

        var metodo = context.Request.Method;
        if (userId != Guid.Empty && metodo is "POST" or "PUT" or "PATCH")
        {
            var m = RutaQueCarga().Match(context.Request.Path.Value ?? string.Empty);
            // PUT /cameras/{id} edita; POST /cameras/{id} no existe. Las rutas sin id solo cuentan con POST.
            var cuenta = m.Success && (m.Groups["id"].Success || metodo == "POST");
            if (cuenta)
            {
                var inst = await bloqueos.InstantaneaAsync();
                var camara = Guid.TryParse(m.Groups["id"].Value, out var cid) && inst.CamarasDirectas.Contains(cid);
                var bloqueado = camara
                    || inst.Cuentas.Contains(userId)
                    || (deviceId is not null && inst.Dispositivos.Contains(deviceId))
                    || (ip is not null && inst.Ips.Contains(ip));
                if (bloqueado)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        codigo = Codigo,
                        error = camara
                            ? "Esta cámara fue deshabilitada por el administrador. Comunícate con el equipo de VigiShield para más información."
                            : "Las cámaras de esta cuenta fueron deshabilitadas por el administrador. Comunícate con el equipo de VigiShield para más información.",
                    });
                    return;
                }
            }
        }
        await next(context);
    }
}
