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
    string? MediaMtxRtspUrl,
    // Zonas de interés (ROI) dibujadas por el usuario — JSON crudo (o null).
    string? ZonesJson = null
);

/// <summary>
/// Datos que la app necesita para hablar con la cámara directamente por la red
/// local. El backend en la nube no alcanza una IP privada (192.168.x.x), así
/// que el control de imagen lo hace el teléfono cuando está en el wifi de casa.
/// Solo se entrega al residente principal, que es quien configuró la cámara.
/// </summary>
public record CameraLanAccessDto(
    string Ip,
    int HttpPort,
    string? Username,
    string? Password
);
