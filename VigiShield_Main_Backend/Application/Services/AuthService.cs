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

    public AuthService(AppDbContext db, JwtService jwt, ILogger<AuthService> logger)
    {
        _db = db;
        _jwt = jwt;
        _logger = logger;
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
        user.HouseholdId, user.WhatsAppNumber, user.CreatedAt);
}
