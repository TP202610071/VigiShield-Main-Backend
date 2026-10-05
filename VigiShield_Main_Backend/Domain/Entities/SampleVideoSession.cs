namespace VigiShield.Domain.Entities;

/// <summary>
/// Cada vez que alguien reproduce un video de ejemplo. Sirve para repartir los
/// videos entre los participantes sin repetir y para contar cuántos lo usaron.
/// </summary>
public class SampleVideoSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public Guid? UserId { get; set; }
    public string VideoKey { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
}
