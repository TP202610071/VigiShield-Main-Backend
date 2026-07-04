using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// Sends notifications via the WhatsApp Cloud API (Meta Graph API) using approved
/// message templates. No-op until WhatsApp:AccessToken + WhatsApp:PhoneNumberId
/// are configured (via the env file), so it is safe to leave disabled.
///
/// Templates used (create + get approved in Meta, language es):
///   vigishield_alert            {{1}} camera {{2}} date {{3}} time   (unauthorized person)
///   vigishield_general_alert    {{1}} event  {{2}} camera {{3}} date {{4}} time
///   vigishield_monitoring_paused {{1}} date {{2}} time
///   vigishield_new_household_member {{1}} date {{2}} time {{3}} member
/// </summary>
public class WhatsAppService
{
    private static readonly TimeZoneInfo Tz = ResolveTz();

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<WhatsAppService> _log;

    public WhatsAppService(IHttpClientFactory httpFactory, IConfiguration cfg, ILogger<WhatsAppService> log)
    {
        _httpFactory = httpFactory;
        _cfg = cfg;
        _log = log;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_cfg["WhatsApp:AccessToken"]) &&
        !string.IsNullOrWhiteSpace(_cfg["WhatsApp:PhoneNumberId"]);

    /// <summary>Local date ("dd/MM/yyyy") + 12-hour time ("hh:mm tt") for a UTC instant.</summary>
    public static (string date, string time) LocalParts(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz);
        return (local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                local.ToString("hh:mm tt", CultureInfo.InvariantCulture));
    }

    /// <summary>Send a template (with ordered body parameters) to each recipient. Fire-and-forget friendly.</summary>
    public async Task SendTemplateAsync(
        IReadOnlyCollection<string> toNumbers, string templateName, params string[] bodyParams)
    {
        if (!IsConfigured || toNumbers.Count == 0) return;

        var version = _cfg["WhatsApp:ApiVersion"] ?? "v21.0";
        var phoneId = _cfg["WhatsApp:PhoneNumberId"];
        var token = _cfg["WhatsApp:AccessToken"];
        var lang = _cfg["WhatsApp:TemplateLang"] ?? "es";
        var url = $"https://graph.facebook.com/{version}/{phoneId}/messages";

        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var parameters = bodyParams.Select(p => new { type = "text", text = p }).ToArray();

        foreach (var raw in toNumbers)
        {
            var to = Normalize(raw);
            if (to is null) continue;

            object payload = new
            {
                messaging_product = "whatsapp",
                to,
                type = "template",
                template = new
                {
                    name = templateName,
                    language = new { code = lang },
                    components = parameters.Length == 0
                        ? Array.Empty<object>()
                        : new object[] { new { type = "body", parameters } }
                }
            };

            try
            {
                var resp = await client.PostAsJsonAsync(url, payload);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    _log.LogWarning("WhatsApp '{Template}' to {To} failed: {Status} {Body}",
                        templateName, Mask(to), (int)resp.StatusCode, err);
                }
                else
                {
                    _log.LogInformation("WhatsApp '{Template}' sent to {To}", templateName, Mask(to));
                }
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "WhatsApp send error to {To}", Mask(to));
            }
        }
    }

    /// <summary>Digits-only E.164 (Cloud API wants no '+'). Returns null if too short.</summary>
    private static string? Normalize(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length >= 8 ? digits : null;
    }

    private static string Mask(string number) =>
        number.Length <= 4 ? "****" : new string('*', number.Length - 4) + number[^4..];

    private static TimeZoneInfo ResolveTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
        catch { return TimeZoneInfo.Utc; }
    }
}
