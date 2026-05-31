namespace VigiShield.Application.DTOs.Auth;

public record SecondaryUserDto(Guid Id, string Email, string Name, DateTime CreatedAt);
