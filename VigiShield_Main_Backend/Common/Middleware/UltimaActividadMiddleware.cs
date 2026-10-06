using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Common.Middleware;

/// <summary>
/// Registra la última actividad de cada usuario autenticado para el panel de
/// administración (activos / inactivos). Escribe como mucho una vez cada 5 min
/// por usuario y en segundo plano: nunca retrasa ni hace fallar la petición.
/// </summary>
public class UltimaActividadMiddleware(RequestDelegate next, IServiceScopeFactory scopes,
    ILogger<UltimaActividadMiddleware> logger)
{
    private static readonly ConcurrentDictionary<Guid, DateTime> Ultima = new();
    private static readonly TimeSpan Cada = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null || !Guid.TryParse(id, out var userId)) return;
        var ahora = DateTime.UtcNow;
        if (Ultima.TryGetValue(userId, out var previa) && ahora - previa < Cada) return;
        Ultima[userId] = ahora;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Users.Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, ahora));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No se pudo registrar la última actividad de {Usuario}", userId);
            }
        });
    }
}
