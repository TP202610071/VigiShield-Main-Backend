using System.ComponentModel.DataAnnotations;
using VigiShield.Common.Security;

namespace VigiShield.Application.DTOs.Auth;

public record ResetPasswordRequest(
    [Required] string Token,
    [Required, StrongPassword] string NewPassword
);
