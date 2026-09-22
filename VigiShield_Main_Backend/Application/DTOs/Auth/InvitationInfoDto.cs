namespace VigiShield.Application.DTOs.Auth;

/// <summary>Lo que la pantalla de aceptación muestra ANTES de pedir datos, para
/// que la persona sepa quién la invita y si el enlace sigue sirviendo.</summary>
public record InvitationInfoDto(
    bool Valid,
    string? Reason,
    string? Email,
    string? InvitedBy,
    string? HouseholdAddress,
    DateTime? ExpiresAt
);
