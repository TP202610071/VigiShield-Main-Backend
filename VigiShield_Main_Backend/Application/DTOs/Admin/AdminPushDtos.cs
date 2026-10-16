using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Admin;

/// <summary>Notificación enviada desde el panel de administración.</summary>
/// <param name="Destino">"usuario" (una cuenta) o "hogar" (todos sus residentes).</param>
/// <param name="Id">Id del usuario o del hogar.</param>
/// <param name="Nivel">"urgente", "normal" o "silencioso".</param>
/// <param name="Abrir">Qué abre al tocarla: "inicio", "camara", "historial", "ajustes" o "evento".</param>
/// <param name="EventoId">Si Abrir es "evento".</param>
public record AdminPushRequest(
    [Required] string Destino,
    [Required] Guid Id,
    [Required, MaxLength(120)] string Titulo,
    [MaxLength(120)] string? Subtitulo,
    [Required, MaxLength(1000)] string Cuerpo,
    [MaxLength(1000)] string? ImagenUrl,
    string? Nivel,
    bool Sonido,
    [Range(0, 9999)] int? Insignia,
    string? Abrir,
    Guid? EventoId);

public record AdminPushResultadoDto(
    int Dispositivos, int Ios, int Android, int Enviados, int Invalidos, int Fallidos,
    bool IosConfigurado, bool AndroidConfigurado);

/// <summary>Cuántos teléfonos tiene registrados cada usuario, por plataforma.</summary>
public record AdminDispositivosDto(Guid UsuarioId, int Ios, int Android, DateTime? UltimoRegistro);
