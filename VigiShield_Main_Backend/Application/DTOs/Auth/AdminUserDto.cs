namespace VigiShield.Application.DTOs.Auth;

public record AdminUserDto(
    Guid Id,
    string Email,
    string Name,
    DateTime CreatedAt
);

public record AddAdminRequest(string Email);

/// <summary>A household + its primary user, for the developer alert-trigger tool.</summary>
public record HouseholdSummaryDto(Guid HouseholdId, string Name, string Email);
