namespace VigiShield.Application.DTOs.Config;

public record AlertConfigDto(
    bool UnknownPersonEnabled,
    bool ForcedAccessEnabled,
    bool TailgatingEnabled,
    bool ClimbingEnabled,
    bool AggressionEnabled,
    int TailgatingThresholdSeconds,
    string? NighttimeStart,
    string? NighttimeEnd,
    bool WhatsAppEnabled
);
