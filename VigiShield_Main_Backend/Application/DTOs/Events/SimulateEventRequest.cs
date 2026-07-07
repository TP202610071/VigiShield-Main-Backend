using System.ComponentModel.DataAnnotations;
using VigiShield.Domain.Enums;

namespace VigiShield.Application.DTOs.Events;

/// <summary>Developer tool: trigger a real alert for any household on demand.</summary>
public record SimulateEventRequest(
    [Required] Guid HouseholdId,
    [Required] EventType EventType,
    string? CameraName
);
