using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using VigiShield.Common.Exceptions;

namespace VigiShield.Infrastructure.Services;

/// <summary>Quién es, según un token de Apple ya verificado.</summary>
public record IdentidadApple(string Sujeto, string? Email, bool EmailVerificado, string ClientId);

/// <summary>
/// Sign in with Apple del lado del servidor:
/// - verifica el token de identidad (firma de Apple, emisor, destinatario,
///   vigencia y nonce);
/// - canjea el código de autorización por un token de actualización, y
/// - revoca ese token al borrar la cuenta (Apple lo exige, guía 5.1.1(v)).
///
/// Verificar no necesita secretos. Canjear y revocar necesitan la llave de
/// Sign in with Apple (Apple:TeamId, Apple:KeyId y Apple:PrivateKeyPath o
/// Apple:PrivateKey); sin ella el inicio de sesión funciona igual y solo se
/// omite la revocación (queda en el log).
/// </summary>
public class AppleSignInService
{
    public const string Emisor = "https://appleid.apple.com";
    public const string BundleIdPorDefecto = "com.vigishield.app";

    private static readonly ConfigurationManager<OpenIdConnectConfiguration> Oidc = new(
        $"{Emisor}/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever { RequireHttps = true });

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<AppleSignInService> _log;

    /// <summary>Claves públicas de Apple. Las pruebas la reemplazan por una propia.</summary>
    public Func<CancellationToken, Task<ICollection<SecurityKey>>> ObtenerClaves { get; set; }

    public AppleSignInService(IConfiguration config, IHttpClientFactory http, ILogger<AppleSignInService> log)
    {
        _config = config;
        _http = http;
        _log = log;
        ObtenerClaves = async ct => (await Oidc.GetConfigurationAsync(ct)).SigningKeys;
    }

    /// <summary>Services ID para Android (web de Apple). Null si no está configurado.</summary>
    public string? ServiceId => string.IsNullOrWhiteSpace(_config["Apple:ServiceId"]) ? null : _config["Apple:ServiceId"]!.Trim();

    public string BundleId => string.IsNullOrWhiteSpace(_config["Apple:BundleId"]) ? BundleIdPorDefecto : _config["Apple:BundleId"]!.Trim();

    /// <summary>A dónde vuelve Apple en el flujo web (Android).</summary>
    public string RedirectUri => _config["Apple:RedirectUri"] ?? "https://api.vigishield.app/api/auth/apple/callback";

    public string PaqueteAndroid => _config["Apple:AndroidPackage"] ?? BundleIdPorDefecto;

    /// <summary>Destinatarios válidos del token: la app de iOS y, si existe, el Services ID.</summary>
    public IReadOnlyList<string> ClientIds => ServiceId is null ? [BundleId] : [BundleId, ServiceId];

    public bool PuedeRevocar =>
        !string.IsNullOrWhiteSpace(_config["Apple:TeamId"]) &&
        !string.IsNullOrWhiteSpace(_config["Apple:KeyId"]) &&
        LeerLlave() is not null;

    public async Task<IdentidadApple> ValidarAsync(string identityToken, string? nonce, CancellationToken ct = default)
    {
        var parametros = new TokenValidationParameters
        {
            ValidIssuer = Emisor,
            ValidAudiences = ClientIds,
            IssuerSigningKeys = await ObtenerClaves(ct),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };
        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(identityToken, parametros);
        if (!resultado.IsValid || resultado.SecurityToken is not JsonWebToken jwt)
        {
            _log.LogWarning(resultado.Exception, "Token de Apple rechazado");
            throw new AppException("No se pudo verificar la cuenta de Apple.");
        }

        // La app manda el nonce sin hashear; Apple guarda su SHA-256.
        if (!string.IsNullOrEmpty(nonce))
        {
            var esperado = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nonce)));
            if (!jwt.TryGetPayloadValue<string>("nonce", out var recibido) || recibido != esperado)
                throw new AppException("No se pudo verificar la cuenta de Apple.");
        }

        jwt.TryGetPayloadValue<string>("email", out var email);
        return new IdentidadApple(jwt.Subject, string.IsNullOrWhiteSpace(email) ? null : email,
            EsVerdadero(jwt, "email_verified"), jwt.Audiences.First());
    }

    // Apple a veces lo manda como booleano y a veces como texto.
    private static bool EsVerdadero(JsonWebToken jwt, string claim) =>
        jwt.TryGetPayloadValue<bool>(claim, out var b) ? b
        : jwt.TryGetPayloadValue<string>(claim, out var s) && string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Canjea el código por el token de actualización. Null si no se pudo (queda en el log).</summary>
    public async Task<string?> CanjearCodigoAsync(string codigo, string clientId, CancellationToken ct = default)
    {
        if (!PuedeRevocar) return null;
        var campos = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = CrearClientSecret(clientId),
            ["code"] = codigo,
            ["grant_type"] = "authorization_code",
        };
        // El flujo web (Android) se emitió para la URL de retorno: hay que repetirla.
        if (clientId == ServiceId) campos["redirect_uri"] = RedirectUri;
        try
        {
            var r = await _http.CreateClient().PostAsync($"{Emisor}/auth/token", new FormUrlEncodedContent(campos), ct);
            var cuerpo = await r.Content.ReadAsStringAsync(ct);
            if (!r.IsSuccessStatusCode)
            {
                _log.LogWarning("Apple no canjeó el código: {Estado} {Cuerpo}", (int)r.StatusCode, cuerpo);
                return null;
            }
            using var json = JsonDocument.Parse(cuerpo);
            return json.RootElement.TryGetProperty("refresh_token", out var t) ? t.GetString() : null;
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "No se pudo canjear el código de Apple");
            return null;
        }
    }

    /// <summary>Revoca el acceso de VigiShield a la cuenta de Apple. No lanza: un fallo queda en el log.</summary>
    public async Task<bool> RevocarAsync(string refreshToken, string clientId, CancellationToken ct = default)
    {
        if (!PuedeRevocar)
        {
            _log.LogWarning("No se revocó el token de Apple: falta la llave de Sign in with Apple");
            return false;
        }
        try
        {
            var r = await _http.CreateClient().PostAsync($"{Emisor}/auth/revoke", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = CrearClientSecret(clientId),
                    ["token"] = refreshToken,
                    ["token_type_hint"] = "refresh_token",
                }), ct);
            if (!r.IsSuccessStatusCode)
                _log.LogWarning("Apple no revocó el token: {Estado}", (int)r.StatusCode);
            return r.IsSuccessStatusCode;
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "No se pudo revocar el token de Apple");
            return false;
        }
    }

    /// <summary>El «client secret» de Apple: un JWT ES256 firmado con la llave de Sign in with Apple.</summary>
    public string CrearClientSecret(string clientId)
    {
        var pem = LeerLlave() ?? throw new InvalidOperationException("Falta la llave de Sign in with Apple.");
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        var ahora = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _config["Apple:TeamId"],
            Audience = Emisor,
            IssuedAt = ahora,
            Expires = ahora.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = clientId },
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(ecdsa) { KeyId = _config["Apple:KeyId"] }, SecurityAlgorithms.EcdsaSha256),
        });
    }

    /// <summary>La llave .p8: de un archivo (Apple:PrivateKeyPath) o del propio valor (Apple:PrivateKey, con \n escritos).</summary>
    private string? LeerLlave()
    {
        var ruta = _config["Apple:PrivateKeyPath"];
        if (!string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta)) return File.ReadAllText(ruta);
        var valor = _config["Apple:PrivateKey"];
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Replace("\\n", "\n");
    }
}
