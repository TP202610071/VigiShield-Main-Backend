using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Auth;

public record ResetPasswordRequest(
    [Required] string Token,
    [Required, MinLength(8)] string NewPassword
);
