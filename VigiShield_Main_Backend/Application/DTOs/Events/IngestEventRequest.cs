using System.ComponentModel.DataAnnotations;
using VigiShield.Domain.Enums;

namespace VigiShield.Application.DTOs.Events;

public record IngestEventRequest(
    [Required] Guid HouseholdId,
    [Required] EventType EventType,
    float? ConfidenceScore,
    string? ImageCapturePath,
    string? VideoClipPath,
    string? PersonName,
    RiskLevel RiskLevel = RiskLevel.None,
    bool IsNighttime = false
);
