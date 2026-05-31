using VigiShield.Domain.Enums;

namespace VigiShield.Domain.Entities;

public class SecurityEvent
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public EventType EventType { get; set; }
    public float? ConfidenceScore { get; set; }
    public string? ImageCapturePath { get; set; }
    public string? VideoClipPath { get; set; }
    public string? PersonName { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public bool IsNighttime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
