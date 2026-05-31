using System.Net.Http.Json;
using System.Text;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// Manages MediaMTX configuration.
///
/// Strategy (in order):
///   1. Write a mediamtx.yml file that includes all DirectRtsp paths + enables API.
///   2. After writing, try to reload via HTTP API (POST /v3/config/reload).
///      If API isn't available (first run), the user must restart mediamtx.exe once.
///      After that first restart with the generated config, the API is enabled and
///      future changes are applied automatically without any restart.
/// </summary>
///
/// DirectRtsp flow:
///   1. User saves camera config in app  →  backend calls RegisterPathAsync()
///   2. MediaMTX auto-pulls RTSP from the camera
///   3. MediaMTX serves HLS at /{streamKey}/index.m3u8
///   4. Flutter plays HLS — user does nothing else
///
/// RtmpRelay flow:
///   MediaMTX auto-creates the path when FFmpeg pushes RTMP to it.
///   No API call needed for that case.
///
/// On startup the backend re-registers all DirectRtsp cameras so MediaMTX
/// stays in sync after restarts.
/// </summary>
public class MediaMtxService
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _config;
    private readonly ILogger<MediaMtxService> _logger;

    public MediaMtxService(IHttpClientFactory factory, IConfiguration config, ILogger<MediaMtxService> logger)
    {
        _factory = factory;
        _config = config;
        _logger = logger;
    }

    private string ApiBase => (_config["MediaMtx:ApiUrl"] ?? "http://localhost:9997").TrimEnd('/');

    /// <summary>
    /// Register a path in MediaMTX so it pulls from the given RTSP URL.
    /// Non-fatal if MediaMTX is not running — it will pick it up when started.
    /// </summary>
    public async Task RegisterPathAsync(string streamKey, string rtspUrl)
    {
        try
        {
            var http = _factory.CreateClient("mediamtx");
            var payload = new { source = rtspUrl, sourceOnDemand = true };

            // Try PATCH first (update existing path). If 404, ADD (new path).
            var patch = await http.PatchAsJsonAsync($"{ApiBase}/v3/config/paths/patch/{streamKey}", payload);

            if (patch.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var add = await http.PostAsJsonAsync($"{ApiBase}/v3/config/paths/add/{streamKey}", payload);
                if (!add.IsSuccessStatusCode)
                    _logger.LogWarning("MediaMTX: could not add path '{Key}': {Status}", streamKey, add.StatusCode);
                else
                    _logger.LogInformation("MediaMTX: added path '{Key}' → {Url}", streamKey, rtspUrl);
            }
            else if (patch.IsSuccessStatusCode)
            {
                _logger.LogInformation("MediaMTX: updated path '{Key}' → {Url}", streamKey, rtspUrl);
            }
            else
            {
                _logger.LogWarning("MediaMTX: could not patch path '{Key}': {Status}", streamKey, patch.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            // Non-fatal: camera is saved in DB; MediaMTX can be started later.
            // The backend re-registers paths on every startup (see Program.cs).
            _logger.LogWarning(
                "MediaMTX API not reachable ({Message}). " +
                "Start MediaMTX and the path will be registered on next backend restart.",
                ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MediaMTX: unexpected error registering path '{Key}'", streamKey);
        }
    }

    /// <summary>Remove a path from MediaMTX when a camera is deleted.</summary>
    public async Task UnregisterPathAsync(string streamKey)
    {
        try
        {
            var http = _factory.CreateClient("mediamtx");
            await http.DeleteAsync($"{ApiBase}/v3/config/paths/delete/{streamKey}");
            _logger.LogInformation("MediaMTX: removed path '{Key}'", streamKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MediaMTX: could not remove path '{Key}': {Message}", streamKey, ex.Message);
        }
    }

    // ── Config file generation ────────────────────────────────────────────────

    /// <summary>
    /// Generate a complete mediamtx.yml from the given camera paths and write it to disk.
    /// Also enables the management API so future changes can be applied without restart.
    /// After writing, tries to reload via API — if that fails (first run), MediaMTX needs
    /// one manual restart, after which everything is automatic.
    /// </summary>
    public async Task WriteConfigFileAsync(IEnumerable<(string StreamKey, string RtspUrl)> cameras)
    {
        var configPath = _config["MediaMtx:ConfigPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "mediamtx.yml");

        configPath = Path.GetFullPath(configPath);

        var sb = new StringBuilder();
        sb.AppendLine("# VigiShield — generated mediamtx.yml");
        sb.AppendLine("# Do not edit manually; this file is regenerated by the backend.");
        sb.AppendLine();
        sb.AppendLine("# Management API — enables auto-configuration on camera changes");
        sb.AppendLine("api: yes");
        sb.AppendLine($"apiAddress: :{_config["MediaMtx:ApiPort"] ?? "9997"}");
        sb.AppendLine();
        sb.AppendLine("paths:");

        foreach (var (key, url) in cameras)
        {
            sb.AppendLine($"  {key}:");
            sb.AppendLine($"    source: {url}");
            sb.AppendLine($"    sourceOnDemand: yes");
        }

        if (!cameras.Any())
            sb.AppendLine("  ~^.*$: {}  # allow any path (RtmpRelay push)");

        try
        {
            await File.WriteAllTextAsync(configPath, sb.ToString());
            _logger.LogInformation("MediaMTX: config written to {Path}", configPath);

            // Try to reload via API (works if API was already enabled)
            await TryReloadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MediaMTX: could not write config file: {Message}", ex.Message);
        }
    }

    private async Task TryReloadAsync()
    {
        try
        {
            var http = _factory.CreateClient("mediamtx");
            var response = await http.PostAsync($"{ApiBase}/v3/config/reload", null);
            if (response.IsSuccessStatusCode)
                _logger.LogInformation("MediaMTX: config reloaded via API ✅");
            else
                _logger.LogInformation("MediaMTX: API reload returned {Status} — restart mediamtx.exe once to activate the new config.", response.StatusCode);
        }
        catch
        {
            _logger.LogInformation(
                "MediaMTX: API not yet available. Restart mediamtx.exe once with the generated config file to enable auto-reload.");
        }
    }
}
