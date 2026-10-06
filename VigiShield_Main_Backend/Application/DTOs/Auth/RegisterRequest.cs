using System.ComponentModel.DataAnnotations;
using VigiShield.Common.Security;

namespace VigiShield.Application.DTOs.Auth;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, StrongPassword] string Password,
    [Required, MinLength(2)] string Name,
    [Required, MinLength(5)] string HouseholdAddress,
    // Versión de los términos que el usuario aceptó al registrarse. Opcional
    // para no romper versiones antiguas de la app: quien no la envía acepta
    // después, con el aviso que la app muestra al iniciar sesión.
    [MaxLength(20)] string? TermsVersion = null
);

public record AcceptTermsRequest([Required, MaxLength(20)] string Version);

/// <summary>Borrar la cuenta: se pide la contraseña para confirmar que es el dueño.</summary>
public record DeleteAccountRequest([Required] string Password);
