using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// Notificaciones a iPhone directo por APNs (Apple Push Notification
/// service), sin Firebase.
///
/// - Se autentica con un JWT ES256 firmado con la llave .p8 de Apple
///   (Apns:TeamId, Apns:KeyId y Apns:PrivateKeyPath o Apns:PrivateKey). Puede
///   ser la misma llave de Sign in with Apple si se creó con los dos servicios.
/// - APNs exige HTTP/2. Hay dos servidores: producción (TestFlight y App Store)
///   y sandbox (apps compiladas desde Xcode).
/// - Sin llave, no envía nada (NoConfigurado).
/// </summary>
public class ApnsService
{
    public const string Produccion = "https://api.push.apple.com";
    public const string Sandbox = "https://api.sandbox.push.apple.com";

    // Una sola conexión HTTP/2 para todo el proceso, como pide Apple.
    private static readonly HttpClient Compartido = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(30),
        EnableMultipleHttp2Connections = true,
    })
    {
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        Timeout = TimeSpan.FromSeconds(15),
    };

    // El JWT vale una hora y Apple pide no renovarlo más de una vez cada 20 min.
    private static readonly object Candado = new();
    private static (string Jwt, DateTime Creado, string Llave)? _cache;

    private readonly IConfiguration _cfg;
    private readonly ILogger<ApnsService> _log;

    /// <summary>Las pruebas lo reemplazan por uno con un manejador falso.</summary>
    public HttpClient Cliente { get; set; } = Compartido;

    public ApnsService(IConfiguration cfg, ILogger<ApnsService> log)
    {
        _cfg = cfg;
        _log = log;
    }

    public string BundleId => string.IsNullOrWhiteSpace(_cfg["Apns:BundleId"]) ? "com.vigishield.app" : _cfg["Apns:BundleId"]!.Trim();

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(_cfg["Apns:TeamId"]) &&
        !string.IsNullOrWhiteSpace(_cfg["Apns:KeyId"]) &&
        LeerLlave() is not null;

    public async Task<(ResultadoPush Resultado, string? Razon)> EnviarAsync(
        string token, bool sandbox, string titulo, string cuerpo,
        IReadOnlyDictionary<string, string> datos, CancellationToken ct = default)
    {
        if (!Configurado) return (ResultadoPush.NoConfigurado, null);

        var aps = new Dictionary<string, object>
        {
            ["alert"] = new Dictionary<string, string> { ["title"] = titulo, ["body"] = cuerpo },
            ["sound"] = "default",
            // Atraviesa los modos de concentración (la app declara la capacidad).
            ["interruption-level"] = "time-sensitive",
            ["thread-id"] = "eventos",
        };
        var carga = new Dictionary<string, object> { ["aps"] = aps };
        foreach (var (k, v) in datos) carga[k] = v;

        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{(sandbox ? Sandbox : Produccion)}/3/device/{token}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = JsonContent.Create(carga),
        };
        pedido.Headers.TryAddWithoutValidation("authorization", $"bearer {Jwt()}");
        pedido.Headers.TryAddWithoutValidation("apns-topic", BundleId);
        pedido.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        pedido.Headers.TryAddWithoutValidation("apns-priority", "10");
        // Una alerta de hace más de una hora ya no sirve: si el teléfono estuvo apagado, se descarta.
        pedido.Headers.TryAddWithoutValidation("apns-expiration",
            DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString());

        try
        {
            using var resp = await Cliente.SendAsync(pedido, ct);
            if (resp.IsSuccessStatusCode) return (ResultadoPush.Enviado, null);

            var razon = await LeerRazonAsync(resp, ct);
            if (resp.StatusCode == HttpStatusCode.Gone ||
                razon is "BadDeviceToken" or "Unregistered" or "DeviceTokenNotForTopic")
                return (ResultadoPush.TokenInvalido, razon);
            if (razon is "ExpiredProviderToken" or "InvalidProviderToken")
                lock (Candado) _cache = null;
            _log.LogWarning("APNs rechazó la notificación: {Estado} {Razon}", (int)resp.StatusCode, razon);
            return (ResultadoPush.Error, razon);
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "No se pudo enviar a APNs");
            return (ResultadoPush.Error, e.GetType().Name);
        }
    }

    /// <summary>
    /// Envía una notificación completa: subtítulo, insignia, sonido, nivel de
    /// interrupción e imagen. La imagen va en "imagen" con mutable-content, para
    /// la extensión de notificaciones de la app, si la tiene.
    /// </summary>
    public async Task<(ResultadoPush Resultado, string? Razon)> EnviarMensajeAsync(
        string token, bool sandbox, MensajePush m, CancellationToken ct = default)
    {
        if (!Configurado) return (ResultadoPush.NoConfigurado, null);

        var alerta = new Dictionary<string, string> { ["title"] = m.Titulo, ["body"] = m.Cuerpo };
        if (!string.IsNullOrWhiteSpace(m.Subtitulo)) alerta["subtitle"] = m.Subtitulo;
        var aps = new Dictionary<string, object>
        {
            ["alert"] = alerta,
            ["interruption-level"] = m.NivelIos,
            ["thread-id"] = "avisos",
        };
        if (m.Sonido) aps["sound"] = "default";
        if (m.Insignia is int n) aps["badge"] = n;
        if (!string.IsNullOrWhiteSpace(m.ImagenUrl)) aps["mutable-content"] = 1;
        var carga = new Dictionary<string, object> { ["aps"] = aps };
        foreach (var (k, v) in m.Datos) carga[k] = v;
        if (!string.IsNullOrWhiteSpace(m.ImagenUrl)) carga["imagen"] = m.ImagenUrl;

        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{(sandbox ? Sandbox : Produccion)}/3/device/{token}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = JsonContent.Create(carga),
        };
        pedido.Headers.TryAddWithoutValidation("authorization", $"bearer {Jwt()}");
        pedido.Headers.TryAddWithoutValidation("apns-topic", BundleId);
        pedido.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        // Las silenciosas no tienen apuro: Apple puede agruparlas para ahorrar batería.
        pedido.Headers.TryAddWithoutValidation("apns-priority", m.Nivel == MensajePush.Silencioso ? "5" : "10");
        pedido.Headers.TryAddWithoutValidation("apns-expiration",
            DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds().ToString());

        try
        {
            using var resp = await Cliente.SendAsync(pedido, ct);
            if (resp.IsSuccessStatusCode) return (ResultadoPush.Enviado, null);
            var razon = await LeerRazonAsync(resp, ct);
            if (resp.StatusCode == HttpStatusCode.Gone ||
                razon is "BadDeviceToken" or "Unregistered" or "DeviceTokenNotForTopic")
                return (ResultadoPush.TokenInvalido, razon);
            if (razon is "ExpiredProviderToken" or "InvalidProviderToken")
                lock (Candado) _cache = null;
            _log.LogWarning("APNs rechazó la notificación: {Estado} {Razon}", (int)resp.StatusCode, razon);
            return (ResultadoPush.Error, razon);
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "No se pudo enviar a APNs");
            return (ResultadoPush.Error, e.GetType().Name);
        }
    }

    private static async Task<string?> LeerRazonAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return json.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>El JWT de proveedor: cabecera {alg ES256, kid}, cuerpo {iss = Team ID, iat}.</summary>
    private string Jwt()
    {
        var keyId = _cfg["Apns:KeyId"]!.Trim();
        var teamId = _cfg["Apns:TeamId"]!.Trim();
        var llave = $"{teamId}/{keyId}";
        lock (Candado)
        {
            if (_cache is { } c && c.Llave == llave && DateTime.UtcNow - c.Creado < TimeSpan.FromMinutes(45))
                return c.Jwt;

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(LeerLlave()!);
            var cabecera = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = keyId }));
            var cuerpo = B64Url(JsonSerializer.SerializeToUtf8Bytes(
                new { iss = teamId, iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }));
            var firma = ecdsa.SignData(Encoding.ASCII.GetBytes($"{cabecera}.{cuerpo}"), HashAlgorithmName.SHA256);
            var jwt = $"{cabecera}.{cuerpo}.{B64Url(firma)}";
            _cache = (jwt, DateTime.UtcNow, llave);
            return jwt;
        }
    }

    private string? LeerLlave()
    {
        var ruta = _cfg["Apns:PrivateKeyPath"];
        if (!string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta)) return File.ReadAllText(ruta);
        var valor = _cfg["Apns:PrivateKey"];
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Replace("\\n", "\n");
    }

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
