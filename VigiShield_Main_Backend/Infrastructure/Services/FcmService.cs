using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// Sends push notifications via Firebase Cloud Messaging (HTTP v1 API).
///
/// No-op until configured: put a Firebase service-account JSON on the server and
/// set Fcm:ServiceAccountPath + Fcm:ProjectId (via the env file). Gets an OAuth2
/// access token by signing a JWT with the service-account key (no external NuGet),
/// caches it, and posts to fcm.googleapis.com. Invalid device tokens are logged.
/// </summary>
public class FcmService
{
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _accessToken;
    private static DateTime _accessTokenExpiry = DateTime.MinValue;

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<FcmService> _log;

    public FcmService(IHttpClientFactory httpFactory, IConfiguration cfg, ILogger<FcmService> log)
    {
        _httpFactory = httpFactory;
        _cfg = cfg;
        _log = log;
    }

    public bool IsConfigured
    {
        get
        {
            var path = _cfg["Fcm:ServiceAccountPath"];
            return !string.IsNullOrWhiteSpace(_cfg["Fcm:ProjectId"])
                && !string.IsNullOrWhiteSpace(path)
                && File.Exists(path);
        }
    }

    public async Task SendAsync(
        IReadOnlyCollection<string> deviceTokens, string title, string body,
        IReadOnlyDictionary<string, string>? data = null)
    {
        if (!IsConfigured || deviceTokens.Count == 0) return;

        var projectId = _cfg["Fcm:ProjectId"];
        var url = $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";

        string accessToken;
        try { accessToken = await GetAccessTokenAsync(); }
        catch (Exception e) { _log.LogWarning(e, "FCM: could not obtain access token"); return; }

        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        foreach (var token in deviceTokens)
        {
            var message = new
            {
                message = new
                {
                    token,
                    notification = new { title, body },
                    data = data ?? new Dictionary<string, string>(),
                    android = new { priority = "high" },
                    apns = new { payload = new { aps = new { sound = "default" } } }
                }
            };
            try
            {
                var resp = await client.PostAsJsonAsync(url, message);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    _log.LogWarning("FCM send failed ({Status}): {Body}", (int)resp.StatusCode, err);
                }
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "FCM send error");
            }
        }
    }

    /// <summary>
    /// Envía una alerta a UN teléfono Android y dice qué pasó, para que quien
    /// llama pueda olvidar los tokens que ya no sirven. Usa el canal «alertas»
    /// que crea la app (importancia alta, con sonido).
    /// </summary>
    public async Task<(ResultadoPush Resultado, string? Razon)> EnviarAndroidAsync(
        string token, string titulo, string cuerpo, IReadOnlyDictionary<string, string> datos,
        CancellationToken ct = default)
    {
        if (!IsConfigured) return (ResultadoPush.NoConfigurado, null);
        string accessToken;
        try { accessToken = await GetAccessTokenAsync(); }
        catch (Exception e) { _log.LogWarning(e, "FCM: no se pudo obtener el token de acceso"); return (ResultadoPush.Error, "auth"); }

        var mensaje = new
        {
            message = new
            {
                token,
                notification = new { title = titulo, body = cuerpo },
                data = datos,
                android = new
                {
                    priority = "HIGH",
                    ttl = "3600s",
                    notification = new
                    {
                        channel_id = "alertas",
                        sound = "default",
                        notification_priority = "PRIORITY_HIGH",
                        // Un aviso por evento: si llega dos veces, se reemplaza.
                        tag = datos.TryGetValue("eventId", out var id) ? id : null,
                    },
                },
            },
        };
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{_cfg["Fcm:ProjectId"]}/messages:send")
            {
                Content = JsonContent.Create(mensaje),
            };
            pedido.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var resp = await _httpFactory.CreateClient().SendAsync(pedido, ct);
            if (resp.IsSuccessStatusCode) return (ResultadoPush.Enviado, null);
            var cuerpoError = await resp.Content.ReadAsStringAsync(ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound || cuerpoError.Contains("UNREGISTERED") ||
                (resp.StatusCode == System.Net.HttpStatusCode.BadRequest && cuerpoError.Contains("registration token")))
                return (ResultadoPush.TokenInvalido, "UNREGISTERED");
            _log.LogWarning("FCM rechazó la notificación ({Estado}): {Cuerpo}", (int)resp.StatusCode, cuerpoError);
            return (ResultadoPush.Error, ((int)resp.StatusCode).ToString());
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "FCM: error al enviar");
            return (ResultadoPush.Error, e.GetType().Name);
        }
    }

    /// <summary>
    /// Envía una notificación completa a un Android: canal según el nivel y el
    /// sonido, imagen grande y subtítulo en la primera línea.
    /// </summary>
    public async Task<(ResultadoPush Resultado, string? Razon)> EnviarAndroidMensajeAsync(
        string token, MensajePush m, CancellationToken ct = default)
    {
        if (!IsConfigured) return (ResultadoPush.NoConfigurado, null);
        string accessToken;
        try { accessToken = await GetAccessTokenAsync(); }
        catch (Exception e) { _log.LogWarning(e, "FCM: no se pudo obtener el token de acceso"); return (ResultadoPush.Error, "auth"); }

        var cuerpo = string.IsNullOrWhiteSpace(m.Subtitulo) ? m.Cuerpo : $"{m.Subtitulo}\n{m.Cuerpo}";
        var imagen = string.IsNullOrWhiteSpace(m.ImagenUrl) ? null : m.ImagenUrl;
        // La app usa "canal" e "imagen" cuando la muestra ella (con la app abierta).
        var datos = new Dictionary<string, string>(m.Datos) { ["canal"] = m.CanalAndroid };
        if (imagen is not null) datos["imagen"] = imagen;
        var mensaje = new
        {
            message = new
            {
                token,
                notification = new { title = m.Titulo, body = cuerpo, image = imagen },
                data = datos,
                android = new
                {
                    priority = m.Nivel == MensajePush.Silencioso ? "NORMAL" : "HIGH",
                    ttl = "86400s",
                    notification = new
                    {
                        channel_id = m.CanalAndroid,
                        sound = m.Sonido ? "default" : null,
                        notification_priority = m.Nivel switch
                        {
                            MensajePush.Urgente => "PRIORITY_HIGH",
                            MensajePush.Silencioso => "PRIORITY_LOW",
                            _ => "PRIORITY_DEFAULT",
                        },
                        image = imagen,
                        notification_count = m.Insignia,
                    },
                },
            },
        };
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{_cfg["Fcm:ProjectId"]}/messages:send")
            {
                Content = JsonContent.Create(mensaje, options: new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                }),
            };
            pedido.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var resp = await _httpFactory.CreateClient().SendAsync(pedido, ct);
            if (resp.IsSuccessStatusCode) return (ResultadoPush.Enviado, null);
            var error = await resp.Content.ReadAsStringAsync(ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound || error.Contains("UNREGISTERED") ||
                (resp.StatusCode == System.Net.HttpStatusCode.BadRequest && error.Contains("registration token")))
                return (ResultadoPush.TokenInvalido, "UNREGISTERED");
            _log.LogWarning("FCM rechazó la notificación ({Estado}): {Cuerpo}", (int)resp.StatusCode, error);
            return (ResultadoPush.Error, ((int)resp.StatusCode).ToString());
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "FCM: error al enviar");
            return (ResultadoPush.Error, e.GetType().Name);
        }
    }

    // ── OAuth2 access token from the service account (cached) ──────────────────

    private async Task<string> GetAccessTokenAsync()
    {
        if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpiry.AddMinutes(-2))
            return _accessToken;

        await TokenLock.WaitAsync();
        try
        {
            if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpiry.AddMinutes(-2))
                return _accessToken;

            var sa = JsonSerializer.Deserialize<JsonElement>(
                await File.ReadAllTextAsync(_cfg["Fcm:ServiceAccountPath"]!));
            var clientEmail = sa.GetProperty("client_email").GetString()!;
            var privateKey = sa.GetProperty("private_key").GetString()!;
            var tokenUri = sa.TryGetProperty("token_uri", out var t)
                ? t.GetString()! : "https://oauth2.googleapis.com/token";

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
            var claims = B64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["iss"] = clientEmail,
                ["scope"] = "https://www.googleapis.com/auth/firebase.messaging",
                ["aud"] = tokenUri,
                ["iat"] = now,
                ["exp"] = now + 3600,
            }));
            var signingInput = $"{header}.{claims}";

            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKey);
            var sig = rsa.SignData(Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var jwt = $"{signingInput}.{B64Url(sig)}";

            var client = _httpFactory.CreateClient();
            var resp = await client.PostAsync(tokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = jwt,
            }));
            resp.EnsureSuccessStatusCode();
            var json = JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync());
            _accessToken = json.GetProperty("access_token").GetString();
            var expiresIn = json.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
            _accessTokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);
            return _accessToken!;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
