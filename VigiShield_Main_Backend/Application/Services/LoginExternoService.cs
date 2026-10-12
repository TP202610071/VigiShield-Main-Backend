using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Auth;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Application.Services;

/// <summary>
/// Iniciar sesión con Apple (y, cuando se configure, con Google).
///
/// - Si la cuenta de Apple ya está vinculada, entra con ese usuario.
/// - Si no, y Apple confirma un correo que ya tiene una cuenta, la vincula.
/// - Si no, crea una cuenta nueva como residente principal de un hogar nuevo,
///   igual que el registro. No tiene contraseña: se identifica con Apple, y
///   los términos los acepta en la app como cualquier cuenta nueva.
/// </summary>
public class LoginExternoService(
    AppDbContext db,
    AppleSignInService apple,
    JwtService jwt,
    AuthService auth,
    CuentaService cuentas,
    ILogger<LoginExternoService> logger)
{
    public const string Apple = "apple";
    public const string Google = "google";

    public async Task<AuthResponse> ConAppleAsync(AppleLoginRequest req, CancellationToken ct = default)
    {
        var id = await apple.ValidarAsync(req.IdentityToken, req.Nonce, ct);
        var vinculo = await db.Set<CuentaExterna>().Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Proveedor == Apple && c.Sujeto == id.Sujeto, ct);

        User user;
        if (vinculo is not null)
        {
            user = vinculo.User;
        }
        else
        {
            var email = id.Email?.Trim().ToLowerInvariant();
            var conEseCorreo = email is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            // Solo se vincula si Apple confirma que el correo es suyo. Si no lo
            // confirma y el correo ya es de otra cuenta, la nueva no lo usa.
            var existente = id.EmailVerificado ? conEseCorreo : null;
            user = existente ?? CrearUsuario(
                email is not null && conEseCorreo is null ? email : CorreoSustituto(id.Sujeto), req.Nombre);
            vinculo = new CuentaExterna
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Proveedor = Apple,
                Sujeto = id.Sujeto,
                Email = id.Email,
                ClientId = id.ClientId,
                CreoLaCuenta = existente is null,
            };
            db.Add(vinculo);
            logger.LogInformation("Apple vinculada al usuario {Usuario} (cuenta nueva: {Nueva})", user.Id, existente is null);
        }

        // El token de actualización solo sirve para revocar el acceso al borrar la cuenta.
        if (!string.IsNullOrWhiteSpace(req.AuthorizationCode) && apple.PuedeRevocar)
        {
            var refresh = await apple.CanjearCodigoAsync(req.AuthorizationCode, id.ClientId, ct);
            if (refresh is not null)
            {
                vinculo.RefreshToken = refresh;
                vinculo.ClientId = id.ClientId;
            }
        }

        await db.SaveChangesAsync(ct);
        return new AuthResponse(jwt.GenerateToken(user), await auth.GetProfileAsync(user.Id));
    }

    public async Task<CuentasExternasDto> EstadoAsync(Guid userId, CancellationToken ct = default)
    {
        var vinculos = await db.Set<CuentaExterna>().Where(c => c.UserId == userId)
            .Select(c => new { c.Proveedor, c.CreoLaCuenta }).ToListAsync(ct);
        return new CuentasExternasDto(
            Apple: vinculos.Any(v => v.Proveedor == Apple),
            Google: vinculos.Any(v => v.Proveedor == Google),
            TieneContrasena: !vinculos.Any(v => v.CreoLaCuenta));
    }

    /// <summary>Borra la cuenta tras volver a entrar con la misma cuenta de Apple.</summary>
    public async Task<CuentaService.Resultado> EliminarConAppleAsync(Guid userId, EliminarConAppleRequest req,
        CancellationToken ct = default)
    {
        var id = await apple.ValidarAsync(req.IdentityToken, req.Nonce, ct);
        var esSuya = await db.Set<CuentaExterna>()
            .AnyAsync(c => c.UserId == userId && c.Proveedor == Apple && c.Sujeto == id.Sujeto, ct);
        if (!esSuya) throw new AppException("Esa cuenta de Apple no es la de este usuario.");

        var revocar = await TokensARevocarAsync(userId, ct);
        var resultado = await cuentas.EliminarConfirmadoAsync(userId, ct);
        await RevocarAsync(revocar, ct);
        return resultado;
    }

    /// <summary>
    /// Tokens de Apple de las cuentas que se borrarán con la de este usuario
    /// (todo el hogar si es el principal). Se leen ANTES de borrar: el vínculo
    /// cae en cascada con el usuario.
    /// </summary>
    public async Task<List<(string Token, string ClientId)>> TokensARevocarAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Household).FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return [];
        var otros = await db.Users.CountAsync(u => u.HouseholdId == user.HouseholdId && u.Id != userId, ct);
        var todoElHogar = user.Household is null || user.Household.PrimaryUserId == userId || otros == 0;
        var usuarios = todoElHogar
            ? db.Users.Where(u => u.HouseholdId == user.HouseholdId).Select(u => u.Id)
            : db.Users.Where(u => u.Id == userId).Select(u => u.Id);
        var filas = await db.Set<CuentaExterna>()
            .Where(c => c.Proveedor == Apple && c.RefreshToken != null && usuarios.Contains(c.UserId))
            .Select(c => new { c.RefreshToken, c.ClientId }).ToListAsync(ct);
        return filas.Select(f => (f.RefreshToken!, f.ClientId ?? apple.BundleId)).ToList();
    }

    public async Task RevocarAsync(IEnumerable<(string Token, string ClientId)> tokens, CancellationToken ct = default)
    {
        foreach (var (token, clientId) in tokens)
            await apple.RevocarAsync(token, clientId, ct);
    }

    private User CrearUsuario(string email, string? nombre)
    {
        var hogar = new Household { Id = Guid.NewGuid(), Address = string.Empty, CreatedAt = DateTime.UtcNow };
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            // Sin contraseña propia: un hash de algo aleatorio que nadie conoce.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            Name = string.IsNullOrWhiteSpace(nombre) ? NombreDesdeCorreo(email) : nombre.Trim(),
            Role = UserRole.Primary,
            HouseholdId = hogar.Id,
            CreatedAt = DateTime.UtcNow,
        };
        hogar.PrimaryUserId = user.Id;
        db.Households.Add(hogar);
        db.Users.Add(user);
        db.AlertConfigs.Add(new AlertConfig { Id = Guid.NewGuid(), HouseholdId = hogar.Id });
        return user;
    }

    /// <summary>Apple casi siempre da un correo; si no, uno que no se puede usar para entrar.</summary>
    private static string CorreoSustituto(string sujeto) =>
        $"apple.{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sujeto)))[..16]}@sin-correo.vigishield.app";

    private static string NombreDesdeCorreo(string email)
    {
        var local = email.Split('@')[0];
        return local.Length is > 0 and <= 40 && !email.EndsWith("privaterelay.appleid.com") && !email.EndsWith("vigishield.app")
            ? local
            : "Residente";
    }
}
