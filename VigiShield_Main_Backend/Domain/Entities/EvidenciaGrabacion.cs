namespace VigiShield.Domain.Entities;

/// <summary>
/// Interruptor «Grabar evidencia» de un hogar, puesto a mano desde el panel de
/// administración. Si no hay fila, se aplica la regla por defecto de
/// <c>EvidenciaService</c>. Se borra junto con el hogar.
/// </summary>
public class EvidenciaGrabacion
{
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public bool Grabar { get; set; }
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;
}
