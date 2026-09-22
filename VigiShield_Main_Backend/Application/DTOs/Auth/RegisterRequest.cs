using System.ComponentModel.DataAnnotations;
using VigiShield.Common.Security;

namespace VigiShield.Application.DTOs.Auth;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, StrongPassword] string Password,
    [Required, MinLength(2)] string Name,
    [Required, MinLength(5)] string HouseholdAddress
);
