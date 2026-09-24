using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Stream;
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
    private readonly CameraAgentBroker _agente;

    public CameraControlService(AppDbContext db, IHttpClientFactory httpFactory,
        ILogger<CameraControlService> logger, CameraAgentBroker agente)
    {
        _db = db;
        _httpFactory = httpFactory;
        _logger = logger;
        _agente = agente;
    }

    // Which CGI command owns each setting key.
    private static readonly HashSet<string> _imageKeys = new()
    {
        "brightness", "contrast", "saturation", "sharpness", "hue",
        "wdr", "aemode", "imgmode", "shutter", "flip", "mirror",
    };
    private static readonly HashSet<string> _vencKeys = new() { "bps", "fps", "gop", "brmode" };
    private const string _mainChannel = "11"; // RTSP /11 = main stream
    private const int _webPort = 80;          // el CGI vive en el puerto web, no en el 554

    // ── LAN access for the app ────────────────────────────────────────────────

    /// <summary>
    /// Devuelve la IP y credenciales de la cámara para que la app la controle
    /// desde la red local. Con el backend en la nube esta es la única vía que
    /// funciona: una IP privada no es alcanzable desde la VM.
    /// </summary>
    public async Task<CameraLanAccessDto> GetLanAccessAsync(Guid householdId, Guid cameraId)
    {
        var cam = await GetCameraAsync(householdId, cameraId);
        return new CameraLanAccessDto(cam.CameraIp!, _webPort, cam.CameraUsername, cam.CameraPassword);
    }

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

        if (result.Count != 0) return result;

        // El backend esta en la nube: una IP privada no se alcanza desde la VM.
        // Si hay un agente corriendo en la casa, el que habla con la camara es el.
        var porAgente = await PorAgenteAsync(householdId, cam, "read", new());
        if (porAgente is not null) return porAgente;

        throw new AppException(MensajeSinAcceso(householdId));
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

        try
        {
            foreach (var cmd in commands)
            {
                var resp = await http.GetStringAsync(BuildUrl(cam, cmd));
                _logger.LogInformation("Camera {Ip} applied: {Cmd} -> {Resp}",
                    cam.CameraIp, cmd.Split('&')[0], resp.Trim());
            }
            return;
        }
        catch (Exception e)
        {
            _logger.LogInformation("La VM no alcanza {Ip} ({Msg}); se intenta por el agente de la casa",
                cam.CameraIp, e.Message);
        }

        if (await PorAgenteAsync(householdId, cam, "apply", settings) is null)
            throw new AppException(MensajeSinAcceso(householdId));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Manda la orden al agente de la vivienda. Devuelve null si no hay agente
    /// conectado, si no contestó a tiempo o si la cámara le falló a él también.
    /// </summary>
    private async Task<Dictionary<string, string>?> PorAgenteAsync(
        Guid householdId, CameraConfig cam, string kind, Dictionary<string, string> settings)
    {
        if (!_agente.AgenteConectado(householdId)) return null;

        var orden = new CameraAgentCommand(
            Guid.NewGuid(), cam.Id, cam.CameraIp!, _webPort,
            cam.CameraUsername, cam.CameraPassword, kind, settings);

        var r = await _agente.EjecutarAsync(householdId, orden);
        if (r is null) return null;
        if (!r.Ok)
        {
            _logger.LogWarning("El agente de {Household} no pudo con la camara: {Error}",
                householdId, r.Error);
            throw new AppException($"La cámara rechazó el ajuste ({r.Error}).");
        }
        return r.Values ?? new Dictionary<string, string>();
    }

    private string MensajeSinAcceso(Guid householdId) =>
        _agente.AgenteConectado(householdId)
            ? "El agente de tu casa no respondió a tiempo. Comprueba que siga corriendo."
            : "No se pudo contactar con la cámara. Conecta el teléfono al wifi de casa, "
              + "o deja corriendo el agente de VigiShield en una PC de la vivienda para "
              + "poder cambiar estos ajustes desde fuera.";

    /// <summary>¿Hay un agente de esta vivienda en línea?</summary>
    public bool HayAgente(Guid householdId) => _agente.AgenteConectado(householdId);

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
