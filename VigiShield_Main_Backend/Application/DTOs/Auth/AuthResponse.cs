namespace VigiShield.Application.DTOs.Auth;

public record AuthResponse(string Token, UserProfileDto User);
