namespace VigiShield.Domain.Entities;

/// <summary>
/// Bloqueo que pone un administrador para proteger la VM de IA durante la beta.
/// Dura hasta que un administrador lo quita.
/// <list type="bullet">
/// <item><c>camara</c>: Valor = id de la cámara. La IA no la analiza y el usuario no puede reactivarla.</item>
/// <item><c>cuenta</c>: Valor = id del usuario. Todas las cámaras de su hogar, las de hoy y las que agregue.</item>
/// <item><c>dispositivo</c>: Valor = identificador del teléfono (cabecera X-Device-Id). Bloquea toda cuenta que se use desde ese teléfono.</item>
/// <item><c>ip</c>: Valor = dirección IP. Igual que dispositivo, pero por IP (cuidado: varias casas pueden compartirla).</item>
/// </list>
/// </summary>
public class BloqueoAdmin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tipo { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public Guid? CreadoPor { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Un teléfono (y su última IP) desde el que entró una cuenta. Sirve para ver
/// en el panel qué cuentas comparten teléfono y para bloquear por dispositivo.
/// Se borra junto con el usuario.
/// </summary>
public class DispositivoVisto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string DeviceId { get; set; } = string.Empty;
    public string? Ip { get; set; }
    public string? Plataforma { get; set; }
    public DateTime PrimeraVez { get; set; } = DateTime.UtcNow;
    public DateTime UltimaVez { get; set; } = DateTime.UtcNow;
}
