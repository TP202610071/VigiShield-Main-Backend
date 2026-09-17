namespace VigiShield.Application.DTOs.Stream;

/// <summary>
/// Una zona de interés (ROI) dibujada por el usuario sobre el video.
/// Coordenadas NORMALIZADAS (0..1) para ser independientes de la resolución.
/// </summary>
/// <param name="Id">Identificador estable de la zona (generado en la app).</param>
/// <param name="Type">door | gate | window | yard | street | custom.</param>
/// <param name="Name">Nombre legible ("Puerta", "Reja"…).</param>
/// <param name="Polygon">Vértices [[x,y], …] en 0..1 (mínimo 3).</param>
public record ZoneDto(
    string Id,
    string Type,
    string Name,
    List<List<double>> Polygon
);

/// <summary>Cuerpo del PUT que guarda todas las zonas de una cámara.</summary>
public record UpdateZonesRequest(
    List<ZoneDto> Zones
);
