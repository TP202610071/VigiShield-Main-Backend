using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Auth;

public record InviteUserRequest([Required, EmailAddress] string Email);
