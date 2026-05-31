using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Auth;

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(8)] string NewPassword
);
