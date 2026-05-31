namespace VigiShield.Domain.Entities;

public class AlertConfig
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public bool UnknownPersonEnabled { get; set; } = true;
    public bool ForcedAccessEnabled { get; set; } = true;
    public bool TailgatingEnabled { get; set; } = true;
    public bool ClimbingEnabled { get; set; } = true;
    public bool AggressionEnabled { get; set; } = true;

    // Sensitivity threshold in seconds (15–120), default 30
    public int TailgatingThresholdSeconds { get; set; } = 30;

    public TimeOnly? NighttimeStart { get; set; }
    public TimeOnly? NighttimeEnd { get; set; }

    public bool WhatsAppEnabled { get; set; }
}
