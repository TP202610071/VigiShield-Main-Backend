namespace VigiShield.Domain.Entities;

public class AuthorizedFace
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public string PersonName { get; set; } = string.Empty;
    public string PhotoPathsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
