using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Auth;

public record AcceptInvitationRequest(
    [Required] string Token,
    [Required, MinLength(2)] string Name,
    [Required, MinLength(8)] string Password
);
