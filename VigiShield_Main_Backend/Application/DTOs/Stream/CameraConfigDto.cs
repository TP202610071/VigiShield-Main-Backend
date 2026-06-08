namespace VigiShield.Application.DTOs.Stream;

public record CameraConfigDto(
    Guid Id,
    string Name,
    bool IsDefault,
    string StreamMode,
    string? CameraIp,
    int CameraPort,
    string? CameraPath,
    string? CameraUsername,
    bool HasPassword,
    string? StreamKey,
    string? RtmpPushUrl,
    string? HlsViewUrl,
    string? RtspUrl,
    bool IsConfigured,
    DateTime? LastVerifiedAt,
    // MediaMTX RTSP re-exposure — use this in the app instead of HlsViewUrl.
    // RTSP starts playing instantly (no keyframe wait), HLS blocks 0-16 s on cold start.
    string? MediaMtxRtspUrl
);
