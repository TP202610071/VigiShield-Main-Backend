using VigiShield.Domain.Enums;

namespace VigiShield.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public string? WhatsAppNumber { get; set; }
    /// <summary>Relative path of the uploaded avatar, e.g. "/avatars/{id}.jpg". Null = use initials.</summary>
    public string? AvatarPath { get; set; }
    public string? FcmToken { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Cuándo aceptó los Términos y la Política de privacidad (Ley 29733). Null = aún no.</summary>
    public DateTime? TermsAcceptedAt { get; set; }
    /// <summary>Versión de los términos aceptados, p. ej. "2026-10-05".</summary>
    public string? TermsVersion { get; set; }
    /// <summary>Última petición autenticada (como mucho se actualiza cada 5 min).
    /// La usa el panel de administración para separar activos e inactivos.</summary>
    public DateTime? LastSeenAt { get; set; }
}
