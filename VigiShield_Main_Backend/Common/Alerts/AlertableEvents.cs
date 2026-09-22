using VigiShield.Domain.Enums;

namespace VigiShield.Common.Alerts;

/// <summary>
/// Qué tipos de evento puede activar o desactivar el residente principal, y cómo
/// se traducen a los cinco interruptores por grupos que existían antes.
///
/// Con los grupos había tipos sin control propio: "Riesgo de intrusión" —el que
/// más genera el motor— no tenía interruptor, y desactivar "Agresión" apagaba de
/// paso robo, hurto, vandalismo e incendio. Ahora cada tipo se controla por
/// separado y los interruptores antiguos se derivan de esta selección.
/// </summary>
public static class AlertableEvents
{
    /// <summary>Todos los tipos que el usuario puede gobernar, en el orden en que
    /// los muestra la app (primero los que el sistema genera de verdad hoy).</summary>
    public static readonly IReadOnlyList<EventType> All = new[]
    {
        EventType.UnknownFace,
        EventType.Tailgating,
        EventType.SuspiciousIntent,
        EventType.WeaponDetected,
        EventType.ForcedAccessAttempt,
        EventType.Climbing,
        EventType.PhysicalAggression,
        EventType.LowConfidenceFace,
        EventType.RecurrentUnknownFace,
        EventType.Burglary,
        EventType.Robbery,
        EventType.Stealing,
        EventType.Shoplifting,
        EventType.Vandalism,
        EventType.Assault,
        EventType.Abuse,
        EventType.Arrest,
        EventType.Arson,
        EventType.Explosion,
        EventType.Roadaccidents,
    };

    /// <summary>Tipos que gobierna cada interruptor antiguo (para recalcularlos).</summary>
    private static readonly Dictionary<string, EventType[]> LegacyGroups = new()
    {
        ["unknownPerson"] = new[] { EventType.UnknownFace, EventType.RecurrentUnknownFace, EventType.LowConfidenceFace },
        ["forcedAccess"]  = new[] { EventType.ForcedAccessAttempt },
        ["tailgating"]    = new[] { EventType.Tailgating },
        ["climbing"]      = new[] { EventType.Climbing },
        ["aggression"]    = new[] { EventType.PhysicalAggression, EventType.Assault, EventType.Abuse,
                                    EventType.Robbery, EventType.Stealing, EventType.Vandalism,
                                    EventType.Burglary, EventType.Arson, EventType.Arrest },
    };

    public static HashSet<string> Parse(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new HashSet<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sólo se guardan nombres válidos y gobernables: así un cliente que
    /// mande basura no puede apagar el sistema entero.</summary>
    public static string Serialize(IEnumerable<string>? disabled)
    {
        if (disabled is null) return "";
        var validos = All.Select(t => t.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return string.Join(',', disabled.Where(validos.Contains)
                                        .Select(d => validos.First(v => v.Equals(d, StringComparison.OrdinalIgnoreCase)))
                                        .Distinct());
    }

    /// <summary>Un interruptor antiguo queda encendido si al menos uno de los
    /// tipos de su grupo sigue activo.</summary>
    public static bool LegacyEnabled(string group, HashSet<string> disabled) =>
        LegacyGroups.TryGetValue(group, out var tipos)
        && tipos.Any(t => !disabled.Contains(t.ToString()));
}
