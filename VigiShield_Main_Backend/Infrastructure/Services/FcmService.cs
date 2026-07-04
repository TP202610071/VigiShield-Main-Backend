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
