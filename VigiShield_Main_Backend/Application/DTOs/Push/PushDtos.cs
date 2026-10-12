using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Push;

/// <param name="Token">Token de APNs (iPhone, en hexadecimal) o de FCM (Android).</param>
/// <param name="Plataforma">"ios" o "android".</param>
/// <param name="Entorno">Solo iPhone: "produccion" o "sandbox".</param>
public record RegistrarDispositivoRequest(
    [Required, MaxLength(512)] string Token,
    [Required] string Plataforma,
    string? Entorno);

public record BajaDispositivoRequest([Required, MaxLength(512)] string Token);

/// <summary>Si el servidor puede enviar a cada plataforma y cuántos teléfonos tiene registrados el usuario.</summary>
public record PushEstadoDto(bool Android, bool Ios, int Dispositivos);

/// <summary>Resultado de la notificación de prueba.</summary>
public record PushPruebaDto(int Dispositivos, int Enviados, int Invalidos, int Fallidos, bool Configurado);
