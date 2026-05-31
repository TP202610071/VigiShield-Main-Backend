namespace VigiShield.Application.DTOs.Events;

public record EventListResponse(
    List<EventDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages
);
