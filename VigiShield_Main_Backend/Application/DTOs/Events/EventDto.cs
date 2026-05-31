namespace VigiShield.Application.DTOs.Events;

public record EventDto(
    Guid Id,
    Guid HouseholdId,
    string EventType,
    float? ConfidenceScore,
    string? ImageCapturePath,
    string? VideoClipPath,
    string? PersonName,
    string RiskLevel,
    bool IsNighttime,
    DateTime CreatedAt
);
