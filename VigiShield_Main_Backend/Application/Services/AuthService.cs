using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Auth;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Application.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly ILogger<AuthService> _logger;
    private readonly WhatsAppService _whatsApp;

    public AuthService(AppDbContext db, JwtService jwt, ILogger<AuthService> logger, WhatsAppService whatsApp)
    {
        _db = db;
        _jwt = jwt;
        _logger = logger;
        _whatsApp = whatsApp;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        if (await _db.Users.AnyAsync(u => u.Email == request.Email.ToLower()))
            throw AppException.Conflict("El correo ya está registrado");

        var household = new Household
        {
            Id = Guid.NewGuid(),
            Address = request.HouseholdAddress,
            CreatedAt = DateTime.UtcNow
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Name = request.Name,
            Role = UserRole.Primary,
            HouseholdId = household.Id,
            CreatedAt = DateTime.UtcNow
        };

        household.PrimaryUserId = user.Id;

        var alertConfig = new AlertConfig
        {
            Id = Guid.NewGuid(),
            HouseholdId = household.Id
        };

        _db.Households.Add(household);
        _db.Users.Add(user);
        _db.AlertConfigs.Add(alertConfig);
        await _db.SaveChangesAsync();

        return new AuthResponse(_jwt.GenerateToken(user), ToProfileDto(user));
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw AppException.Unauthorized();

        return new AuthResponse(_jwt.GenerateToken(user), ToProfileDto(user));
    }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw AppException.NotFound("Usuario no encontrado");

        return ToProfileDto(user);
    }

    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw AppException.NotFound("Usuario no encontrado");

        user.Name = request.Name;
        user.WhatsAppNumber = request.WhatsAppNumber;
        if (request.FcmToken is not null)
            user.FcmToken = request.FcmToken;

        await _db.SaveChangesAsync();
        return ToProfileDto(user);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw AppException.NotFound("Usuario no encontrado");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw new AppException("Contraseña actual incorrecta");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();
    }

    public async Task RecoverPasswordAsync(RecoverPasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());

        // Always return success — prevents email enumeration
        if (user is null) return;

        user.PasswordResetToken = Guid.NewGuid().ToString("N");
        user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(24);
        await _db.SaveChangesAsync();

        // TODO: Send email with reset link containing the token
        _logger.LogInformation("Password reset requested for {Email}. Token: {Token}", user.Email, user.PasswordResetToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.PasswordResetToken == request.Token &&
            u.PasswordResetTokenExpiry > DateTime.UtcNow);

        if (user is null)
            throw new AppException("Token inválido o expirado");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiry = null;
        await _db.SaveChangesAsync();
    }

    public async Task<InviteUserResponse> InviteUserAsync(Guid householdId, InviteUserRequest request)
    {
        var email = request.Email.ToLower();

        if (await _db.Users.AnyAsync(u => u.Email == email && u.HouseholdId == householdId))
            throw AppException.Conflict("El usuario ya pertenece a este hogar");

        if (await _db.Invitations.AnyAsync(i =>
            i.Email == email &&
            i.HouseholdId == householdId &&
            i.AcceptedAt == null &&
            i.ExpiresAt > DateTime.UtcNow))
            throw AppException.Conflict("Ya existe una invitación pendiente para este correo");

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Email = email,
            Token = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        _db.Invitations.Add(invitation);
        await _db.SaveChangesAsync();

        // TODO: Send invitation email with the token
        _logger.LogInformation("Invitation created for {Email}. Token: {Token}", invitation.Email, invitation.Token);

        return new InviteUserResponse(invitation.Token, invitation.Email, invitation.ExpiresAt);
    }

    public async Task<AuthResponse> AcceptInvitationAsync(AcceptInvitationRequest request)
    {
        var invitation = await _db.Invitations
            .FirstOrDefaultAsync(i => i.Token == request.Token);

        if (invitation is null || invitation.IsExpired)
            throw new AppException("Invitación inválida o expirada");

        if (invitation.IsAccepted)
            throw new AppException("Esta invitación ya fue utilizada");

        if (await _db.Users.AnyAsync(u => u.Email == invitation.Email))
            throw AppException.Conflict("El correo ya está registrado");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = invitation.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Name = request.Name,
            Role = UserRole.Secondary,
            HouseholdId = invitation.HouseholdId,
            CreatedAt = DateTime.UtcNow
        };

        invitation.AcceptedAt = DateTime.UtcNow;
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Notify existing household members that a new person now has camera access.
        if (_whatsApp.IsConfigured)
        {
            var numbers = await _db.Users
                .Where(u => u.HouseholdId == invitation.HouseholdId && u.Id != user.Id
                            && u.WhatsAppNumber != null && u.WhatsAppNumber != "")
                .Select(u => u.WhatsAppNumber!)
                .ToListAsync();
            if (numbers.Count > 0)
            {
                var (date, time) = WhatsAppService.LocalParts(DateTime.UtcNow);
                _ = _whatsApp.SendTemplateAsync(numbers, "vigishield_new_household_member", null, null, date, time, user.Name);
            }
        }

        return new AuthResponse(_jwt.GenerateToken(user), ToProfileDto(user));
    }

    public async Task<List<SecondaryUserDto>> GetSecondaryUsersAsync(Guid householdId)
    {
        return await _db.Users
            .Where(u => u.HouseholdId == householdId && u.Role == UserRole.Secondary)
            .Select(u => new SecondaryUserDto(u.Id, u.Email, u.Name, u.CreatedAt))
            .ToListAsync();
    }

    public async Task RevokeUserAsync(Guid householdId, Guid targetUserId)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == targetUserId && u.HouseholdId == householdId)
            ?? throw AppException.NotFound("Usuario no encontrado");

        if (user.Role == UserRole.Primary)
            throw AppException.Forbidden("No se puede revocar al residente principal");

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
    }

    private static UserProfileDto ToProfileDto(User user) => new(
        user.Id, user.Email, user.Name, user.Role.ToString(),
        user.HouseholdId, user.WhatsAppNumber, user.AvatarPath, user.CreatedAt);

    // ── Avatar ────────────────────────────────────────────────────────────────

    public async Task<UserProfileDto> SetAvatarAsync(Guid userId, string relativePath)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw AppException.NotFound("Usuario no encontrado");
        user.AvatarPath = relativePath;
        await _db.SaveChangesAsync();
        return ToProfileDto(user);
    }

    // ── Admin management (developer accounts) ───────────────────────────────────

    public async Task<List<AdminUserDto>> GetAdminsAsync()
    {
        return await _db.Users
            .Where(u => u.Role == UserRole.Admin)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new AdminUserDto(u.Id, u.Email, u.Name, u.CreatedAt))
            .ToListAsync();
    }

    /// <summary>Search households by their primary user's name or email (admin tool).</summary>
    public async Task<List<HouseholdSummaryDto>> SearchHouseholdsAsync(string? query)
    {
        var q = from h in _db.Households
                join u in _db.Users on h.PrimaryUserId equals u.Id
                select new { h.Id, u.Name, u.Email };
        if (!string.IsNullOrWhiteSpace(query))
        {
            var ql = query.ToLower();
            q = q.Where(x => x.Name.ToLower().Contains(ql) || x.Email.ToLower().Contains(ql));
        }
        return await q.OrderBy(x => x.Name).Take(30)
            .Select(x => new HouseholdSummaryDto(x.Id, x.Name, x.Email))
            .ToListAsync();
    }

    /// <summary>Promote an existing user (by email) to Admin.</summary>
    public async Task<AdminUserDto> AddAdminAsync(string email)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower())
            ?? throw AppException.NotFound("No existe un usuario con ese correo. Debe registrarse primero.");

        if (user.Role != UserRole.Admin)
        {
            user.Role = UserRole.Admin;
            await _db.SaveChangesAsync();
        }
        return new AdminUserDto(user.Id, user.Email, user.Name, user.CreatedAt);
    }

    /// <summary>Demote an Admin back to Primary. Refuses to remove the last admin.</summary>
    public async Task RemoveAdminAsync(Guid targetUserId)
    {
        var admins = await _db.Users.Where(u => u.Role == UserRole.Admin).ToListAsync();
        var target = admins.FirstOrDefault(u => u.Id == targetUserId)
            ?? throw AppException.NotFound("Administrador no encontrado");

        if (admins.Count <= 1)
            throw AppException.Forbidden("No se puede quitar al último administrador");

        target.Role = UserRole.Primary;
        await _db.SaveChangesAsync();
    }
}
