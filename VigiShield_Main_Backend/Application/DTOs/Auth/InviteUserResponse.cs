namespace VigiShield.Application.DTOs.Auth;

public record InviteUserResponse(string Token, string Email, DateTime ExpiresAt);
