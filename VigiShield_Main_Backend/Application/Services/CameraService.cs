using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Stream;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Application.Services;

public class CameraService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly MediaMtxService _mediaMtx;

    public CameraService(AppDbContext db, IConfiguration config, MediaMtxService mediaMtx)
    {
        _db = db;
        _config = config;
        _mediaMtx = mediaMtx;
    }

    // ── List / Get ────────────────────────────────────────────────────────────

    public async Task<List<CameraConfigDto>> GetCamerasAsync(Guid householdId)
    {
        var cameras = await _db.CameraConfigs
            .Where(c => c.HouseholdId == householdId)
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync();

        return cameras.Select(ToDto).ToList();
    }

    public async Task<CameraConfigDto> GetCameraAsync(Guid householdId, Guid cameraId)
    {
        var cam = await _db.CameraConfigs
            .FirstOrDefaultAsync(c => c.Id == cameraId && c.HouseholdId == householdId)
            ?? throw AppException.NotFound("Cámara no encontrada");

        return ToDto(cam);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<CameraConfigDto> CreateCameraAsync(Guid householdId, UpdateCameraConfigRequest req)
    {
        if (!Enum.TryParse<StreamMode>(req.StreamMode, ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
            throw new AppException("Modo inválido. Usa 'DirectRtsp', 'RtmpRelay' o 'MobileWebRtc'.");

        var isFirst = !await _db.CameraConfigs.AnyAsync(c => c.HouseholdId == householdId);
        var makeDefault = req.IsDefault || isFirst;

        if (makeDefault)
            await ClearDefaultFlagAsync(householdId);

        // Cámara DEMO: si el usuario pone la IP "DEMO", se conecta al stream de
        // demostración fijo (rtsp://localhost:8554/demo) que un servicio en el AI VM
        // reproduce en loop. Se agrega/quita como cualquier cámara.
        var isDemo = string.Equals(req.CameraIp?.Trim(), "DEMO", StringComparison.OrdinalIgnoreCase);

        var cam = new CameraConfig
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Name = string.IsNullOrWhiteSpace(req.Name) ? "Cámara" : req.Name.Trim(),
            IsDefault = makeDefault,
            StreamMode = mode,
            CameraIp = mode == StreamMode.MobileWebRtc ? null : isDemo ? "DEMO" : req.CameraIp?.Trim(),
            CameraPort = req.CameraPort > 0 ? req.CameraPort : 554,
            CameraPath = string.IsNullOrWhiteSpace(req.CameraPath) ? null : req.CameraPath.Trim(),
            CameraUsername = string.IsNullOrWhiteSpace(req.CameraUsername) ? null : req.CameraUsername,
            CameraPassword = string.IsNullOrWhiteSpace(req.CameraPassword) ? null : req.CameraPassword,
            CustomHlsUrl = string.IsNullOrWhiteSpace(req.CustomHlsUrl) ? null : req.CustomHlsUrl.Trim(),
            StreamKey = isDemo && mode != StreamMode.MobileWebRtc ? "demo" : Guid.NewGuid().ToString("N")[..12],
            IsConfigured = mode == StreamMode.MobileWebRtc || isDemo || !string.IsNullOrEmpty(req.CameraIp),
        };

        _db.CameraConfigs.Add(cam);
        await _db.SaveChangesAsync();

        // Regenerate MediaMTX config + try API reload
        if (cam.StreamMode != StreamMode.MobileWebRtc) await SyncMediaMtxAsync();

        return ToDto(cam);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task<CameraConfigDto> UpdateCameraAsync(Guid householdId, Guid cameraId, UpdateCameraConfigRequest req)
    {
        var cam = await _db.CameraConfigs
            .FirstOrDefaultAsync(c => c.Id == cameraId && c.HouseholdId == householdId)
            ?? throw AppException.NotFound("Cámara no encontrada");

        if (!Enum.TryParse<StreamMode>(req.StreamMode, ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
            throw new AppException("Modo inválido. Usa 'DirectRtsp', 'RtmpRelay' o 'MobileWebRtc'.");

        if (!string.IsNullOrWhiteSpace(req.Name)) cam.Name = req.Name.Trim();
        var previousMode = cam.StreamMode;
        cam.StreamMode = mode;
        cam.CameraIp = mode == StreamMode.MobileWebRtc ? null : req.CameraIp?.Trim();
        cam.CameraPort = req.CameraPort > 0 ? req.CameraPort : 554;
        cam.CameraPath = string.IsNullOrWhiteSpace(req.CameraPath) ? null : req.CameraPath.Trim();
        cam.CameraUsername = string.IsNullOrWhiteSpace(req.CameraUsername) ? null : req.CameraUsername;

        if (req.CameraPassword is not null)
            cam.CameraPassword = req.CameraPassword.Length == 0 ? null : req.CameraPassword;

        cam.CustomHlsUrl = string.IsNullOrWhiteSpace(req.CustomHlsUrl) ? null : req.CustomHlsUrl.Trim();
        cam.IsConfigured = mode == StreamMode.MobileWebRtc || !string.IsNullOrEmpty(cam.CameraIp);
        if (mode == StreamMode.MobileWebRtc)
        {
            cam.CameraPath = cam.CameraUsername = cam.CameraPassword = cam.CustomHlsUrl = null;
            if (previousMode != mode || string.IsNullOrEmpty(cam.StreamKey) || cam.StreamKey == "demo")
                cam.StreamKey = Guid.NewGuid().ToString("N")[..12];
        }

        // Cámara DEMO (IP "DEMO") → apunta al stream de demostración fijo.
        if (string.Equals(cam.CameraIp, "DEMO", StringComparison.OrdinalIgnoreCase))
        {
            cam.CameraIp = "DEMO";
            cam.StreamKey = "demo";
            cam.IsConfigured = true;
        }

        if (req.IsDefault && !cam.IsDefault)
        {
            await ClearDefaultFlagAsync(householdId);
            cam.IsDefault = true;
        }

        cam.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Regenerate MediaMTX config + try API reload
        if (cam.StreamMode != StreamMode.MobileWebRtc) await SyncMediaMtxAsync();

        return ToDto(cam);
    }

    // ── Zonas de interés (ROI) ────────────────────────────────────────────────

    /// <summary>Guarda las zonas dibujadas por el usuario para una cámara.
    /// Lista vacía/null borra las zonas (vuelve al comportamiento sin contexto).</summary>
    private static readonly JsonSerializerOptions ZonasJson = new(JsonSerializerDefaults.Web);

    public async Task<CameraConfigDto> UpdateZonesAsync(Guid householdId, Guid cameraId, UpdateZonesRequest req)
    {
        var cam = await _db.CameraConfigs
            .FirstOrDefaultAsync(c => c.Id == cameraId && c.HouseholdId == householdId)
            ?? throw AppException.NotFound("Cámara no encontrada");

        if (req.Zones is null || req.Zones.Count == 0)
        {
            cam.ZonesJson = null;
        }
        else
        {
            // camelCase como el resto de la API: con las opciones por defecto salía
            // {"Id","Type","Polygon"} y ni la app ni la IA (que leen en minúsculas)
            // encontraban las zonas; se guardaban pero quedaban sin efecto.
            cam.ZonesJson = JsonSerializer.Serialize(new { version = 1, zones = req.Zones }, ZonasJson);
        }

        cam.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(cam);
    }

    /// <summary>
    /// Enciende o apaga el procesamiento de IA de una cámara.
    ///
    /// Apagarla es la única forma de que deje de consumir: el motor analiza
    /// cada cámara configurada la vea alguien o no.
    /// </summary>
    public async Task<CameraConfigDto> UpdateActiveAsync(Guid householdId, Guid cameraId, bool enabled)
    {
        var cam = await _db.CameraConfigs.FirstOrDefaultAsync(c => c.Id == cameraId && c.HouseholdId == householdId)
            ?? throw AppException.NotFound("Cámara no encontrada");
        cam.IsActive = enabled;
        cam.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(cam);
    }

    public async Task<CameraConfigDto> UpdateNotificationsAsync(Guid householdId, Guid cameraId, bool enabled)
    {
        var cam = await _db.CameraConfigs.FirstOrDefaultAsync(c => c.Id == cameraId && c.HouseholdId == householdId)
            ?? throw AppException.NotFound("Cámara no encontrada");
        cam.NotificationsEnabled = enabled;
        cam.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(cam);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task DeleteCameraAsync(Guid householdId, Guid cameraId)
    {
        var cam = await _db.CameraConfigs
            .FirstOrDefaultAsync(c => c.Id == cameraId && c.HouseholdId == householdId)
            ?? throw AppException.NotFound("Cámara no encontrada");

        _db.CameraConfigs.Remove(cam);
        await _db.SaveChangesAsync();

        // Promote another camera as default if needed
        if (cam.IsDefault)
        {
            var next = await _db.CameraConfigs
                .Where(c => c.HouseholdId == householdId)
                .OrderBy(c => c.CreatedAt)
                .FirstOrDefaultAsync();

            if (next is not null)
            {
                next.IsDefault = true;
                await _db.SaveChangesAsync();
            }
        }

        // Regenerate MediaMTX config (path is now absent from the file)
        if (cam.StreamMode != StreamMode.MobileWebRtc) await SyncMediaMtxAsync();
    }

    // ── AI Backend config ─────────────────────────────────────────────────────

    /// <summary>All configured cameras across every household — for the Python AI service.</summary>
    public async Task<List<object>> GetAllAiConfigAsync()
    {
        // Solo las activas: una camara desactivada no debe costar CPU.
        var cameras = await _db.CameraConfigs
            .Where(c => c.IsConfigured && c.IsActive)
            .ToListAsync();

        var rtspPort = _config["MediaMtx:RtspPort"] ?? "8554";

        var hlsPort = _config["MediaMtx:HlsPort"] ?? "8888";

        return cameras.Select(c => (object)new
        {
            id = c.Id,
            householdId = c.HouseholdId,
            name = c.Name,
            rtspUrl = BuildRtspUrl(c),
            mediaMtxRtspUrl = c.StreamKey is not null
                ? $"rtsp://localhost:{rtspPort}/{c.StreamKey}"
                : null,
            // Local HLS URL — used by AI backend to keep the muxer warm
            hlsLocalUrl = c.StreamKey is not null
                ? $"http://localhost:{hlsPort}/{c.StreamKey}/index.m3u8"
                : null,
            streamMode = c.StreamMode.ToString(),
            streamKey = c.StreamKey,
            isDefault = c.IsDefault,
            // Zonas ROI (JSON crudo) — el backend de IA (CAIEE) las parsea.
            zones = c.ZonesJson,
        }).ToList();
    }

    public async Task<List<object>> GetAiConfigAsync(Guid householdId)
    {
        var cameras = await _db.CameraConfigs
            .Where(c => c.HouseholdId == householdId && c.IsConfigured)
            .ToListAsync();

        return cameras.Select(c => (object)new
        {
            id = c.Id,
            name = c.Name,
            rtspUrl = BuildRtspUrl(c),
            streamMode = c.StreamMode.ToString(),
            streamKey = c.StreamKey,
            isDefault = c.IsDefault,
        }).ToList();
    }

    // ── Backwards-compat: default camera ─────────────────────────────────────

    public async Task<CameraConfigDto?> GetDefaultCameraAsync(Guid householdId)
    {
        var cam = await _db.CameraConfigs
            .Where(c => c.HouseholdId == householdId)
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        return cam is null ? null : ToDto(cam);
    }

    // ── MediaMTX sync ─────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuild the full MediaMTX config from the current DB state (all households)
    /// and write it to disk. Then try to reload MediaMTX via API.
    /// </summary>
    public async Task SyncMediaMtxAsync()
    {
        var allCameras = await _db.CameraConfigs
            .Where(c => c.StreamMode == StreamMode.DirectRtsp
                        && c.IsConfigured
                        && c.StreamKey != null
                        && c.CameraIp != null
                        && c.CameraIp != "DEMO")  // la demo la publica el loop, no se hace pull
            .ToListAsync();

        var paths = allCameras.Select(c => (c.StreamKey!, BuildRtspUrl(c)!))
                              .Where(t => t.Item2 is not null);

        await _mediaMtx.WriteConfigFileAsync(paths);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task ClearDefaultFlagAsync(Guid householdId)
    {
        await _db.CameraConfigs
            .Where(c => c.HouseholdId == householdId && c.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, false));
    }

    private CameraConfigDto ToDto(CameraConfig cam) => new(
        cam.Id,
        cam.Name,
        cam.IsDefault,
        cam.StreamMode.ToString(),
        cam.CameraIp,
        cam.CameraPort,
        cam.CameraPath,
        cam.CameraUsername,
        cam.CameraPassword is not null,
        cam.StreamKey,
        BuildRtmpPushUrl(cam),
        BuildHlsViewUrl(cam),
        BuildRtspUrl(cam),
        cam.IsConfigured,
        cam.LastVerifiedAt,
        BuildMediaMtxRtspViewUrl(cam),
        cam.ZonesJson,
        cam.NotificationsEnabled,
        cam.IsActive
    );

    private static string? BuildRtspUrl(CameraConfig cam)
    {
        if (string.IsNullOrEmpty(cam.CameraIp)) return null;
        var auth = (!string.IsNullOrEmpty(cam.CameraUsername) && !string.IsNullOrEmpty(cam.CameraPassword))
            ? $"{Uri.EscapeDataString(cam.CameraUsername)}:{Uri.EscapeDataString(cam.CameraPassword)}@"
            : "";
        var path = string.IsNullOrEmpty(cam.CameraPath) ? "stream" : cam.CameraPath.TrimStart('/');
        return $"rtsp://{auth}{cam.CameraIp}:{cam.CameraPort}/{path}";
    }

    private string? BuildHlsViewUrl(CameraConfig cam)
    {
        if (!string.IsNullOrEmpty(cam.CustomHlsUrl))
            return cam.CustomHlsUrl;

        var baseUrl = _config["MediaMtx:HlsBaseUrl"]?.TrimEnd('/');
        return baseUrl is null || cam.StreamKey is null
            ? null
            : $"{baseUrl}/{cam.StreamKey}/index.m3u8";
    }

    private string? BuildMediaMtxRtspViewUrl(CameraConfig cam)
    {
        if (cam.StreamKey is null) return null;
        var host = _config["MediaMtx:ServerHost"];
        var port = _config["MediaMtx:RtspPort"] ?? "8554";
        return host is null ? null : $"rtsp://{host}:{port}/{cam.StreamKey}";
    }

    private string? BuildRtmpPushUrl(CameraConfig cam)
    {
        if (cam.StreamMode != StreamMode.RtmpRelay || cam.StreamKey is null) return null;
        var host = _config["MediaMtx:ServerHost"];
        var port = _config["MediaMtx:RtmpPort"] ?? "1935";
        return host is null ? null : $"rtmp://{host}:{port}/live/{cam.StreamKey}";
    }

    /// <summary>Generates the mediamtx.yml snippet for reference (advanced users only).</summary>
    public string? BuildMediaMtxConfig(CameraConfig cam)
    {
        var rtspUrl = BuildRtspUrl(cam);
        if (rtspUrl is null || cam.StreamKey is null) return null;
        return $"paths:\n  {cam.StreamKey}:\n    source: {rtspUrl}\n    sourceOnDemand: true";
    }
}
