using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Auth;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    [Required, MinLength(2)] string Name,
    [Required, MinLength(5)] string HouseholdAddress
);
