using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Auth;

public record RecoverPasswordRequest([Required, EmailAddress] string Email);
