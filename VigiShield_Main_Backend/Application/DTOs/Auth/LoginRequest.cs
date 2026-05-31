using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Auth;

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);
