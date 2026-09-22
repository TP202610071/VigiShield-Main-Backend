using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Config;
using VigiShield.Common.Alerts;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Application.Services;

public class ConfigService
{
    private readonly AppDbContext _db;

    public ConfigService(AppDbContext db) => _db = db;

    public async Task<AlertConfigDto> GetAlertConfigAsync(Guid householdId)
    {
        var config = await GetOrCreateAsync(householdId);
        return ToDto(config);
    }

    public async Task<AlertConfigDto> UpdateAlertConfigAsync(Guid householdId, UpdateAlertConfigRequest request)
    {
        var config = await GetOrCreateAsync(householdId);

        if (request.DisabledEventTypes is not null)
        {
            // Cliente nuevo: la selección por tipo es la fuente de verdad y los
            // interruptores por grupos se recalculan a partir de ella, para que
            // no puedan quedar diciendo cosas distintas.
            config.DisabledEventTypes = AlertableEvents.Serialize(request.DisabledEventTypes);
            var off = AlertableEvents.Parse(config.DisabledEventTypes);
            config.UnknownPersonEnabled = AlertableEvents.LegacyEnabled("unknownPerson", off);
            config.ForcedAccessEnabled = AlertableEvents.LegacyEnabled("forcedAccess", off);
            config.TailgatingEnabled = AlertableEvents.LegacyEnabled("tailgating", off);
            config.ClimbingEnabled = AlertableEvents.LegacyEnabled("climbing", off);
            config.AggressionEnabled = AlertableEvents.LegacyEnabled("aggression", off);
        }
        else
        {
            config.UnknownPersonEnabled = request.UnknownPersonEnabled;
            config.ForcedAccessEnabled = request.ForcedAccessEnabled;
            config.TailgatingEnabled = request.TailgatingEnabled;
            config.ClimbingEnabled = request.ClimbingEnabled;
            config.AggressionEnabled = request.AggressionEnabled;
        }
        config.TailgatingThresholdSeconds = request.TailgatingThresholdSeconds;
        config.WhatsAppEnabled = request.WhatsAppEnabled;

        config.NighttimeStart = TimeOnly.TryParse(request.NighttimeStart, out var start) ? start : null;
        config.NighttimeEnd = TimeOnly.TryParse(request.NighttimeEnd, out var end) ? end : null;

        await _db.SaveChangesAsync();
        return ToDto(config);
    }

    private async Task<AlertConfig> GetOrCreateAsync(Guid householdId)
    {
        var config = await _db.AlertConfigs.FirstOrDefaultAsync(c => c.HouseholdId == householdId);
        if (config is not null) return config;

        config = new AlertConfig { Id = Guid.NewGuid(), HouseholdId = householdId };
        _db.AlertConfigs.Add(config);
        await _db.SaveChangesAsync();
        return config;
    }

    private static AlertConfigDto ToDto(AlertConfig c) => new(
        c.UnknownPersonEnabled, c.ForcedAccessEnabled, c.TailgatingEnabled,
        c.ClimbingEnabled, c.AggressionEnabled, c.TailgatingThresholdSeconds,
        c.NighttimeStart?.ToString("HH:mm"), c.NighttimeEnd?.ToString("HH:mm"),
        c.WhatsAppEnabled,
        AlertableEvents.All.Select(t => t.ToString()).ToList(),
        AlertableEvents.Parse(c.DisabledEventTypes).ToList());
}
