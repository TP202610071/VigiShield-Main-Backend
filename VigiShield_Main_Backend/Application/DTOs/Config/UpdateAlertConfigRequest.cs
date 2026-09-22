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
    bool WhatsAppEnabled,
    // Cuando llega (app nueva) manda sobre los booleanos; si es null se conserva
    // lo guardado, para no romper a un cliente antiguo que no envíe el campo.
    IReadOnlyList<string>? DisabledEventTypes = null
);
