namespace VigiShield.Application.DTOs.Faces;

public record FaceDto(Guid Id, string PersonName, List<string> PhotoPaths, DateTime CreatedAt);
