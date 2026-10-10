using Microsoft.EntityFrameworkCore;
using VigiShield.Common.Exceptions;
using VigiShield.Common.Media;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Application.Services;

/// <summary>
/// Borrado de la cuenta desde la app (exigido por Apple, guía 5.1.1(v), y
/// prometido en la política de privacidad).
///
/// - Residente principal: se borra su HOGAR entero (cámaras, eventos, rostros,
///   alertas, invitaciones y las cuentas de los miembros invitados), porque un
///   hogar sin dueño no se puede administrar.
/// - Miembro invitado: se borra solo su cuenta.
///
/// Los datos se borran en una transacción. Después, sin poder deshacer, se
/// borran los archivos: fotos y clips de eventos en R2, fotos de rostros y
/// avatares en el disco del servidor. Si un archivo falla, queda en el log.
/// </summary>
public class CuentaService(AppDbContext db, R2Service r2, IWebHostEnvironment env, ILogger<CuentaService> logger)
{
    public record Resultado(bool HogarEliminado, int UsuariosEliminados);

    public async Task<Resultado> EliminarAsync(Guid userId, string password, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Household).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw AppException.NotFound("Usuario no encontrado");
        // 400 y no 401: la app trata un 401 como sesión vencida y cerraría la sesión.
        if (string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new AppException("La contraseña no es correcta.");

        var hogarId = user.HouseholdId;
        var otros = await db.Users.CountAsync(u => u.HouseholdId == hogarId && u.Id != userId, ct);
        var borrarHogar = user.Household is null || user.Household.PrimaryUserId == userId || otros == 0;
        var afectados = borrarHogar
            ? await db.Users.Where(u => u.HouseholdId == hogarId).Select(u => u.Id).ToListAsync(ct)
            : [userId];

        // Lo que hay que borrar fuera de la base, recogido antes de perder las rutas.
        var urlsEventos = borrarHogar
            ? (await db.Events.Where(e => e.HouseholdId == hogarId)
                    .Select(e => new { e.ImageCapturePath, e.VideoClipPath }).ToListAsync(ct))
                .SelectMany(e => new[] { e.ImageCapturePath, e.VideoClipPath })
                .Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).ToList()
            : [];

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Los avisos enviados apuntan al destinatario con RESTRICT: van primero.
            await db.NotificationLogs
                .Where(n => afectados.Contains(n.RecipientUserId) || (borrarHogar && n.HouseholdId == hogarId))
                .ExecuteDeleteAsync(ct);
            if (borrarHogar)
            {
                // El resto cae en cascada: usuarios, cámaras, eventos, rostros,
                // alertas, invitaciones y sesiones del video de ejemplo.
                await db.Households.Where(h => h.Id == hogarId).ExecuteDeleteAsync(ct);
            }
            else
            {
                await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        logger.LogInformation("Cuenta {Usuario} eliminada (hogar eliminado: {Hogar}, usuarios: {N})",
            userId, borrarHogar, afectados.Count);

        await BorrarArchivosAsync(hogarId, borrarHogar, afectados, urlsEventos);
        return new Resultado(borrarHogar, afectados.Count);
    }

    private async Task BorrarArchivosAsync(Guid hogarId, bool borrarHogar, List<Guid> usuarios, List<string> urls)
    {
        try
        {
            // Con la foto de cada evento se borran los rostros que la IA subió junto a ella.
            var todas = MediosDelEvento.ConDerivados(urls).ToList();
            var n = await r2.DeleteByUrlsAsync(todas);
            if (urls.Count > 0) logger.LogInformation("R2: {N} de {Total} archivos de eventos borrados", n, todas.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudieron borrar de R2 los archivos del hogar {Hogar}", hogarId);
        }

        var wwwroot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        if (borrarHogar)
            Intentar(() =>
            {
                var rostros = Path.Combine(wwwroot, "uploads", "faces", hogarId.ToString());
                if (Directory.Exists(rostros)) Directory.Delete(rostros, recursive: true);
            });
        // Avatares: los guarda AuthController en ContentRootPath/wwwroot/avatars/{id}.ext
        foreach (var raiz in new[] { wwwroot, Path.Combine(env.ContentRootPath, "wwwroot") }.Distinct())
        {
            var avatares = Path.Combine(raiz, "avatars");
            if (!Directory.Exists(avatares)) continue;
            foreach (var id in usuarios)
                foreach (var archivo in Directory.GetFiles(avatares, $"{id}.*"))
                    Intentar(() => File.Delete(archivo));
        }
    }

    private void Intentar(Action accion)
    {
        try { accion(); }
        catch (Exception ex) { logger.LogError(ex, "No se pudo borrar un archivo de una cuenta eliminada"); }
    }
}
