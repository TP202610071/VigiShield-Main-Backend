namespace VigiShield.Domain.Entities;

public class AlertConfig
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    // Interruptores originales, por GRUPOS de tipos. Se conservan porque el
    // motor de IA y las versiones antiguas de la app los siguen leyendo; el
    // backend los recalcula a partir de DisabledEventTypes para que no puedan
    // contradecirse.
    public bool UnknownPersonEnabled { get; set; } = true;
    public bool ForcedAccessEnabled { get; set; } = true;
    public bool TailgatingEnabled { get; set; } = true;
    public bool ClimbingEnabled { get; set; } = true;
    public bool AggressionEnabled { get; set; } = true;

    /// <summary>
    /// Tipos de evento desactivados, separados por coma (nombres de EventType).
    ///
    /// Sustituye a los cinco interruptores por grupos: con ellos había tipos que
    /// no se podían activar ni desactivar por separado (p. ej. "Riesgo de
    /// intrusión" no tenía interruptor propio). Vacío o null = todo activo.
    /// </summary>
    public string? DisabledEventTypes { get; set; }

    // Sensitivity threshold in seconds (15–120), default 30
    public int TailgatingThresholdSeconds { get; set; } = 30;

    public TimeOnly? NighttimeStart { get; set; }
    public TimeOnly? NighttimeEnd { get; set; }

    public bool WhatsAppEnabled { get; set; }
}
