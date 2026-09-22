using System.ComponentModel.DataAnnotations;
using VigiShield.Common.Security;

namespace VigiShield.Application.DTOs.Auth;

public record AcceptInvitationRequest(
    [Required] string Token,
    [Required, MinLength(2)] string Name,
    [Required, StrongPassword] string Password
);
