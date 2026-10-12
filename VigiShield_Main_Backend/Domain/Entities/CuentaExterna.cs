namespace VigiShield.Domain.Entities;

/// <summary>
/// Cuenta de Apple (y más adelante de Google) con la que un usuario inicia
/// sesión. Un usuario puede tener contraseña, cuenta externa o ambas. Se borra
/// junto con el usuario.
/// </summary>
public class CuentaExterna
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>"apple" o "google".</summary>
    public string Proveedor { get; set; } = string.Empty;

    /// <summary>Identificador estable del usuario en el proveedor (el "sub" del token).</summary>
    public string Sujeto { get; set; } = string.Empty;

    /// <summary>Correo que dio el proveedor (en Apple puede ser uno de reenvío privado).</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Token de actualización de Apple. Solo sirve para revocar el acceso al
    /// borrar la cuenta (lo exige Apple). Null si el backend no tiene la llave
    /// de Sign in with Apple configurada.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>Con qué identificador de cliente se emitió el token (bundle id o Services ID).</summary>
    public string? ClientId { get; set; }

    /// <summary>True si la cuenta de VigiShield se creó al entrar con este proveedor (no tiene contraseña propia).</summary>
    public bool CreoLaCuenta { get; set; }

    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}
