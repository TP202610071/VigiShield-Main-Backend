namespace VigiShield.Infrastructure.Services;

/// <summary>
/// Una notificación completa, como la arma el panel de administración.
/// </summary>
/// <param name="Titulo">Admite emojis (UTF-8).</param>
/// <param name="Subtitulo">iPhone lo muestra bajo el título; en Android va en la primera línea del texto.</param>
/// <param name="ImagenUrl">HTTPS. Android la muestra grande; en iPhone hace falta la extensión de notificaciones.</param>
/// <param name="Nivel">"urgente" (atraviesa los modos de concentración), "normal" o "silencioso".</param>
/// <param name="Sonido">Sin sonido, en Android se usa el canal silencioso.</param>
/// <param name="Insignia">Número en el ícono de la app (iPhone). Null = no se toca.</param>
/// <param name="Datos">Datos para la app (p. ej. "eventId" con un evento o una ruta).</param>
public record MensajePush(
    string Titulo,
    string? Subtitulo,
    string Cuerpo,
    string? ImagenUrl,
    string Nivel,
    bool Sonido,
    int? Insignia,
    IReadOnlyDictionary<string, string> Datos)
{
    public const string Urgente = "urgente";
    public const string Normal = "normal";
    public const string Silencioso = "silencioso";

    /// <summary>Canal de Android que corresponde (los crea la app).</summary>
    public string CanalAndroid => !Sonido || Nivel == Silencioso ? "silenciosas" : Nivel == Urgente ? "alertas" : "avisos";

    /// <summary>Nivel de interrupción de iPhone.</summary>
    public string NivelIos => Nivel switch
    {
        Urgente => "time-sensitive",
        Silencioso => "passive",
        _ => "active",
    };
}
