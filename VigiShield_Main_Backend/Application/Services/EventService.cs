using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Events;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Application.Services;

public class EventService
{
    private readonly AppDbContext _db;

    public EventService(AppDbContext db) => _db = db;

    public async Task<EventDto> IngestEventAsync(IngestEventRequest request)
    {
        if (!await _db.Households.AnyAsync(h => h.Id == request.HouseholdId))
            throw AppException.NotFound("Hogar no encontrado");

        var ev = new SecurityEvent
        {
            Id = Guid.NewGuid(),
            HouseholdId = request.HouseholdId,
            CameraId = request.CameraId,
            CameraName = request.CameraName,
            EventType = request.EventType,
            ConfidenceScore = request.ConfidenceScore,
            ImageCapturePath = request.ImageCapturePath,
            VideoClipPath = request.VideoClipPath,
            PersonName = request.PersonName,
            RiskLevel = request.RiskLevel,
            IsNighttime = request.IsNighttime,
            CreatedAt = DateTime.UtcNow
        };

        _db.Events.Add(ev);
        await _db.SaveChangesAsync();

        // TODO: Trigger FCM push notification to household devices
        // TODO: Trigger WhatsApp alert if configured

        return ToDto(ev);
    }

    public async Task<EventListResponse> GetEventsAsync(
        Guid householdId, string? type, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var query = _db.Events.Where(e => e.HouseholdId == householdId);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<EventType>(type, ignoreCase: true, out var parsedType))
            query = query.Where(e => e.EventType == parsedType);

        if (from.HasValue)
            query = query.Where(e => e.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.CreatedAt <= to.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new EventListResponse(
            items.Select(ToDto).ToList(),
            total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize));
    }

    public async Task<EventDto> GetEventByIdAsync(Guid householdId, Guid eventId)
    {
        var ev = await _db.Events
            .FirstOrDefaultAsync(e => e.Id == eventId && e.HouseholdId == householdId)
            ?? throw AppException.NotFound("Evento no encontrado");

        return ToDto(ev);
    }

    public async Task DeleteEventAsync(Guid householdId, Guid eventId)
    {
        var ev = await _db.Events
            .FirstOrDefaultAsync(e => e.Id == eventId && e.HouseholdId == householdId)
            ?? throw AppException.NotFound("Evento no encontrado");

        _db.Events.Remove(ev);
        await _db.SaveChangesAsync();
    }

    public async Task BulkDeleteEventsAsync(Guid householdId, List<Guid> eventIds)
    {
        var events = await _db.Events
            .Where(e => eventIds.Contains(e.Id) && e.HouseholdId == householdId)
            .ToListAsync();

        _db.Events.RemoveRange(events);
        await _db.SaveChangesAsync();
    }

    private static EventDto ToDto(SecurityEvent ev) => new(
        ev.Id, ev.HouseholdId, ev.CameraId, ev.CameraName, ev.EventType.ToString(),
        ev.ConfidenceScore, ev.ImageCapturePath, ev.VideoClipPath,
        ev.PersonName, ev.RiskLevel.ToString(), ev.IsNighttime, ev.CreatedAt);
}
