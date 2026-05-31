using Microsoft.EntityFrameworkCore;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// On backend startup:
///   1. Generates mediamtx.yml with all DirectRtsp camera paths (+ API enabled).
///   2. Tries to reload MediaMTX via its API.
///      If API isn't available yet (first run), it logs a message telling the user
///      to restart mediamtx.exe once — after that, all future changes are automatic.
/// </summary>
public class MediaMtxStartupSync : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaMtxStartupSync> _logger;

    public MediaMtxStartupSync(IServiceScopeFactory scopeFactory, ILogger<MediaMtxStartupSync> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var mediaMtx = scope.ServiceProvider.GetRequiredService<MediaMtxService>();

            var cameras = await db.CameraConfigs
                .Where(c => c.StreamMode == StreamMode.DirectRtsp
                            && c.IsConfigured
                            && c.StreamKey != null
                            && c.CameraIp != null)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("MediaMTX sync: found {Count} DirectRtsp camera(s).", cameras.Count);

            var paths = cameras.Select(c =>
            {
                var auth = !string.IsNullOrEmpty(c.CameraUsername) && !string.IsNullOrEmpty(c.CameraPassword)
                    ? $"{Uri.EscapeDataString(c.CameraUsername)}:{Uri.EscapeDataString(c.CameraPassword)}@"
                    : "";
                var path = string.IsNullOrEmpty(c.CameraPath) ? "stream" : c.CameraPath.TrimStart('/');
                return (c.StreamKey!, $"rtsp://{auth}{c.CameraIp}:{c.CameraPort}/{path}");
            });

            await mediaMtx.WriteConfigFileAsync(paths);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("MediaMTX startup sync failed: {Message}", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
