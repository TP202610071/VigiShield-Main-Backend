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
    bool WhatsAppEnabled,
    // Control por tipo de evento. `AvailableEventTypes` es la lista completa que
    // la app debe ofrecer; `DisabledEventTypes` los que están apagados. Los cinco
    // booleanos de arriba se derivan de esto y se mantienen sólo por compatibilidad.
    IReadOnlyList<string> AvailableEventTypes,
    IReadOnlyList<string> DisabledEventTypes
);
