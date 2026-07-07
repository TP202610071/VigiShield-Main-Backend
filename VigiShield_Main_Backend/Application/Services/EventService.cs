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

        return await CreateAndNotifyAsync(ev);
    }

    /// <summary>Developer/admin tool: create a real event (saved + notified, shown in
    /// the app) for any household as if it had truly fired. Bypasses monitoring-pause
    /// so alerts can be demoed/tested on demand.</summary>
    public async Task<EventDto> SimulateEventAsync(Guid householdId, EventType type, string? cameraName)
    {
        _ = await _db.Households.FirstOrDefaultAsync(h => h.Id == householdId)
            ?? throw AppException.NotFound("Hogar no encontrado");

        // Use the household's default camera name + reuse its most recent snapshot
        // so the simulated event looks real in the app history.
        var cam = cameraName
            ?? await _db.CameraConfigs.Where(c => c.HouseholdId == householdId)
                .OrderByDescending(c => c.IsDefault).Select(c => c.Name).FirstOrDefaultAsync()
            ?? "Cámara";
        var snapshot = await _db.Events
            .Where(e => e.HouseholdId == householdId && e.ImageCapturePath != null)
            .OrderByDescending(e => e.CreatedAt).Select(e => e.ImageCapturePath).FirstOrDefaultAsync();

        var ev = new SecurityEvent
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            CameraName = cam,
            EventType = type,
            ConfidenceScore = 0.95f,
            ImageCapturePath = snapshot,
            RiskLevel = DefaultRisk(type),
            IsNighttime = DateTime.UtcNow.Hour is >= 3 or < 11, // Lima night-ish
            CreatedAt = DateTime.UtcNow
        };
        return await CreateAndNotifyAsync(ev);
    }

    /// <summary>Persist an event and fire its push/WhatsApp alerts (Medium+). Shared by
    /// real ingest and the simulate tool.</summary>
    private async Task<EventDto> CreateAndNotifyAsync(SecurityEvent ev)
    {
        _db.Events.Add(ev);
        await _db.SaveChangesAsync();

        if (ev.RiskLevel >= RiskLevel.Medium)
        {
            var (date, time) = WhatsAppService.LocalParts(ev.CreatedAt);
            var camera = ev.CameraName ?? "Cámara";

            if (_fcm.IsConfigured)
            {
                var tokens = await _db.Users
                    .Where(u => u.HouseholdId == ev.HouseholdId && u.FcmToken != null && u.FcmToken != "")
                    .Select(u => u.FcmToken!).ToListAsync();
                if (tokens.Count > 0)
                    _ = _fcm.SendAsync(tokens, "VigiShield",
                        $"{SpanishLabel(ev.EventType)} · {camera} · {time}",
                        new Dictionary<string, string> { ["eventId"] = ev.Id.ToString(), ["type"] = ev.EventType.ToString() });
            }

            if (_whatsApp.IsConfigured)
            {
                var numbers = await _db.Users
                    .Where(u => u.HouseholdId == ev.HouseholdId && u.WhatsAppNumber != null && u.WhatsAppNumber != "")
                    .Select(u => u.WhatsAppNumber!).ToListAsync();
                if (numbers.Count > 0)
                    _ = _whatsApp.SendEventAlertAsync(
                        numbers, ev.Id.ToString(), SpanishLabel(ev.EventType), camera, date, time);
            }
        }

        return ToDto(ev);
    }

    private static RiskLevel DefaultRisk(EventType t) => t switch
    {
        EventType.WeaponDetected or EventType.Robbery or EventType.Assault
            or EventType.PhysicalAggression or EventType.Burglary => RiskLevel.Critical,
        EventType.ForcedAccessAttempt or EventType.Climbing or EventType.Stealing
            or EventType.Vandalism => RiskLevel.High,
        EventType.FaceRecognized => RiskLevel.None,
        _ => RiskLevel.Medium,
    };

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
