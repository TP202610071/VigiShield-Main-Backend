namespace VigiShield.Application.DTOs.Auth;

public record UserProfileDto(
    Guid Id,
    string Email,
    string Name,
    string Role,
    Guid HouseholdId,
    string? WhatsAppNumber,
    string? AvatarPath,
    DateTime CreatedAt,
    DateTime? TermsAcceptedAt = null,
    string? TermsVersion = null
);
