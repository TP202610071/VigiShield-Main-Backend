namespace VigiShield.Application.DTOs.Auth;

public record AdminUserDto(
    Guid Id,
    string Email,
    string Name,
    DateTime CreatedAt
);

public record AddAdminRequest(string Email);
