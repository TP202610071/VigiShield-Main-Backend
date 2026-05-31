using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Auth;

public record UpdateProfileRequest(
    [Required, MinLength(2)] string Name,
    string? WhatsAppNumber,
    string? FcmToken
);
