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
        if (!Enum.TryParse<StreamMode>(req.StreamMode, ignoreCase: true, out var mode))
            throw new AppException("Modo inválido. Usa 'DirectRtsp' o 'RtmpRelay'.");

        var isFirst = !await _db.CameraConfigs.AnyAsync(c => c.HouseholdId == householdId);
        var makeDefault = req.IsDefault || isFirst;

        if (makeDefault)
            await ClearDefaultFlagAsync(householdId);

        var cam = new CameraConfig
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Name = string.IsNullOrWhiteSpace(req.Name) ? "Cámara" : req.Name.Trim(),
            IsDefault = makeDefault,
            StreamMode = mode,
            CameraIp = req.CameraIp?.Trim(),
            CameraPort = req.CameraPort > 0 ? req.CameraPort : 554,
            CameraPath = string.IsNullOrWhiteSpace(req.CameraPath) ? null : req.CameraPath.Trim(),
            CameraUsername = string.IsNullOrWhiteSpace(req.CameraUsername) ? null : req.CameraUsername,
            CameraPassword = string.IsNullOrWhiteSpace(req.CameraPassword) ? null : req.CameraPassword,
            CustomHlsUrl = string.IsNullOrWhiteSpace(req.CustomHlsUrl) ? null : req.CustomHlsUrl.Trim(),
            StreamKey = Guid.NewGuid().ToString("N")[..12],
            IsConfigured = !string.IsNullOrEmpty(req.CameraIp),
        };

        _db.CameraConfigs.Add(cam);
        await _db.SaveChangesAsync();

        // Regenerate MediaMTX config + try API reload
        await SyncMediaMtxAsync();

        return ToDto(cam);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task<CameraConfigDto> UpdateCameraAsync(Guid householdId, Guid cameraId, UpdateCameraConfigRequest req)
    {
        var cam = await _db.CameraConfigs
            .FirstOrDefaultAsync(c => c.Id == cameraId && c.HouseholdId == householdId)
            ?? throw AppException.NotFound("Cámara no encontrada");

        if (!Enum.TryParse<StreamMode>(req.StreamMode, ignoreCase: true, out var mode))
            throw new AppException("Modo inválido. Usa 'DirectRtsp' o 'RtmpRelay'.");

        if (!string.IsNullOrWhiteSpace(req.Name)) cam.Name = req.Name.Trim();
        cam.StreamMode = mode;
        cam.CameraIp = req.CameraIp?.Trim();
        cam.CameraPort = req.CameraPort > 0 ? req.CameraPort : 554;
        cam.CameraPath = string.IsNullOrWhiteSpace(req.CameraPath) ? null : req.CameraPath.Trim();
        cam.CameraUsername = string.IsNullOrWhiteSpace(req.CameraUsername) ? null : req.CameraUsername;

        if (req.CameraPassword is not null)
            cam.CameraPassword = req.CameraPassword.Length == 0 ? null : req.CameraPassword;

        cam.CustomHlsUrl = string.IsNullOrWhiteSpace(req.CustomHlsUrl) ? null : req.CustomHlsUrl.Trim();
        cam.IsConfigured = !string.IsNullOrEmpty(cam.CameraIp);

        if (req.IsDefault && !cam.IsDefault)
        {
            await ClearDefaultFlagAsync(householdId);
            cam.IsDefault = true;
        }

        cam.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Regenerate MediaMTX config + try API reload
        await SyncMediaMtxAsync();

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
        await SyncMediaMtxAsync();
    }

    // ── AI Backend config ─────────────────────────────────────────────────────

    /// <summary>All configured cameras across every household — for the Python AI service.</summary>
    public async Task<List<object>> GetAllAiConfigAsync()
    {
        var cameras = await _db.CameraConfigs
            .Where(c => c.IsConfigured)
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
                        && c.CameraIp != null)
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
        BuildMediaMtxRtspViewUrl(cam)
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
