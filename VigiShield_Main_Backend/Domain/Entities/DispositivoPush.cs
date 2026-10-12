namespace VigiShield.Domain.Entities;

/// <summary>
/// Un teléfono que recibe notificaciones push. Un usuario puede tener varios.
/// En iPhone el token es de APNs (directo, sin Firebase); en Android, de FCM.
/// Se borra junto con el usuario.
/// </summary>
public class DispositivoPush
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>"ios" o "android".</summary>
    public string Plataforma { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Solo iPhone: "produccion" (TestFlight y App Store) o "sandbox" (compilado
    /// desde Xcode). APNs tiene un servidor para cada uno.
    /// </summary>
    public string Entorno { get; set; } = "produccion";

    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;
}
