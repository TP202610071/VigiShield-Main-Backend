namespace VigiShield.Infrastructure.Services;

/// <summary>Qué pasó al enviar una notificación a un teléfono.</summary>
public enum ResultadoPush
{
    Enviado,
    /// <summary>El token ya no sirve (app borrada, otro teléfono...): hay que olvidarlo.</summary>
    TokenInvalido,
    Error,
    /// <summary>El servidor no tiene las llaves de APNs o FCM.</summary>
    NoConfigurado,
}
