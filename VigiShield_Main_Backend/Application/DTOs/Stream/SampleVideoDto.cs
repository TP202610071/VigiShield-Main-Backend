namespace VigiShield.Application.DTOs.Stream;

/// <summary>
/// Sesión en curso del video de ejemplo. <see cref="Seen"/> cuenta los videos
/// distintos que ya vio el hogar, de <see cref="Total"/>.
/// </summary>
public record SampleVideoDto(
    CameraConfigDto Camera,
    string VideoKey,
    string Title,
    string TitleEn,
    DateTime EndsAt,
    int Seen,
    int Total
);
