using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// Sends event alerts via the WhatsApp Cloud API (Meta Graph API).
///
/// Business-initiated messages must use an approved message template. Create one
/// named per WhatsApp:TemplateName with 3 body variables — {{1}} event, {{2}}
/// camera, {{3}} time — e.g. "🚨 VigiShield: {{1}} en {{2}} ({{3}})".
///
/// No-op until WhatsApp:AccessToken + WhatsApp:PhoneNumberId are configured, so
/// it is safe to leave disabled.
/// </summary>
public class WhatsAppService
{
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

    /// <summary>Send an event alert template to each recipient (fire-and-forget friendly).</summary>
    public async Task SendEventAlertAsync(
        IReadOnlyCollection<string> toNumbers, string eventLabel, string cameraName, string timeText)
    {
        if (!IsConfigured || toNumbers.Count == 0) return;

        var version = _cfg["WhatsApp:ApiVersion"] ?? "v21.0";
        var phoneId = _cfg["WhatsApp:PhoneNumberId"];
        var token = _cfg["WhatsApp:AccessToken"];
        var template = _cfg["WhatsApp:TemplateName"] ?? "vigishield_alert";
        var lang = _cfg["WhatsApp:TemplateLang"] ?? "es";
        var url = $"https://graph.facebook.com/{version}/{phoneId}/messages";

        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
                    name = template,
                    language = new { code = lang },
                    components = new[]
                    {
                        new
                        {
                            type = "body",
                            parameters = new[]
                            {
                                new { type = "text", text = eventLabel },
                                new { type = "text", text = cameraName },
                                new { type = "text", text = timeText },
                            }
                        }
                    }
                }
            };

            try
            {
                var resp = await client.PostAsJsonAsync(url, payload);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    _log.LogWarning("WhatsApp send to {To} failed: {Status} {Body}",
                        Mask(to), (int)resp.StatusCode, err);
                }
                else
                {
                    _log.LogInformation("WhatsApp alert sent to {To}", Mask(to));
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
}
