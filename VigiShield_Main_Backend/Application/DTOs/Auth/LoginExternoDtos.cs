using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Auth;

/// <summary>
/// Lo que manda la app tras «Continuar con Apple».
/// </summary>
/// <param name="IdentityToken">JWT firmado por Apple.</param>
/// <param name="AuthorizationCode">Código de un solo uso; sirve para poder revocar el acceso al borrar la cuenta.</param>
/// <param name="Nonce">El nonce sin hashear que la app pidió a Apple (Apple devuelve su SHA-256).</param>
/// <param name="Nombre">Nombre que da Apple. Solo llega la primera vez que el usuario autoriza la app.</param>
/// <param name="Plataforma">"ios" (nativo) o "android" (por la web de Apple).</param>
public record AppleLoginRequest(
    [Required] string IdentityToken,
    string? AuthorizationCode,
    string? Nonce,
    string? Nombre,
    string? Plataforma);

/// <summary>Confirmar el borrado de la cuenta volviendo a entrar con Apple (para quien no tiene contraseña).</summary>
public record EliminarConAppleRequest([Required] string IdentityToken, string? Nonce);

/// <summary>Con qué proveedores está vinculada la cuenta.</summary>
public record CuentasExternasDto(bool Apple, bool Google, bool TieneContrasena);

/// <summary>
/// Lo que la app necesita para ofrecer «Continuar con Apple» en Android (por
/// la web de Apple). Null si el Services ID no está configurado.
/// </summary>
public record AppleAndroidDto(string ServiceId, string RedirectUri);

public record LoginExternoConfigDto(AppleAndroidDto? AppleAndroid);
