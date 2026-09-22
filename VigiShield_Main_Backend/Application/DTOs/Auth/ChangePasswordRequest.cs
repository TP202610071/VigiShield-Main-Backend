using System.ComponentModel.DataAnnotations;
using VigiShield.Common.Security;

namespace VigiShield.Application.DTOs.Auth;

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StrongPassword] string NewPassword
);
