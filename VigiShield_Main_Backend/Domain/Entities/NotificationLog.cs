using VigiShield.Domain.Enums;

namespace VigiShield.Domain.Entities;

/// <summary>
/// Audit log of every push notification or WhatsApp alert sent to household members.
/// </summary>
public class NotificationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    /// <summary>The security event that triggered this notification, if any.</summary>
    public Guid? SecurityEventId { get; set; }
    public SecurityEvent? SecurityEvent { get; set; }

    public Guid RecipientUserId { get; set; }
    public User Recipient { get; set; } = null!;

    public NotificationChannel Channel { get; set; }

    public string Message { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
