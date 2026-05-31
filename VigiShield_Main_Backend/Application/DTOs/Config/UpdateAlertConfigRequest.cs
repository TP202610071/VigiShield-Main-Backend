using System.ComponentModel.DataAnnotations;

namespace VigiShield.Application.DTOs.Config;

public record UpdateAlertConfigRequest(
    bool UnknownPersonEnabled,
    bool ForcedAccessEnabled,
    bool TailgatingEnabled,
    bool ClimbingEnabled,
    bool AggressionEnabled,
    [Range(15, 120)] int TailgatingThresholdSeconds,
    string? NighttimeStart,
    string? NighttimeEnd,
    bool WhatsAppEnabled
);
