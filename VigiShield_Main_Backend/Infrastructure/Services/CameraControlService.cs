using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// Reads and writes live image/video settings on Xiongmai/hi3510 IP cameras
/// via their CGI API (cgi-bin/hi3510/param.cgi), using the camera's stored
/// IP + credentials. Lets the app expose brightness/contrast/bitrate/etc.
///
/// The backend must be on the same network as the camera (it is, locally).
/// In a cloud deployment the home camera is unreachable from the VM, so this
/// only works when the backend runs on the LAN with the camera.
/// </summary>
public class CameraControlService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CameraControlService> _logger;

    public CameraControlService(AppDbContext db, IHttpClientFactory httpFactory, ILogger<CameraControlService> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    // Which CGI command owns each setting key.
    private static readonly HashSet<string> _imageKeys = new()
    {
        "brightness", "contrast", "saturation", "sharpness", "hue",
        "wdr", "aemode", "imgmode", "shutter", "flip", "mirror",
    };
    private static readonly HashSet<string> _vencKeys = new() { "bps", "fps", "gop", "brmode" };
    private const string _mainChannel = "11"; // RTSP /11 = main stream

    // ── Read current settings ─────────────────────────────────────────────────

    public async Task<Dictionary<string, string>> GetSettingsAsync(Guid householdId, Guid cameraId)
    {
        var cam = await GetCameraAsync(householdId, cameraId);
        var http = BuildClient(cam);

        var result = new Dictionary<string, string>();

        foreach (var cmd in new[]
                 {
                     $"getimageattr",
                     $"getvencattr&-chn={_mainChannel}",
                     $"getinfrared",
                     $"getvideoattr",
                 })
        {
            try
            {
                var body = await http.GetStringAsync(BuildUrl(cam, cmd));
                foreach (Match m in Regex.Matches(body, "var\\s+(\\w+)\\s*=\\s*\"([^\"]*)\";"))
                    result[m.Groups[1].Value] = m.Groups[2].Value;
            }
            catch (Exception e)
            {
                _logger.LogWarning("Camera {Ip} {Cmd} read failed: {Msg}", cam.CameraIp, cmd, e.Message);
            }
        }

        if (result.Count == 0)
            throw new AppException("No se pudo leer la configuración de la cámara. Verifica que esté en línea y en la misma red.");

        return result;
    }

    // ── Apply settings ─────────────────────────────────────────────────────────

    public async Task ApplySettingsAsync(Guid householdId, Guid cameraId, Dictionary<string, string> settings)
    {
        var cam = await GetCameraAsync(householdId, cameraId);
        var http = BuildClient(cam);

        var imageArgs = new StringBuilder();
        var vencArgs = new StringBuilder();
        string? infrared = null;

        foreach (var (key, value) in settings)
        {
            var k = key.Trim();
            var v = Uri.EscapeDataString(value.Trim());
            if (_imageKeys.Contains(k)) imageArgs.Append($"&-{k}={v}");
            else if (_vencKeys.Contains(k)) vencArgs.Append($"&-{k}={v}");
            else if (k is "infraredstat" or "night") infrared = v;
        }

        var commands = new List<string>();
        if (imageArgs.Length > 0) commands.Add($"setimageattr{imageArgs}");
        if (vencArgs.Length > 0) commands.Add($"setvencattr&-chn={_mainChannel}{vencArgs}");
        if (infrared is not null) commands.Add($"setinfrared&-infraredstat={infrared}");

        if (commands.Count == 0)
            throw new AppException("No se enviaron ajustes válidos.");

        foreach (var cmd in commands)
        {
            try
            {
                var resp = await http.GetStringAsync(BuildUrl(cam, cmd));
                _logger.LogInformation("Camera {Ip} applied: {Cmd} → {Resp}",
                    cam.CameraIp, cmd.Split('&')[0], resp.Trim());
            }
            catch (Exception e)
            {
                throw new AppException($"La cámara rechazó el ajuste ({e.Message}).");
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<CameraConfig> GetCameraAsync(Guid householdId, Guid cameraId)
    {
        var cam = await _db.CameraConfigs
            .FirstOrDefaultAsync(c => c.Id == cameraId && c.HouseholdId == householdId)
            ?? throw AppException.NotFound("Cámara no encontrada");

        if (string.IsNullOrEmpty(cam.CameraIp))
            throw new AppException("La cámara no tiene una IP configurada.");

        return cam;
    }

    private HttpClient BuildClient(CameraConfig cam)
    {
        var http = _httpFactory.CreateClient("camera-control");
        http.Timeout = TimeSpan.FromSeconds(8);
        if (!string.IsNullOrEmpty(cam.CameraUsername))
        {
            var creds = $"{cam.CameraUsername}:{cam.CameraPassword}";
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(creds));
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", token);
        }
        return http;
    }

    private static string BuildUrl(CameraConfig cam, string cmd)
        => $"http://{cam.CameraIp}/cgi-bin/hi3510/param.cgi?cmd={cmd}";
}
