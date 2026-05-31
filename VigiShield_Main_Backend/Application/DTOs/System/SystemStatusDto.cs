namespace VigiShield.Application.DTOs.System;

public record SystemStatusDto(
    bool IsMonitoringActive,
    DateTime? LastEventAt,
    int EventsTodayCount,
    string StreamUrl
);
