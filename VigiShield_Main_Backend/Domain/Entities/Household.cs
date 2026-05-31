namespace VigiShield.Domain.Entities;

public class Household
{
    public Guid Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public Guid PrimaryUserId { get; set; }
    public bool IsMonitoringPaused { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<SecurityEvent> Events { get; set; } = new List<SecurityEvent>();
    public ICollection<AuthorizedFace> AuthorizedFaces { get; set; } = new List<AuthorizedFace>();
    public AlertConfig? AlertConfig { get; set; }

    /// <summary>All cameras configured for this household (can be multiple).</summary>
    public ICollection<CameraConfig> CameraConfigs { get; set; } = new List<CameraConfig>();

    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
    public ICollection<NotificationLog> NotificationLogs { get; set; } = new List<NotificationLog>();
}
