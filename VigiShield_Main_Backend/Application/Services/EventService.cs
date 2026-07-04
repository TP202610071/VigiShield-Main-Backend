using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Events;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Application.Services;

public class EventService
{
    private readonly AppDbContext _db;
    private readonly WhatsAppService _whatsApp;
    private readonly FcmService _fcm;

    public EventService(AppDbContext db, WhatsAppService whatsApp, FcmService fcm)
    {
        _db = db;
        _whatsApp = whatsApp;
        _fcm = fcm;
    }

    public async Task<EventDto?> IngestEventAsync(IngestEventRequest request)
    {
        var household = await _db.Households.FirstOrDefaultAsync(h => h.Id == request.HouseholdId)
            ?? throw AppException.NotFound("Hogar no encontrado");

        // Monitoring paused → suppress the event entirely (no record, no alert).
        if (household.IsMonitoringPaused)
            return null;

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

        // Push notification (FCM) to household devices for Medium+ events.
        if (_fcm.IsConfigured && ev.RiskLevel >= RiskLevel.Medium)
        {
            var tokens = await _db.Users
                .Where(u => u.HouseholdId == ev.HouseholdId && u.FcmToken != null && u.FcmToken != "")
                .Select(u => u.FcmToken!)
                .ToListAsync();
            if (tokens.Count > 0)
            {
                var (date, time) = WhatsAppService.LocalParts(ev.CreatedAt);
                _ = _fcm.SendAsync(tokens,
                    "VigiShield", $"{SpanishLabel(ev.EventType)} · {ev.CameraName ?? "Cámara"} · {time}",
                    new Dictionary<string, string> { ["eventId"] = ev.Id.ToString(), ["type"] = ev.EventType.ToString() });
            }
        }

        // WhatsApp alert for anything Medium or worse (skips FaceRecognized/Low).
        if (_whatsApp.IsConfigured && ev.RiskLevel >= RiskLevel.Medium)
        {
            var numbers = await _db.Users
                .Where(u => u.HouseholdId == ev.HouseholdId && u.WhatsAppNumber != null && u.WhatsAppNumber != "")
                .Select(u => u.WhatsAppNumber!)
                .ToListAsync();
            if (numbers.Count > 0)
            {
                var camera = ev.CameraName ?? "Cámara";
                var (date, time) = WhatsAppService.LocalParts(ev.CreatedAt);
                // Fire-and-forget: don't block the AI ingest call on Meta's API.
                // Unauthorized-person events use the dedicated template; the rest
                // use the generic one (with the event label as the first param).
                if (ev.EventType is EventType.UnknownFace or EventType.RecurrentUnknownFace)
                    _ = _whatsApp.SendTemplateAsync(numbers, "vigishield_alert", camera, date, time);
                else
                    _ = _whatsApp.SendTemplateAsync(numbers, "vigishield_general_alert",
                        SpanishLabel(ev.EventType), camera, date, time);
            }
        }

        return ToDto(ev);
    }

    /// <summary>WhatsApp numbers of household members (for alerts).</summary>
    public Task<List<string>> HouseholdWhatsAppNumbersAsync(Guid householdId) => _db.Users
        .Where(u => u.HouseholdId == householdId && u.WhatsAppNumber != null && u.WhatsAppNumber != "")
        .Select(u => u.WhatsAppNumber!)
        .ToListAsync();

    private static string SpanishLabel(EventType t) => t switch
    {
        EventType.UnknownFace => "Persona desconocida",
        EventType.RecurrentUnknownFace => "Persona desconocida recurrente",
        EventType.LowConfidenceFace => "Rostro no confirmado",
        EventType.FaceRecognized => "Persona reconocida",
        EventType.WeaponDetected => "Arma detectada",
        EventType.Tailgating => "Merodeador detectado",
        EventType.ForcedAccessAttempt => "Intento de acceso forzado",
        EventType.Climbing => "Escalamiento detectado",
        EventType.PhysicalAggression => "Agresión física",
        EventType.Robbery => "Robo",
        EventType.Burglary => "Allanamiento",
        EventType.Assault => "Asalto",
        EventType.Vandalism => "Vandalismo",
        EventType.Stealing => "Hurto",
        _ => t.ToString(),
    };

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

    /// <summary>Attach a recorded clip URL to an event (called by the AI service).</summary>
    public async Task AttachClipAsync(Guid eventId, string videoClipPath)
    {
        var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId)
            ?? throw AppException.NotFound("Evento no encontrado");

        ev.VideoClipPath = videoClipPath;
        await _db.SaveChangesAsync();
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
