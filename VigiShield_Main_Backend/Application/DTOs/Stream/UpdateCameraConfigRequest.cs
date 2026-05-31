using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Stream;

public record UpdateCameraConfigRequest(
    string? Name,
    [Required] string StreamMode,
    string? CameraIp,
    int CameraPort = 554,
    string? CameraPath = null,
    string? CameraUsername = null,
    /// <summary>Null = keep existing. Empty string = clear password.</summary>
    string? CameraPassword = null,
    string? CustomHlsUrl = null,
    bool IsDefault = false
);
