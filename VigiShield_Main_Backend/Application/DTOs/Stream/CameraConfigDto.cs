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
    DateTime? LastVerifiedAt
);
