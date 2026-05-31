using VigiShield.Domain.Enums;

namespace VigiShield.Domain.Entities;

/// <summary>
/// Configuration for one IP camera belonging to a household.
/// A household can have multiple cameras (front door, garden, garage, …).
/// </summary>
public class CameraConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    /// <summary>Human-readable name, e.g. "Entrada principal", "Jardín".</summary>
    public string Name { get; set; } = "Cámara";

    /// <summary>Marks which camera to display by default in the app.</summary>
    public bool IsDefault { get; set; }

    public StreamMode StreamMode { get; set; } = StreamMode.DirectRtsp;

    // ── Camera RTSP credentials ──────────────────────────────────────────────
    public string? CameraIp { get; set; }
    public int CameraPort { get; set; } = 554;
    public string? CameraPath { get; set; }
    public string? CameraUsername { get; set; }
    public string? CameraPassword { get; set; }

    // ── RTMP Relay ───────────────────────────────────────────────────────────
    public string? StreamKey { get; set; }

    // ── Direct RTSP — custom view URL ────────────────────────────────────────
    public string? CustomHlsUrl { get; set; }

    // ── Status ───────────────────────────────────────────────────────────────
    public bool IsConfigured { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
