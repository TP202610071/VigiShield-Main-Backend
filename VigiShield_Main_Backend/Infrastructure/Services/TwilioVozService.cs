using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Text.Json;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// Llamadas de voz sintetizada con Twilio para la alerta de emergencia.
///
/// En la cuenta de prueba, Twilio solo deja llamar a números verificados
/// («Verified Caller IDs»). Por eso solo se llama a un número que ya está en
/// esa lista. Un usuario puede verificar el suyo desde la app: Twilio lo llama
/// y él marca el código de 6 dígitos que muestra la app.
///
/// Variables: Twilio__AccountSid, Twilio__ApiKeySid, Twilio__ApiKeySecret,
/// Twilio__From (opcional: si falta, se usa el primer número de la cuenta) y
/// Twilio__Verificados (opcional: números extra separados por coma).
/// </summary>
public class TwilioVozService(IHttpClientFactory http, IConfiguration config, ILogger<TwilioVozService> log)
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(3);
    private Dictionary<string, string>? _verificados;   // E.164 → sid del caller id
    private string? _origen;
    private DateTime _leido = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? Sid => config["Twilio:AccountSid"];

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(Sid) && !string.IsNullOrWhiteSpace(config["Twilio:ApiKeySid"])
        && !string.IsNullOrWhiteSpace(config["Twilio:ApiKeySecret"]);

    private HttpClient Cliente()
    {
        var c = http.CreateClient("twilio");
        c.BaseAddress = new Uri($"https://api.twilio.com/2010-04-01/Accounts/{Sid}/");
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config["Twilio:ApiKeySid"]}:{config["Twilio:ApiKeySecret"]}"));
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return c;
    }

    /// <summary>Número en formato E.164. Un celular peruano de 9 dígitos recibe el +51.</summary>
    public static string? E164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digitos = new string(raw.Where(char.IsDigit).ToArray());
        if (digitos.Length == 9 && digitos[0] == '9') digitos = "51" + digitos;
        return digitos.Length is >= 8 and <= 15 ? "+" + digitos : null;
    }

    public void Invalidar() => _leido = DateTime.MinValue;

    private async Task CargarAsync()
    {
        if (_verificados is not null && DateTime.UtcNow - _leido < Vigencia) return;
        await _lock.WaitAsync();
        try
        {
            if (_verificados is not null && DateTime.UtcNow - _leido < Vigencia) return;
            using var c = Cliente();
            var ids = new Dictionary<string, string>();
            using (var doc = JsonDocument.Parse(await c.GetStringAsync("OutgoingCallerIds.json?PageSize=1000")))
                foreach (var e in doc.RootElement.GetProperty("outgoing_caller_ids").EnumerateArray())
                    ids[e.GetProperty("phone_number").GetString()!] = e.GetProperty("sid").GetString()!;
            foreach (var extra in (config["Twilio:Verificados"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (E164(extra) is { } n) ids.TryAdd(n, "");
            var origen = config["Twilio:From"];
            if (string.IsNullOrWhiteSpace(origen))
            {
                using var doc = JsonDocument.Parse(await c.GetStringAsync("IncomingPhoneNumbers.json?PageSize=5"));
                origen = doc.RootElement.GetProperty("incoming_phone_numbers").EnumerateArray()
                    .Select(e => e.GetProperty("phone_number").GetString()).FirstOrDefault();
            }
            _verificados = ids;
            _origen = origen;
            _leido = DateTime.UtcNow;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Hay cuenta y número de origen para llamar.</summary>
    public async Task<bool> DisponibleAsync()
    {
        if (!Configurado) return false;
        try { await CargarAsync(); return !string.IsNullOrEmpty(_origen); }
        catch (Exception ex) { log.LogWarning(ex, "Twilio no responde"); return false; }
    }

    public async Task<bool> EsVerificadoAsync(string? numero)
    {
        if (!Configurado || E164(numero) is not { } n) return false;
        try { await CargarAsync(); return _verificados!.ContainsKey(n); }
        catch (Exception ex) { log.LogWarning(ex, "Twilio no responde"); return false; }
    }

    /// <summary>Llama y lee el texto dos veces con voz sintetizada en español.</summary>
    public async Task<string?> LlamarAsync(string numero, string texto)
    {
        if (!await DisponibleAsync()) return "Llamadas no disponibles";
        if (E164(numero) is not { } to || !_verificados!.ContainsKey(to)) return "Número no verificado en Twilio";
        var t = SecurityElement.Escape(texto);
        var twiml = $"<Response><Pause length=\"1\"/><Say language=\"es-MX\" voice=\"Polly.Mia\">{t}</Say>"
                  + $"<Pause length=\"1\"/><Say language=\"es-MX\" voice=\"Polly.Mia\">{t}</Say></Response>";
        using var c = Cliente();
        var r = await c.PostAsync("Calls.json", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = to, ["From"] = _origen!, ["Twiml"] = twiml, ["Timeout"] = "30",
        }));
        if (r.IsSuccessStatusCode) return null;
        var error = await MensajeAsync(r);
        log.LogWarning("Twilio no pudo llamar: {Error}", error);
        return error;
    }

    /// <summary>Pide a Twilio verificar el número: lo llama y el usuario marca el código devuelto.</summary>
    public async Task<(string? Codigo, string? Error)> SolicitarVerificacionAsync(string numero, string nombre)
    {
        if (!Configurado) return (null, "Llamadas no disponibles");
        if (E164(numero) is not { } n) return (null, "Número inválido");
        using var c = Cliente();
        var r = await c.PostAsync("OutgoingCallerIds.json", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PhoneNumber"] = n, ["FriendlyName"] = Recortar($"VigiShield {nombre}", 64),
        }));
        if (!r.IsSuccessStatusCode) return (null, await MensajeAsync(r));
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Invalidar();
        return (doc.RootElement.GetProperty("validation_code").ToString(), null);
    }

    public async Task<bool> QuitarVerificacionAsync(string numero)
    {
        if (E164(numero) is not { } n || !await EsVerificadoAsync(n) || _verificados![n] == "") return false;
        using var c = Cliente();
        var r = await c.DeleteAsync($"OutgoingCallerIds/{_verificados[n]}.json");
        Invalidar();
        return r.IsSuccessStatusCode;
    }

    private static string Recortar(string s, int n) => s.Length <= n ? s : s[..n];

    private static async Task<string> MensajeAsync(HttpResponseMessage r)
    {
        try
        {
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            var code = doc.RootElement.TryGetProperty("code", out var cd) ? cd.ToString() : "";
            var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            return $"Twilio {code}: {msg ?? r.StatusCode.ToString()}";
        }
        catch { return $"Twilio {(int)r.StatusCode}"; }
    }
}
