namespace VigiShield.Domain.Enums;

public enum EventType
{
    FaceRecognized,
    UnknownFace,
    LowConfidenceFace,
    RecurrentUnknownFace,
    ForcedAccessAttempt,
    Tailgating,
    Climbing,
    PhysicalAggression
}
