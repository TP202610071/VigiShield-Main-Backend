using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.System;
using VigiShield.Common.Exceptions;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Application.Services;

public class SystemService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public SystemService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<SystemStatusDto> GetStatusAsync(Guid householdId)
    {
        var household = await _db.Households.FindAsync(householdId)
            ?? throw AppException.NotFound("Hogar no encontrado");

        var today = DateTime.UtcNow.Date;

        var lastEventAt = await _db.Events
            .Where(e => e.HouseholdId == householdId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => (DateTime?)e.CreatedAt)
            .FirstOrDefaultAsync();

        var eventsTodayCount = await _db.Events
            .CountAsync(e => e.HouseholdId == householdId && e.CreatedAt >= today);

        var streamUrl = _config["MediaMtx:HlsBaseUrl"] ?? string.Empty;

        return new SystemStatusDto(!household.IsMonitoringPaused, lastEventAt, eventsTodayCount, streamUrl);
    }

    public async Task PauseMonitoringAsync(Guid householdId)
    {
        var household = await _db.Households.FindAsync(householdId)
            ?? throw AppException.NotFound("Hogar no encontrado");

        household.IsMonitoringPaused = true;
        await _db.SaveChangesAsync();
    }

    public async Task ResumeMonitoringAsync(Guid householdId)
    {
        var household = await _db.Households.FindAsync(householdId)
            ?? throw AppException.NotFound("Hogar no encontrado");

        household.IsMonitoringPaused = false;
        await _db.SaveChangesAsync();
    }
}
