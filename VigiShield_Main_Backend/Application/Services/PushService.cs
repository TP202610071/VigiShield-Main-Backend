using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Push;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Application.Services;

/// <summary>
/// Notificaciones push de los eventos: APNs directo a los iPhone y FCM a los
/// Android. Llegan a todos los teléfonos registrados del hogar, con las mismas
/// reglas que WhatsApp (cámara con avisos activados y riesgo medio o mayor).
///
/// Los tokens que APNs o FCM dan por muertos se borran solos. En iPhone, si un
/// token es de sandbox y se envió a producción (o al revés), se reintenta en el
/// otro servidor y se corrige el registro.
/// </summary>
public class PushService(
    AppDbContext db,
    ApnsService apns,
    FcmService fcm,
    IServiceScopeFactory scopes,
    ILogger<PushService> logger)
{
    public const string Ios = "ios";
    public const string Android = "android";
    public const string Produccion = "produccion";
    public const string Sandbox = "sandbox";
    private const int MaximoPorUsuario = 10;

    public record Destino(Guid Id, string Plataforma, string Token, string Entorno);
    private record Resultado(Destino Destino, ResultadoPush Estado, string? EntornoCorregido);

    public async Task RegistrarAsync(Guid userId, RegistrarDispositivoRequest req, CancellationToken ct = default)
    {
        var plataforma = req.Plataforma.Trim().ToLowerInvariant();
        if (plataforma is not (Ios or Android)) throw new AppException("Plataforma no válida.");
        var token = req.Token.Trim();
        var entorno = plataforma == Ios && req.Entorno?.Trim().ToLowerInvariant() == Sandbox ? Sandbox : Produccion;

        var dispositivos = db.Set<DispositivoPush>();
        var existente = await dispositivos.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (existente is null)
        {
            dispositivos.Add(new DispositivoPush
            {
                Id = Guid.NewGuid(), UserId = userId, Plataforma = plataforma, Token = token, Entorno = entorno,
            });
        }
        else
        {
            // Si en ese teléfono entra otra cuenta, las alertas pasan a ser de ella.
            existente.UserId = userId;
            existente.Plataforma = plataforma;
            existente.Entorno = entorno;
            existente.ActualizadoEn = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        // Teléfonos viejos que nunca se dieron de baja: se guardan los más recientes.
        var sobrantes = await dispositivos.Where(d => d.UserId == userId)
            .OrderByDescending(d => d.ActualizadoEn).Skip(MaximoPorUsuario).Select(d => d.Id).ToListAsync(ct);
        if (sobrantes.Count > 0)
            await dispositivos.Where(d => sobrantes.Contains(d.Id)).ExecuteDeleteAsync(ct);
    }

    /// <summary>Al cerrar sesión: ese teléfono deja de recibir las alertas de la cuenta.</summary>
    public Task<int> DarDeBajaAsync(string token, CancellationToken ct = default) =>
        db.Set<DispositivoPush>().Where(d => d.Token == token.Trim()).ExecuteDeleteAsync(ct);

    public async Task<PushEstadoDto> EstadoAsync(Guid userId, CancellationToken ct = default) =>
        new(fcm.IsConfigured, apns.Configurado,
            await db.Set<DispositivoPush>().CountAsync(d => d.UserId == userId, ct));

    /// <summary>
    /// Avisa de un evento a los teléfonos del hogar. Solo espera la consulta; el
    /// envío sigue en segundo plano para no demorar la respuesta a la IA.
    /// </summary>
    public async Task EnviarEventoAsync(SecurityEvent ev, string camara, string hora)
    {
        var destinos = await DestinosAsync(db.Set<DispositivoPush>().Where(d => d.User.HouseholdId == ev.HouseholdId));
        if (destinos.Count == 0) return;
        var titulo = EventService.SpanishLabel(ev.EventType);
        var cuerpo = $"{camara} · {hora}";
        var datos = new Dictionary<string, string> { ["eventId"] = ev.Id.ToString(), ["type"] = ev.EventType.ToString() };
        _ = Task.Run(async () =>
        {
            try
            {
                var resultados = await EnviarATodosAsync(destinos, titulo, cuerpo, datos, CancellationToken.None);
                await using var scope = scopes.CreateAsyncScope();
                await AplicarAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), resultados, CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "No se pudieron enviar las notificaciones del evento {Evento}", ev.Id);
            }
        });
    }

    /// <summary>«Enviar notificación de prueba» de Ajustes: a los teléfonos del propio usuario.</summary>
    public async Task<PushPruebaDto> EnviarPruebaAsync(Guid userId, CancellationToken ct = default)
    {
        var destinos = await DestinosAsync(db.Set<DispositivoPush>().Where(d => d.UserId == userId));
        var resultados = await EnviarATodosAsync(destinos, "Notificación de prueba",
            "Si ves esto, las alertas de VigiShield llegan a este teléfono.",
            new Dictionary<string, string> { ["type"] = "Prueba" }, ct);
        await AplicarAsync(db, resultados, ct);
        return new PushPruebaDto(
            destinos.Count,
            resultados.Count(r => r.Estado == ResultadoPush.Enviado),
            resultados.Count(r => r.Estado == ResultadoPush.TokenInvalido),
            resultados.Count(r => r.Estado is ResultadoPush.Error or ResultadoPush.NoConfigurado),
            resultados.All(r => r.Estado != ResultadoPush.NoConfigurado));
    }

    private static Task<List<Destino>> DestinosAsync(IQueryable<DispositivoPush> q) =>
        q.Select(d => new Destino(d.Id, d.Plataforma, d.Token, d.Entorno)).ToListAsync();

    private async Task<List<Resultado>> EnviarATodosAsync(List<Destino> destinos, string titulo, string cuerpo,
        IReadOnlyDictionary<string, string> datos, CancellationToken ct)
    {
        var tareas = destinos.Select(async d =>
        {
            if (d.Plataforma == Android)
            {
                var (estado, _) = await fcm.EnviarAndroidAsync(d.Token, titulo, cuerpo, datos, ct);
                return new Resultado(d, estado, null);
            }
            var sandbox = d.Entorno == Sandbox;
            var (r, razon) = await apns.EnviarAsync(d.Token, sandbox, titulo, cuerpo, datos, ct);
            // Un token de sandbox enviado a producción (o al revés) da BadDeviceToken.
            if (r == ResultadoPush.TokenInvalido && razon == "BadDeviceToken")
            {
                var (otro, _) = await apns.EnviarAsync(d.Token, !sandbox, titulo, cuerpo, datos, ct);
                if (otro == ResultadoPush.Enviado)
                    return new Resultado(d, otro, sandbox ? Produccion : Sandbox);
            }
            return new Resultado(d, r, null);
        });
        return [.. await Task.WhenAll(tareas)];
    }

    private async Task AplicarAsync(AppDbContext contexto, List<Resultado> resultados, CancellationToken ct)
    {
        var muertos = resultados.Where(r => r.Estado == ResultadoPush.TokenInvalido).Select(r => r.Destino.Id).ToList();
        if (muertos.Count > 0)
        {
            await contexto.Set<DispositivoPush>().Where(d => muertos.Contains(d.Id)).ExecuteDeleteAsync(ct);
            logger.LogInformation("Push: {N} tokens dados de baja (ya no sirven)", muertos.Count);
        }
        foreach (var r in resultados.Where(r => r.EntornoCorregido is not null))
            await contexto.Set<DispositivoPush>().Where(d => d.Id == r.Destino.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Entorno, r.EntornoCorregido!), ct);
    }
}
