namespace VigiShield.Domain.Enums;

public enum EventType
{
    // Face recognition
    FaceRecognized,
    UnknownFace,
    LowConfidenceFace,
    RecurrentUnknownFace,

    // Access & intrusion (activity model + manual)
    ForcedAccessAttempt,
    Tailgating,
    Climbing,
    Burglary,

    // Physical events (activity model)
    PhysicalAggression,
    Assault,
    Abuse,
    Arrest,

    // Property crime (activity model)
    Stealing,
    Shoplifting,
    Vandalism,
    Robbery,
    Arson,

    // Hazard (activity model)
    Explosion,
    Roadaccidents,

    // Object detection (YOLO)
    WeaponDetected,
}
